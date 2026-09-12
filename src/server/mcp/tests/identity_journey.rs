//! W2-A identity journeys: identity CRUD and handle uniqueness, per-caller allowlist
//! resolution (listed → resolve; listed-None and unlisted → instruction, even with one
//! identity; CLI → never), the W2-contract enrollment exchange against the fake server
//! (ok path, local and server `already_enrolled`, blocked `unbound_enrollment_disabled`),
//! the legacy-enrollment drop at load, and the operator listener's token gate. All data
//! is synthetic.

mod common;

use std::io::{Read, Write};
use std::net::{TcpListener, TcpStream};
use std::sync::Arc;

use serde_json::{json, Value};

use common::FakeServer;
use tangent_connector::adapters::experience::UreqExperience;
use tangent_connector::adapters::store::StateStore;
use tangent_connector::application::bus::EventBus;
use tangent_connector::application::hub::ConnectorHub;
use tangent_connector::application::ports::ExperiencePort;
use tangent_connector::domain::identity::{CallerId, ClientRule};
use tangent_connector::domain::intake::IntakeChannel;

fn workspace(label: &str, caller: CallerId) -> Arc<ConnectorHub> {
    // Test sessions are synthetic.
    let dir = std::env::temp_dir().join(format!("tangent-connector-identity-{}-{}", label, std::process::id()));
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

fn select(hub: &ConnectorHub, moniker: Option<&str>) -> tangent_connector::application::hub::ToolOutcome {
    let mut arguments = json!({});
    if let Some(moniker) = moniker {
        arguments["moniker"] = json!(moniker);
    }
    hub.invoke(IntakeChannel::Mcp, "SelectCompanion", &arguments)
}

fn enrolled_unbound(hub: &ConnectorHub, server: &FakeServer, handle: &str) -> (String, String) {
    let identity = hub.create_identity(handle, None).expect("identity");
    let entry = hub.enroll_unbound(&identity.local_id, server.origin()).expect("unbound enrollment");
    (identity.local_id, entry.companion_id)
}

// ---------- identity CRUD ----------

#[test]
fn identity_crud_enforces_handle_uniqueness() {
    let hub = workspace("crud", CallerId("cli".into()));
    let lumen = hub.create_identity("lumen", Some("Lumen")).expect("create");
    assert_eq!(lumen.handle, "lumen");
    assert_eq!(lumen.local_id.len(), 32, "local ids are guid-v7 hex");

    // Uniqueness is case-insensitive within the connector.
    assert!(hub.create_identity("LUMEN", None).is_err());
    let other = hub.create_identity("ada", None).expect("second identity");
    assert!(hub.update_identity(&other.local_id, Some("lumen"), None).is_err(), "an update may not steal a handle");
    assert!(hub.create_identity("x", None).is_err(), "handles are at least 2 characters");
    assert!(hub.create_identity(&"h".repeat(254), None).is_err(), "handles are at most 253 characters");

    let renamed = hub.update_identity(&lumen.local_id, Some("lumen-primary"), Some(Some("Lumen E."))).expect("update");
    assert_eq!(renamed.handle, "lumen-primary");
    assert_eq!(renamed.display_name.as_deref(), Some("Lumen E."));
    let cleared = hub.update_identity(&lumen.local_id, None, Some(None)).expect("clear display name");
    assert!(cleared.display_name.is_none());

    hub.delete_identity(&other.local_id, false).expect("delete without enrollments");
    assert!(hub.delete_identity(&other.local_id, false).is_err(), "the identity is gone");
    assert!(hub.identity(&lumen.local_id).is_some());
}

#[test]
fn delete_refuses_while_enrollments_exist_and_cascades_when_confirmed() {
    let server = FakeServer::start();
    let hub = workspace("cascade", CallerId("cli".into()));
    let (local_id, companion_id) = enrolled_unbound(&hub, &server, "lumen");

    let refused = hub.delete_identity(&local_id, false).expect_err("must refuse");
    assert!(refused.contains("enrollment"), "error was: {refused}");

    hub.delete_identity(&local_id, true).expect("cascade delete");
    assert!(hub.identity(&local_id).is_none());
    assert!(hub.enrollments_of(&local_id).is_empty());
    assert!(hub.store().lock().unwrap().companion(&companion_id).is_none(), "the enrollment cascades");
}

// ---------- allowlist resolution ----------

#[test]
fn a_listed_client_resolves_its_identity_and_only_its_identity() {
    let server = FakeServer::start();
    let hub = mcp_workspace("listed", "codex-host");
    let (local_id, companion_id) = enrolled_unbound(&hub, &server, "lumen");
    hub.set_client_rules(vec![ClientRule { client_name: "codex-host".into(), local_id: Some(local_id) }])
        .expect("allowlist");

    let outcome = select(&hub, None);
    assert!(!outcome.is_error, "text: {}", outcome.text);
    assert_eq!(
        outcome.structured.pointer("/connector/companionId").and_then(Value::as_str),
        Some(companion_id.as_str())
    );
    // An explicit moniker still works for a listed client.
    assert!(!select(&hub, Some("lumen")).is_error);
}

#[test]
fn a_listed_client_without_an_identity_resolves_nothing() {
    let server = FakeServer::start();
    let hub = mcp_workspace("listed-none", "codex-host");
    enrolled_unbound(&hub, &server, "lumen");
    hub.set_client_rules(vec![ClientRule { client_name: "codex-host".into(), local_id: None }])
        .expect("allowlist");

    let outcome = select(&hub, None);
    assert!(outcome.is_error);
    assert_eq!(
        outcome.structured.pointer("/problem/code").and_then(Value::as_str),
        Some("identity_selection_required")
    );
    assert!(outcome.text.contains("operator"), "instruction names the operator path: {}", outcome.text);
}

#[test]
fn an_unlisted_client_resolves_nothing_even_with_one_identity() {
    let server = FakeServer::start();
    let hub = mcp_workspace("unlisted", "codex-host");
    enrolled_unbound(&hub, &server, "lumen");
    // A rule exists — for a different client.
    hub.set_client_rules(vec![ClientRule { client_name: "some-other-host".into(), local_id: None }])
        .expect("allowlist");

    let outcome = select(&hub, None);
    assert!(outcome.is_error);
    assert_eq!(
        outcome.structured.pointer("/problem/code").and_then(Value::as_str),
        Some("identity_selection_required")
    );
    // With exactly one identity present, resolution still never happens silently.
    let identities = hub.identities();
    assert_eq!(identities.len(), 1);
}

#[test]
fn the_command_line_never_auto_resolves() {
    let hub = workspace("cli-never", CallerId("cli".into()));
    let outcome = hub.invoke(IntakeChannel::Cli, "SelectCompanion", &json!({}));
    assert!(outcome.is_error);
    assert_eq!(
        outcome.structured.pointer("/problem/code").and_then(Value::as_str),
        Some("identity_selection_required")
    );
}

// ---------- the W2-contract enrollment exchange ----------

#[test]
fn unbound_enrollment_stores_the_session_and_creates_the_enrollment() {
    let server = FakeServer::start();
    let hub = workspace("unbound-ok", CallerId("cli".into()));
    let identity = hub.create_identity("jeff", Some("Jeff")).expect("identity");

    let entry = hub.enroll_unbound(&identity.local_id, server.origin()).expect("enrollment");
    assert_eq!(entry.local_id, identity.local_id);
    assert_eq!(entry.origin, server.origin());
    assert_eq!(entry.participant_ref, format!("prt_{}", identity.local_id));
    assert_eq!(entry.did, None, "the unbound tier carries no DID");
    assert_eq!(entry.handle.as_deref(), Some("jeff"), "the server's best label rides the enrollment");

    // The session lives in connector state by design, keyed per enrollment.
    let available = hub
        .enrollment_inventory()
        .into_iter()
        .find(|(candidate, _)| candidate.companion_id == entry.companion_id)
        .map(|(_, available)| available)
        .expect("inventory entry");
    assert!(available, "the enrollment holds its session");
    let state_text = std::fs::read_to_string(
        std::env::temp_dir().join(format!("tangent-connector-identity-unbound-ok-{}", std::process::id())).join("state.json"),
    )
    .expect("state file");
    let port = server.origin().rsplit(':').next().unwrap_or("0");
    assert!(
        state_text.contains("\"sessions\"") && state_text.contains(&format!("ts_unbound_{port}_")),
        "sessions are a state.json section keyed per enrollment, by design: {state_text}"
    );

    // The exchange left without an Authorization header: pre-session by contract.
    let requests = server.requests();
    let enroll_request = requests
        .iter()
        .find(|request| request.path == "/api/v1/experience/identities/enroll")
        .expect("enroll request");
    assert!(enroll_request.bearer.is_empty(), "enrollment is pre-session");
    assert_eq!(
        enroll_request.body.pointer("/client/localId").and_then(Value::as_str),
        Some(identity.local_id.as_str())
    );

    // The participant view drives participation: selection works, and arrival renders.
    let selected = select_mcp(&hub);
    assert!(!selected.is_error, "text: {}", selected.text);
}

fn select_mcp(hub: &ConnectorHub) -> tangent_connector::application::hub::ToolOutcome {
    hub.invoke(IntakeChannel::Cli, "SelectCompanion", &json!({ "moniker": "jeff" }))
}

#[test]
fn already_enrolled_is_an_honest_error_locally_and_from_the_server() {
    let server = FakeServer::start();
    let hub = workspace("already", CallerId("cli".into()));
    let identity = hub.create_identity("jeff", None).expect("identity");
    hub.enroll_unbound(&identity.local_id, server.origin()).expect("first enrollment");

    // Local guard: the identity already holds a credential for this origin.
    let local = hub.enroll_unbound(&identity.local_id, server.origin()).expect_err("must refuse");
    assert!(local.contains("already_enrolled"), "error was: {local}");

    // Server-side guard: forgetting locally leaves the server's mapping, which reports
    // already_enrolled without a new credential.
    let companion_id = hub.enrollments_of(&identity.local_id)[0].companion_id.clone();
    hub.forget_enrollment(&companion_id).expect("forget");
    let remote = hub.enroll_unbound(&identity.local_id, server.origin()).expect_err("must refuse");
    assert!(remote.contains("already_enrolled"), "error was: {remote}");
    assert!(hub.enrollments_of(&identity.local_id).is_empty(), "no enrollment was created");
    assert_eq!(server.enrolled_local_ids(), vec![identity.local_id.clone()]);
}

#[test]
fn unbound_enrollment_disabled_is_an_honest_error() {
    let server = FakeServer::start_with_unbound_disabled();
    let hub = workspace("disabled", CallerId("cli".into()));
    let identity = hub.create_identity("jeff", None).expect("identity");

    let error = hub.enroll_unbound(&identity.local_id, server.origin()).expect_err("must refuse");
    assert!(error.contains("unbound_enrollment_disabled"), "error was: {error}");
    assert!(hub.enrollments_of(&identity.local_id).is_empty());
    assert!(server.enrolled_local_ids().is_empty());
}

// ---------- legacy state ----------

#[test]
fn legacy_enrollments_are_dropped_once_and_the_drop_persists() {
    let dir = std::env::temp_dir().join(format!("tangent-connector-identity-legacy-{}", std::process::id()));
    let _ = std::fs::remove_dir_all(&dir);
    std::fs::create_dir_all(&dir).expect("temp dir");
    std::fs::write(
        dir.join("state.json"),
        r#"{ "version": 1, "companions": [ { "companion_id": "cmp_legacy01", "name": "old", "origin": "https://tangent.example", "did": "did:plc:old", "display_name": null, "handle": null, "enrolled_at": 1, "auto_check": false, "credential_source": "plaintext-dev" } ] }"#,
    )
    .expect("legacy state");

    let mut store = StateStore::open(&dir).expect("open legacy state");
    // The loader saves once after any drop, so the drop persists immediately.
    assert!(store.companions().is_empty(), "the legacy enrollment fails the identity join honestly");
    let on_disk = std::fs::read_to_string(dir.join("state.json")).expect("persisted state");
    assert!(on_disk.contains("\"companions\": []"), "the cleaned state is saved at load: {on_disk}");
    let dropped = store.take_dropped_enrollments();
    assert_eq!(dropped.len(), 1);
    assert_eq!((dropped[0].0.as_str(), dropped[0].1.as_str()), ("cmp_legacy01", "old"));
    // Additive serde defaults: no identities and no allowlist means empty, not malformed.
    assert!(store.identities().is_empty());
    assert!(store.client_rules().is_empty());
    // A second launch finds nothing left to drop — EnrollmentDropped journals once.
    let mut again = StateStore::open(&dir).expect("reopen");
    assert!(again.take_dropped_enrollments().is_empty());
}

// ---------- two origins, two sessions ----------

#[test]
fn one_identity_at_two_servers_keeps_distinct_working_sessions() {
    let server_a = FakeServer::start();
    let server_b = FakeServer::start();
    let hub = workspace("two-origins", CallerId("cli".into()));
    let identity = hub.create_identity("jeff", None).expect("identity");

    let at_a = hub.enroll_unbound(&identity.local_id, server_a.origin()).expect("enroll at a");
    let at_b = hub.enroll_unbound(&identity.local_id, server_b.origin()).expect("enroll at b");
    assert_ne!(at_a.companion_id, at_b.companion_id, "each enrollment is its own session key");
    let token_of = |server: &FakeServer| {
        let port = server.origin().rsplit(':').next().unwrap_or("0");
        format!("ts_unbound_{port}_{}", identity.local_id)
    };
    let (token_a, token_b) = (token_of(&server_a), token_of(&server_b));
    assert_ne!(token_a, token_b);
    {
        // The session map holds one distinct entry per enrollment.
        let store = hub.store().lock().unwrap();
        assert_eq!(store.session(&at_a.companion_id).as_deref(), Some(token_a.as_str()));
        assert_eq!(store.session(&at_b.companion_id).as_deref(), Some(token_b.as_str()));
    }

    // Both enrollments hold distinct working sessions: arrival at each origin uses that
    // origin's own bearer, and neither session ever reaches the other server.
    for (entry, server, token) in [(&at_a, &server_a, &token_a), (&at_b, &server_b, &token_b)] {
        let outcome = hub.invoke(
            IntakeChannel::Cli,
            "Arrive",
            &json!({ "companionId": entry.companion_id, "serverUrl": server.origin() }),
        );
        assert!(!outcome.is_error, "arrival failed: {}", outcome.text);
        let seen = server
            .requests()
            .iter()
            .any(|request| request.path == "/api/v1/experience" && request.bearer == format!("Bearer {token}"));
        assert!(seen, "the origin did not see its own bearer");
    }
    assert!(server_a.requests().iter().all(|request| !request.bearer.contains(&token_b)), "session b never reaches server a");
    assert!(server_b.requests().iter().all(|request| !request.bearer.contains(&token_a)), "session a never reaches server b");

    // Forgetting one enrollment removes exactly its session; the other stays intact and
    // working.
    hub.forget_enrollment(&at_a.companion_id).expect("forget a");
    let inventory = hub.enrollment_inventory();
    assert!(inventory.iter().all(|(entry, _)| entry.companion_id != at_a.companion_id));
    let (_, available) = inventory
        .iter()
        .find(|(entry, _)| entry.companion_id == at_b.companion_id)
        .expect("enrollment b remains");
    assert!(available, "b's session is intact under its own key");
    {
        let store = hub.store().lock().unwrap();
        assert_eq!(store.session(&at_b.companion_id).as_deref(), Some(token_b.as_str()), "b's session value survives");
        assert_eq!(store.session(&at_a.companion_id), None, "a's session is gone with its enrollment");
    }
    let again = hub.invoke(
        IntakeChannel::Cli,
        "Arrive",
        &json!({ "companionId": at_b.companion_id, "serverUrl": server_b.origin() }),
    );
    assert!(!again.is_error, "b still participates after forgetting a: {}", again.text);
}

// ---------- allowlist bookkeeping ----------

#[test]
fn allowlist_rules_dedupe_by_client_name_with_the_last_winning() {
    let hub = workspace("dedupe", CallerId("cli".into()));
    let alpha = hub.create_identity("alpha", None).expect("identity");
    let beta = hub.create_identity("beta", None).expect("identity");
    hub.set_client_rules(vec![
        ClientRule { client_name: "codex-host".into(), local_id: Some(alpha.local_id.clone()) },
        ClientRule { client_name: "codex-host".into(), local_id: Some(beta.local_id.clone()) },
        ClientRule { client_name: "other-host".into(), local_id: None },
    ])
    .expect("allowlist");

    let rules = hub.client_rules();
    assert_eq!(rules.len(), 2, "one rule per client name: {:?}", rules);
    let codex = rules.iter().find(|rule| rule.client_name == "codex-host").expect("codex rule");
    assert_eq!(codex.local_id.as_deref(), Some(beta.local_id.as_str()), "the last occurrence wins");
}

// ---------- the operator listener ----------

fn http_round_trip(stream: &mut TcpStream, request: &str) -> (String, String) {
    stream.write_all(request.as_bytes()).expect("write request");
    stream.flush().expect("flush");
    let mut raw = Vec::new();
    stream.read_to_end(&mut raw).expect("read response");
    let text = String::from_utf8_lossy(&raw).to_string();
    let (head, body) = text.split_once("\r\n\r\n").unwrap_or((text.as_str(), ""));
    (head.to_string(), body.to_string())
}

#[test]
fn the_operator_api_refuses_missing_or_wrong_tokens_and_accepts_right_ones() {
    let hub = workspace("operator-auth", CallerId("operator".into()));
    let listener = TcpListener::bind("127.0.0.1:0").expect("bind");
    let address = listener.local_addr().unwrap();
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

    // Static asset without a token: inert HTML, served.
    let mut page = TcpStream::connect(address).expect("connect");
    let (head, body) = http_round_trip(&mut page, "GET / HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n");
    assert!(head.starts_with("HTTP/1.1 200"), "head was: {head}");
    assert!(body.contains("Atmosphere handle"), "the Atmosphere-handle column is live on the page");
    // The enroll buttons are gone (R2): the page never enrolls, and the per-identity
    // sign-in anchors exist for the Connect handshake to open.
    assert!(!body.contains("enroll-bound") && !body.contains("Enroll unbound") && !body.contains("Enroll with bound"), "no enroll buttons remain: {body}");
    assert!(body.contains("bind-"), "the per-identity bind anchors are wired");

    // API without a token: 401 JSON refusal.
    let mut bare = TcpStream::connect(address).expect("connect");
    let (head, body) = http_round_trip(&mut bare, "GET /api/identities HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n");
    assert!(head.starts_with("HTTP/1.1 401"), "head was: {head}");
    assert!(body.contains("\"unauthorized\""), "body was: {body}");

    // API with a wrong token (header form): still refused.
    let mut wrong = TcpStream::connect(address).expect("connect");
    let (head, _) = http_round_trip(
        &mut wrong,
        "GET /api/identities HTTP/1.1\r\nHost: 127.0.0.1\r\nX-Tangent-Token: deadbeef\r\nConnection: close\r\n\r\n",
    );
    assert!(head.starts_with("HTTP/1.1 401"), "head was: {head}");

    // Correct token via query: works.
    let mut via_query = TcpStream::connect(address).expect("connect");
    let (head, body) = http_round_trip(
        &mut via_query,
        &format!("GET /api/identities?token={token} HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n"),
    );
    assert!(head.starts_with("HTTP/1.1 200"), "head was: {head}");
    assert!(body.contains("\"status\":\"ok\""), "body was: {body}");

    // Correct token via header: creating an identity through the API.
    let mut via_header = TcpStream::connect(address).expect("connect");
    let payload = json!({ "handle": "lumen", "displayName": "Lumen" }).to_string();
    let (head, body) = http_round_trip(
        &mut via_header,
        &format!(
            "POST /api/identities HTTP/1.1\r\nHost: 127.0.0.1\r\nX-Tangent-Token: {token}\r\nContent-Type: application/json\r\nContent-Length: {}\r\nConnection: close\r\n\r\n{payload}",
            payload.len()
        ),
    );
    assert!(head.starts_with("HTTP/1.1 200"), "head was: {head}");
    assert!(body.contains("\"handle\":\"lumen\""), "body was: {body}");
    assert_eq!(hub.identities().len(), 1, "the mutation crossed the same hub");

    // The enroll API routes are gone too (R2): enrollment lives in the Connect
    // handshake and the hub/CLI disarm tier, never on this page's API.
    let mut enroll_attempt = TcpStream::connect(address).expect("connect");
    let (head, _) = http_round_trip(
        &mut enroll_attempt,
        &format!(
            "POST /api/identities/00000000000000000000000000000000/enroll HTTP/1.1\r\nHost: 127.0.0.1\r\nX-Tangent-Token: {token}\r\nContent-Type: application/json\r\nContent-Length: 2\r\nConnection: close\r\n\r\n{{}}"
        ),
    );
    assert!(head.starts_with("HTTP/1.1 404"), "the enroll route is gone: {head}");
}
