//! The operator spoke: a loopback-only web page for identity and enrollment stewardship.
//! Hand-rolled minimal HTTP/1.1 in the house style — request line, headers and a
//! Content-Length body under an 8 KiB header cap and a 1 MiB body cap, GET/POST only,
//! `Connection: close`, a 30 s read timeout, JSON-only bodies, no CORS headers. The page
//! is an inert embedded string; every `/api/*` JSON call crosses the SAME hub as the CLI
//! and MCP intakes (attribution channel `Operator`). Structural local-only guarantees
//! (loopback bind, method/caps discipline) carry the trust: the operator is the trust
//! root and a local process can read state.json directly anyway, so the page carries no
//! interactive token (owner correction). Nothing but the startup banner is ever printed
//! to stdout.

use std::io::{BufRead, BufReader, Read, Write};
use std::net::{TcpListener, TcpStream};
use std::sync::atomic::{AtomicUsize, Ordering};
use std::sync::mpsc::RecvTimeoutError;
use std::sync::Arc;
use std::time::Duration;

use serde_json::{json, Value};

use crate::adapters::lockfile::DataDirLock;
use crate::adapters::{browser, tray};
use crate::application::hub::ConnectorHub;
use crate::domain::events::DomainEvent;
use crate::domain::identity::CallerId;
use crate::{build_hub, data_directory};

const HEADER_LIMIT: usize = 8 * 1024;
const BODY_LIMIT: usize = 1024 * 1024;
const READ_TIMEOUT: Duration = Duration::from_secs(30);
/// Settling pause after a failed accept, so a persistent socket-level failure cannot
/// spin the loop hot.
const ACCEPT_ERROR_PAUSE: Duration = Duration::from_millis(100);
/// Concurrent SSE feed clients this server will hold open. A small bound: each pins a
/// connection thread, and one operator needs at most a couple of tabs.
const SSE_CLIENT_LIMIT: usize = 4;
/// SSE keepalive cadence: a comment frame that also proves the peer is still there.
const SSE_KEEPALIVE: Duration = Duration::from_secs(15);
const INDEX_HTML: &str = include_str!("operator.html");

/// Entry point of the `operator` verb. Owns stdout for its banner; the MCP edge is a
/// separate process and never runs here.
pub fn operator(rest: &[String]) -> i32 {
    let mut port: Option<u16> = None;
    let mut open_browser = true;
    let mut force = false;
    let mut remaining = rest.iter();
    while let Some(argument) = remaining.next() {
        match argument.as_str() {
            "--port" => match remaining.next().and_then(|value| value.parse().ok()) {
                Some(value) => port = Some(value),
                None => {
                    eprintln!("--port needs a number");
                    return 1;
                }
            },
            "--no-open" => open_browser = false,
            "--force" => force = true,
            other => {
                eprintln!("unknown operator option {other}");
                return 1;
            }
        }
    }
    let data_dir = data_directory();
    // Long-running verbs are mutually exclusive per data directory: whole-file state
    // saves from two processes would clobber each other.
    let lock = match DataDirLock::acquire(&data_dir, force) {
        Ok(lock) => lock,
        Err(error) => {
            eprintln!("{error}");
            return 4;
        }
    };
    let hub = match build_hub(CallerId("operator".into()), data_dir) {
        Ok(hub) => hub,
        Err(error) => {
            eprintln!("cannot open connector state: {error}");
            return 4;
        }
    };
    // Loopback only: the listener binds 127.0.0.1, never anything reachable off-machine.
    let listener = match TcpListener::bind(("127.0.0.1", port.unwrap_or(0))) {
        Ok(listener) => listener,
        Err(error) => {
            eprintln!("cannot bind the operator listener: {error}");
            return 4;
        }
    };
    let bound_port = listener.local_addr().map(|address| address.port()).unwrap_or_default();
    let url = format!("http://127.0.0.1:{bound_port}/");
    println!("Tangent connector operator page: {url}");
    println!("This address lives for the life of this process; the connector records it in its state so any Connect can pop this page.");
    // The page URL is recorded in memory AND durable state (P4): a Connect in any
    // process — the CLI one-shots included — pops this page at the sign-in anchor.
    hub.announce_operator_page(&url);
    if open_browser {
        // Guarded like every other spawn path: TANGENT_CONNECTOR_NO_BROWSER=1 covers
        // the startup open too (tests, headless hosts).
        let _ = browser::open_guarded(&url);
    }
    // The tray's Quit releases the data-directory lock and clears the recorded page
    // URL before exiting the process.
    let quit_lock = lock.clone();
    let quit_hub = hub.clone();
    tray::spawn(
        hub.clone(),
        url,
        Box::new(move || {
            quit_hub.clear_persisted_operator_page();
            quit_lock.release();
            std::process::exit(0);
        }),
    );
    let serving = listener.try_clone().expect("clone listener");
    let serve_hub = hub.clone();
    let server = std::thread::Builder::new()
        .name("tangent-operator".into())
        .spawn(move || serve(serving, serve_hub))
        .expect("operator server thread");
    // The server thread owns the listener; the tray's Quit exits the process. Either
    // path releases the lock (Drop here, release() in the quit hook) and clears the
    // recorded page URL.
    let _ = server.join();
    hub.clear_persisted_operator_page();
    0
}

/// The accept loop: one short-lived connection thread per request (`Connection: close`);
/// an SSE feed connection is the one deliberate exception and stays open. A failed
/// accept pauses briefly and continues — a transient socket-level error must not end the
/// verb. Shared with the in-process serve mode and tests, which pass their own listener.
pub fn serve(listener: TcpListener, hub: Arc<ConnectorHub>) {
    let sse_clients = Arc::new(AtomicUsize::new(0));
    for stream in listener.incoming() {
        let stream = match stream {
            Ok(stream) => stream,
            Err(_) => {
                std::thread::sleep(ACCEPT_ERROR_PAUSE);
                continue;
            }
        };
        let hub = hub.clone();
        let sse_clients = sse_clients.clone();
        std::thread::spawn(move || serve_connection(stream, hub, sse_clients));
    }
}

/// Why a capped read stopped.
#[derive(Debug)]
enum CappedError {
    /// The line exceeded the byte budget while still arriving.
    OverLimit,
    /// The stream failed or the peer vanished.
    Io,
}

/// Reads one line incrementally, refusing to buffer beyond `cap`: the limit bites while
/// bytes arrive, not after, and only the line itself (newline included) is consumed —
/// following request bytes stay queued for the next read. Returns the raw byte count
/// consumed; `Ok(None)` is a clean end of stream before any byte.
fn read_line_capped(reader: &mut impl BufRead, buffer: &mut String, cap: usize) -> Result<Option<usize>, CappedError> {
    buffer.clear();
    let mut total = 0usize;
    loop {
        let (consumed, hit_newline) = {
            let available = match reader.fill_buf() {
                Ok(available) => available,
                Err(error) if error.kind() == std::io::ErrorKind::Interrupted => continue,
                Err(_) => return Err(CappedError::Io),
            };
            if available.is_empty() {
                return if total == 0 { Ok(None) } else { Ok(Some(total)) };
            }
            match available.iter().position(|byte| *byte == b'\n') {
                Some(index) => {
                    total += index + 1;
                    if total > cap {
                        return Err(CappedError::OverLimit);
                    }
                    buffer.push_str(&String::from_utf8_lossy(&available[..index]));
                    (index + 1, true)
                }
                None => {
                    total += available.len();
                    if total > cap {
                        return Err(CappedError::OverLimit);
                    }
                    buffer.push_str(&String::from_utf8_lossy(available));
                    (available.len(), false)
                }
            }
        };
        reader.consume(consumed);
        if hit_newline {
            return Ok(Some(total));
        }
    }
}

/// One request per connection (`Connection: close`), parsed under the caps. The request
/// line and every header are read incrementally against the 8 KiB header budget, so an
/// unterminated peer cannot grow memory first and fail later; a mid-request IO error
/// just drops that connection — the verb keeps serving. `GET /api/events` is the one
/// exception: it becomes a held-open SSE feed.
fn serve_connection(stream: TcpStream, hub: Arc<ConnectorHub>, sse_clients: Arc<AtomicUsize>) {
    let _ = stream.set_read_timeout(Some(READ_TIMEOUT));
    let mut reader = BufReader::new(match stream.try_clone() {
        Ok(clone) => clone,
        Err(_) => return,
    });
    let mut writer = stream;
    let mut request_line = String::new();
    match read_line_capped(&mut reader, &mut request_line, HEADER_LIMIT) {
        Ok(Some(_)) => {}
        Ok(None) | Err(CappedError::Io) => return,
        Err(CappedError::OverLimit) => {
            let _ = respond(&mut writer, 400, problem_json("headers_too_large", "request line exceeds 8 KiB"));
            return;
        }
    }
    let mut parts = request_line.split_whitespace();
    let method = parts.next().unwrap_or_default().to_string();
    let target = parts.next().unwrap_or_default().to_string();
    if method.is_empty() || target.is_empty() {
        let _ = respond(&mut writer, 400, problem_json("bad_request", "malformed request line"));
        return;
    }
    if method != "GET" && method != "POST" {
        let _ = respond(&mut writer, 405, problem_json("method_not_allowed", "GET and POST only"));
        return;
    }
    let mut content_length: usize = 0;
    let mut budget = HEADER_LIMIT;
    loop {
        let mut header = String::new();
        match read_line_capped(&mut reader, &mut header, budget) {
            Ok(Some(raw)) => {
                budget = budget.saturating_sub(raw);
            }
            Ok(None) | Err(CappedError::Io) => return,
            Err(CappedError::OverLimit) => {
                let _ = respond(&mut writer, 400, problem_json("headers_too_large", "headers exceed 8 KiB"));
                return;
            }
        }
        if header.trim().is_empty() {
            break;
        }
        // Header names are case-insensitive; compare one lowercased view.
        let lowered = header.to_ascii_lowercase();
        if let Some(value) = lowered.strip_prefix("content-length:") {
            content_length = value.trim().parse().unwrap_or(0);
        }
    }
    if content_length > BODY_LIMIT {
        let _ = respond(&mut writer, 413, problem_json("body_too_large", "body exceeds 1 MiB"));
        return;
    }
    let mut body_bytes = vec![0u8; content_length];
    if content_length > 0 && reader.read_exact(&mut body_bytes).is_err() {
        return;
    }
    // The live activity feed (A1): the connection is handed to the streaming handler
    // and never returns here.
    if method == "GET" && target.split('?').next() == Some("/api/events") {
        stream_events(writer, hub, sse_clients);
        return;
    }
    let body: Value = serde_json::from_slice(&body_bytes).unwrap_or(Value::Null);
    let response = route(&hub, &method, &target, &body);
    let _ = respond(&mut writer, response.0, response.1);
}

/// The SSE feed (owner addendum): the one deliberate exception to this server's
/// one-response-per-connection shape — the response is held open and written as
/// events arrive. Frames are the existing `DomainEvent` vocabulary serialized as
/// `data:` JSON (the page reads `kind` from the payload). The `OperatorPageReady`
/// event is deliberately skipped: it is journal material, not feed material. A
/// keepalive comment every [`SSE_KEEPALIVE`] keeps intermediaries honest and surfaces
/// a vanished peer as a write error; past [`SSE_CLIENT_LIMIT`] concurrent clients the
/// refusal is an honest 503 JSON problem.
fn stream_events(mut writer: TcpStream, hub: Arc<ConnectorHub>, sse_clients: Arc<AtomicUsize>) {
    if sse_clients.fetch_add(1, Ordering::AcqRel) >= SSE_CLIENT_LIMIT {
        sse_clients.fetch_sub(1, Ordering::AcqRel);
        let _ = respond(&mut writer, 503, problem_json("sse_clients_busy", "too many live activity feeds are open; close one and reload"));
        return;
    }
    let _guard = SseSlot { count: sse_clients };
    // Subscribe BEFORE the response head is written: a client that has seen the head
    // can then never miss a later event (the channel buffers in between).
    let receiver = hub.events().subscribe();
    let head = "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream; charset=utf-8\r\nCache-Control: no-store\r\n\r\n";
    if writer.write_all(head.as_bytes()).is_err() || writer.flush().is_err() {
        return;
    }
    loop {
        match receiver.recv_timeout(SSE_KEEPALIVE) {
            Ok(event) => {
                if matches!(event, DomainEvent::OperatorPageReady { .. }) {
                    continue;
                }
                let data = serde_json::to_string(&event)
                    .unwrap_or_else(|_| "{\"kind\":\"unserializable\"}".to_string());
                let frame = format!("data: {data}\n\n");
                if writer.write_all(frame.as_bytes()).is_err() || writer.flush().is_err() {
                    return;
                }
            }
            Err(RecvTimeoutError::Timeout) => {
                if writer.write_all(b": keepalive\n\n").is_err() || writer.flush().is_err() {
                    return;
                }
            }
            Err(RecvTimeoutError::Disconnected) => return,
        }
    }
}

/// Decrements the live SSE client count when the feed ends, however it ends.
struct SseSlot {
    count: Arc<AtomicUsize>,
}

impl Drop for SseSlot {
    fn drop(&mut self) {
        self.count.fetch_sub(1, Ordering::AcqRel);
    }
}

struct ApiResponse(u16, Value);

fn route(hub: &ConnectorHub, method: &str, target: &str, body: &Value) -> ApiResponse {
    let (path, _query) = match target.split_once('?') {
        Some((path, query)) => (path, query),
        None => (target, ""),
    };
    // The embedded page is inert HTML+JS: served plainly. Everything under /api/ is the
    // same local-only trust boundary (loopback bind, GET/POST, caps, JSON bodies).
    if !path.starts_with("/api/") {
        return match (method, path) {
            ("GET", "/") | ("GET", "/index.html") => ApiResponse(200, Value::String(INDEX_HTML.to_string())),
            _ => ApiResponse(404, problem_json("not_found", "only the operator page and /api/* live here")),
        };
    }
    let segments: Vec<&str> = path.trim_start_matches("/api/").split('/').filter(|segment| !segment.is_empty()).collect();
    match (method, segments.as_slice()) {
        ("GET", ["identities"]) => ApiResponse(200, ok_json(json!({ "identities": identity_list(hub) }))),
        ("POST", ["identities"]) => {
            let handle = body.get("handle").and_then(Value::as_str).unwrap_or_default();
            let display = body.get("displayName").and_then(Value::as_str);
            finish(hub.create_identity(handle, display), |identity| ok_json(json!({ "identity": identity_json(&identity) })))
        }
        ("POST", ["identities", local_id]) => {
            let handle = body.get("handle").and_then(Value::as_str);
            let display = match body.get("displayName") {
                None | Some(Value::Null) => None,
                Some(Value::String(value)) => Some(Some(value.as_str())),
                Some(_) => return ApiResponse(400, problem_json("bad_request", "displayName must be a string or null")),
            };
            finish(hub.update_identity(local_id, handle, display), |identity| ok_json(json!({ "identity": identity_json(&identity) })))
        }
        ("POST", ["identities", local_id, "delete"]) => {
            let cascade = body.get("cascade").and_then(Value::as_bool).unwrap_or(false);
            finish(hub.delete_identity(local_id, cascade), |_| ok_json(json!({ "deleted": local_id })))
        }
        ("GET", ["identities", local_id, "enrollments"]) => {
            if hub.identity(local_id).is_none() {
                return ApiResponse(200, blocked_json("unknown_identity", "no identity matches that id"));
            }
            let enrollments: Vec<Value> = hub
                .enrollment_inventory()
                .into_iter()
                .filter(|(entry, _)| entry.local_id == *local_id)
                .map(|(entry, available)| enrollment_json(&entry, available))
                .collect();
            ApiResponse(200, ok_json(json!({ "enrollments": enrollments })))
        }
        // Enrollment deliberately has no route here (R2): it is a consequence of
        // connecting (the Connect handshake) or an explicit hub/CLI action — the disarm
        // tier — never an operator-page ceremony. The old enroll routes are gone.
        ("POST", ["identities", local_id, "atproto", "bind"]) => {
            let handle = body.get("handle").and_then(Value::as_str).unwrap_or_default();
            let password = body.get("appPassword").and_then(Value::as_str).unwrap_or_default();
            let pds = match body.get("pds") {
                None | Some(Value::Null) => None,
                Some(Value::String(value)) if value.trim().is_empty() => None,
                Some(Value::String(value)) => Some(value.as_str()),
                Some(_) => return ApiResponse(400, problem_json("bad_request", "pds must be a string or null")),
            };
            let bound = hub.bind_atproto(local_id, handle, password, pds);
            // The response carries the identity view (with binding status) only — never
            // the app password and never the atproto session token. The binding read
            // happens here, after `bind_atproto` returned and released its guards.
            finish(bound, |identity| {
                ok_json(json!({ "identity": identity_with_atproto(&identity, hub.atproto_binding(&identity.local_id)) }))
            })
        }
        ("POST", ["identities", local_id, "atproto", "unbind"]) => {
            finish(hub.unbind_atproto(local_id), |identity| {
                ok_json(json!({ "identity": identity_with_atproto(&identity, hub.atproto_binding(&identity.local_id)) }))
            })
        }
        ("POST", ["enrollments", companion_id, "forget"]) => {
            finish(hub.forget_enrollment(companion_id), |_| ok_json(json!({ "forgotten": companion_id })))
        }
        ("GET", ["status"]) => {
            let enrollments: Vec<Value> = hub
                .enrollment_statuses()
                .into_iter()
                .map(|status| {
                    json!({
                        "companionId": status.companion_id,
                        "identityId": status.identity_local_id,
                        "origin": status.origin,
                        "waiting": status.waiting,
                        "pendingAttention": status.pending_attention,
                        "unresolvedWrites": status.unresolved_writes,
                    })
                })
                .collect();
            ApiResponse(200, ok_json(json!({ "enrollments": enrollments })))
        }
        _ => ApiResponse(404, problem_json("not_found", "no such operator API route")),
    }
}

fn identity_list(hub: &ConnectorHub) -> Vec<Value> {
    // One batched hub read (one store guard inside it), then pure JSON assembly. This
    // route once walked the store under its own guard and asked the hub per identity —
    // `atproto_binding` re-locked the same non-reentrant mutex on the same thread and
    // froze the entire hub (store held forever): every Connect, every operator
    // mutation, the page itself. Never re-enter the store from under a store guard.
    let inventory = hub.identity_inventory();
    let availability: std::collections::HashMap<String, bool> = hub
        .enrollment_inventory()
        .into_iter()
        .map(|(entry, available)| (entry.local_id.clone(), available))
        .collect();
    inventory
        .into_iter()
        .map(|(identity, atproto, count)| {
            let local_id = identity.local_id.clone();
            let mut value = identity_with_atproto(&identity, atproto);
            value["enrollmentCount"] = json!(count);
            value["sessionsAvailable"] = json!(availability.get(&local_id).copied().unwrap_or(true));
            value
        })
        .collect()
}

fn identity_json(identity: &crate::domain::identity::Identity) -> Value {
    json!({
        "localId": identity.local_id,
        "handle": identity.handle,
        "displayName": identity.display_name,
        "boundDid": identity.bound_did,
        "createdAt": identity.created_at,
    })
}

/// The identity view plus its atproto binding status: what is bound, where, and how old
/// the session is — never the access token, never the app password. Pure rendering: the
/// binding is fetched by the caller, so no store guard is ever held here.
fn identity_with_atproto(
    identity: &crate::domain::identity::Identity,
    atproto: Option<crate::application::hub::AtprotoBinding>,
) -> Value {
    let mut value = identity_json(identity);
    value["atproto"] = match atproto {
        Some(binding) => json!({
            "did": binding.did,
            "handle": binding.handle,
            "pds": binding.pds,
            "obtainedAt": binding.obtained_at,
        }),
        None => Value::Null,
    };
    value
}

/// Session STATUS only: whether the enrollment holds one — never the token value.
fn enrollment_json(entry: &crate::domain::identity::CompanionEntry, available: bool) -> Value {
    json!({
        "companionId": entry.companion_id,
        "origin": entry.origin,
        "participantRef": entry.participant_ref,
        "did": entry.did,
        "handle": entry.handle,
        "displayName": entry.display_name,
        "autoCheck": entry.auto_check,
        "sessionStatus": if available { "stored" } else { "missing" },
    })
}

fn finish<T>(result: Result<T, String>, render: impl FnOnce(T) -> Value) -> ApiResponse {
    match result {
        Ok(value) => ApiResponse(200, render(value)),
        Err(message) => {
            let (code, text) = match message.split_once(": ") {
                Some((code, text)) => (code.to_string(), text.to_string()),
                None => ("blocked".to_string(), message),
            };
            ApiResponse(200, blocked_json(&code, &text))
        }
    }
}

fn ok_json(data: Value) -> Value {
    let mut value = json!({ "status": "ok" });
    if let (Some(object), Some(payload)) = (value.as_object_mut(), data.as_object()) {
        for (key, item) in payload {
            if item.is_null() {
                continue;
            }
            object.insert(key.clone(), item.clone());
        }
    }
    value
}

fn blocked_json(code: &str, message: &str) -> Value {
    json!({ "status": "blocked", "problem": { "code": code, "message": message } })
}

fn problem_json(code: &str, message: &str) -> Value {
    blocked_json(code, message)
}

fn respond(writer: &mut TcpStream, status: u16, body: Value) -> std::io::Result<()> {
    let (content_type, bytes) = match body {
        Value::String(html) => ("text/html; charset=utf-8", html.into_bytes()),
        other => ("application/json", serde_json::to_vec(&other).unwrap_or_default()),
    };
    let reason = match status {
        200 => "OK",
        400 => "Bad Request",
        401 => "Unauthorized",
        404 => "Not Found",
        405 => "Method Not Allowed",
        413 => "Payload Too Large",
        _ => "Error",
    };
    let head = format!(
        "HTTP/1.1 {status} {reason}\r\nContent-Type: {content_type}\r\nContent-Length: {}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n",
        bytes.len()
    );
    writer.write_all(head.as_bytes())?;
    writer.write_all(&bytes)?;
    writer.flush()
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::Cursor;

    #[test]
    fn capped_reads_consume_one_line_at_a_time_and_refuse_oversize() {
        let mut reader = Cursor::new(b"first line\r\nsecond\r\n".to_vec());
        let mut line = String::new();
        assert_eq!(read_line_capped(&mut reader, &mut line, 64).unwrap(), Some(12));
        assert_eq!(line.trim_end(), "first line", "the CR of a CRLF line is trimming territory");
        assert_eq!(read_line_capped(&mut reader, &mut line, 64).unwrap(), Some(8));
        assert_eq!(line.trim_end(), "second");
        assert_eq!(read_line_capped(&mut reader, &mut line, 64).unwrap(), None, "clean end of stream");
        // An unterminated run longer than the cap is refused while arriving.
        let mut endless = Cursor::new(vec![b'x'; 4096]);
        assert!(matches!(read_line_capped(&mut endless, &mut line, 128), Err(CappedError::OverLimit)));
        // A final unterminated fragment still yields its bytes.
        let mut partial = Cursor::new(b"tail".to_vec());
        assert_eq!(read_line_capped(&mut partial, &mut line, 64).unwrap(), Some(4));
        assert_eq!(line, "tail");
    }
}
