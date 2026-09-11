//! Operator attention policy. Server posting limits and connector spending policy are
//! independent; neither a post nor a coordinator enlarges another participant's allowance.

use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct AttentionPolicy {
    /// Seconds between ordinary background checks. Separate from visit frequency.
    pub poll_seconds: u64,
    /// Minimum seconds between automatic turn attempts for one companion (coalescing window).
    pub cooldown_seconds: u64,
    /// Recipient-wide automatic turn allowance per UTC day, across all connected servers.
    pub daily_turn_allowance: u32,
    /// Automatic turns start disabled until an operator enables a working adapter.
    pub automatic_turns: bool,
    /// Allowed attention sender DIDs. None means every participant may request attention;
    /// unknown senders still never acquire authority, only deference.
    pub allowed_senders: Option<Vec<String>>,
}

impl Default for AttentionPolicy {
    fn default() -> Self {
        Self {
            poll_seconds: 300,
            cooldown_seconds: 300,
            daily_turn_allowance: 12,
            automatic_turns: false,
            allowed_senders: None,
        }
    }
}

/// The mutable accounting side of the policy.
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct PolicyLedger {
    /// UTC day key (YYYY-MM-DD) the counters belong to.
    pub day_key: String,
    /// Automatic turns consumed today, across every connected server and sender.
    pub turns_used: u32,
    /// Epoch seconds of the last automatic turn attempt.
    pub last_turn_at: i64,
}

impl PolicyLedger {
    pub fn day_key(now_secs: i64) -> String {
        // Civil-days conversion (Howard Hinnant's algorithm), no chrono dependency.
        let days = now_secs.div_euclid(86_400);
        let z = days + 719_468;
        let era = z.div_euclid(146_097);
        let doe = z.rem_euclid(146_097);
        let yoe = (doe - doe / 1460 - doe / 36_524 + doe / 146_096) / 365;
        let y = yoe + era * 400;
        let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
        let mp = (5 * doy + 2) / 153;
        let d = doy - (153 * mp + 2) / 5 + 1;
        let m = if mp < 10 { mp + 3 } else { mp - 9 };
        let year = if m <= 2 { y + 1 } else { y };
        format!("{year:04}-{m:02}-{d:02}")
    }

    fn roll_day(&mut self, now_secs: i64) {
        let key = Self::day_key(now_secs);
        if key != self.day_key {
            self.day_key = key;
            self.turns_used = 0;
        }
    }

    /// Whether an automatic model turn may be attempted now, for this sender. The
    /// recipient-wide allowance binds across senders and servers: no sender coalition can
    /// exceed it, and an unknown sender merely defers.
    pub fn may_auto_turn(&mut self, policy: &AttentionPolicy, sender_did: &str, now_secs: i64) -> TurnDecision {
        self.roll_day(now_secs);
        if !policy.automatic_turns {
            return TurnDecision::Disabled;
        }
        if let Some(allowed) = &policy.allowed_senders {
            if !allowed.iter().any(|did| did == sender_did) {
                return TurnDecision::SenderDeferred;
            }
        }
        if self.turns_used >= policy.daily_turn_allowance {
            return TurnDecision::AllowanceExhausted;
        }
        if now_secs.saturating_sub(self.last_turn_at) < policy.cooldown_seconds as i64 {
            return TurnDecision::Cooldown;
        }
        TurnDecision::Allowed
    }

    /// Records a consumed automatic turn. Persisted before any adapter dispatch.
    pub fn record_turn(&mut self, now_secs: i64) {
        self.roll_day(now_secs);
        self.turns_used += 1;
        self.last_turn_at = now_secs;
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum TurnDecision {
    Allowed,
    Disabled,
    SenderDeferred,
    AllowanceExhausted,
    Cooldown,
}

impl TurnDecision {
    pub fn allowed(self) -> bool {
        matches!(self, TurnDecision::Allowed)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn day_key_matches_known_dates() {
        assert_eq!(PolicyLedger::day_key(1_758_048_000), "2025-09-16"); // 2025-09-16T16:00Z
        assert_eq!(PolicyLedger::day_key(0), "1970-01-01");
    }

    #[test]
    fn senders_cannot_bypass_the_total_allowance() {
        let policy = AttentionPolicy { automatic_turns: true, daily_turn_allowance: 2, ..Default::default() };
        let mut ledger = PolicyLedger { day_key: PolicyLedger::day_key(1000), turns_used: 0, last_turn_at: 0 };
        let now = 10_000;
        assert!(ledger.may_auto_turn(&policy, "did:plc:a", now).allowed());
        ledger.record_turn(now);
        assert!(ledger.may_auto_turn(&policy, "did:plc:b", now + 1000).allowed(), "cooldown is 300s");
        ledger.record_turn(now + 1000);
        // Same UTC day (86_400s), well past cooldown: the recipient-wide cap binds across senders.
        for sender in ["did:plc:a", "did:plc:b", "did:plc:c"] {
            assert_eq!(ledger.may_auto_turn(&policy, sender, now + 5000), TurnDecision::AllowanceExhausted);
        }
        // A new day restores the allowance.
        assert!(ledger.may_auto_turn(&policy, "did:plc:c", now + 90_000).allowed());
    }

    #[test]
    fn unknown_senders_defer_and_remain_inspectable() {
        let policy = AttentionPolicy {
            automatic_turns: true,
            cooldown_seconds: 1,
            allowed_senders: Some(vec!["did:plc:known".into()]),
            ..Default::default()
        };
        let mut ledger = PolicyLedger::default();
        assert_eq!(ledger.may_auto_turn(&policy, "did:plc:known", 60), TurnDecision::Allowed);
        assert_eq!(ledger.may_auto_turn(&policy, "did:plc:stranger", 60), TurnDecision::SenderDeferred);
    }

    #[test]
    fn automatic_turns_start_disabled() {
        let policy = AttentionPolicy::default();
        let mut ledger = PolicyLedger::default();
        assert_eq!(ledger.may_auto_turn(&policy, "did:plc:any", 60), TurnDecision::Disabled);
    }

    #[test]
    fn cooldown_coalesces_repeated_turns() {
        let policy = AttentionPolicy { automatic_turns: true, cooldown_seconds: 300, ..Default::default() };
        let mut ledger = PolicyLedger::default();
        assert!(ledger.may_auto_turn(&policy, "did:plc:a", 1000).allowed());
        ledger.record_turn(1000);
        assert_eq!(ledger.may_auto_turn(&policy, "did:plc:a", 1100), TurnDecision::Cooldown);
        assert!(ledger.may_auto_turn(&policy, "did:plc:a", 1301).allowed());
    }
}
