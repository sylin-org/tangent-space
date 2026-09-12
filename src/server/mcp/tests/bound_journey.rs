//! W2-D bound journeys against the scripted fake server (synthetic data): atproto
//! binding (createSession acquisition, password-never-logged, re-bind, unbind), the
//! three-step bound enrollment (discovery → getServiceAuth → `/mcp/token`) with its
//! exact wire discipline, honest 503/401/403 error mapping, audience
//! percent-encoding, and the OpenRegistration tool's internal URL construction under
//! the no-browser guard.

mod common;

use std::sync::Arc;

use serde_json::{json, Value};

use common::{BoundMode, FakeServer, AUDIENCE};
use tangent_connector::adapters::experience::UreqExperience;
use tangent_connector::adapters::store::StateStore;
use tangent_connector::application::bus::EventBus;
use tangent_connector::application::hub::{registration_target, ConnectorHub};
use tangent_connector::application::ports::ExperiencePort;
use tangent_connector::domain::identity::CallerId;
use tangent_connector::domain::intake::IntakeChannel;

fn workspace(label: &str) -> (Arc<ConnectorHub>, std::path::PathBuf) {
    // Test sessions are synthetic.
    let dir = std::env::temp_dir().join(format!("tangent-connector-bound-{}-{}", label, std::process::id()));
    let _ = std::fs::remove_dir_all(&dir);
    std::fs::create_dir_all(&dir).expect("temp dir");
    let events = Arc::new(EventBus::new());
    let port: Arc<dyn ExperiencePort> = Arc::new(UreqExperience::new());
    let store = StateStore::open(&dir).expect("store");
    (Arc::new(ConnectorHub::new(port, store, events, CallerId("cli".into()))), dir)
}

/// A workspace built through `build_hub`, so the diagnostics journal runs.
fn journaled_workspace(label: &str) -> (Arc<ConnectorHub>, std::path::PathBuf) {
    let dir = std::env::temp_dir().join(format!("tangent-connector-bound-{}-{}", label, std::process::id()));
    let _ = std::fs::remove_dir_all(&dir);
    std::fs::create_dir_all(&dir).expect("temp dir");
    let hub = tangent_connector::build_hub(CallerId("cli".into()), dir.clone()).expect("hub");
    (hub, dir)
}

fn bound_identity(hub: &ConnectorHub, server: &FakeServer, handle: &str, password: &str, did: &str) -> String {
    server.add_account(handle, password, did);
    let identity = hub.create_identity(handle.split('.').next().unwrap_or("user"), None).expect("identity");
    hub.bind_atproto(&identity.local_id, handle, password, Some(server.origin())).expect("binding");
    identity.local_id
}

fn query_of(path: &str) -> Vec<(String, String)> {
    path.split_once('?')
        .map(|(_, query)| query.to_string())
        .unwrap_or_default()
        .split('&')
        .filter_map(|pair| pair.split_once('=').map(|(key, value)| (key.to_string(), value.to_string())))
        .collect()
}

fn param(path: &str, name: &str) -> Option<String> {
    query_of(path).into_iter().find(|(key, _)| key == name).map(|(_, value)| value)
}

// ---------- binding ----------

#[test]
fn binding_stores_an_atproto_session_and_sets_the_bound_did() {
    let server = FakeServer::start();
    let (hub, _dir) = workspace("bind");
    server.add_account("lumen.bsky.example", "app-pass-1234", "did:plc:fake1");
    let identity = hub.create_identity("lumen", None).expect("identity");

    let bound = hub.bind_atproto(&identity.local_id, "@lumen.bsky.example", "app-pass-1234", Some(server.origin())).expect("binding");
    assert_eq!(bound.bound_did.as_deref(), Some("did:plc:fake1"));

    let status = hub.atproto_binding(&identity.local_id).expect("binding status");
    assert_eq!(status.did, "did:plc:fake1");
    assert_eq!(status.handle, "lumen.bsky.example");
    assert_eq!(status.pds, server.origin(), "the didDoc's serviceEndpoint is the authoritative PDS");
    assert!(status.obtained_at > 0);

    // The session lives in connector state by design (cookie-jar posture), keyed per identity.
    let state_text = std::fs::read_to_string(_dir.join("state.json")).expect("state file");
    assert!(state_text.contains("atproto_sessions"), "the dedicated state map exists: {state_text}");
    assert!(state_text.contains("sat_"), "the PDS session token is state, by design");

    // createSession saw exactly the identifier and password, once.
    let requests = server.requests();
    let sign_ins: Vec<_> = requests.iter().filter(|request| request.path.contains("createSession")).collect();
    assert_eq!(sign_ins.len(), 1);
    assert_eq!(sign_ins[0].body.get("identifier").and_then(Value::as_str), Some("lumen.bsky.example"));
    assert_eq!(sign_ins[0].body.get("password").and_then(Value::as_str), Some("app-pass-1234"));
    assert!(sign_ins[0].bearer.is_empty(), "createSession carries no Authorization header");
}

#[test]
fn the_app_password_never_reaches_state_or_the_diagnostics_journal() {
    let server = FakeServer::start();
    let (hub, dir) = journaled_workspace("password");
    server.add_account("keeper.bsky.example", "secret-app-pass-42", "did:plc:keeper");
    let identity = hub.create_identity("keeper", None).expect("identity");
    hub.bind_atproto(&identity.local_id, "keeper.bsky.example", "secret-app-pass-42", Some(server.origin())).expect("binding");

    let state_text = std::fs::read_to_string(dir.join("state.json")).expect("state file");
    assert!(!state_text.contains("secret-app-pass-42"), "the app password is never state: {state_text}");
    // The diagnostics journal needs a beat to flush the attributed events.
    for _ in 0..20 {
        std::thread::sleep(std::time::Duration::from_millis(50));
        if dir.join("connector.log").exists() {
            break;
        }
    }
    let journal = std::fs::read_to_string(dir.join("connector.log")).unwrap_or_default();
    assert!(journal.contains("bind_atproto"), "the binding action is journaled: {journal}");
    assert!(!journal.contains("secret-app-pass-42"), "the app password is never journaled");
    assert!(!journal.contains("sat_"), "the PDS session token is never journaled either");
}

#[test]
fn a_wrong_password_or_unusable_pds_is_an_honest_error() {
    let server = FakeServer::start();
    let (hub, _dir) = workspace("bad-bind");
    server.add_account("real.bsky.example", "right-pass", "did:plc:real");
    let identity = hub.create_identity("real", None).expect("identity");

    let wrong = hub.bind_atproto(&identity.local_id, "real.bsky.example", "wrong-pass", Some(server.origin())).expect_err("must refuse");
    assert!(wrong.contains("rejected the handle or app password"), "error was: {wrong}");
    assert!(hub.identity(&identity.local_id).unwrap().bound_did.is_none(), "nothing was bound");

    let missing = hub.bind_atproto(&identity.local_id, "ghost.bsky.example", "right-pass", Some(server.origin())).expect_err("must refuse");
    assert!(missing.contains("rejected the handle or app password"), "error was: {missing}");

    let remote_http = hub.bind_atproto(&identity.local_id, "real.bsky.example", "right-pass", Some("http://attacker.example")).expect_err("must refuse");
    assert!(remote_http.contains("invalid_pds"), "plain HTTP off loopback is refused: {remote_http}");
}

// ---------- the bound enrollment recipe ----------

#[test]
fn bound_enrollment_runs_the_three_steps_and_stores_the_session_per_enrollment() {
    let server = FakeServer::start();
    let (hub, _dir) = workspace("recipe");
    let local_id = bound_identity(&hub, &server, "lumen.bsky.example", "app-pass-1234", "did:plc:fake1");

    let entry = hub.enroll_bound(&local_id, server.origin()).expect("bound enrollment");
    assert_eq!(entry.local_id, local_id);
    assert_eq!(entry.origin, server.origin());
    assert_eq!(entry.did.as_deref(), Some("did:plc:fake1"));
    assert!(entry.participant_ref.starts_with("prt_bound_"), "participant ref from the credential view");
    assert_eq!(entry.handle.as_deref(), Some("lumen.bsky.example"));

    let port = server.origin().rsplit(':').next().unwrap_or("0");
    // Step discipline: discovery (anonymous), getServiceAuth (PDS bearer, aud/lxm/exp),
    // /mcp/token (proof bearer, exact body).
    let requests = server.requests();
    let discovery = requests.iter().find(|request| request.path == "/.well-known/tangent-mcp").expect("discovery request");
    assert!(discovery.bearer.is_empty(), "discovery is anonymous");
    let auth = requests
        .iter()
        .find(|request| request.path.starts_with("/xrpc/com.atproto.server.getServiceAuth"))
        .expect("service-auth request");
    assert_eq!(auth.bearer, format!("Bearer sat_{port}_lumen.bsky.example"), "the PDS session mints the proof");
    assert_eq!(param(&auth.path, "aud").as_deref(), Some(AUDIENCE.replace(':', "%3A").as_str()), "the audience comes from discovery (percent-encoded for the query), never hardcoded");
    assert_eq!(param(&auth.path, "lxm").as_deref(), Some("local.tangent.mcp.exchange"));
    let now = std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap().as_secs() as i64;
    let exp = param(&auth.path, "exp").and_then(|value| value.parse::<i64>().ok()).expect("exp");
    assert!((now..=now + 121).contains(&exp), "exp is within now+120 s: {exp} vs {now}");
    let exchange = requests.iter().find(|request| request.path == "/mcp/token").expect("exchange request");
    assert!(exchange.bearer.starts_with("Bearer proof_"), "the proof rides the Authorization header");
    assert_eq!(
        exchange.body,
        json!({ "name": "lumen", "lifetimeDays": 7, "grants": ["welcome", "read", "post"] }),
        "the exchange body is exactly the bounded contract"
    );

    // The Tangent session is stored per enrollment, by design.
    {
        let store = hub.store().lock().unwrap();
        assert!(store.session(&entry.companion_id).unwrap().starts_with("ts_bound_"));
    }
    let state_text = std::fs::read_to_string(_dir.join("state.json")).expect("state file");
    assert!(state_text.contains("ts_bound_"), "sessions remain state.json's per-enrollment map");

    // A second enrollment at the same origin is the honest already_enrolled.
    let again = hub.enroll_bound(&local_id, server.origin()).expect_err("must refuse");
    assert!(again.contains("already_enrolled"), "error was: {again}");

    // The bound enrollment participates: selection and arrival carry its session.
    let selected = hub.invoke(IntakeChannel::Cli, "SelectCompanion", &json!({ "moniker": "lumen" }));
    assert!(!selected.is_error, "text: {}", selected.text);
    let arrived = hub.invoke(
        IntakeChannel::Cli,
        "Arrive",
        &json!({ "companionId": entry.companion_id, "serverUrl": server.origin() }),
    );
    assert!(!arrived.is_error, "arrival failed: {}", arrived.text);
    let session = { hub.store().lock().unwrap().session(&entry.companion_id).unwrap() };
    assert!(
        server.requests().iter().any(|request| request.path == "/api/v1/experience" && request.bearer == format!("Bearer {session}")),
        "arrival used the bound enrollment's own session"
    );
}

#[test]
fn enrolling_before_binding_or_without_a_session_is_an_honest_error() {
    let server = FakeServer::start();
    let (hub, _dir) = workspace("prereq");
    let identity = hub.create_identity("plain", None).expect("identity");

    let unbound = hub.enroll_bound(&identity.local_id, server.origin()).expect_err("must refuse");
    assert!(unbound.contains("atproto_binding_required"), "error was: {unbound}");

    let local_id = bound_identity(&hub, &server, "lumen.bsky.example", "app-pass-1234", "did:plc:fake1");
    // Simulate the honest hand-edited-state case: the session map lost the entry.
    {
        let mut store = hub.store().lock().unwrap();
        store.remove_atproto_session(&local_id);
        store.save().expect("save");
    }
    let missing = hub.enroll_bound(&local_id, server.origin()).expect_err("must refuse");
    assert!(missing.contains("atproto_session_missing"), "error was: {missing}");
    assert!(hub.enrollments_of(&local_id).is_empty());
}

#[test]
fn the_exchange_failures_map_to_honest_operator_errors() {
    // 503 family: the server has no proof audience configured.
    let server = FakeServer::start_bound_with(BoundMode::AudienceUnconfigured);
    let (hub, _dir) = workspace("fifty-o-three");
    let local_id = bound_identity(&hub, &server, "fifty.bsky.example", "pass-1", "did:plc:fifty");
    let error = hub.enroll_bound(&local_id, server.origin()).expect_err("must refuse");
    assert!(error.contains("exchange_unavailable") && error.contains("no proof audience configured"), "error was: {error}");
    assert!(hub.enrollments_of(&local_id).is_empty());
    assert!(!server.requests().iter().any(|request| request.path == "/mcp/token"), "the exchange was never reached");

    // 401 at the exchange: the proof is rejected.
    let server = FakeServer::start_bound_with(BoundMode::RejectProofs);
    let (hub, _dir) = workspace("four-o-one");
    let local_id = bound_identity(&hub, &server, "one.bsky.example", "pass-2", "did:plc:one");
    let error = hub.enroll_bound(&local_id, server.origin()).expect_err("must refuse");
    assert!(error.contains("invalid_service_proof"), "error was: {error}");
    assert!(hub.enrollments_of(&local_id).is_empty());

    // 403: the participant is suspended there.
    let server = FakeServer::start_bound_with(BoundMode::Suspended);
    let (hub, _dir) = workspace("four-o-three");
    let local_id = bound_identity(&hub, &server, "three.bsky.example", "pass-3", "did:plc:three");
    let error = hub.enroll_bound(&local_id, server.origin()).expect_err("must refuse");
    assert!(error.contains("participant_suspended"), "error was: {error}");
    assert!(hub.enrollments_of(&local_id).is_empty());
}

#[test]
fn an_expired_pds_session_demands_a_rebind_and_rebinding_recovers() {
    let server = FakeServer::start();
    let (hub, _dir) = workspace("expired");
    server.add_expiring_account("wanderer.bsky.example", "pass-4", "did:plc:wanderer");
    let identity = hub.create_identity("wanderer", None).expect("identity");
    hub.bind_atproto(&identity.local_id, "wanderer.bsky.example", "pass-4", Some(server.origin())).expect("binding");

    let error = hub.enroll_bound(&identity.local_id, server.origin()).expect_err("must refuse");
    assert!(error.contains("atproto_session_expired") && error.contains("Re-bind"), "error was: {error}");

    // Re-bind replaces the session and the enrollment then succeeds.
    server.add_account("wanderer.bsky.example", "pass-5", "did:plc:wanderer");
    hub.bind_atproto(&identity.local_id, "wanderer.bsky.example", "pass-5", Some(server.origin())).expect("re-bind");
    let entry = hub.enroll_bound(&identity.local_id, server.origin()).expect("bound enrollment after re-bind");
    assert_eq!(entry.did.as_deref(), Some("did:plc:wanderer"));
}

#[test]
fn rebinding_keeps_existing_enrollment_sessions_and_unbind_clears_only_the_binding() {
    let server = FakeServer::start();
    let (hub, _dir) = workspace("rebind");
    server.add_account("first.bsky.example", "pass-a", "did:plc:first");
    server.add_account("second.bsky.example", "pass-b", "did:plc:second");
    let identity = hub.create_identity("dual", None).expect("identity");

    hub.bind_atproto(&identity.local_id, "first.bsky.example", "pass-a", Some(server.origin())).expect("bind one");
    let entry = hub.enroll_bound(&identity.local_id, server.origin()).expect("enrollment");
    let tangent_session = { hub.store().lock().unwrap().session(&entry.companion_id).expect("tangent session") };

    // Re-bind to the other account: the atproto session is replaced, the identity's
    // bound DID follows, and the enrollment keeps its own Tangent session.
    hub.bind_atproto(&identity.local_id, "second.bsky.example", "pass-b", Some(server.origin())).expect("re-bind");
    assert_eq!(hub.identity(&identity.local_id).unwrap().bound_did.as_deref(), Some("did:plc:second"));
    {
        let store = hub.store().lock().unwrap();
        let atproto = store.atproto_session(&identity.local_id).expect("replaced session");
        assert_eq!(atproto.did, "did:plc:second");
        assert!(atproto.access_jwt.contains("second"), "the PDS session itself was replaced");
        assert_eq!(store.session(&entry.companion_id).as_deref(), Some(tangent_session.as_str()), "the enrollment's Tangent session is untouched");
    }
    let arrived = hub.invoke(
        IntakeChannel::Cli,
        "Arrive",
        &json!({ "companionId": entry.companion_id, "serverUrl": server.origin() }),
    );
    assert!(!arrived.is_error, "the old enrollment still participates: {}", arrived.text);

    // Unbind clears the binding and the atproto session — and only those.
    let unbound = hub.unbind_atproto(&identity.local_id).expect("unbind");
    assert!(unbound.bound_did.is_none());
    assert!(hub.atproto_binding(&identity.local_id).is_none());
    {
        let store = hub.store().lock().unwrap();
        assert!(store.atproto_session(&identity.local_id).is_none());
        assert_eq!(store.session(&entry.companion_id).as_deref(), Some(tangent_session.as_str()), "unbind keeps enrollment sessions");
    }
    let again = hub.invoke(
        IntakeChannel::Cli,
        "Arrive",
        &json!({ "companionId": entry.companion_id, "serverUrl": server.origin() }),
    );
    assert!(!again.is_error, "participation survives unbinding: {}", again.text);
}

#[test]
fn a_hostile_audience_cannot_inject_query_structure_into_the_proof_request() {
    let server = FakeServer::start_bound_with(BoundMode::TamperedAudience);
    let (hub, _dir) = workspace("tamper");
    let local_id = bound_identity(&hub, &server, "guard.bsky.example", "pass-c", "did:plc:guard");

    // The tampered audience ("did:plc:fixture&lxm=evil&exp=99999999999") is delivered
    // as ONE percent-encoded parameter: the lxm and exp stay exactly ours, and the
    // fake PDS (which decodes) still sees a DID — nothing injected.
    let entry = hub.enroll_bound(&local_id, server.origin()).expect("the enrollment survives a hostile document");
    let requests = server.requests();
    let auth = requests
        .iter()
        .find(|request| request.path.starts_with("/xrpc/com.atproto.server.getServiceAuth"))
        .expect("service-auth request");
    let query = auth.path.split_once('?').map(|(_, query)| query).unwrap_or_default();
    assert_eq!(
        param(&auth.path, "aud").as_deref(),
        Some("did%3Aplc%3Afixture%26lxm%3Devil%26exp%3D99999999999"),
        "the audience is one encoded parameter: {query}"
    );
    assert_eq!(query.matches("lxm=").count(), 1, "no injected lxm parameter: {query}");
    assert_eq!(query.matches("exp=").count(), 1, "no injected exp parameter: {query}");
    assert_eq!(param(&auth.path, "lxm").as_deref(), Some("local.tangent.mcp.exchange"));
    assert!(hub.store().lock().unwrap().session(&entry.companion_id).is_some());
}

#[test]
fn a_server_advertising_another_exchange_method_is_refused_before_any_proof() {
    let server = FakeServer::start_bound_with(BoundMode::WrongMethod);
    let (hub, _dir) = workspace("wrong-method");
    let local_id = bound_identity(&hub, &server, "strict.bsky.example", "pass-d", "did:plc:strict");
    let error = hub.enroll_bound(&local_id, server.origin()).expect_err("must refuse");
    assert!(error.contains("exchange_method_mismatch"), "error was: {error}");
    assert!(!server.requests().iter().any(|request| request.path.contains("getServiceAuth")), "the PDS was never asked");
}

// ---------- OpenRegistration ----------

/// Restores the no-browser env var on drop, so parallel tests observe the prior world.
struct EnvGuard(String);
impl Drop for EnvGuard {
    fn drop(&mut self) {
        std::env::set_var("TANGENT_CONNECTOR_NO_BROWSER", &self.0);
    }
}

#[test]
fn open_registration_opens_the_identity_view_without_ever_showing_the_token() {
    let (hub, _dir) = workspace("open-reg");
    // Without an operator page in this process, the tool answers honestly.
    let none = hub.invoke(IntakeChannel::Mcp, "OpenRegistration", &json!({}));
    assert!(none.is_error);
    assert_eq!(none.structured.pointer("/problem/code").and_then(Value::as_str), Some("operator_page_unavailable"));

    hub.set_operator_page_url("http://127.0.0.1:59999/?token=page-secret-1");
    // URL construction is pure and assertable without any browser.
    assert_eq!(registration_target("http://127.0.0.1:1/?token=x", "#create-identity"), "http://127.0.0.1:1/?token=x#create-identity");
    assert_eq!(
        hub.registration_target_url().as_deref(),
        Some("http://127.0.0.1:59999/?token=page-secret-1#create-identity")
    );

    let previous = std::env::var("TANGENT_CONNECTOR_NO_BROWSER").unwrap_or_default();
    let _guard = EnvGuard(previous);
    std::env::set_var("TANGENT_CONNECTOR_NO_BROWSER", "1");
    let outcome = hub.invoke(IntakeChannel::Mcp, "OpenRegistration", &json!({}));
    assert!(!outcome.is_error, "text: {}", outcome.text);
    assert_eq!(outcome.status, "ok");
    assert_eq!(
        outcome.text,
        "Opened the local operator page for the operator to create or bind an identity; ask the operator when done."
    );
    assert_eq!(
        outcome.structured.pointer("/connector/registration").and_then(Value::as_str),
        Some("operator_page")
    );
    // De-dup (F2): a looping model's repeated call does not spawn another tab — the
    // second answer is the honest "already opened".
    let again = hub.invoke(IntakeChannel::Mcp, "OpenRegistration", &json!({}));
    assert!(!again.is_error, "text: {}", again.text);
    assert!(again.text.contains("already open"), "text was: {}", again.text);
    assert_eq!(
        again.structured.pointer("/connector/registration").and_then(Value::as_str),
        Some("operator_page_already_open")
    );
    for outcome in [&outcome, &again] {
        let rendered = serde_json::to_string(&outcome.structured).unwrap_or_default();
        assert!(!rendered.contains("page-secret-1") && !rendered.contains("59999"), "the URL never renders: {rendered}");
        assert!(!outcome.text.contains("page-secret-1"), "the text never carries the token");
    }

    // Argument discipline: the tool takes nothing.
    let refused = hub.invoke(IntakeChannel::Mcp, "OpenRegistration", &json!({ "moniker": "x" }));
    assert!(refused.is_error);
    assert_eq!(refused.structured.pointer("/problem/code").and_then(Value::as_str), Some("invalid_arguments"));
}
