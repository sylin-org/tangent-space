//! Local identity and context bindings. Handles are routing state, never credentials: a
//! companion is an enrolled credential plus its verified identity and server binding, and a
//! context binds one companion to one canonical server origin for one caller.

use serde::{Deserialize, Serialize};

/// The local caller identity. stdio v1 admits exactly one operator-approved caller per
/// process; the field exists so a later daemon does not silently merge principals.
#[derive(Debug, Clone, PartialEq, Eq, Hash, Serialize, Deserialize)]
pub struct CallerId(pub String);

impl Default for CallerId {
    fn default() -> Self {
        Self("stdio".to_string())
    }
}

/// One enrolled companion: manual import of a scoped participant credential, verified at
/// enrollment against the server's own experience identity response.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct CompanionEntry {
    /// Stable local handle, e.g. `cmp_lumen`.
    pub companion_id: String,
    /// Operator-chosen short name used for selection matching.
    pub name: String,
    /// Canonical server origin this companion is enrolled against.
    pub origin: String,
    /// Verified participant DID from the server's identity response.
    pub did: String,
    /// Display name and handle as the server reports them (presentation only).
    pub display_name: Option<String>,
    pub handle: Option<String>,
    pub enrolled_at: i64,
    /// Whether the background checker runs for this companion while the process lives.
    pub auto_check: bool,
    /// Where the credential is kept: `platform` store or `plaintext-dev` fallback.
    pub credential_source: String,
}

impl CompanionEntry {
    /// Moniker matching mirrors the server's companion selection: exact DID, or exact handle
    /// (case-insensitive, one optional leading @), or the local name. Never a display-name guess.
    pub fn matches(&self, moniker: &str) -> bool {
        let trimmed = moniker.trim();
        if trimmed.is_empty() || trimmed.len() > 253 {
            return false;
        }
        if trimmed == self.did || trimmed == self.name {
            return true;
        }
        let Some(handle) = &self.handle else { return false };
        let supplied = trimmed.strip_prefix('@').unwrap_or(trimmed);
        supplied.eq_ignore_ascii_case(handle)
    }
}

/// A participation context: caller + companion + canonical origin + credential binding.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct LocalContext {
    /// Stable local handle, e.g. `ctx_9f01ab`.
    pub context_id: String,
    pub caller: CallerId,
    pub companion_id: String,
    pub origin: String,
    pub did: String,
    pub created_at: i64,
    pub last_used_at: i64,
}

impl LocalContext {
    /// A context is usable only for the exact caller and companion it was issued to.
    pub fn belongs_to(&self, caller: &CallerId, companion_id: &str) -> bool {
        &self.caller == caller && self.companion_id == companion_id
    }
}
