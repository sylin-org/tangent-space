//! The on-the-fly handshake (owner-directed realignment): `Connect` resolves the acting
//! identity through the client allowlist, discovers the server, pops the operator page
//! at the per-identity sign-in anchor when no atproto binding exists (never a silent
//! unbound fallback), enrolls bound only when no usable enrollment/session exists, and
//! exits through arrival. The owner addendum adds the auto-resume: a waiting connect
//! finishes by itself when the operator signs in on the page, narrated live over SSE.
//! All data is synthetic.

mod common;

use std::io::{Read, Write};
use std::net::{TcpListener, TcpStream};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

use serde_json::{json, Value};

use common::{BoundMode, FakeServer};
use tangent_connector::adapters::experience::UreqExperience;
use tangent_connector::adapters::store::StateStore;
use tangent_connector::application::bus::EventBus;
use tangent_connector::application::hub::{bind_anchor, registration_target, ConnectorHub, ToolOutcome};
use tangent_connector::application::ports::ExperiencePort;
use tangent_connector::domain::identity::{CallerId, ClientRule};
use tangent_connector::domain::intake::IntakeChannel;

fn workspace(label: &str, caller: CallerId) -> Arc<ConnectorHub> {
    // Test sessions are synthetic.
    let dir = std::env::temp_dir().join(format!("tangent-connector-connect-{}-{}", label, std::process::id()));
    let _ = std::fs::remove_dir_all(&dir);
    std::fs::create_dir_all(&dir).expect("temp dir");
    let events = Arc::new(EventBus::new());
    let port: Arc<dyn ExperiencePort> = Arc::new(UreqExperience::new());
    let store = StateStore::open(&dir).expect("store");
    Arc::new(ConnectorHub::new(port, store, events, caller))
}

fn mcp_workspace(label: &str, client: &str) -> Arc<ConnectorHub> {
    workspace(label, CallerId(format!("mcp:{client}")))
}

/// An allowlisted MCP workspace holding one already-bound identity, ready to connect.
fn bound_workspace(label: &str, server: &FakeServer) -> (Arc<ConnectorHub>, String) {
    server.add_account("ox_omega.bsky.example", "app-pass-1", "did:plc:ox");
    let hub = mcp_workspace(label, "codex-host");
    let identity = hub.create_identity("ox_omega", None).expect("identity");
    hub.set_client_rules(vec![ClientRule { client_name: "codex-host".into(), local_id: Some(identity.local_id.clone()) }])
        .expect("allowlist");
    hub.bind_atproto(&identity.local_id, "ox_omega.bsky.example", "app-pass-1", Some(server.origin()))
        .expect("binding");
    (hub, identity.local_id)
}

fn connect(hub: &ConnectorHub, origin: &str) -> ToolOutcome {
    hub.invoke(IntakeChannel::Mcp, "Connect", &json!({ "serverUrl": origin }))
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

// ---------- the four Connect branches ----------

#[test]
fn connect_resolves_enrolls_and_arrives_through_the_allowlist() {
    let server = FakeServer::start();
    let (hub, local_id) = bound_workspace("ok", &server);

    let outcome = connect(&hub, server.origin());
    assert!(!outcome.is_error, "text: {}", outcome.text);
    assert!(outcome.text.contains("you are participating as"), "orientation view: {}", outcome.text);
    let context_id = outcome
        .structured
        .pointer("/connector/contextId")
        .and_then(Value::as_str)
        .expect("a context for subsequent calls")
        .to_string();
    assert!(!context_id.is_empty());

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

    // A second connect finds the enrollment ready: arrival again, no new exchange.
    let again = connect(&hub, server.origin());
    assert!(!again.is_error, "text: {}", again.text);
    assert_eq!(hub.enrollments_of(&local_id).len(), 1, "no duplicate enrollment");
    assert_eq!(
        server.requests().iter().filter(|request| request.path == "/mcp/token").count(),
        1,
        "no second proof exchange"
    );
}

#[test]
fn connect_without_a_resolvable_identity_lists_them_honestly() {
    // Unlisted MCP client: the honest question lists the identities, never guesses.
    let hub = mcp_workspace("unlisted", "codex-host");
    hub.create_identity("alpha", None).expect("identity");
    hub.create_identity("beta", None).expect("identity");
    let outcome = connect(&hub, "https://tangent.example");
    assert!(outcome.is_error);
    assert_eq!(code_of(&outcome), "identity_selection_required");
    assert!(outcome.text.contains("alpha") && outcome.text.contains("beta"), "text was: {}", outcome.text);
    assert!(outcome.text.contains("which one is yours"), "text was: {}", outcome.text);

    // The CLI intake never auto-resolves either.
    let cli = workspace("cli-never", CallerId("cli".into()));
    cli.create_identity("gamma", None).expect("identity");
    let refused = cli.invoke(IntakeChannel::Cli, "Connect", &json!({ "serverUrl": "https://tangent.example" }));
    assert!(refused.is_error);
    assert_eq!(code_of(&refused), "identity_selection_required");
    assert!(refused.text.contains("never auto-resolves"), "text was: {}", refused.text);

    // No identities at all: the honest answer names the OpenRegistration path.
    let empty = mcp_workspace("empty", "codex-host");
    let none = connect(&empty, "https://tangent.example");
    assert!(none.is_error);
    assert!(none.text.contains("no local identity exists yet"), "text was: {}", none.text);
    assert!(none.text.contains("OpenRegistration"), "text was: {}", none.text);
}

#[test]
fn connect_with_no_binding_pops_the_sign_in_page_and_enrolls_nothing() {
    let server = FakeServer::start();
    let hub = mcp_workspace("pop", "codex-host");
    let identity = hub.create_identity("ox_omega", None).expect("identity");
    hub.set_client_rules(vec![ClientRule { client_name: "codex-host".into(), local_id: Some(identity.local_id.clone()) }])
        .expect("allowlist");
    hub.set_operator_page_url("http://127.0.0.1:59999/?token=page-secret-2");
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

    // The URL and its token never render into the model-facing response.
    assert!(!outcome.text.contains("page-secret-2"), "text was: {}", outcome.text);
    let rendered = serde_json::to_string(&outcome.structured).unwrap_or_default();
    assert!(!rendered.contains("page-secret-2"), "structured was: {rendered}");

    // R3: OpenRegistration now routes to this identity's bind anchor.
    assert_eq!(
        hub.registration_target_url().as_deref(),
        Some(format!("http://127.0.0.1:59999/?token=page-secret-2#{}", bind_anchor(&identity.local_id)).as_str())
    );
    assert_eq!(registration_target("http://127.0.0.1:1/?token=x", &bind_anchor("abc")), "http://127.0.0.1:1/?token=x#bind-abc");

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

// ---------- the auto-resume and the SSE feed (owner addendum) ----------

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
    let identity = hub.create_identity("ox_omega", None).expect("identity");
    hub.set_client_rules(vec![ClientRule { client_name: "codex-host".into(), local_id: Some(identity.local_id.clone()) }])
        .expect("allowlist");

    // The operator server this page-and-feed journey runs against.
    let listener = TcpListener::bind("127.0.0.1:0").expect("bind");
    let address = listener.local_addr().expect("address");
    let token = "0123456789abcdef0123456789abcdef".to_string();
    {
        let hub = hub.clone();
        let token = token.clone();
        let serving = listener.try_clone().expect("clone listener");
        std::thread::Builder::new()
            .name("operator-under-test".into())
            .spawn(move || tangent_connector::adapters::operator::serve(serving, hub, token))
            .expect("server thread");
    }
    hub.set_operator_page_url(&format!("http://{address}/?token={token}"));

    // A test client holds the SSE feed open BEFORE anything happens, reading frames
    // into a shared buffer until the journey completes.
    let received = Arc::new(Mutex::new(String::new()));
    {
        let received = received.clone();
        let address = address.clone();
        let token = token.clone();
        std::thread::spawn(move || {
            let mut stream = match TcpStream::connect(address) {
                Ok(stream) => stream,
                Err(_) => return,
            };
            if stream
                .write_all(format!("GET /api/events?token={token} HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n").as_bytes())
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

    // The operator completes the sign-in on the page (the API the form calls). The
    // response returns only after the auto-resume has run: enrollment plus arrival,
    // connector-side, no model involved.
    let mut binder = TcpStream::connect(address).expect("connect");
    let payload = json!({
        "handle": "ox_omega.bsky.example",
        "appPassword": "app-pass-2",
        "pds": server.origin(),
    })
    .to_string();
    let bound = http_round_trip(
        &mut binder,
        &format!(
            "POST /api/identities/{}/atproto/bind HTTP/1.1\r\nHost: 127.0.0.1\r\nX-Tangent-Token: {token}\r\nContent-Type: application/json\r\nContent-Length: {}\r\nConnection: close\r\n\r\n{payload}",
            identity.local_id,
            payload.len()
        ),
    );
    assert!(bound.starts_with("HTTP/1.1 200") && bound.contains("\"status\":\"ok\""), "bind was: {bound}");

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

    // The feed narrated the whole story, in order.
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

    // The feed never carries a secret: no page token, no app password, no session or
    // proof values.
    assert!(!stream_text.contains(&token), "the page token never rides the feed: {stream_text}");
    assert!(!stream_text.contains("app-pass-2"), "the app password never rides the feed");
    assert!(!stream_text.contains("ts_bound_") && !stream_text.contains("sat_") && !stream_text.contains("proof_"), "no session or proof values ride the feed");

    // R3: with the sign-in satisfied, OpenRegistration routes back to identity creation.
    assert_eq!(
        hub.registration_target_url().as_deref(),
        Some(format!("http://{address}/?token={token}#create-identity").as_str())
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
/// (already-opened page, never a new spawn, never a hang).
#[test]
fn repeated_connects_while_waiting_for_the_operator_all_answer_promptly() {
    let server = FakeServer::start();
    let hub = mcp_workspace("loop", "codex-host");
    let identity = hub.create_identity("ox_omega", None).expect("identity");
    hub.set_client_rules(vec![ClientRule { client_name: "codex-host".into(), local_id: Some(identity.local_id.clone()) }])
        .expect("allowlist");
    hub.set_operator_page_url("http://127.0.0.1:59999/?token=page-secret-loop");
    let _guard = no_browser();

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
        assert!(!again.text.contains("page-secret-loop"), "the page token never renders: {}", again.text);
    }

    // The store stays lockable after the whole loop, and nothing enrolled meanwhile.
    assert_eq!(hub.identities().len(), 1);
    assert!(hub.enrollments_of(&identity.local_id).is_empty(), "still nothing enrolled while waiting");
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
    let identity = hub.create_identity("ox_omega", None).expect("identity");
    hub.set_client_rules(vec![ClientRule { client_name: "codex-host".into(), local_id: Some(identity.local_id.clone()) }])
        .expect("allowlist");

    let listener = TcpListener::bind("127.0.0.1:0").expect("bind");
    let address = listener.local_addr().expect("address");
    let token = "0123456789abcdef0123456789abcdef".to_string();
    {
        let hub = hub.clone();
        let token = token.clone();
        let serving = listener.try_clone().expect("clone listener");
        std::thread::Builder::new()
            .name("operator-under-test".into())
            .spawn(move || tangent_connector::adapters::operator::serve(serving, hub, token))
            .expect("server thread");
    }
    hub.set_operator_page_url(&format!("http://{address}/?token={token}"));
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
        &format!("GET /api/identities?token={token} HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n"),
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
            "POST /api/identities?token={token} HTTP/1.1\r\nHost: 127.0.0.1\r\nX-Tangent-Token: {token}\r\nContent-Type: application/json\r\nContent-Length: {}\r\nConnection: close\r\n\r\n{payload}",
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
    let hub_caller = hub.clone();
    let origin = server.origin().to_string();
    let again = bounded(move || connect(&hub_caller, &origin), Duration::from_secs(5));
    assert_eq!(code_of(&again), "operator_action_needed");
    assert!(again.text.contains("already opened"), "the repeated connect stays honest: {}", again.text);
}

#[test]
fn the_sse_feed_refuses_missing_tokens_and_caps_concurrent_clients() {
    let hub = mcp_workspace("sse-cap", "codex-host");
    let listener = TcpListener::bind("127.0.0.1:0").expect("bind");
    let address = listener.local_addr().expect("address");
    let token = "0123456789abcdef0123456789abcdef".to_string();
    {
        let hub = hub.clone();
        let token = token.clone();
        let serving = listener.try_clone().expect("clone listener");
        std::thread::Builder::new()
            .name("operator-under-test".into())
            .spawn(move || tangent_connector::adapters::operator::serve(serving, hub, token))
            .expect("server thread");
    }

    // No token: an honest 401 JSON refusal, like every other /api call.
    let mut bare = TcpStream::connect(address).expect("connect");
    let refused = http_round_trip(&mut bare, "GET /api/events HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n");
    assert!(refused.starts_with("HTTP/1.1 401"), "refused was: {refused}");

    // Four holders fill the cap; each is a live stream (head seen = counted).
    let mut holders: Vec<TcpStream> = Vec::new();
    for _ in 0..4 {
        let mut stream = TcpStream::connect(address).expect("connect");
        stream
            .write_all(format!("GET /api/events?token={token} HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n").as_bytes())
            .expect("request");
        let _ = stream.set_read_timeout(Some(Duration::from_secs(5)));
        let mut buffer = [0u8; 256];
        let read = stream.read(&mut buffer).expect("head bytes");
        assert!(String::from_utf8_lossy(&buffer[..read]).contains("text/event-stream"), "the feed answered");
        holders.push(stream);
    }

    // The fifth is refused honestly — the cap is small by design.
    let mut fifth = TcpStream::connect(address).expect("connect");
    let beyond = http_round_trip(&mut fifth, &format!("GET /api/events?token={token} HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n"));
    assert!(beyond.starts_with("HTTP/1.1 503"), "beyond the cap was: {beyond}");
    assert!(beyond.contains("sse_clients_busy"), "beyond was: {beyond}");
    drop(holders);
}
