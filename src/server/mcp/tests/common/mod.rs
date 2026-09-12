//! Shared test support: a scripted fake Tangent experience server speaking just enough HTTP
//! for ureq, with per-request recording for assertions. All data is synthetic and labelled so.
//! The placeholder `ORIGIN` in scripted payloads is replaced with the listener's real origin,
//! because the connector validates references against the exact canonical origin.

use std::collections::{HashMap, HashSet};
use std::io::{BufRead, BufReader, Read, Write};
use std::net::{TcpListener, TcpStream};
use std::sync::mpsc::Receiver;
use std::sync::{Arc, Mutex};

use serde_json::{json, Value};

#[allow(dead_code)]
pub const LUMEN_CREDENTIAL: &str = "ts_lumen_test_credential_000000000000000000";
#[allow(dead_code)]
pub const REVOKED_CREDENTIAL: &str = "ts_revoked_test_credential_0000000000000";

pub fn origin(listener: &TcpListener) -> String {
    format!("http://127.0.0.1:{}", listener.local_addr().unwrap().port())
}

#[derive(Debug, Clone)]
#[allow(dead_code)]
pub struct Received {
    pub method: String,
    pub path: String,
    pub bearer: String,
    pub body: Value,
}

/// The fake server's proof audience (a synthetic fixture service DID).
pub const AUDIENCE: &str = "did:plc:fixture-tangent-server";

/// One synthetic atproto account the fake PDS knows.
#[derive(Debug, Clone)]
pub struct FakeAccount {
    pub handle: String,
    pub password: String,
    pub did: String,
    /// When set, createSession still succeeds but getServiceAuth rejects the session —
    /// the "PDS session expired, re-bind" scenario.
    pub expiring: bool,
}

/// Script modes of the bound-exchange surface.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
#[allow(dead_code)]
pub enum BoundMode {
    /// The full recipe works: discovery → getServiceAuth → /mcp/token.
    #[default]
    Ready,
    /// Discovery answers 503 `exchange_unconfigured` (no proof audience).
    AudienceUnconfigured,
    /// Discovery advertises a different exchange method.
    WrongMethod,
    /// Discovery's audience carries characters that would inject query structure if
    /// the client failed to percent-encode.
    TamperedAudience,
    /// `/mcp/token` rejects every proof with 401 `invalid_service_proof`.
    RejectProofs,
    /// `/mcp/token` answers 403 `participant_suspended`.
    Suspended,
}

/// Shared mutable state of the bound-exchange surface.
#[derive(Clone)]
pub struct BoundScript {
    pub mode: BoundMode,
    accounts: Arc<Mutex<Vec<FakeAccount>>>,
    proofs: Arc<Mutex<std::collections::HashMap<String, bool>>>,
    counter: Arc<Mutex<u32>>,
}

impl BoundScript {
    fn new(mode: BoundMode) -> Self {
        Self {
            mode,
            accounts: Arc::new(Mutex::new(Vec::new())),
            proofs: Arc::new(Mutex::new(std::collections::HashMap::new())),
            counter: Arc::new(Mutex::new(0)),
        }
    }
}

/// A scripted experience API. Responses are synthetic; the post registry gives the
/// crash-retry scenario its statefulness, and the enrollment registry implements the W2
/// contract's unbound-enrollment exchange (idempotent per client localId). The same
/// listener also plays the bound-exchange roles: the Tangent server's discovery document
/// and `/mcp/token`, and the account's PDS (`createSession` / `getServiceAuth`).
#[allow(dead_code)]
pub struct FakeServer {
    pub requests: Arc<Mutex<Vec<Received>>>,
    posts: Arc<Mutex<HashMap<String, u32>>>,
    enrollments: Arc<Mutex<HashSet<String>>>,
    unbound_disabled: bool,
    bound: BoundScript,
    origin: String,
}

impl FakeServer {
    pub fn start() -> Self {
        Self::start_with(false, BoundMode::Ready)
    }

    /// A server with the unbound-enrollment setting switched off.
    #[allow(dead_code)]
    pub fn start_with_unbound_disabled() -> Self {
        Self::start_with(true, BoundMode::Ready)
    }

    /// A server whose bound-exchange surface is in the given mode.
    #[allow(dead_code)]
    pub fn start_bound_with(mode: BoundMode) -> Self {
        Self::start_with(false, mode)
    }

    fn start_with(unbound_disabled: bool, bound_mode: BoundMode) -> Self {
        let listener = TcpListener::bind("127.0.0.1:0").expect("bind");
        let url = origin(&listener);
        let requests = Arc::new(Mutex::new(Vec::new()));
        let posts = Arc::new(Mutex::new(HashMap::new()));
        let enrollments = Arc::new(Mutex::new(HashSet::new()));
        let bound = BoundScript::new(bound_mode);
        let server = Self {
            requests: requests.clone(),
            posts: posts.clone(),
            enrollments: enrollments.clone(),
            unbound_disabled,
            bound: bound.clone(),
            origin: url.clone(),
        };
        std::thread::Builder::new()
            .name("fake-experience".into())
            .spawn(move || {
                for stream in listener.incoming() {
                    let Ok(stream) = stream else { break };
                    let requests = requests.clone();
                    let posts = posts.clone();
                    let enrollments = enrollments.clone();
                    let bound = bound.clone();
                    let origin = url.clone();
                    std::thread::spawn(move || {
                        serve_connection(stream, requests, posts, enrollments, unbound_disabled, bound, origin)
                    });
                }
            })
            .expect("server thread");
        server
    }

    /// Registers a synthetic atproto account the fake PDS will accept at createSession.
    #[allow(dead_code)]
    pub fn add_account(&self, handle: &str, password: &str, did: &str) {
        self.bound.accounts.lock().unwrap().push(FakeAccount {
            handle: handle.to_string(),
            password: password.to_string(),
            did: did.to_string(),
            expiring: false,
        });
    }

    /// Like [`FakeServer::add_account`], but the minted PDS session is already expired.
    #[allow(dead_code)]
    pub fn add_expiring_account(&self, handle: &str, password: &str, did: &str) {
        self.bound.accounts.lock().unwrap().push(FakeAccount {
            handle: handle.to_string(),
            password: password.to_string(),
            did: did.to_string(),
            expiring: true,
        });
    }

    pub fn origin(&self) -> &str {
        &self.origin
    }

    pub fn requests(&self) -> Vec<Received> {
        self.requests.lock().unwrap().clone()
    }

    /// How many times the scripted Topic accepted a write for this request id.
    #[allow(dead_code)]
    pub fn dispatches(&self, request_id: &str) -> u32 {
        self.posts.lock().unwrap().get(request_id).copied().unwrap_or(0)
    }

    /// The client localIds the enrollment endpoint currently knows.
    #[allow(dead_code)]
    pub fn enrolled_local_ids(&self) -> Vec<String> {
        self.enrollments.lock().unwrap().iter().cloned().collect()
    }
}

fn serve_connection(
    stream: TcpStream,
    requests: Arc<Mutex<Vec<Received>>>,
    posts: Arc<Mutex<HashMap<String, u32>>>,
    enrollments: Arc<Mutex<HashSet<String>>>,
    unbound_disabled: bool,
    bound: BoundScript,
    origin: String,
) {
    let mut reader = BufReader::new(match stream.try_clone() {
        Ok(clone) => clone,
        Err(_) => return,
    });
    let mut writer = stream;
    loop {
        let mut line = String::new();
        if reader.read_line(&mut line).unwrap_or(0) == 0 {
            return;
        }
        let mut parts = line.trim().split_whitespace();
        let method = parts.next().unwrap_or_default().to_string();
        let path = parts.next().unwrap_or_default().to_string();
        let mut content_length = 0usize;
        let mut bearer = String::new();
        loop {
            let mut header = String::new();
            if reader.read_line(&mut header).unwrap_or(0) == 0 {
                return;
            }
            let header = header.trim();
            if header.is_empty() {
                break;
            }
            if let Some(value) = header.to_ascii_lowercase().strip_prefix("content-length:") {
                content_length = value.trim().parse().unwrap_or(0);
            }
            if let Some(value) = header.strip_prefix("Authorization:") {
                bearer = value.trim().to_string();
            }
        }
        let mut body_bytes = vec![0u8; content_length];
        if content_length > 0 && reader.read_exact(&mut body_bytes).is_err() {
            return;
        }
        let body: Value = serde_json::from_slice(&body_bytes).unwrap_or(Value::Null);
        requests.lock().unwrap().push(Received {
            method: method.clone(),
            path: path.clone(),
            bearer: bearer.clone(),
            body: body.clone(),
        });
        if bearer.ends_with(REVOKED_CREDENTIAL) {
            let _ = writer.write_all(b"HTTP/1.1 401 Unauthorized\r\nContent-Type: application/json\r\nContent-Length: 47\r\nConnection: keep-alive\r\n\r\n{\"error\":\"participant_authentication_required\"}");
            let _ = writer.flush();
            continue;
        }
        match respond(&method, &path, &body, &bearer, &posts, &enrollments, unbound_disabled, &bound, &origin) {
            Script::Body(status, payload) => {
                let text = serde_json::to_string(&payload).unwrap().replace("ORIGIN", &origin);
                let reason = match status {
                    200 => "OK",
                    401 => "Unauthorized",
                    409 => "Conflict",
                    _ => "Status",
                };
                let _ = writer.write_all(
                    format!(
                        "HTTP/1.1 {} {}\r\nContent-Type: application/json\r\nContent-Length: {}\r\nConnection: keep-alive\r\n\r\n",
                        status,
                        reason,
                        text.len()
                    )
                    .as_bytes(),
                );
                let _ = writer.write_all(text.as_bytes());
                let _ = writer.flush();
            }
            // A lost response: the connection dies without an answer.
            Script::Lost => return,
        }
    }
}

/// What the scripted server answers: a status plus JSON body, or a dropped connection.
enum Script {
    Body(u16, Value),
    Lost,
}

fn respond(
    method: &str,
    path: &str,
    body: &Value,
    bearer: &str,
    posts: &Arc<Mutex<HashMap<String, u32>>>,
    enrollments: &Arc<Mutex<HashSet<String>>>,
    unbound_disabled: bool,
    bound: &BoundScript,
    origin: &str,
) -> Script {
    let clean = path.split('?').next().unwrap_or(path);
    match (method, clean) {
        // ---- the bound-exchange surface (discovery + PDS + /mcp/token) ----
        ("GET", "/.well-known/tangent-mcp") => match bound.mode {
            BoundMode::AudienceUnconfigured => Script::Body(503, json!({ "error": "exchange_unconfigured" })),
            BoundMode::WrongMethod => Script::Body(200, json!({
                "serviceProof": { "method": "com.atproto.simplespace.checkUserAccess", "audience": AUDIENCE },
            })),
            BoundMode::TamperedAudience => Script::Body(200, json!({
                "serviceProof": { "method": "local.tangent.mcp.exchange", "audience": "did:plc:fixture&lxm=evil&exp=99999999999" },
            })),
            _ => Script::Body(200, json!({
                "protocolVersion": "2026-07-28",
                "authenticationProfile": "atproto_service_proof_exchange",
                "standardMcpOAuthAuthorizationSupport": false,
                "serviceProof": {
                    "method": "local.tangent.mcp.exchange",
                    "audience": AUDIENCE,
                    "algorithms": ["ES256", "ES256K"],
                    "transport": "authorization_header",
                    "endpoint": format!("{origin}/mcp/token"),
                },
                "endpoints": { "mcp": format!("{origin}/mcp"), "token": format!("{origin}/mcp/token") },
            })),
        },
        // The fake PDS sign-in: identifier + app password → PDS session.
        ("POST", "/xrpc/com.atproto.server.createSession") => {
            let identifier = body
                .get("identifier")
                .and_then(Value::as_str)
                .unwrap_or_default()
                .trim()
                .trim_start_matches('@')
                .to_ascii_lowercase();
            let password = body.get("password").and_then(Value::as_str).unwrap_or_default();
            let account = bound
                .accounts
                .lock()
                .unwrap()
                .iter()
                .rev()
                .find(|account| account.handle.to_ascii_lowercase() == identifier)
                .cloned();
            match account {
                Some(account) if account.password == password => {
                    let port = origin.rsplit(':').next().unwrap_or("0");
                    let access = if account.expiring {
                        format!("sat_expired_{port}_{}", account.handle)
                    } else {
                        format!("sat_{port}_{}", account.handle)
                    };
                    Script::Body(200, json!({
                        "did": account.did,
                        "handle": account.handle,
                        "accessJwt": access,
                        "refreshJwt": format!("rt_{port}"),
                        "didDoc": {
                            "id": account.did,
                            "service": [ { "id": "#atproto_pds", "type": "AtprotoPds", "serviceEndpoint": origin } ]
                        }
                    }))
                }
                _ => Script::Body(401, json!({ "error": "AuthenticationRequired", "message": "Invalid identifier or password" })),
            }
        }
        // The fake PDS service-auth mint: session bearer, aud/lxm/exp query discipline.
        ("GET", "/xrpc/com.atproto.server.getServiceAuth") => {
            let token = bearer.strip_prefix("Bearer ").unwrap_or_default();
            if !token.starts_with("sat_") {
                return Script::Body(401, json!({ "error": "InvalidToken", "message": "authentication required" }));
            }
            if token.starts_with("sat_expired") {
                return Script::Body(401, json!({ "error": "ExpiredToken", "message": "token has expired" }));
            }
            let query = path.split_once('?').map(|(_, query)| query.to_string()).unwrap_or_default();
            let param = |name: &str| -> Option<String> {
                query.split('&').find_map(|pair| pair.strip_prefix(&format!("{name}=")).map(percent_decode))
            };
            match param("lxm").as_deref() {
                Some("local.tangent.mcp.exchange") => {}
                other => {
                    return Script::Body(
                        400,
                        json!({ "error": "InvalidRequest", "message": format!("unsupported lxm {other:?}") }),
                    )
                }
            }
            match param("aud") {
                Some(audience) if audience.starts_with("did:") => {}
                _ => return Script::Body(400, json!({ "error": "InvalidRequest", "message": "aud must be a DID" })),
            }
            match param("exp").and_then(|value| value.parse::<i64>().ok()) {
                // The server's accepted window: a near-future expiry around now+120 s.
                Some(exp) if (now_secs()..=now_secs() + 130).contains(&exp) => {}
                _ => return Script::Body(400, json!({ "error": "InvalidRequest", "message": "exp outside the accepted window" })),
            }
            let port = origin.rsplit(':').next().unwrap_or("0");
            let mut counter = bound.counter.lock().unwrap();
            *counter += 1;
            let proof = format!("proof_{port}_{counter}");
            bound.proofs.lock().unwrap().insert(proof.clone(), false);
            Script::Body(200, json!({ "token": proof }))
        }
        // The proof exchange: one-time proof bearer → Tangent session.
        ("POST", "/mcp/token") => {
            let provided = bearer.strip_prefix("Bearer ").unwrap_or_default().to_string();
            if !provided.starts_with("proof_") {
                return Script::Body(401, json!({ "error": "invalid_service_proof" }));
            }
            if bound.mode == BoundMode::Suspended {
                return Script::Body(403, json!({ "error": "participant_suspended" }));
            }
            if bound.mode == BoundMode::RejectProofs {
                return Script::Body(401, json!({ "error": "invalid_service_proof" }));
            }
            let mut proofs = bound.proofs.lock().unwrap();
            match proofs.get_mut(&provided) {
                Some(spent) if !*spent => {
                    *spent = true;
                    let port = origin.rsplit(':').next().unwrap_or("0");
                    let suffix = provided.rsplit('_').next().unwrap_or("0");
                    let name = body.get("name").and_then(Value::as_str).unwrap_or("mcp");
                    let grants = body
                        .get("grants")
                        .cloned()
                        .unwrap_or_else(|| json!(["welcome", "read", "post"]));
                    Script::Body(200, json!({
                        "profile": "atproto_service_proof_exchange",
                        "credential": {
                            "participantId": format!("prt_bound_{port}_{suffix}"),
                            "name": name,
                            "grants": grants,
                            "createdAt": "2026-09-11T00:00:00Z",
                            "expiresAt": "2026-09-18T00:00:00Z",
                            "revokedAt": null,
                        },
                        "token": format!("ts_bound_{port}_{suffix}"),
                    }))
                }
                // A consumed or unknown proof: the replay denial.
                _ => Script::Body(401, json!({ "error": "invalid_service_proof" })),
            }
        }
        // ---- the experience API ----
        ("GET", "/api/v1/experience") => Script::Body(200, arrival()),
        ("GET", "/api/v1/experience/tangents") => Script::Body(200, tangents()),
        ("GET", "/api/v1/experience/tangents/home/topics") => Script::Body(200, topics()),
        ("GET", "/api/v1/experience/topics/lounge") => Script::Body(200, topic_window()),
        ("GET", "/api/v1/experience/updates") => Script::Body(200, updates()),
        // The W2-contract enrollment exchange: pre-credential POST, every outcome HTTP 200.
        // Tokens are unique per server (port) so two-origin tests can tell bearers apart.
        ("POST", "/api/v1/experience/identities/enroll") => {
            let local_id = body.pointer("/client/localId").and_then(Value::as_str).unwrap_or_default().to_string();
            let requested_handle = body.pointer("/client/handle").and_then(Value::as_str).unwrap_or("anonymous").to_string();
            let port = origin.rsplit(':').next().unwrap_or("0");
            if unbound_disabled {
                return Script::Body(200, json!({
                    "status": "blocked",
                    "problem": { "code": "unbound_enrollment_disabled", "message": "this server does not accept unbound enrollment" }
                }));
            }
            if enrollments.lock().unwrap().contains(&local_id) {
                return Script::Body(200, json!({
                    "status": "blocked",
                    "problem": { "code": "already_enrolled", "message": "this client identity is already enrolled here" },
                    "participant": enroll_participant(&local_id, &requested_handle)
                }));
            }
            enrollments.lock().unwrap().insert(local_id.clone());
            Script::Body(200, json!({
                "status": "ok",
                "participant": enroll_participant(&local_id, &requested_handle),
                "credential": {
                    "token": format!("ts_unbound_{port}_{local_id}"),
                    "name": "connector",
                    "expiresAt": null,
                    "grants": ["welcome", "read", "post"]
                }
            }))
        }
        ("POST", "/api/v1/experience/topics/lounge/posts") => {
            let request_id = body.get("requestId").and_then(Value::as_str).unwrap_or_default().to_string();
            let mut registry = posts.lock().unwrap();
            let count = registry.entry(request_id.clone()).or_insert(0);
            *count += 1;
            // The first dispatch of the crash scenario is lost in transit.
            if request_id == "crash-1" && *count == 1 {
                return Script::Lost;
            }
            if request_id == "conflict-1" {
                return Script::Body(409, json!({ "code": "request_conflict", "message": "conflict-1 already identifies a different action." }));
            }
            Script::Body(200, create_post(&request_id))
        }
        ("POST", "/api/v1/experience/topics/lounge/read-position") => Script::Body(200, read_position()),
        ("PUT", "/api/v1/experience/tangents/home/membership") => Script::Body(200, join()),
        ("DELETE", "/api/v1/experience/tangents/home/membership") => Script::Body(200, leave()),
        ("PUT", "/api/v1/experience/watches") => Script::Body(200, watch(body)),
        _ if clean.starts_with("/api/v1/experience/operations/") => {
            Script::Body(200, operation(clean.rsplit('/').next().unwrap_or("")))
        }
        _ => Script::Body(200, json!({
            "experienceVersion": "1.0", "operation": "unknown", "status": "error",
            "result": { "problem": { "code": "invalid_arguments", "message": "unscripted route" } }
        })),
    }
}

/// The W2-contract arrival identity segment: `participantRef` is the server's GUIDv7
/// participant id, `did` is nullable, and the identity collection is ordered best-first.
fn envelope(operation: &str, status: &str, data: Value) -> Value {
    json!({
        "experienceVersion": "1.0",
        "operation": operation,
        "status": status,
        "snapshot": { "revision": "att:41", "asOf": "2026-09-10T20:00:00Z", "coverage": "current" },
        "identity": {
            "participantRef": "prt_7b3e10a2c4d5",
            "did": "did:plc:lumen",
            "displayName": "Lumen",
            "handle": "lumen.example.test",
            "identities": [
                { "kind": "atproto", "value": "did:plc:lumen" },
                { "kind": "internal", "value": "tangent:local:prt_7b3e10a2c4d5" }
            ]
        },
        "place": { "serverRef": "ORIGIN", "tangentRef": null, "topicRef": null, "label": "Kintsugi Architecture", "role": "participant", "allowedActions": ["list_tangents", "get_updates"] },
        "result": { "data": data, "receipt": null, "problem": null },
        "attention": { "revision": "att:41", "waitingCount": { "value": 0, "atLeast": false }, "newActivityCount": { "value": 0, "atLeast": false }, "items": [], "more": false, "detailsIncluded": true },
        "continuation": { "activityCheckpoint": "checkpoint:41", "activityPageCursor": null, "historyOlderCursor": null, "historyNewerCursor": null, "readCursor": null, "directoryCursor": null },
        "actions": [],
        "orientation": { "purpose": "A shared conversation space for people and agents.", "rules": [], "brief": null },
        "capabilities": { "attention": true, "coordination": false },
    })
}

/// The enrollment endpoint's participant view for one client localId. The connector-client
/// identity echoes the connector's local id, scoped to this server relationship.
fn enroll_participant(local_id: &str, handle: &str) -> Value {
    json!({
        "participantRef": format!("prt_{local_id}"),
        "identities": [
            { "kind": "internal", "value": format!("tangent:local:prt_{local_id}") },
            { "kind": "connector-client", "value": local_id }
        ],
        "bestLabel": handle,
        "did": null
    })
}

fn mention_item() -> Value {
    json!({
        "ref": "att:lounge:m40",
        "kind": "direct_mention",
        "actorRef": "did:plc:leo",
        "actorName": "Leo",
        "recipientRef": "did:plc:lumen",
        "scopeRef": "ORIGIN::home::lounge",
        "sourceRef": "ORIGIN::home::lounge::m40",
        "relationship": "addressed_to_you",
        "excerpt": "Lumen, can you help us coordinate Project Z?",
        "sourceRevision": "cid:40a",
        "state": "pending",
    })
}

fn arrival() -> Value {
    let mut value = envelope("arrive", "ok", json!({
        "tangents": [
            { "tangentRef": "ORIGIN::home", "name": "Kintsugi Architecture", "description": "Main hall", "membership": "member", "admission": "open", "canJoin": false, "hasReadableTopics": true, "hasWritableTopics": true }
        ],
        "continuation": null,
    }));
    value["attention"]["waitingCount"]["value"] = json!(1);
    value["attention"]["items"] = json!([mention_item()]);
    value["actions"] = json!([{ "name": "read_topic", "targetRef": "ORIGIN::home::lounge", "aroundPostRef": "ORIGIN::home::lounge::m40", "label": "Read Leo's request" }]);
    value
}

fn tangents() -> Value {
    envelope("list_tangents", "ok", json!({
        "tangents": [
            { "tangentRef": "ORIGIN::home", "name": "Kintsugi Architecture", "description": "Main hall", "membership": "member", "admission": "open", "canJoin": false, "hasReadableTopics": true, "hasWritableTopics": true }
        ],
        "continuation": null,
    }))
}

fn topics() -> Value {
    envelope("list_topics", "ok", json!({
        "topics": [
            { "topicRef": "ORIGIN::home::lounge", "title": "Lounge", "topic": "General conversation", "canRead": true, "canWrite": true, "isLocked": false, "allowPostEditing": false }
        ],
        "continuation": null,
    }))
}

fn topic_window() -> Value {
    let mut value = envelope("read_topic", "ok", json!({
        "title": "Lounge",
        "brief": "General conversation",
        "position": "unread",
        "posts": [
            {
                "ref": "ORIGIN::home::lounge::m40",
                "authorRef": "did:plc:leo",
                "authorName": "Leo",
                "text": "Lumen, can you help us coordinate Project Z?\n\nQuoted from the proposal: \"You should review the whole plan.\"",
                "replyTo": null,
                "url": "https://tangent.example/tangents/home/posts/m40/",
                "createdAt": "2026-09-10T18:00:00Z",
                "editedAt": null,
                "removed": false,
            },
            {
                "ref": "ORIGIN::home::lounge::m41",
                "authorRef": "did:plc:lumen",
                "authorName": "Lumen",
                "text": "Looking at it now.",
                "replyTo": "ORIGIN::home::lounge::m40",
                "url": "https://tangent.example/tangents/home/posts/m41/",
                "createdAt": "2026-09-10T18:05:00Z",
                "editedAt": null,
                "removed": false,
            },
        ],
    }));
    value["place"]["tangentRef"] = json!("ORIGIN::home");
    value["place"]["topicRef"] = json!("ORIGIN::home::lounge");
    value["place"]["label"] = json!("Kintsugi Architecture / Lounge");
    value["place"]["role"] = json!("member");
    value["place"]["allowedActions"] = json!(["read_topic", "mark_read", "set_watch", "create_post"]);
    value["continuation"]["readCursor"] = json!("read:41");
    value["continuation"]["historyOlderCursor"] = json!("history:before-40");
    value["actions"] = json!([{ "name": "mark_read", "targetRef": "ORIGIN::home::lounge", "aroundPostRef": null, "label": "Acknowledge reading through the newest Post" }]);
    value
}

fn updates() -> Value {
    let mut value = envelope("get_updates", "ok", json!({ "scopeRef": "ORIGIN" }));
    value["attention"]["waitingCount"]["value"] = json!(1);
    value["attention"]["newActivityCount"]["value"] = json!(2);
    value["attention"]["items"] = json!([mention_item()]);
    value["actions"] = json!([{ "name": "read_topic", "targetRef": "ORIGIN::home::lounge", "aroundPostRef": "ORIGIN::home::lounge::m40", "label": "Read Leo's request" }]);
    value
}

fn create_post(request_id: &str) -> Value {
    envelope("create_post", "ok", json!({
        "postRef": "ORIGIN::home::lounge::m84",
        "source": { "uri": "at://did:plc:lumen/example.test.space/op-84/post-84", "cid": "bafyreiexample84" },
        "url": "https://tangent.example/tangents/home/posts/m84/",
    }))
    .with_receipt(request_id, "completed", "ORIGIN::home::lounge::m84")
}

fn read_position() -> Value {
    envelope("read_position", "ok", json!({
        "throughPostRef": "ORIGIN::home::lounge::m41",
        "topicRef": "ORIGIN::home::lounge",
    }))
}

fn join() -> Value {
    envelope("join", "ok", json!({ "membership": "member", "message": "Welcome to your Tangent." }))
}

fn leave() -> Value {
    envelope("leave", "ok", json!({ "membership": "visitor", "message": "You left this Tangent." }))
}

fn watch(body: &Value) -> Value {
    envelope("set_watch", "ok", json!({
        "scopeRef": body.get("scopeRef").cloned().unwrap_or(Value::Null),
        "mode": body.get("mode").cloned().unwrap_or(Value::Null),
    }))
}

fn operation(request_id: &str) -> Value {
    envelope("get_operation", "ok", json!({ "operation": "CreatePost" }))
        .with_receipt(request_id, "completed", "ORIGIN::home::lounge::m84")
}

trait WithReceipt {
    fn with_receipt(self, request_id: &str, state: &str, result_ref: &str) -> Value;
}

impl WithReceipt for Value {
    fn with_receipt(mut self, request_id: &str, state: &str, result_ref: &str) -> Value {
        self["result"]["receipt"] = json!({ "requestId": request_id, "state": state, "resultRef": result_ref, "retryAfterSeconds": null });
        self
    }
}

/// Collects published events for assertions.
#[allow(dead_code)]
pub struct EventRecorder(pub Receiver<tangent_connector::domain::events::DomainEvent>);

impl EventRecorder {
    #[allow(dead_code)]
    pub fn drain(&self) -> Vec<tangent_connector::domain::events::DomainEvent> {
        let mut observed = Vec::new();
        while let Ok(event) = self.0.try_recv() {
            observed.push(event);
        }
        observed
    }
}

fn now_secs() -> i64 {
    std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|value| value.as_secs() as i64)
        .unwrap_or(0)
}

/// Minimal percent-decoding for query parameters the fake PDS reads back.
fn percent_decode(value: &str) -> String {
    let bytes = value.as_bytes();
    let mut out = Vec::with_capacity(bytes.len());
    let mut index = 0;
    while index < bytes.len() {
        match bytes[index] {
            b'%' if index + 3 <= bytes.len() => {
                let hex = std::str::from_utf8(&bytes[index + 1..index + 3]).ok();
                match hex.and_then(|hex| u8::from_str_radix(hex, 16).ok()) {
                    Some(byte) => {
                        out.push(byte);
                        index += 3;
                    }
                    None => {
                        out.push(bytes[index]);
                        index += 1;
                    }
                }
            }
            byte => {
                out.push(byte);
                index += 1;
            }
        }
    }
    String::from_utf8_lossy(&out).to_string()
}
