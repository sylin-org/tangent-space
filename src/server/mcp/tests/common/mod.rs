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

/// A scripted experience API. Responses are synthetic; the post registry gives the
/// crash-retry scenario its statefulness, and the enrollment registry implements the W2
/// contract's unbound-enrollment exchange (idempotent per client localId).
#[allow(dead_code)]
pub struct FakeServer {
    pub requests: Arc<Mutex<Vec<Received>>>,
    posts: Arc<Mutex<HashMap<String, u32>>>,
    enrollments: Arc<Mutex<HashSet<String>>>,
    unbound_disabled: bool,
    origin: String,
}

impl FakeServer {
    pub fn start() -> Self {
        Self::start_with(false)
    }

    /// A server with the unbound-enrollment setting switched off.
    #[allow(dead_code)]
    pub fn start_with_unbound_disabled() -> Self {
        Self::start_with(true)
    }

    fn start_with(unbound_disabled: bool) -> Self {
        let listener = TcpListener::bind("127.0.0.1:0").expect("bind");
        let url = origin(&listener);
        let requests = Arc::new(Mutex::new(Vec::new()));
        let posts = Arc::new(Mutex::new(HashMap::new()));
        let enrollments = Arc::new(Mutex::new(HashSet::new()));
        let server = Self {
            requests: requests.clone(),
            posts: posts.clone(),
            enrollments: enrollments.clone(),
            unbound_disabled,
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
                    let origin = url.clone();
                    std::thread::spawn(move || {
                        serve_connection(stream, requests, posts, enrollments, unbound_disabled, origin)
                    });
                }
            })
            .expect("server thread");
        server
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
        match respond(&method, &path, &body, &posts, &enrollments, unbound_disabled, &origin) {
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
    posts: &Arc<Mutex<HashMap<String, u32>>>,
    enrollments: &Arc<Mutex<HashSet<String>>>,
    unbound_disabled: bool,
    origin: &str,
) -> Script {
    let clean = path.split('?').next().unwrap_or(path);
    match (method, clean) {
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
