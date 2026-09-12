//! The closed in-process event vocabulary. Adapters and the poller publish; delivery and the
//! diagnostics journal subscribe. Fan-out is best-effort in-process messaging, not a broker.

use serde::Serialize;

use crate::domain::intake::IntakeChannel;

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "snake_case", tag = "kind")]
pub enum DomainEvent {
    CompanionSelected { companion_id: String },
    ContextArrived { context_id: String, origin: String },
    /// A persisted enrollment was dropped at load: it predates the identity model, so it
    /// fails the identity join honestly. Re-enrollment is the documented path.
    EnrollmentDropped { companion_id: String, name: String, reason: String },
    PollCompleted { companion_id: String, revision: String, waiting: i64, activity: i64 },
    PollFailed { companion_id: String, attempt: u32, reason: String },
    BackoffScheduled { companion_id: String, seconds: u64 },
    AttentionObserved { companion_id: String, new_items: Vec<String>, coalesced: usize },
    AttentionDelivered { companion_id: String, item_count: usize, delivery_mode: String },
    WriteRegistered { request_id: String, operation: String },
    WriteSettled { request_id: String, state: String },
    ToolInvoked { channel: IntakeChannel, tool: String },
    ToolCompleted { channel: IntakeChannel, tool: String, status: String, text_bytes: usize },
    /// The serve-mode operator page became ready. Deliberate exception to the no-secrets
    /// rule (W2-D brief): the URL carries the one-time page token so the operator can
    /// recover it from the local diagnostics journal after stderr scrolls away. The
    /// journal is user-profile state, never model-visible output. The SSE feed skips it.
    OperatorPageReady { url: String },
    /// The on-the-fly handshake's live progress (owner addendum): narration for the
    /// operator page's activity feed. Presentation-only — never a token, password,
    /// proof or session value. `identity` is the identity handle; `code` on failure is
    /// the honest problem code the model saw.
    ConnectStarted { origin: String },
    ConnectResolved { origin: String, identity: String },
    ConnectWaitingForOperator { origin: String, identity: String, needed: String },
    ConnectOperatorCompleted { origin: String, identity: String },
    ConnectEnrolled { origin: String, identity: String },
    ConnectArrived { origin: String, identity: String },
    ConnectFailed { origin: String, identity: String, code: String },
    Shutdown { reason: String },
}
