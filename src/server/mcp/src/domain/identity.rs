//! Local identity, enrollment and context bindings. Handles are routing state, never
//! credentials: an identity is a locally minted participant persona, an enrollment is the
//! credential + server binding for one identity at one origin, and a context binds one
//! enrollment to one caller for one canonical origin.

use serde::{Deserialize, Serialize};

/// The local caller identity. stdio v1 admits exactly one operator-approved caller per
/// process; the field exists so a later daemon does not silently merge principals. For the
/// MCP intake the caller is `mcp:{clientInfo.name}`; the CLI and operator intakes are
/// `cli` and `operator`. Attribution and feed labeling only — identity resolution is the
/// same behavior for every caller.
#[derive(Debug, Clone, PartialEq, Eq, Hash, Serialize, Deserialize)]
pub struct CallerId(pub String);

impl CallerId {
    /// The `clientInfo.name` this caller was admitted under, when it is an MCP caller.
    /// Used for feed attribution ("model (via {client})"), never for a domain decision.
    pub fn mcp_client_name(&self) -> Option<&str> {
        self.0.strip_prefix("mcp:")
    }
}

impl Default for CallerId {
    fn default() -> Self {
        Self("stdio".to_string())
    }
}

/// One local identity: the persona the connector acts as. Minted locally (GUIDv7,
/// immutable); binding it to an atproto DID is a later wave, so `bound_did` is normally
/// `None`. The handle is unique within the connector.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Identity {
    /// Connector-minted GUIDv7 (32 hex chars). Never formatted as a DID.
    pub local_id: String,
    /// Operator-chosen handle, 2..=253 characters, unique (case-insensitive) within the
    /// connector. Used for moniker selection.
    pub handle: String,
    pub display_name: Option<String>,
    /// The atproto DID once a binding is verified; `None` in the unbound tier.
    #[serde(default)]
    pub bound_did: Option<String>,
    pub created_at: i64,
}

/// Identity handle discipline: 2..=253 characters, no whitespace, no separators.
pub fn valid_handle(handle: &str) -> bool {
    let trimmed = handle.trim();
    (2..=253).contains(&trimmed.chars().count())
        && trimmed == handle
        && !handle.chars().any(|c| c.is_whitespace() || c == ':')
}

/// One enrollment: the session + server binding for one identity at one origin. Manual
/// import of an existing session token, or an unbound enrollment performed against the
/// server's enrollment endpoint. Verified at enrollment time against the server's own
/// identity response. The bearer session itself lives in the store's per-enrollment
/// session map — deliberately NOT on this struct, so cloned entries never carry it.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct CompanionEntry {
    /// Stable local handle for this enrollment, e.g. `cmp_lumen`; also its session key.
    pub companion_id: String,
    /// The owning identity's `local_id`. Legacy entries without one are dropped at load.
    #[serde(default)]
    pub local_id: String,
    /// Operator-chosen short name used for selection matching.
    pub name: String,
    /// Canonical server origin this enrollment is bound to.
    pub origin: String,
    /// The server's canonical participant reference (its GUIDv7 id per the W2 contract).
    #[serde(default)]
    pub participant_ref: String,
    /// The participant's atproto DID when the server reports one; optional since W2.
    #[serde(default)]
    pub did: Option<String>,
    /// Display name and handle as the server reports them (presentation only).
    pub display_name: Option<String>,
    pub handle: Option<String>,
    pub enrolled_at: i64,
    /// Whether the background checker runs for this enrollment while the process lives.
    pub auto_check: bool,
}

impl CompanionEntry {
    /// Moniker matching mirrors the server's companion selection: exact participant
    /// reference or DID, exact handle (case-insensitive, one optional leading @), or the
    /// local name. Never a display-name guess.
    pub fn matches(&self, moniker: &str) -> bool {
        let trimmed = moniker.trim();
        if trimmed.is_empty() || trimmed.len() > 253 {
            return false;
        }
        if trimmed == self.name || trimmed == self.companion_id || trimmed == self.participant_ref {
            return true;
        }
        if let Some(did) = &self.did {
            if trimmed == did {
                return true;
            }
        }
        let Some(handle) = &self.handle else { return false };
        let supplied = trimmed.strip_prefix('@').unwrap_or(trimmed);
        supplied.eq_ignore_ascii_case(handle)
    }
}

/// The atproto session one identity holds after the operator binds an app password:
/// the PDS-issued bearer (`accessJwt`, cookie-jar posture — same exposure class as the
/// per-enrollment sessions) plus where to reach the PDS again. The app password itself is
/// NEVER part of this record: it exists in memory for the one `createSession` request.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct AtprotoSession {
    /// The bound account's DID (`did:plc:…`); mirrors the identity's `bound_did`.
    pub did: String,
    /// The handle the PDS confirmed (canonical form, no leading `@`).
    pub handle: String,
    /// The PDS access token used for `getServiceAuth`. Session state, not a vault secret.
    pub access_jwt: String,
    /// Canonical PDS origin for follow-up service-auth requests.
    pub pds: String,
    /// Epoch milliseconds of the `createSession` that produced this session.
    pub obtained_at: i64,
}

/// A participation context: caller + enrollment + canonical origin + credential binding.
/// The acting identity rides the enrollment; it is never chosen here.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct LocalContext {
    /// Stable local handle, e.g. `ctx_9f01ab`.
    pub context_id: String,
    pub caller: CallerId,
    pub companion_id: String,
    pub origin: String,
    /// The enrollment's participant reference at enrollment time (presentation only).
    #[serde(default)]
    pub participant_ref: String,
    pub created_at: i64,
    pub last_used_at: i64,
}

impl LocalContext {
    /// A context is usable only for the exact caller and enrollment it was issued to.
    pub fn belongs_to(&self, caller: &CallerId, companion_id: &str) -> bool {
        &self.caller == caller && self.companion_id == companion_id
    }
}
