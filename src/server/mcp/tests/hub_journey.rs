//! Focused hub checks against the scripted fake experience server (synthetic data). Covers
//! the acceptance invariants that live in the connector: identity/context isolation, honest
//! transport failures, crash-safe mutation recovery, conflict rejection, attention
//! coalescing, and you-rendered presentation.

mod common;

use std::sync::Arc;

use serde_json::json;

use common::{EventRecorder, FakeServer, LUMEN_CREDENTIAL, REVOKED_CREDENTIAL};
use tangent_connector::adapters::experience::UreqExperience;
use tangent_connector::adapters::store::StateStore;
use tangent_connector::application::bus::EventBus;
use tangent_connector::application::hub::ConnectorHub;
use tangent_connector::application::ports::ExperiencePort;
use tangent_connector::domain::events::DomainEvent;
use tangent_connector::domain::identity::CallerId;

struct Workspace {
    hub: Arc<ConnectorHub>,
    events: Arc<EventBus>,
    dir: std::path::PathBuf,
}

fn workspace(label: &str) -> Workspace {
    // Test credentials are synthetic: never touch the real platform credential store.
    std::env::set_var("TANGENT_CONNECTOR_PLAINTEXT_CREDENTIALS", "1");
    let dir = std::env::temp_dir().join(format!("tangent-connector-test-{}-{}", label, std::process::id()));
    let _ = std::fs::remove_dir_all(&dir);
    std::fs::create_dir_all(&dir).expect("temp dir");
    let events = Arc::new(EventBus::new());
    let port: Arc<dyn ExperiencePort> = Arc::new(UreqExperience::new());
    let store = StateStore::open(&dir).expect("store");
    let hub = Arc::new(ConnectorHub::new(port, store, events.clone(), CallerId("cli".into()), dir.clone()));
    Workspace { hub, events, dir }
}

fn enrolled(hub: &ConnectorHub, server: &FakeServer) -> String {
    let entry = hub.enroll("lumen", server.origin(), LUMEN_CREDENTIAL, false).expect("enrollment");
    entry.companion_id
}

fn arrived(hub: &ConnectorHub, server: &FakeServer, companion_id: &str) -> String {
    let outcome = hub.invoke(
        tangent_connector::domain::intake::IntakeChannel::Cli,
        "Arrive",
        &json!({ "companionId": companion_id, "serverUrl": server.origin() }),
    );
    assert!(!outcome.is_error, "arrival failed: {}", outcome.text);
    outcome
        .structured
        .pointer("/connector/contextId")
        .and_then(|value| value.as_str())
        .expect("context id")
        .to_string()
}

#[test]
fn arrival_renders_orientation_with_you_identity() {
    let server = FakeServer::start();
    let work = workspace("arrival");
    let companion = enrolled(&work.hub, &server);
    let outcome = work.hub.invoke(
        tangent_connector::domain::intake::IntakeChannel::Cli,
        "Arrive",
        &json!({ "companionId": companion, "serverUrl": server.origin() }),
    );
    assert_eq!(outcome.status, "ok");
    assert!(outcome.text.contains("you are participating as Lumen"), "text was: {}", outcome.text);
    assert!(outcome.text.contains("Kintsugi Architecture"));
    assert_eq!(
        outcome.structured.pointer("/experience/experienceVersion").and_then(|value| value.as_str()),
        Some("1.0")
    );
    assert_eq!(
        outcome.structured.pointer("/connector/deliveryMode").and_then(|value| value.as_str()),
        Some("tool_response_only")
    );
    // Arrival mentioned the pending directed request with its actor and an alias.
    assert!(outcome.text.contains("Leo asked you"), "text was: {}", outcome.text);
}

#[test]
fn an_arrival_at_a_different_destination_never_rebinds() {
    let server = FakeServer::start();
    let work = workspace("rebind");
    let companion = enrolled(&work.hub, &server);
    let outcome = work.hub.invoke(
        tangent_connector::domain::intake::IntakeChannel::Cli,
        "Arrive",
        &json!({ "companionId": companion, "serverUrl": "https://elsewhere.example" }),
    );
    assert!(outcome.is_error);
    assert!(outcome.text.contains("does not match this companion's server"), "text was: {}", outcome.text);
    assert_eq!(
        outcome.structured.pointer("/problem/code").and_then(|value| value.as_str()),
        Some("unreachable")
    );
    let requests = server.requests();
    assert!(requests.iter().all(|request| request.path != "/api/v1/experience/tangents"), "no call may leave for the wrong origin");
}

#[test]
fn context_handles_do_not_cross_connector_states() {
    // E02: a context issued under one caller/companion/store is unknown in another.
    let server = FakeServer::start();
    let left = workspace("iso-left");
    let right = workspace("iso-right");
    let companion = enrolled(&left.hub, &server);
    let context = arrived(&left.hub, &server, &companion);
    let outcome = right.hub.invoke(
        tangent_connector::domain::intake::IntakeChannel::Cli,
        "ListTangents",
        &json!({ "contextId": context }),
    );
    assert!(outcome.is_error);
    assert_eq!(
        outcome.structured.pointer("/problem/code").and_then(|value| value.as_str()),
        Some("context_expired")
    );
}

#[test]
fn lost_write_responses_recover_without_a_duplicate() {
    // E07: the first dispatch dies in transit; a restart with the same tuple reconciles to
    // one accepted receipt, and the journal settles exactly once.
    let server = FakeServer::start();
    let first = workspace("crash-first");
    let companion = enrolled(&first.hub, &server);
    let context = arrived(&first.hub, &server, &companion);
    let topic = format!("{}::home::lounge", server.origin());
    let arguments = json!({
        "contextId": context, "topicRef": topic, "requestId": "crash-1", "text": "Reply after the crash."
    });
    let lost = first.hub.invoke(tangent_connector::domain::intake::IntakeChannel::Cli, "CreatePost", &arguments);
    assert!(lost.is_error, "a lost response must not look like success");
    assert!(lost.text.to_lowercase().contains("unreachable"), "text was: {}", lost.text);
    assert!(lost.text.contains("crash-1"), "the saved request id stays visible: {}", lost.text);
    {
        let store = first.hub.store().lock().unwrap();
        let unsettled = store.unsettled_writes();
        assert_eq!(unsettled.len(), 1);
        assert_eq!(unsettled[0].request_id, "crash-1");
    }
    // Restart: same state directory, same tuple.
    let second = Workspace { hub: rebuild(&first.dir), events: first.events.clone(), dir: first.dir.clone() };
    let recovered = second.hub.invoke(tangent_connector::domain::intake::IntakeChannel::Cli, "CreatePost", &arguments);
    assert!(!recovered.is_error, "recovery failed: {}", recovered.text);
    assert!(recovered.text.contains("accepted"), "text was: {}", recovered.text);
    {
        let store = second.hub.store().lock().unwrap();
        assert!(store.unsettled_writes().is_empty(), "the journal settles after reconciliation");
    }
    let requests = server.requests();
    let read_positions = requests.iter().filter(|request| request.path.ends_with("/read-position")).count();
    assert_eq!(read_positions, 0, "no implicit read acknowledgement happens");
}

fn rebuild(dir: &std::path::Path) -> Arc<ConnectorHub> {
    let port: Arc<dyn ExperiencePort> = Arc::new(UreqExperience::new());
    let store = StateStore::open(dir).expect("reopen store");
    let events = Arc::new(EventBus::new());
    Arc::new(ConnectorHub::new(port, store, events, CallerId("cli".into()), dir.to_path_buf()))
}

#[test]
fn a_changed_payload_under_the_same_key_is_a_conflict() {
    let server = FakeServer::start();
    let work = workspace("conflict");
    let companion = enrolled(&work.hub, &server);
    let context = arrived(&work.hub, &server, &companion);
    let topic = format!("{}::home::lounge", server.origin());
    let outcome = work.hub.invoke(
        tangent_connector::domain::intake::IntakeChannel::Cli,
        "CreatePost",
        &json!({ "contextId": context, "topicRef": topic, "requestId": "conflict-1", "text": "Different content." }),
    );
    assert!(outcome.is_error);
    assert_eq!(
        outcome.structured.pointer("/problem/code").and_then(|value| value.as_str()),
        Some("request_conflict")
    );
}

#[test]
fn a_revoked_credential_is_an_honest_enrollment_failure() {
    let server = FakeServer::start();
    let work = workspace("revoked");
    let error = work.hub.enroll("ghost", server.origin(), REVOKED_CREDENTIAL, false).expect_err("must fail");
    assert!(error.contains("rejected"), "error was: {error}");
}

#[test]
fn background_checks_coalesce_repeats_and_enqueue_nothing() {
    // E03/E04: repeated identical digests create one record, publish attention once, and
    // never trigger any delivery or model-facing activity on their own.
    let server = FakeServer::start();
    let work = workspace("poll");
    let companion = enrolled(&work.hub, &server);
    let recorder = EventRecorder(work.events.subscribe());
    let first = work.hub.background_check(&companion).expect("check");
    assert!(first.contains("waiting 1"), "summary was: {first}");
    let second = work.hub.background_check(&companion).expect("check");
    assert_eq!(first, second, "an unchanged digest produces an identical summary");
    let events = recorder.drain();
    let observed = events.iter().filter(|event| matches!(event, DomainEvent::AttentionObserved { .. })).count();
    assert_eq!(observed, 1, "the repeated mention coalesces into one observation");
    let delivered = events.iter().filter(|event| matches!(event, DomainEvent::AttentionDelivered { .. })).count();
    assert_eq!(delivered, 0, "a background check delivers nothing by itself");
    {
        let store = work.hub.store().lock().unwrap();
        let records = store.attention_records(&companion);
        assert_eq!(records.len(), 1);
        assert!(records[0].is_directed());
    }
}

#[test]
fn expanded_reading_preserves_source_words_and_marks_you() {
    let server = FakeServer::start();
    let work = workspace("read");
    let companion = enrolled(&work.hub, &server);
    let context = arrived(&work.hub, &server, &companion);
    let topic = format!("{}::home::lounge", server.origin());
    let outcome = work.hub.invoke(
        tangent_connector::domain::intake::IntakeChannel::Cli,
        "ReadTopic",
        &json!({ "contextId": context, "topicRef": topic, "view": "expanded" }),
    );
    assert!(!outcome.is_error, "text was: {}", outcome.text);
    let text = outcome.text;
    // Source prose is preserved verbatim, laid out as quoted content.
    assert!(text.contains("| Lumen, can you help us coordinate Project Z?"), "text was: {text}");
    assert!(text.contains("\"You should review the whole plan.\""), "quoted prose is not rewritten: {text}");
    // Perspective markers apply to labels, not content.
    assert!(text.contains("Lumen (you)"), "own authorship is marked as you: {text}");
    assert!(text.contains("reply to"), "reply relationships are visible: {text}");
    assert!(text.contains("Reading this response does not acknowledge"), "no implicit read acknowledgement: {text}");
}

#[test]
fn compact_responses_stay_small_and_self_contained() {
    let server = FakeServer::start();
    let work = workspace("compact");
    let companion = enrolled(&work.hub, &server);
    let context = arrived(&work.hub, &server, &companion);
    let topic = format!("{}::home::lounge", server.origin());
    let outcome = work.hub.invoke(
        tangent_connector::domain::intake::IntakeChannel::Cli,
        "ReadTopic",
        &json!({ "contextId": context, "topicRef": topic, "view": "compact" }),
    );
    assert!(!outcome.is_error);
    assert!(outcome.text.starts_with("You: Lumen ·"), "compact keeps its identity anchor: {}", outcome.text.lines().next().unwrap_or_default());
    assert!(outcome.text.len() < 2048, "compact scaffolding stays bounded, was {} bytes", outcome.text.len());
}
