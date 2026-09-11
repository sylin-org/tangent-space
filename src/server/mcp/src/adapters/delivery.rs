//! Host delivery modes, reported truthfully. v1 supports exactly one: bounded pending
//! attention rides along with later tool responses. Resource notifications and host-event
//! adapters are distinct modes that must never be advertised without a verified host test.

use crate::domain::attention::AttentionRecord;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum DeliveryMode {
    /// Queued attention is delivered inside the next tool response. This does not wake an
    /// idle host; it is the always-available fallback.
    ToolResponseOnly,
    /// MCP resource update notifications (subscriptions). Not implemented in v1.
    ResourceNotification,
    /// A verified host-event adapter that can start a turn. Not implemented in v1.
    HostAdapter,
}

impl DeliveryMode {
    pub const fn as_str(self) -> &'static str {
        match self {
            Self::ToolResponseOnly => "tool_response_only",
            Self::ResourceNotification => "resource_notification",
            Self::HostAdapter => "host_adapter",
        }
    }

    /// The effective mode of this build. Automatic turns start disabled; an operator-enabled
    /// verified adapter would change this, and its claim would need a real host test.
    pub const fn effective() -> Self {
        Self::ToolResponseOnly
    }
}

/// The bounded attention segment a tool response carries in tool-response-only mode.
pub fn segment(records: &[AttentionRecord], limit: usize) -> Vec<String> {
    let mut lines = Vec::new();
    let directed: Vec<&AttentionRecord> = records.iter().filter(|record| record.is_directed()).take(limit).collect();
    if records.is_empty() {
        return lines;
    }
    let waiting: usize = records.iter().filter(|record| record.is_directed()).count();
    if waiting > directed.len() {
        lines.push(format!("{waiting} item(s) awaiting you; {} shown.", directed.len()));
    } else if waiting > 0 {
        lines.push(format!("{waiting} item(s) awaiting you."));
    }
    lines
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::domain::attention::{AttentionRecord, AttentionState};

    fn record(id: &str, relationship: Option<&str>) -> AttentionRecord {
        AttentionRecord {
            id: id.into(),
            companion_id: "cmp_x".into(),
            kind: "direct_mention".into(),
            actor_ref: "did:plc:a".into(),
            actor_name: Some("Leo".into()),
            scope_ref: "origin::t::r".into(),
            source_ref: format!("origin::t::r::{id}"),
            relationship: relationship.map(str::to_string),
            excerpt: "hello".into(),
            source_revision: "rev".into(),
            state: AttentionState::Pending,
            first_seen_at: 0,
            last_seen_at: 0,
            revision_seen: "rev".into(),
        }
    }

    #[test]
    fn the_effective_mode_is_truthful() {
        assert_eq!(DeliveryMode::effective(), DeliveryMode::ToolResponseOnly);
        assert_eq!(DeliveryMode::effective().as_str(), "tool_response_only");
    }

    #[test]
    fn the_segment_counts_directed_attention_only() {
        let records = vec![
            record("m1", Some("addressed_to_you")),
            record("m2", Some("replies_to_you")),
            record("m3", None),
        ];
        let lines = segment(&records, 5);
        assert_eq!(lines, vec!["2 item(s) awaiting you.".to_string()]);
    }

    #[test]
    fn an_empty_segment_reports_nothing() {
        assert!(segment(&[], 5).is_empty());
    }
}
