//! The closed in-process event vocabulary. Adapters and the poller publish; delivery and the
//! diagnostics journal subscribe. Fan-out is best-effort in-process messaging, not a broker.

use serde::Serialize;

use crate::domain::intake::IntakeChannel;

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "snake_case", tag = "kind")]
pub enum DomainEvent {
    CompanionSelected { companion_id: String },
    ContextArrived { context_id: String, origin: String },
    PollCompleted { companion_id: String, revision: String, waiting: i64, activity: i64 },
    PollFailed { companion_id: String, attempt: u32, reason: String },
    BackoffScheduled { companion_id: String, seconds: u64 },
    AttentionObserved { companion_id: String, new_items: Vec<String>, coalesced: usize },
    AttentionDelivered { companion_id: String, item_count: usize, delivery_mode: String },
    WriteRegistered { request_id: String, operation: String },
    WriteSettled { request_id: String, state: String },
    ToolInvoked { channel: IntakeChannel, tool: String },
    ToolCompleted { channel: IntakeChannel, tool: String, status: String, text_bytes: usize },
    /// The serve-mode operator page became ready. The loopback URL is journal material
    /// (the operator's recovery path once stderr has scrolled away) and state material
    /// (any process's Connect pops this page); it is never model-visible output. The
    /// SSE feed skips it.
    OperatorPageReady { url: String },
    /// The on-the-fly handshake's live progress (owner addendum): narration for the
    /// operator page's activity feed. Presentation-only — never a token, password,
    /// proof or session value. `identity` is the identity handle; `code` on failure is
    /// the honest problem code the model saw; `initiator` names who started this
    /// connect ("model (via {client})" for MCP tool calls, "operator (CLI)" for the
    /// command line, "operator (page)" for page-driven actions and the auto-resume).
    ConnectStarted { origin: String, initiator: String },
    ConnectResolved { origin: String, identity: String, initiator: String },
    ConnectWaitingForOperator { origin: String, identity: String, needed: String, initiator: String },
    ConnectOperatorCompleted { origin: String, identity: String, initiator: String },
    ConnectEnrolled { origin: String, identity: String, initiator: String },
    ConnectArrived { origin: String, identity: String, initiator: String },
    ConnectFailed { origin: String, identity: String, code: String, initiator: String },
    Shutdown { reason: String },
}
