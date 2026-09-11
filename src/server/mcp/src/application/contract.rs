//! The canonical experience response as the connector reads it. Additive minor fields are
//! ignored (serde defaults); an unsupported major version is a recoverable compatibility
//! problem surfaced by [`parse`].

use serde::Deserialize;
use serde_json::Value;

#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ExperienceDto {
    #[serde(default)]
    pub experience_version: String,
    #[serde(default)]
    pub operation: String,
    #[serde(default)]
    pub status: String,
    #[serde(default)]
    pub snapshot: SnapshotDto,
    #[serde(default)]
    pub identity: Option<IdentityDto>,
    #[serde(default)]
    pub place: PlaceDto,
    #[serde(default)]
    pub result: ResultDto,
    #[serde(default)]
    pub attention: AttentionDto,
    #[serde(default)]
    pub continuation: ContinuationDto,
    #[serde(default)]
    pub actions: Vec<ActionDto>,
    #[serde(default)]
    pub orientation: Option<OrientationDto>,
    #[serde(default)]
    pub capabilities: Option<CapabilitiesDto>,
}

impl ExperienceDto {
    pub fn ok(&self) -> bool {
        self.status == "ok"
    }

    pub fn problem(&self) -> Option<&ProblemDto> {
        self.result.problem.as_ref()
    }
}

#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SnapshotDto {
    #[serde(default)]
    pub revision: String,
    #[serde(default)]
    pub coverage: String,
}

#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct IdentityDto {
    /// The server's canonical participant reference (its GUIDv7 id). The connector keys
    /// on this; it is never empty on a usable response.
    #[serde(default)]
    pub participant_ref: String,
    /// The participant's atproto DID when one is held; optional since the W2 contract.
    #[serde(default)]
    pub did: Option<String>,
    #[serde(default)]
    pub display_name: String,
    #[serde(default)]
    pub handle: Option<String>,
    /// The participant's identity collection, best-first for display per the registry
    /// (atproto > internal > connector-client > future kinds). Presentation only.
    #[serde(default)]
    pub identities: Vec<IdentityKindDto>,
}

/// One entry of a participant's identity collection.
#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct IdentityKindDto {
    #[serde(default)]
    pub kind: String,
    #[serde(default)]
    pub value: String,
}

#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PlaceDto {
    #[serde(default)]
    pub server_ref: Option<String>,
    #[serde(default)]
    pub tangent_ref: Option<String>,
    #[serde(default)]
    pub topic_ref: Option<String>,
    #[serde(default)]
    pub label: String,
    #[serde(default)]
    pub role: String,
    #[serde(default)]
    pub allowed_actions: Vec<String>,
}

#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ResultDto {
    #[serde(default)]
    pub data: Value,
    #[serde(default)]
    pub receipt: Option<ReceiptDto>,
    #[serde(default)]
    pub problem: Option<ProblemDto>,
}

#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ReceiptDto {
    #[serde(default)]
    pub request_id: String,
    #[serde(default)]
    pub state: String,
    #[serde(default)]
    pub result_ref: Option<String>,
    #[serde(default)]
    pub retry_after_seconds: Option<u64>,
}

#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ProblemDto {
    #[serde(default)]
    pub code: String,
    #[serde(default)]
    pub message: String,
}

/// The W2 enrollment exchange: `POST {origin}/api/v1/experience/identities/enroll`, no
/// Authorization header (pre-credential). Every outcome is HTTP 200 with a status.
#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct EnrollResponseDto {
    #[serde(default)]
    pub status: String,
    #[serde(default)]
    pub participant: Option<EnrollParticipantDto>,
    #[serde(default)]
    pub credential: Option<EnrollCredentialDto>,
    #[serde(default)]
    pub problem: Option<ProblemDto>,
}

/// The server's view of the enrolled participant.
#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct EnrollParticipantDto {
    #[serde(default)]
    pub participant_ref: String,
    #[serde(default)]
    pub identities: Vec<IdentityKindDto>,
    #[serde(default)]
    pub best_label: Option<String>,
    #[serde(default)]
    pub did: Option<String>,
}

/// The one-time scoped credential. The token crosses custody directly; it is never
/// rendered, journaled or echoed.
#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct EnrollCredentialDto {
    #[serde(default)]
    pub token: String,
    #[serde(default)]
    pub name: Option<String>,
    #[serde(default)]
    pub expires_at: Option<String>,
    #[serde(default)]
    pub grants: Vec<String>,
}

#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AttentionDto {
    #[serde(default)]
    pub revision: String,
    #[serde(default)]
    pub waiting_count: CountDto,
    #[serde(default)]
    pub new_activity_count: CountDto,
    #[serde(default)]
    pub items: Vec<AttentionItemDto>,
    #[serde(default)]
    pub more: bool,
    #[serde(default)]
    pub details_included: bool,
}

#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CountDto {
    #[serde(default)]
    pub value: Option<i64>,
    #[serde(default)]
    pub at_least: bool,
}

impl CountDto {
    /// An unknown count stays unknown; it never becomes zero.
    pub fn describe(&self) -> String {
        match self.value {
            None => "unknown".to_string(),
            Some(value) if self.at_least => format!("{value}+"),
            Some(value) => value.to_string(),
        }
    }
}

#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AttentionItemDto {
    #[serde(default, rename = "ref")]
    pub reference: String,
    #[serde(default)]
    pub kind: String,
    #[serde(default)]
    pub actor_ref: String,
    #[serde(default)]
    pub actor_name: Option<String>,
    #[serde(default)]
    pub recipient_ref: String,
    #[serde(default)]
    pub scope_ref: String,
    #[serde(default)]
    pub source_ref: String,
    #[serde(default)]
    pub relationship: Option<String>,
    #[serde(default)]
    pub excerpt: String,
    #[serde(default)]
    pub source_revision: String,
    #[serde(default)]
    pub state: String,
}

#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ContinuationDto {
    #[serde(default)]
    pub activity_checkpoint: Option<String>,
    #[serde(default)]
    pub activity_page_cursor: Option<String>,
    #[serde(default)]
    pub history_older_cursor: Option<String>,
    #[serde(default)]
    pub history_newer_cursor: Option<String>,
    #[serde(default)]
    pub read_cursor: Option<String>,
    #[serde(default)]
    pub directory_cursor: Option<String>,
}

#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ActionDto {
    #[serde(default)]
    pub name: String,
    #[serde(default)]
    pub target_ref: String,
    #[serde(default)]
    pub around_post_ref: Option<String>,
    #[serde(default)]
    pub label: String,
}

#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct OrientationDto {
    #[serde(default)]
    pub purpose: Option<String>,
    #[serde(default)]
    pub rules: Vec<String>,
    #[serde(default)]
    pub brief: Option<String>,
}

#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CapabilitiesDto {
    #[serde(default)]
    pub attention: bool,
    #[serde(default)]
    pub coordination: bool,
}

/// Parses a raw experience response, rejecting unsupported major contract versions. Minor
/// additive fields are already ignored by deserialization.
pub fn parse(raw: &Value) -> Result<ExperienceDto, String> {
    let major = raw
        .get("experienceVersion")
        .and_then(Value::as_str)
        .and_then(|value| value.split('.').next())
        .unwrap_or("1")
        .to_string();
    if major != "1" {
        let seen = raw.get("experienceVersion").and_then(Value::as_str).unwrap_or("missing").to_string();
        return Err(format!("unsupported experience version {seen}"));
    }
    serde_json::from_value(raw.clone()).map_err(|error| format!("malformed experience response: {error}"))
}
