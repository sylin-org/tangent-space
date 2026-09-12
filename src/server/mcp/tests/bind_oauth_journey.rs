//! The OAuth bind journeys against the fake authorization server (synthetic data): the
//! connector-served /bind pages run the atproto local-client profile end to end —
//! handle form → PAR (loopback client, PKCE S256, DPoP-bound) → authorize redirect →
//! loopback callback → tokens stored as the identity's atproto session (refresh token
//! and DPoP key included) → the waiting connect auto-resumes. Refusals are honest:
//! state mismatch binds nothing, an unknown provider is a 404 page, and the operator
//! page carries no password form anywhere. The silent refresh runs before the PDS
//! session is used.

mod common;

use std::io::{Read, Write};
use std::net::{TcpListener, TcpStream, ToSocketAddrs};
use std::sync::Arc;

use serde_json::Value;

use common::FakeServer;
use tangent_connector::adapters::atproto_oauth::AtprotoOauth;
use tangent_connector::adapters::experience::UreqExperience;
use tangent_connector::adapters::operator;
use tangent_connector::adapters::store::StateStore;
use tangent_connector::application::bus::EventBus;
use tangent_connector::application::hub::ConnectorHub;
use tangent_connector::application::ports::ExperiencePort;
use tangent_connector::domain::identity::CallerId;
use tangent_connector::domain::intake::IntakeChannel;

/// A hub plus a real operator server on an ephemeral loopback port, with the OAuth
/// resolution origins pointed at the fake. Answers the server's page URL.
fn operator_workspace(label: &str, server: &FakeServer) -> (Arc<ConnectorHub>, std::path::PathBuf, String, std::net::SocketAddr) {
    let dir = std::env::temp_dir().join(format!("tangent-connector-bind-{}-{}", label, std::process::id()));
    let _ = std::fs::remove_dir_all(&dir);
    std::fs::create_dir_all(&dir).expect("temp dir");
    let events = Arc::new(EventBus::new());
    let port: Arc<dyn ExperiencePort> = Arc::new(UreqExperience::new());
    let store = StateStore::open(&dir).expect("store");
    let hub = Arc::new(ConnectorHub::new(port, store, events, CallerId("operator".into())));
    hub.set_atproto_oauth(AtprotoOauth::with_origins(server.origin(), server.origin()));
    let listener = TcpListener::bind("127.0.0.1:0").expect("bind");
    let address = listener.local_addr().expect("address");
    {
        let hub = hub.clone();
        let serving = listener.try_clone().expect("clone listener");
        std::thread::Builder::new()
            .name("operator-under-test".into())
            .spawn(move || operator::serve(serving, hub))
            .expect("server thread");
    }
    let page = format!("http://{address}/");
    hub.set_operator_page_url(&page);
    (hub, dir, page, address)
}

fn server_addr(origin: &str) -> std::net::SocketAddr {
    origin.trim_start_matches("http://").to_socket_addrs().expect("resolve").next().expect("an address")
}

fn http_round_trip(stream: &mut TcpStream, request: &str) -> String {
    stream.write_all(request.as_bytes()).expect("write request");
    stream.flush().expect("flush");
    let mut raw = Vec::new();
    stream.read_to_end(&mut raw).expect("read response");
    String::from_utf8_lossy(&raw).to_string()
}

fn get(stream_addr: std::net::SocketAddr, target: &str) -> String {
    let mut stream = TcpStream::connect(stream_addr).expect("connect");
    http_round_trip(&mut stream, &format!("GET {target} HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n"))
}

/// Runs the operator-driven OAuth dance against the fake: form POST → 302 authorize →
/// 302 callback → the callback page's response. Answers the callback response.
fn drive_bind(address: std::net::SocketAddr, server: &FakeServer, local_id: &str, handle: &str) -> String {
    let mut binder = TcpStream::connect(address).expect("connect");
    let payload = format!("handle={handle}");
    let started = http_round_trip(
        &mut binder,
        &format!(
            "POST /bind/{local_id}/atproto HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Type: application/x-www-form-urlencoded\r\nContent-Length: {}\r\nConnection: close\r\n\r\n{payload}",
            payload.len()
        ),
    );
    assert!(started.starts_with("HTTP/1.1 302"), "the bind page redirects to authorize: {started}");
    let authorize = started
        .lines()
        .find_map(|line| line.strip_prefix("Location: "))
        .expect("authorize location")
        .trim()
        .to_string();
    assert!(authorize.starts_with(&format!("{}/oauth/authorize", server.origin())), "authorize on the fake AS: {authorize}");
    let authorize_path = authorize.strip_prefix(server.origin()).unwrap_or(&authorize);
    let redirected = get(server_addr(server.origin()), authorize_path);
    assert!(redirected.starts_with("HTTP/1.1 302"), "the AS redirects back: {redirected}");
    let callback = redirected
        .lines()
        .find_map(|line| line.strip_prefix("Location: "))
        .expect("callback location")
        .trim()
        .to_string();
    assert!(callback.starts_with(&format!("http://{address}/?")), "the callback lands on the operator root: {callback}");
    let callback_path = callback.strip_prefix(&format!("http://{address}")).unwrap_or(&callback);
    get(address, callback_path)
}

// ---------- the full journey ----------

#[test]
fn the_bind_pages_run_the_oauth_profile_end_to_end_and_resume_the_waiting_connect() {
    let server = FakeServer::start();
    server.add_account("lumen.bsky.example", "unused-password", "did:plc:lumen");
    let (hub, dir, _page, address) = operator_workspace("journey", &server);
    let identity = hub.create_identity("lumen", None).expect("identity");

    // The bind page serves an inert form — and no password field anywhere.
    let page = get(address, &format!("/bind/{}/atproto", identity.local_id));
    assert!(page.starts_with("HTTP/1.1 200"), "the bind page serves: {page}");
    assert!(page.contains("Sign in lumen") && page.contains("atproto handle"), "the page names the identity and asks for the handle: {page}");
    assert!(!page.contains("type=\"password\"") && !page.contains("appPassword"), "no password field on the bind page: {page}");
    // The operator page links the bind page and carries no password form either.
    let operator_page = get(address, "/");
    assert!(operator_page.contains("signIn.href = \"/bind/\" + identity.localId + \"/atproto\""), "Sign In links the bind route: {operator_page}");
    assert!(!operator_page.contains("type=\"password\"") && !operator_page.contains("appPassword"), "the operator page has no password form: {operator_page}");

    // A connect waits for the operator (no binding yet) — the honest popped-page answer.
    let _guard = no_browser();
    let waiting = hub.invoke(IntakeChannel::Mcp, "Connect", &serde_json::json!({ "serverUrl": server.origin() }));
    assert!(waiting.is_error);
    assert_eq!(
        waiting.structured.pointer("/problem/code").and_then(Value::as_str),
        Some("operator_action_needed"),
        "text: {}",
        waiting.text
    );
    assert!(hub.enrollments_of(&identity.local_id).is_empty(), "nothing enrolled while waiting");

    // The operator drives the bind through the fake authorization server.
    let callback_page = drive_bind(address, &server, &identity.local_id, "lumen.bsky.example");
    assert!(callback_page.starts_with("HTTP/1.1 200"), "the callback answers: {callback_page}");
    assert!(callback_page.contains("Bound as <strong>lumen.bsky.example</strong>"), "the success page names the handle: {callback_page}");
    assert!(callback_page.contains("you can close this tab"), "the tab is freed honestly: {callback_page}");

    // The waiting connect finished by itself: one bound enrollment with a stored session.
    let enrollments = hub.enrollments_of(&identity.local_id);
    assert_eq!(enrollments.len(), 1, "the resume enrolled exactly once");
    assert_eq!(enrollments[0].did.as_deref(), Some("did:plc:lumen"));
    assert!(hub.store().lock().unwrap().has_session(&enrollments[0].companion_id));

    // The identity is bound and the OAuth session is durable state (cookie-jar posture):
    // refresh token and DPoP key included, access token present, app password never.
    assert_eq!(hub.identity(&identity.local_id).unwrap().bound_did.as_deref(), Some("did:plc:lumen"));
    let session = hub.store().lock().unwrap().atproto_session(&identity.local_id).expect("oauth session");
    assert_eq!(session.did, "did:plc:lumen");
    assert_eq!(session.handle, "lumen.bsky.example");
    assert_eq!(session.pds, server.origin(), "the PDS comes from the DID document");
    assert_eq!(session.authserver.as_deref(), Some(server.origin()));
    assert!(session.access_jwt.split('.').count() == 3, "the access token is JWT-shaped");
    assert!(session.refresh_jwt.as_deref().is_some_and(|token| token.starts_with("rt_oauth")), "the refresh token is stored");
    assert!(session.dpop_key.as_deref().is_some_and(|key| key.len() >= 43), "the DPoP key is stored");
    let state_text = std::fs::read_to_string(dir.join("state.json")).expect("state file");
    assert!(state_text.contains("rt_oauth") && state_text.contains("atproto_sessions"), "the refresh token lives in the state map: {state_text}");
    assert!(!state_text.contains("unused-password"), "the password never reaches state");

    // The wire discipline the local-client profile demands (all against the fake AS).
    let requests = server.requests();
    let par = requests.iter().find(|request| request.path == "/oauth/par").expect("the PAR request");
    assert_eq!(par.body.get("client_id").and_then(Value::as_str), Some("http://localhost"), "the literal loopback client id");
    assert_eq!(par.body.get("response_type").and_then(Value::as_str), Some("code"));
    assert_eq!(par.body.get("scope").and_then(Value::as_str), Some("atproto"), "the implicit localhost metadata's one scope");
    assert!(par.body.get("nonce").is_none(), "the atproto profile sends no nonce (the request_uri binding replaces it)");
    assert_eq!(par.body.get("code_challenge_method").and_then(Value::as_str), Some("S256"));
    let redirect = par.body.get("redirect_uri").and_then(Value::as_str).unwrap_or_default();
    assert!(redirect.starts_with("http://127.0.0.1:") && redirect.ends_with('/'), "the redirect is the loopback root: {redirect}");
    let token = requests.iter().find(|request| request.path == "/oauth/token").expect("the token request");
    assert_eq!(token.body.get("grant_type").and_then(Value::as_str), Some("authorization_code"));
    assert!(token.body.get("code_verifier").and_then(Value::as_str).is_some_and(|verifier| verifier.len() >= 43), "the PKCE verifier rides the exchange");
    assert!(token.body.get("refresh_token").is_none(), "the initial grant is not a refresh");
    // The silent-refresh and service-auth steps of the auto-resume used the OAuth session.
    let service_auth = requests
        .iter()
        .find(|request| request.path.starts_with("/xrpc/com.atproto.server.getServiceAuth"))
        .expect("service-auth request");
    assert!(service_auth.bearer.starts_with("Bearer ey"), "the OAuth access token minted the proof");
}

#[test]
fn a_state_mismatch_or_foreign_issuer_binds_nothing() {
    let server = FakeServer::start();
    server.add_account("keeper.bsky.example", "unused-password", "did:plc:keeper");
    let (hub, _dir, _page, address) = operator_workspace("mismatch", &server);
    let identity = hub.create_identity("keeper", None).expect("identity");

    // Start a real bind, then hand the callback a forged state and a foreign issuer.
    let mut binder = TcpStream::connect(address).expect("connect");
    let payload = "handle=keeper.bsky.example";
    let started = http_round_trip(
        &mut binder,
        &format!(
            "POST /bind/{}/atproto HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Type: application/x-www-form-urlencoded\r\nContent-Length: {}\r\nConnection: close\r\n\r\n{payload}",
            identity.local_id,
            payload.len()
        ),
    );
    assert!(started.starts_with("HTTP/1.1 302"), "the bind started: {started}");

    let forged = get(address, "/?code=code-999&state=forged-state&iss=http://127.0.0.1:1");
    assert!(forged.starts_with("HTTP/1.1 200"), "the failure still renders a page: {forged}");
    assert!(forged.contains("state_mismatch") && forged.contains("Bind failed"), "the failure names the code: {forged}");
    assert!(hub.identity(&identity.local_id).unwrap().bound_did.is_none(), "nothing was bound");
    assert!(hub.store().lock().unwrap().atproto_session(&identity.local_id).is_none(), "no session was stored");

    // The in-flight bind is still completable... no — it was consumed by the forged
    // attempt (single use): the honest answer for a replay is a fresh start.
    let replay = get(address, "/?code=code-1&state=whatever");
    assert!(replay.contains("state_mismatch"), "a second state is refused too: {replay}");
}

#[test]
fn an_unknown_provider_or_identity_is_an_honest_404_page() {
    let server = FakeServer::start();
    let (hub, _dir, _page, address) = operator_workspace("forty-four", &server);
    let identity = hub.create_identity("plain", None).expect("identity");

    let provider = get(address, &format!("/bind/{}/github", identity.local_id));
    assert!(provider.starts_with("HTTP/1.1 404"), "the unknown provider is a 404: {provider}");
    assert!(provider.contains("Unknown bind provider 'github'") && provider.contains("atproto"), "the page names what lives there: {provider}");

    let unknown = get(address, "/bind/ox_missing/atproto");
    assert!(unknown.starts_with("HTTP/1.1 404"), "the unknown identity is a 404: {unknown}");
    assert!(unknown.contains("No local identity"), "the page says so honestly: {unknown}");

    // POST routes refuse the same way.
    let mut post = TcpStream::connect(address).expect("connect");
    let posted = http_round_trip(
        &mut post,
        &format!(
            "POST /bind/{}/github HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Type: application/x-www-form-urlencoded\r\nContent-Length: 7\r\nConnection: close\r\n\r\nhandle=x",
            identity.local_id
        ),
    );
    assert!(posted.starts_with("HTTP/1.1 404"), "the POST refuses the unknown provider: {posted}");
    assert!(hub.identity(&identity.local_id).unwrap().bound_did.is_none());
}

// ---------- the silent refresh ----------

#[test]
fn an_expired_oauth_session_refreshes_silently_before_use() {
    let server = FakeServer::start();
    server.add_account("wanderer.bsky.example", "unused-password", "did:plc:wanderer");
    // Tokens issued from here expire inside the refresh margin.
    server.set_oauth_token_lifetime(30);
    let (hub, _dir, _page, address) = operator_workspace("refresh", &server);
    let identity = hub.create_identity("wanderer", None).expect("identity");

    let callback_page = drive_bind(address, &server, &identity.local_id, "wanderer.bsky.example");
    assert!(callback_page.contains("Bound as <strong>wanderer.bsky.example</strong>"), "the bind completed: {callback_page}");
    let stale = hub.store().lock().unwrap().atproto_session(&identity.local_id).expect("session").access_jwt;

    // The enrollment uses the PDS session: the stale token must refresh first (silently
    // — the enrollment simply succeeds and the stored token changes).
    let entry = hub.enroll_bound(&identity.local_id, server.origin()).expect("bound enrollment with a silent refresh");
    assert_eq!(entry.did.as_deref(), Some("did:plc:wanderer"));

    let requests = server.requests();
    let refresh = requests
        .iter()
        .find(|request| request.path == "/oauth/token" && request.body.get("grant_type").and_then(Value::as_str) == Some("refresh_token"))
        .expect("a refresh grant ran before use");
    assert_eq!(refresh.body.get("client_id").and_then(Value::as_str), Some("http://localhost"));

    let renewed = hub.store().lock().unwrap().atproto_session(&identity.local_id).expect("renewed session");
    assert_ne!(renewed.access_jwt, stale, "the access token was replaced");
    assert!(renewed.access_jwt.split('.').count() == 3, "still JWT-shaped");
    assert!(renewed.refresh_jwt.as_deref().is_some_and(|token| token.starts_with("rt_oauth")), "the rotated refresh token is stored");

    // The proof mint rode the renewed token, not the stale one.
    let service_auth = requests
        .iter()
        .find(|request| request.path.starts_with("/xrpc/com.atproto.server.getServiceAuth"))
        .expect("service-auth request");
    assert_eq!(service_auth.bearer, format!("Bearer {}", renewed.access_jwt), "the refreshed token minted the proof");
}

// ---------- the fixed port and its honest conflicts ----------

#[test]
fn the_port_discipline_is_default_then_environment_then_flag() {
    use tangent_connector::adapters::operator::{port_from, DEFAULT_PORT};
    assert_eq!(port_from(None, None).unwrap(), DEFAULT_PORT);
    assert_eq!(port_from(None, Some("")).unwrap(), DEFAULT_PORT, "an empty environment value falls back");
    assert_eq!(port_from(None, Some("5321")).unwrap(), 5321);
    assert_eq!(port_from(Some(0), Some("5321")).unwrap(), 0, "the flag wins, and 0 stays ephemeral");
    assert_eq!(port_from(Some(60123), None).unwrap(), 60123);
    let refused = port_from(None, Some("not-a-port")).expect_err("garbage is an honest refusal");
    assert!(refused.contains("TANGENT_CONNECTOR_PORT"), "the message names the knob: {refused}");
}

#[test]
fn an_in_use_port_is_a_refusal_naming_the_holder() {
    let dir = std::env::temp_dir().join(format!("tangent-connector-bind-conflict-{}", std::process::id()));
    let _ = std::fs::remove_dir_all(&dir);
    std::fs::create_dir_all(&dir).expect("temp dir");
    // The lockfile names the one-process holder (us, in this test).
    let _lock = tangent_connector::adapters::lockfile::DataDirLock::acquire(&dir, false).expect("lock");
    // Another listener holds the fixed port.
    let squatter = TcpListener::bind(("127.0.0.1", operator::DEFAULT_PORT)).expect("squatter binds 5219");
    let refused = operator::bind_listener(&dir, operator::DEFAULT_PORT).expect_err("the bind conflict refuses");
    drop(squatter);
    assert!(refused.contains("already listening"), "names the conflict: {refused}");
    assert!(refused.contains(&format!("pid={}", std::process::id())), "names the lockfile holder: {refused}");
    assert!(refused.contains("TANGENT_CONNECTOR_PORT"), "offers the escape: {refused}");
    // With the port free again, the same call binds.
    assert!(operator::bind_listener(&dir, operator::DEFAULT_PORT).is_ok(), "the fixed port binds once free");
}

// ---------- helpers ----------

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
