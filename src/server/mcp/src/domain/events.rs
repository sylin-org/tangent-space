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
    Shutdown { reason: String },
}
