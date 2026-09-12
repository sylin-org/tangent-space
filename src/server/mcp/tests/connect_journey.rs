//! The on-the-fly handshake (owner-directed realignment): `Connect` resolves the acting
//! identity by behavior (an explicit identity argument, or exactly one local identity,
//! for every intake alike), discovers the server, pops the operator page (this
//! process's own, or a recorded reachable one from state) at the per-identity sign-in
//! anchor when no atproto binding exists (never a silent unbound fallback), enrolls
//! bound only when no usable enrollment/session exists, and exits through arrival led
//! by the "You are … — session …" line (P2). The owner addendum adds the auto-resume: a
//! waiting connect finishes by itself when the operator signs in on the page, narrated
//! live over SSE with initiator labels (P5b), and repeated waiting connects coalesce to
//! one narration (P5a). CLI-shaped connects are stateless one-shots: a later call (or a
//! fresh process) re-runs the checks and completes (P3). All data is synthetic.

mod common;

use std::io::{Read, Write};
use std::net::{TcpListener, TcpStream};
use std::sync::mpsc::Receiver;
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

use serde_json::{json, Value};

use common::{BoundMode, FakeServer};
use tangent_connector::adapters::experience::UreqExperience;
use tangent_connector::adapters::store::StateStore;
use tangent_connector::application::bus::EventBus;
use tangent_connector::application::hub::{bind_anchor, registration_target, ConnectorHub, ToolOutcome};
use tangent_connector::application::ports::ExperiencePort;
use tangent_connector::domain::events::DomainEvent;
use tangent_connector::domain::identity::CallerId;
use tangent_connector::domain::intake::IntakeChannel;

fn workspace(label: &str, caller: CallerId) -> Arc<ConnectorHub> {
    let dir = std::env::temp_dir().join(format!("tangent-connector-connect-{}-{}", label, std::process::id()));
    let _ = std::fs::remove_dir_all(&dir);
    Arc::new(workspace_at(&dir, caller))
}

/// A hub over an existing directory (fresh state on disk), for stateless re-entry.
fn workspace_at(dir: &std::path::Path, caller: CallerId) -> ConnectorHub {
    std::fs::create_dir_all(dir).expect("temp dir");
    let events = Arc::new(EventBus::new());
    let port: Arc<dyn ExperiencePort> = Arc::new(UreqExperience::new());
    let store = StateStore::open(dir).expect("store");
    ConnectorHub::new(port, store, events, caller)
}

fn mcp_workspace(label: &str, client: &str) -> Arc<ConnectorHub> {
    workspace(label, CallerId(format!("mcp:{client}")))
}

/// A workspace holding exactly one already-bound identity, ready to connect.
fn bound_workspace(label: &str, server: &FakeServer) -> (Arc<ConnectorHub>, String) {
    server.add_account("ox_omega.bsky.example", "app-pass-1", "did:plc:ox");
    let hub = mcp_workspace(label, "codex-host");
    let identity = hub.create_identity("ox_omega", None).expect("identity");
    hub.bind_atproto(&identity.local_id, "ox_omega.bsky.example", "app-pass-1", Some(server.origin()))
        .expect("binding");
    (hub, identity.local_id)
}

fn connect(hub: &ConnectorHub, origin: &str) -> ToolOutcome {
    hub.invoke(IntakeChannel::Mcp, "Connect", &json!({ "serverUrl": origin }))
}

fn connect_as(hub: &ConnectorHub, channel: IntakeChannel, origin: &str, identity: Option<&str>) -> ToolOutcome {
    let mut arguments = json!({ "serverUrl": origin });
    if let Some(identity) = identity {
        arguments["identity"] = json!(identity);
    }
    hub.invoke(channel, "Connect", &arguments)
}

fn code_of(outcome: &ToolOutcome) -> String {
    outcome
        .structured
        .pointer("/problem/code")
        .and_then(Value::as_str)
        .unwrap_or_default()
        .to_string()
}

/// Restores the no-browser env var on drop, so parallel tests observe the prior world.
struct EnvGuard(String);
impl Drop for EnvGuard {
    fn drop(&mut self) {
        std::env::set_var("TANGENT_CONNECTOR_NO_BROWSER", &self.0);
    }
}

fn no_browser() -> EnvGuard {
    let guard = EnvGuard(std::env::var("TANGENT_CONNECTOR_NO_BROWSER").unwrap_or_default());
    std::env::set_var("TANGENT_CONNECTOR_NO_BROWSER", "1");
    guard
}

// ---------- the Connect branches ----------

#[test]
fn connect_resolves_enrolls_and_arrives_with_the_one_identity() {
    let server = FakeServer::start();
    let (hub, local_id) = bound_workspace("ok", &server);

    let outcome = connect(&hub, server.origin());
    assert!(!outcome.is_error, "text: {}", outcome.text);
    let context_id = outcome
        .structured
        .pointer("/connector/contextId")
        .and_then(Value::as_str)
        .expect("a context for subsequent calls")
        .to_string();
    assert!(!context_id.is_empty());
    // P2: the compact text LEADS with the identity + session line, and the structured
    // layer carries both fields.
    assert!(
        outcome.text.starts_with(&format!("You are ox_omega — session {context_id}")),
        "the You-are line leads: {}",
        outcome.text
    );
    assert!(
        outcome.text.contains("you are participating as"),
        "the orientation view follows: {}",
        outcome.text
    );
    assert_eq!(
        outcome.structured.pointer("/connector/identityHandle").and_then(Value::as_str),
        Some("ox_omega")
    );

    // The handshake enrolled bound and stored the session per enrollment.
    let enrollments = hub.enrollments_of(&local_id);
    assert_eq!(enrollments.len(), 1, "exactly one enrollment");
    assert_eq!(enrollments[0].did.as_deref(), Some("did:plc:ox"));
    assert_eq!(enrollments[0].origin, server.origin());
    assert!(hub.store().lock().unwrap().has_session(&enrollments[0].companion_id));

    // Wire discipline: discovery (both the pre-flight and the exchange's own), exactly
    // one PDS proof mint, exactly one token exchange, and arrival on the session.
    let requests = server.requests();
    assert!(requests.iter().any(|request| request.path == "/.well-known/tangent-mcp"), "discovery ran");
    assert_eq!(requests.iter().filter(|request| request.path.contains("getServiceAuth")).count(), 1);
    assert_eq!(requests.iter().filter(|request| request.path == "/mcp/token").count(), 1);
    let session = hub.store().lock().unwrap().session(&enrollments[0].companion_id).expect("session");
    assert!(
        requests
            .iter()
            .any(|request| request.path == "/api/v1/experience" && request.bearer == format!("Bearer {session}")),
        "arrival used the freshly enrolled session"
    );

    // A second connect finds the enrollment ready: arrival again, no new exchange, and
    // the P2 line still leads.
    let again = connect(&hub, server.origin());
    assert!(!again.is_error, "text: {}", again.text);
    assert!(again.text.starts_with("You are ox_omega — session "), "second connect: {}", again.text);
    assert_eq!(hub.enrollments_of(&local_id).len(), 1, "no duplicate enrollment");
    assert_eq!(
        server.requests().iter().filter(|request| request.path == "/mcp/token").count(),
        1,
        "no second proof exchange"
    );
}

/// P3, the CLI shape: one-shot processes with no in-process pendings. The first CLI
/// connect waits honestly; the operator signs in through ANOTHER process's hub (the
/// serve-run page); a FRESH CLI process then completes the handshake by itself — and
/// is idempotent on a third run.
#[test]
fn cli_shaped_connects_complete_statelessly_after_the_operator_signs_in() {
    let server = FakeServer::start();
    server.add_account("ox_omega.bsky.example", "app-pass-3", "did:plc:ox");
    let dir = std::env::temp_dir().join(format!("tangent-connector-connect-cli-stateless-{}", std::process::id()));
    let _ = std::fs::remove_dir_all(&dir);

    // CLI process #1: one identity, no binding, no operator page anywhere — the honest
    // start-operator instruction, never a hang and never an invented enrollment.
    let first = Arc::new(workspace_at(&dir, CallerId("cli".into())));
    let identity = first.create_identity("ox_omega", None).expect("identity");
    let _guard = no_browser();
    let waiting = connect_as(&first, IntakeChannel::Cli, server.origin(), None);
    assert!(waiting.is_error);
    assert_eq!(code_of(&waiting), "operator_page_unavailable");
    assert!(waiting.text.contains("start tangent-connector operator"), "text: {}", waiting.text);
    assert!(waiting.text.contains("sign in identity 'ox_omega'"), "text: {}", waiting.text);
    assert!(first.enrollments_of(&identity.local_id).is_empty(), "nothing enrolled while waiting");

    // The operator signs in through the serve-run page (modeled here by another
    // process's hub over the same state): the binding lands in state.json.
    let serving = workspace_at(&dir, CallerId("mcp:someone-else".into()));
    serving
        .bind_atproto(&identity.local_id, "ox_omega.bsky.example", "app-pass-3", Some(server.origin()))
        .expect("operator sign-in");

    // CLI process #2: a fresh one-shot re-runs discovery and the binding check, then
    // enrolls, arrives and announces identity + session — all by itself.
    let second = Arc::new(workspace_at(&dir, CallerId("cli".into())));
    let done = connect_as(&second, IntakeChannel::Cli, server.origin(), None);
    assert!(!done.is_error, "text: {}", done.text);
    let context_id = done
        .structured
        .pointer("/connector/contextId")
        .and_then(Value::as_str)
        .expect("a context")
        .to_string();
    assert!(done.text.starts_with(&format!("You are ox_omega — session {context_id}")), "text: {}", done.text);
    assert_eq!(
        done.structured.pointer("/connector/identityHandle").and_then(Value::as_str),
        Some("ox_omega")
    );
    let enrollments = second.enrollments_of(&identity.local_id);
    assert_eq!(enrollments.len(), 1);
    assert_eq!(enrollments[0].did.as_deref(), Some("did:plc:ox"));
    assert_eq!(
        server.requests().iter().filter(|request| request.path == "/mcp/token").count(),
        1,
        "exactly one proof exchange across the whole journey"
    );

    // A third run is idempotent: same session handle reused, no new exchange.
    let third = Arc::new(workspace_at(&dir, CallerId("cli".into())));
    let repeat = connect_as(&third, IntakeChannel::Cli, server.origin(), None);
    assert!(!repeat.is_error, "text: {}", repeat.text);
    assert!(repeat.text.starts_with(&format!("You are ox_omega — session {context_id}")), "text: {}", repeat.text);
    assert_eq!(
        server.requests().iter().filter(|request| request.path == "/mcp/token").count(),
        1,
        "still exactly one proof exchange"
    );
}

/// The honest selection question: several identities resolve nothing without an
/// explicit choice; the identity argument is the explicit pick, with an honest miss.
#[test]
fn several_identities_require_an_explicit_choice_and_the_argument_resolves() {
    let server = FakeServer::start();
    server.add_account("alpha.bsky.example", "app-pass-a", "did:plc:alpha");
    let hub = mcp_workspace("choice", "codex-host");
    let alpha = hub.create_identity("alpha", None).expect("identity");
    let _beta = hub.create_identity("beta", None).expect("identity");
    hub.bind_atproto(&alpha.local_id, "alpha.bsky.example", "app-pass-a", Some(server.origin()))
        .expect("binding");
    hub.set_operator_page_url("http://127.0.0.1:59998/");
    let _guard = no_browser();

    // No argument: the honest question lists both handles.
    let question = connect(&hub, server.origin());
    assert!(question.is_error);
    assert_eq!(code_of(&question), "identity_selection_required");
    assert!(question.text.contains("alpha") && question.text.contains("beta"), "text: {}", question.text);
    assert!(question.text.contains("which one is yours"), "text: {}", question.text);

    // The explicit argument resolves exactly that identity: alpha is bound and
    // completes; beta, unbound, waits honestly; a miss is a miss.
    let by_handle = connect_as(&hub, IntakeChannel::Mcp, server.origin(), Some("alpha"));
    assert!(!by_handle.is_error, "text: {}", by_handle.text);
    assert!(by_handle.text.starts_with("You are alpha — session "), "text: {}", by_handle.text);
    let beta_waits = connect_as(&hub, IntakeChannel::Mcp, server.origin(), Some("beta"));
    assert_eq!(code_of(&beta_waits), "operator_action_needed");
    assert!(beta_waits.text.contains("sign in identity 'beta'"), "text: {}", beta_waits.text);
    let missed = connect_as(&hub, IntakeChannel::Mcp, server.origin(), Some("gamma"));
    assert_eq!(code_of(&missed), "identity_selection_required");
    assert!(missed.text.contains("No local identity matches 'gamma'"), "text: {}", missed.text);
}

#[test]
fn connect_with_no_identities_points_at_creation() {
    let empty = mcp_workspace("empty", "codex-host");
    let none = connect(&empty, "https://tangent.example");
    assert!(none.is_error);
    assert_eq!(code_of(&none), "identity_selection_required");
    assert!(none.text.contains("No local identity exists yet"), "text: {}", none.text);
    assert!(none.text.contains("OpenRegistration"), "text: {}", none.text);
}

#[test]
fn connect_with_no_binding_pops_the_sign_in_page_and_enrolls_nothing() {
    let server = FakeServer::start();
    let hub = mcp_workspace("pop", "codex-host");
    let identity = hub.create_identity("ox_omega", None).expect("identity");
    hub.set_operator_page_url("http://127.0.0.1:59999/");
    let _guard = no_browser();

    let outcome = connect(&hub, server.origin());
    assert!(outcome.is_error, "the handshake must not pretend to have connected");
    assert_eq!(code_of(&outcome), "operator_action_needed");
    assert!(outcome.text.contains("operator action needed"), "text was: {}", outcome.text);
    assert!(outcome.text.contains("sign in identity 'ox_omega'"), "text was: {}", outcome.text);
    assert!(outcome.text.contains("connect again"), "text was: {}", outcome.text);

    // No enrollment side effects on this branch — and no unbound fallback either.
    assert!(hub.enrollments_of(&identity.local_id).is_empty(), "nothing enrolled");
    let requests = server.requests();
    assert!(requests.iter().any(|request| request.path == "/.well-known/tangent-mcp"), "discovery ran first");
    assert!(requests.iter().all(|request| !request.path.contains("getServiceAuth")), "no proof was minted");
    assert!(requests.iter().all(|request| request.path != "/mcp/token"), "no exchange happened");
    assert!(requests.iter().all(|request| !request.path.contains("createSession")), "no sign-in was invented");
    assert!(
        requests.iter().all(|request| request.path != "/api/v1/experience/identities/enroll"),
        "never a silent unbound fallback"
    );

    // The page URL is internal: it never renders into the model-facing response.
    assert!(!outcome.text.contains("59999"), "text was: {}", outcome.text);
    let rendered = serde_json::to_string(&outcome.structured).unwrap_or_default();
    assert!(!rendered.contains("59999"), "structured was: {rendered}");

    // R3: OpenRegistration now routes to this identity's bind page.
    assert_eq!(
        hub.registration_target_url().as_deref(),
        Some(format!("http://127.0.0.1:59999/{}", bind_anchor(&identity.local_id)).as_str())
    );
    assert_eq!(registration_target("http://127.0.0.1:1/", &bind_anchor("abc")), "http://127.0.0.1:1/bind/abc/atproto");

    // A looping model's repeated connect does not spawn another tab: honest already-opened.
    let second = connect(&hub, server.origin());
    assert_eq!(code_of(&second), "operator_action_needed");
    assert!(second.text.contains("already opened"), "text was: {}", second.text);
}

#[test]
fn connect_maps_a_server_without_a_proof_audience_to_an_honest_error() {
    let server = FakeServer::start_bound_with(BoundMode::AudienceUnconfigured);
    let (hub, local_id) = bound_workspace("fifty-three", &server);

    let outcome = connect(&hub, server.origin());
    assert!(outcome.is_error);
    assert_eq!(code_of(&outcome), "exchange_unavailable");
    assert!(outcome.text.contains("no proof audience configured"), "text was: {}", outcome.text);
    assert!(hub.enrollments_of(&local_id).is_empty(), "nothing enrolled");
    assert!(
        server.requests().iter().all(|request| request.path != "/mcp/token"),
        "the exchange was never reached"
    );
}

// ---------- P4: the recorded operator page, consulted from any process ----------

/// Spawns a real operator server on a loopback listener and returns its plain page URL
/// (no token — the owner correction) plus the bound address.
fn spawn_operator_page() -> (String, std::net::SocketAddr) {
    let listener = TcpListener::bind("127.0.0.1:0").expect("bind");
    let address = listener.local_addr().expect("address");
    let url = format!("http://{address}/");
    let serving = listener.try_clone().expect("clone listener");
    let throwaway = workspace("page-host", CallerId("operator".into()));
    std::thread::Builder::new()
        .name("operator-under-test".into())
        .spawn(move || tangent_connector::adapters::operator::serve(serving, throwaway))
        .expect("server thread");
    (url, address)
}

/// A Connect in a process that hosts no page of its own (the CLI one-shots) pops the
/// page URL the running long-running process recorded in state — after one cheap
/// reachability probe — at the per-identity bind anchor.
#[test]
fn a_pageless_connect_pops_the_recorded_reachable_page_at_the_bind_anchor() {
    let server = FakeServer::start();
    let (page_url, _address) = spawn_operator_page();
    // A CLI-caller hub whose state carries the recorded page URL, as the serve process
    // would have written it — nothing in memory for this process.
    let dir = std::env::temp_dir().join(format!("tangent-connector-connect-recorded-{}", std::process::id()));
    let _ = std::fs::remove_dir_all(&dir);
    let hub = Arc::new(workspace_at(&dir, CallerId("cli".into())));
    let identity = hub.create_identity("ox_omega", None).expect("identity");
    {
        let mut store = hub.store().lock().unwrap();
        store.set_operator_page_url(&page_url);
        store.save().expect("save");
    }
    let _guard = no_browser();

    let outcome = connect_as(&hub, IntakeChannel::Cli, server.origin(), None);
    assert_eq!(code_of(&outcome), "operator_action_needed");
    assert!(outcome.text.contains("page opened"), "the recorded page was popped: {}", outcome.text);
    assert!(outcome.text.contains("sign in identity 'ox_omega'"), "text: {}", outcome.text);
    // The guarded open targets exactly that page's bind route for this identity.
    assert_eq!(
        hub.sign_in_target_url(&identity.local_id).as_deref(),
        Some(format!("{page_url}{}", bind_anchor(&identity.local_id)).as_str())
    );
    // The CLI one-shot is honestly told a later connect completes the handshake (the
    // auto-resume belongs to the page-hosting process, not to this dead one-shot).
    assert!(outcome.text.contains("connect again"), "text: {}", outcome.text);
    assert!(hub.enrollments_of(&identity.local_id).is_empty(), "nothing enrolled while waiting");
}

/// A recorded page that no longer answers (an unclean shutdown left it behind) gets the
/// honest start-operator instruction instead of a dead tab.
#[test]
fn an_unreachable_recorded_page_gets_the_honest_start_operator_instruction() {
    let server = FakeServer::start();
    // Bind a listener, note its port, then drop it: a recorded URL whose page died.
    let dead_port = {
        let listener = TcpListener::bind("127.0.0.1:0").expect("bind");
        listener.local_addr().expect("address").port()
    };
    let dir = std::env::temp_dir().join(format!("tangent-connector-connect-deadpage-{}", std::process::id()));
    let _ = std::fs::remove_dir_all(&dir);
    let hub = Arc::new(workspace_at(&dir, CallerId("cli".into())));
    let identity = hub.create_identity("ox_omega", None).expect("identity");
    {
        let mut store = hub.store().lock().unwrap();
        store.set_operator_page_url(&format!("http://127.0.0.1:{dead_port}/"));
        store.save().expect("save");
    }
    let _guard = no_browser();

    let outcome = connect_as(&hub, IntakeChannel::Cli, server.origin(), None);
    assert_eq!(code_of(&outcome), "operator_page_unavailable");
    assert!(outcome.text.contains("start tangent-connector operator"), "text: {}", outcome.text);
    assert!(outcome.text.contains("sign in identity 'ox_omega'"), "text: {}", outcome.text);
    assert_eq!(hub.sign_in_target_url(&identity.local_id), None, "no target is offered for a dead page");
    assert!(hub.enrollments_of(&identity.local_id).is_empty(), "nothing enrolled");
}

// ---------- the auto-resume and the SSE feed (owner addendum) ----------

/// The socket address behind one of the fake server's origins.
fn server_addr(origin: &str) -> std::net::SocketAddr {
    use std::net::ToSocketAddrs as _;
    origin
        .trim_start_matches("http://")
        .to_socket_addrs()
        .expect("resolve")
        .next()
        .expect("an address")
}

fn http_round_trip(stream: &mut TcpStream, request: &str) -> String {
    stream.write_all(request.as_bytes()).expect("write request");
    stream.flush().expect("flush");
    let mut raw = Vec::new();
    stream.read_to_end(&mut raw).expect("read response");
    String::from_utf8_lossy(&raw).to_string()
}

fn wait_for(received: &Mutex<String>, needle: &str, timeout: Duration) {
    let deadline = Instant::now() + timeout;
    while Instant::now() < deadline {
        if received.lock().unwrap().contains(needle) {
            return;
        }
        std::thread::sleep(Duration::from_millis(25));
    }
    panic!("timed out waiting for {needle:?}; stream was: {}", received.lock().unwrap());
}

#[test]
fn a_waiting_connect_auto_resumes_when_the_operator_signs_in_with_live_sse() {
    let server = FakeServer::start();
    server.add_account("ox_omega.bsky.example", "app-pass-2", "did:plc:ox");
    let hub = mcp_workspace("resume", "codex-host");
    // The bind flow resolves identities against the fake, not the public resolvers.
    hub.set_atproto_oauth(tangent_connector::adapters::atproto_oauth::AtprotoOauth::with_origins(
        server.origin(),
        server.origin(),
    ));
    let identity = hub.create_identity("ox_omega", None).expect("identity");

    // The operator server this page-and-feed journey runs against. The page URL is a
    // plain loopback address — the owner correction removed the token.
    let listener = TcpListener::bind("127.0.0.1:0").expect("bind");
    let address = listener.local_addr().expect("address");
    {
        let hub = hub.clone();
        let serving = listener.try_clone().expect("clone listener");
        std::thread::Builder::new()
            .name("operator-under-test".into())
            .spawn(move || tangent_connector::adapters::operator::serve(serving, hub))
            .expect("server thread");
    }
    let page_url = format!("http://{address}/");
    hub.set_operator_page_url(&page_url);

    // A test client holds the SSE feed open BEFORE anything happens, reading frames
    // into a shared buffer until the journey completes.
    let received = Arc::new(Mutex::new(String::new()));
    {
        let received = received.clone();
        let address = address.clone();
        std::thread::spawn(move || {
            let mut stream = match TcpStream::connect(address) {
                Ok(stream) => stream,
                Err(_) => return,
            };
            if stream
                .write_all(format!("GET /api/events HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n").as_bytes())
                .is_err()
            {
                return;
            }
            let _ = stream.set_read_timeout(Some(Duration::from_millis(100)));
            let deadline = Instant::now() + Duration::from_secs(60);
            let mut buffer = [0u8; 8192];
            while Instant::now() < deadline {
                match stream.read(&mut buffer) {
                    Ok(0) => break,
                    Ok(count) => {
                        received.lock().unwrap().push_str(&String::from_utf8_lossy(&buffer[..count]));
                    }
                    Err(error) if error.kind() == std::io::ErrorKind::WouldBlock => continue,
                    Err(_) => break,
                }
            }
        });
    }
    // The feed is subscribed before its response head is written, so once the head is
    // visible no later event can be missed.
    wait_for(&received, "text/event-stream", Duration::from_secs(10));

    let _guard = no_browser();
    // The model connects; the identity has no binding yet, so the tool returns
    // waiting-for-operator honestly.
    let waiting = connect(&hub, server.origin());
    assert!(waiting.is_error);
    assert_eq!(code_of(&waiting), "operator_action_needed");
    wait_for(&received, "connect_waiting_for_operator", Duration::from_secs(10));
    assert!(hub.enrollments_of(&identity.local_id).is_empty(), "nothing enrolled while waiting");

    // The operator completes the sign-in on the connector-served bind page: the form
    // POST redirects to the fake authorization server, which (auto-approving) redirects
    // back to the operator page's loopback root with code+state; that callback response
    // returns only after the auto-resume has run: enrollment plus arrival, connector-
    // side, no model involved.
    let mut binder = TcpStream::connect(address).expect("connect");
    let payload = "handle=ox_omega.bsky.example".to_string();
    let started = http_round_trip(
        &mut binder,
        &format!(
            "POST /bind/{}/atproto HTTP/1.1
Host: 127.0.0.1
Content-Type: application/x-www-form-urlencoded
Content-Length: {}
Connection: close

{payload}",
            identity.local_id,
            payload.len()
        ),
    );
    assert!(started.starts_with("HTTP/1.1 302"), "the bind page redirects to the authorize URL: {started}");
    let authorize = started
        .lines()
        .find_map(|line| line.strip_prefix("Location: "))
        .expect("authorize location")
        .trim()
        .to_string();
    assert!(authorize.starts_with(&format!("{}/oauth/authorize", server.origin())), "authorize on the fake AS: {authorize}");
    let mut follower = TcpStream::connect(server_addr(server.origin())).expect("connect to AS");
    let authorize_path = authorize.strip_prefix(server.origin()).unwrap_or(&authorize);
    let redirected = http_round_trip(&mut follower, &format!("GET {authorize_path} HTTP/1.1
Host: as
Connection: close

"));
    assert!(redirected.starts_with("HTTP/1.1 302"), "the AS redirects to the loopback callback: {redirected}");
    let callback = redirected
        .lines()
        .find_map(|line| line.strip_prefix("Location: "))
        .expect("callback location")
        .trim()
        .to_string();
    assert!(callback.starts_with(&format!("http://{address}/?")), "the callback lands on the operator page root: {callback}");
    let mut finisher = TcpStream::connect(address).expect("connect");
    let callback_path = callback.strip_prefix(&format!("http://{address}")).unwrap_or(&callback);
    let bound = http_round_trip(&mut finisher, &format!("GET {callback_path} HTTP/1.1
Host: 127.0.0.1
Connection: close

"));
    assert!(bound.starts_with("HTTP/1.1 200") && bound.contains("Bound as"), "the callback reports the bind: {bound}");

    // The pending connect finished by itself: one bound enrollment, its session stored.
    let enrollments = hub.enrollments_of(&identity.local_id);
    assert_eq!(enrollments.len(), 1, "the resume enrolled exactly once");
    assert_eq!(enrollments[0].did.as_deref(), Some("did:plc:ox"));
    assert!(hub.store().lock().unwrap().has_session(&enrollments[0].companion_id));
    let session = hub.store().lock().unwrap().session(&enrollments[0].companion_id).expect("session");
    assert!(
        server.requests()
            .iter()
            .any(|request| request.path == "/api/v1/experience" && request.bearer == format!("Bearer {session}")),
        "the resume arrived on the freshly enrolled session"
    );

    // The feed narrated the whole story, in order, with the initiator on every line
    // (P5b): the model started it; the operator's page completed it.
    wait_for(&received, "connect_arrived", Duration::from_secs(10));
    let stream_text = received.lock().unwrap().clone();
    let position = |needle: &str| stream_text.find(needle).unwrap_or_else(|| panic!("{needle} missing: {stream_text}"));
    let started = position("\"kind\":\"connect_started\"");
    let resolved = position("\"kind\":\"connect_resolved\"");
    let waiting_at = position("\"kind\":\"connect_waiting_for_operator\"");
    let completed = position("\"kind\":\"connect_operator_completed\"");
    let enrolled = position("\"kind\":\"connect_enrolled\"");
    let arrived = position("\"kind\":\"connect_arrived\"");
    assert!(started < resolved && resolved < waiting_at && waiting_at < completed && completed < enrolled && enrolled < arrived);
    assert!(stream_text.contains("\"initiator\":\"model (via codex-host)\""), "model lines carry the client: {stream_text}");
    assert!(stream_text.contains("\"initiator\":\"operator (page)\""), "the resume carries the page: {stream_text}");

    // The feed never carries a secret: no app password, no session or proof values.
    assert!(!stream_text.contains("app-pass-2"), "the app password never rides the feed");
    assert!(!stream_text.contains("ts_bound_") && !stream_text.contains("sat_") && !stream_text.contains("proof_"), "no session or proof values ride the feed");

    // R3: with the sign-in satisfied, OpenRegistration routes back to identity creation.
    assert_eq!(
        hub.registration_target_url().as_deref(),
        Some(format!("{page_url}#create-identity").as_str())
    );

    // The model's next connect simply finds everything ready.
    let next = connect(&hub, server.origin());
    assert!(!next.is_error, "text: {}", next.text);
    assert_eq!(hub.enrollments_of(&identity.local_id).len(), 1, "still exactly one enrollment");
}

// ---------- the looping-model and frozen-hub guarantees ----------

/// Runs one hub call on its own thread and enforces a deadline: a call that would hang
/// (the live deadlock shape) fails the test instead of freezing the suite.
fn bounded<T: Send + 'static>(run: impl FnOnce() -> T + Send + 'static, timeout: Duration) -> T {
    let (sender, receiver) = std::sync::mpsc::channel();
    std::thread::spawn(move || {
        let _ = sender.send(run());
    });
    receiver.recv_timeout(timeout).unwrap_or_else(|_| panic!("the call did not answer within {timeout:?}"))
}

/// One HTTP request/response with a hard read deadline: a handler that never answers
/// (the frozen-page shape) surfaces as an error instead of a hang.
fn http_round_trip_bounded(stream: &mut TcpStream, request: &str, timeout: Duration) -> Result<String, String> {
    stream.write_all(request.as_bytes()).map_err(|error| format!("write failed: {error}"))?;
    stream.flush().map_err(|error| format!("flush failed: {error}"))?;
    stream.set_read_timeout(Some(timeout)).map_err(|error| format!("deadline refused: {error}"))?;
    let mut raw = Vec::new();
    stream
        .read_to_end(&mut raw)
        .map(|_| String::from_utf8_lossy(&raw).to_string())
        .map_err(|_| format!("no answer within {timeout:?} — the operator API is frozen"))
}

/// A looping model is an expected client: while one connect waits for the operator,
/// any number of repeated Connect calls must keep answering promptly and honestly
/// (already-opened page, never a new spawn, never a hang) — and (P5a) the feed hears
/// the waiting narration exactly once, not the full trio per retry.
#[test]
fn repeated_connects_while_waiting_for_the_operator_all_answer_promptly() {
    let server = FakeServer::start();
    let hub = mcp_workspace("loop", "codex-host");
    let identity = hub.create_identity("ox_omega", None).expect("identity");
    hub.set_operator_page_url("http://127.0.0.1:59999/");
    let _guard = no_browser();
    let events = hub.events().subscribe();

    let hub_caller = hub.clone();
    let origin = server.origin().to_string();
    let first = bounded(move || connect(&hub_caller, &origin), Duration::from_secs(5));
    assert_eq!(code_of(&first), "operator_action_needed");
    assert!(first.text.contains("page opened"), "first connect narrates the pop: {}", first.text);

    for attempt in 0..6 {
        let hub_caller = hub.clone();
        let origin = server.origin().to_string();
        let again = bounded(move || connect(&hub_caller, &origin), Duration::from_secs(5));
        assert_eq!(code_of(&again), "operator_action_needed", "attempt {attempt}: {}", again.text);
        assert!(again.text.contains("already opened"), "attempt {attempt} is honest: {}", again.text);
        assert!(!again.text.contains("59999"), "the page URL never renders: {}", again.text);
    }

    // The store stays lockable after the whole loop, and nothing enrolled meanwhile.
    assert_eq!(hub.identities().len(), 1);
    assert!(hub.enrollments_of(&identity.local_id).is_empty(), "still nothing enrolled while waiting");
    assert_narrated_once(&events, &["connect_started", "connect_resolved", "connect_waiting_for_operator"]);
}

/// P5a, asserted directly on emitted events: three consecutive waiting Connects produce
/// ONE waiting narration. The feed line also carries the initiator (P5b) — the model
/// via its client here, the operator (CLI) for command-line connects.
#[test]
fn three_waiting_connects_narrate_the_wait_once_with_the_initiator() {
    let server = FakeServer::start();
    let hub = mcp_workspace("coalesce", "codex-host");
    let _identity = hub.create_identity("ox_omega", None).expect("identity");
    hub.set_operator_page_url("http://127.0.0.1:59997/");
    let _guard = no_browser();
    let events = hub.events().subscribe();

    for _ in 0..3 {
        let outcome = connect(&hub, server.origin());
        assert_eq!(code_of(&outcome), "operator_action_needed");
    }
    let frames = drain_serialized(&events);
    assert_eq!(count_kind(&frames, "connect_waiting_for_operator"), 1, "one waiting narration: {frames:?}");
    assert_eq!(count_kind(&frames, "connect_started"), 1, "the trio does not re-narrate: {frames:?}");
    assert_eq!(count_kind(&frames, "connect_resolved"), 1, "the trio does not re-narrate: {frames:?}");
    let waiting = frames
        .iter()
        .find(|frame| frame.contains("connect_waiting_for_operator"))
        .expect("the waiting narration");
    assert!(waiting.contains("\"initiator\":\"model (via codex-host)\""), "waiting frame was: {waiting}");

    // The CLI intake labels its own connect lines "operator (CLI)".
    let cli = workspace("coalesce-cli", CallerId("cli".into()));
    let _cli_identity = cli.create_identity("ox_cli", None).expect("identity");
    cli.set_operator_page_url("http://127.0.0.1:59996/");
    let cli_events = cli.events().subscribe();
    let outcome = connect_as(&cli, IntakeChannel::Cli, server.origin(), None);
    assert_eq!(code_of(&outcome), "operator_action_needed");
    let frames = drain_serialized(&cli_events);
    let waiting = frames
        .iter()
        .find(|frame| frame.contains("connect_waiting_for_operator"))
        .expect("the CLI waiting narration");
    assert!(waiting.contains("\"initiator\":\"operator (CLI)\""), "waiting frame was: {waiting}");
}

fn drain_serialized(receiver: &Receiver<DomainEvent>) -> Vec<String> {
    let mut frames = Vec::new();
    while let Ok(event) = receiver.try_recv() {
        frames.push(serde_json::to_string(&event).unwrap_or_default());
    }
    frames
}

fn count_kind(frames: &[String], kind: &str) -> usize {
    frames.iter().filter(|frame| frame.contains(&format!("\"kind\":\"{kind}\""))).count()
}

fn assert_narrated_once(receiver: &Receiver<DomainEvent>, kinds: &[&str]) {
    let frames = drain_serialized(receiver);
    for kind in kinds {
        assert_eq!(count_kind(&frames, kind), 1, "{kind} narrated exactly once: {frames:?}");
    }
}

/// The live-deadlock regression (journal: Connect → pending → page refresh → frozen hub).
/// The operator page's identity fetch (`GET /api/identities`, which every tab runs at
/// boot and after every click) must answer while a connect is pending, and operator
/// mutations plus a repeated Connect must keep answering too. Before the fix, the page's
/// identity list held the store lock and re-locked it per identity, freezing the hub.
#[test]
fn a_pending_connect_never_freezes_the_page_or_operator_mutations() {
    let server = FakeServer::start();
    let hub = mcp_workspace("frozen", "codex-host");
    let _identity = hub.create_identity("ox_omega", None).expect("identity");

    let listener = TcpListener::bind("127.0.0.1:0").expect("bind");
    let address = listener.local_addr().expect("address");
    {
        let hub = hub.clone();
        let serving = listener.try_clone().expect("clone listener");
        std::thread::Builder::new()
            .name("operator-under-test".into())
            .spawn(move || tangent_connector::adapters::operator::serve(serving, hub))
            .expect("server thread");
    }
    hub.set_operator_page_url(&format!("http://{address}/"));
    let _guard = no_browser();

    // Connect #1: waiting for the operator, honest return.
    let hub_caller = hub.clone();
    let origin = server.origin().to_string();
    let waiting = bounded(move || connect(&hub_caller, &origin), Duration::from_secs(5));
    assert_eq!(code_of(&waiting), "operator_action_needed");

    // The popped tab boots and refreshes: the identity list must answer (the freeze point).
    let mut page = TcpStream::connect(address).expect("connect page");
    let listed = http_round_trip_bounded(
        &mut page,
        "GET /api/identities HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n",
        Duration::from_secs(5),
    )
    .expect("the page's identity fetch answered");
    assert!(listed.starts_with("HTTP/1.1 200"), "identity list was: {listed}");
    assert!(listed.contains("ox_omega"), "the list names the identity: {listed}");
    assert!(listed.contains("\"atproto\":null"), "the binding status renders: {listed}");

    // The operator's mutation on the frozen-looking page: create still works.
    let mut creating = TcpStream::connect(address).expect("connect create");
    let payload = json!({ "handle": "ox_second", "displayName": null }).to_string();
    let created = http_round_trip_bounded(
        &mut creating,
        &format!(
            "POST /api/identities HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Type: application/json\r\nContent-Length: {}\r\nConnection: close\r\n\r\n{payload}",
            payload.len()
        ),
        Duration::from_secs(5),
    )
    .expect("operator.create_identity answered while a connect was pending");
    assert!(created.contains("\"status\":\"ok\""), "create was: {created}");

    // The same mutation through the hub directly, and the polling client's next Connect.
    let hub_caller = hub.clone();
    let second_identity = bounded(move || hub_caller.create_identity("ox_third", None), Duration::from_secs(5));
    assert!(second_identity.is_ok(), "hub create while pending: {:?}", second_identity.err());
    // Several identities now exist, so the repeated Connect names its identity
    // explicitly (the honest explicit path) — it must still answer promptly.
    let hub_caller = hub.clone();
    let origin = server.origin().to_string();
    let again = bounded(move || connect_as(&hub_caller, IntakeChannel::Mcp, &origin, Some("ox_omega")), Duration::from_secs(5));
    assert_eq!(code_of(&again), "operator_action_needed");
    assert!(again.text.contains("already opened"), "the repeated connect stays honest: {}", again.text);
}

#[test]
fn the_sse_feed_caps_concurrent_clients() {
    let hub = mcp_workspace("sse-cap", "codex-host");
    let listener = TcpListener::bind("127.0.0.1:0").expect("bind");
    let address = listener.local_addr().expect("address");
    {
        let hub = hub.clone();
        let serving = listener.try_clone().expect("clone listener");
        std::thread::Builder::new()
            .name("operator-under-test".into())
            .spawn(move || tangent_connector::adapters::operator::serve(serving, hub))
            .expect("server thread");
    }

    // Four holders fill the cap; each is a live stream (head seen = counted). The feed
    // connects plainly — no token (the owner correction).
    let mut holders: Vec<TcpStream> = Vec::new();
    for _ in 0..4 {
        let mut stream = TcpStream::connect(address).expect("connect");
        stream
            .write_all("GET /api/events HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n".as_bytes())
            .expect("request");
        let _ = stream.set_read_timeout(Some(Duration::from_secs(5)));
        let mut buffer = [0u8; 256];
        let read = stream.read(&mut buffer).expect("head bytes");
        assert!(String::from_utf8_lossy(&buffer[..read]).contains("text/event-stream"), "the feed answered");
        holders.push(stream);
    }

    // The fifth is refused honestly — the cap is small by design.
    let mut fifth = TcpStream::connect(address).expect("connect");
    let beyond = http_round_trip(&mut fifth, "GET /api/events HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n");
    assert!(beyond.starts_with("HTTP/1.1 503"), "beyond the cap was: {beyond}");
    assert!(beyond.contains("sse_clients_busy"), "beyond was: {beyond}");
    drop(holders);
}
