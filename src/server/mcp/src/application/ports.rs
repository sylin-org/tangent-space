//! Outbound port to a Tangent server's experience API. The hub speaks only this trait; the
//! ureq adapter implements it. `RequestContext` carries the credential for one call and is
//! never logged, journaled or rendered.

use serde_json::Value;

/// Per-call routing context: canonical origin, scoped credential and the participant
/// reference it belongs to. `dpop` carries one RFC 9449 resource-request proof when the
/// call presents an OAuth access token the resource server demands DPoP for (the PDS
/// `getServiceAuth` mint); like the credential, it never renders, logs or journals.
pub struct RequestContext {
    pub origin: String,
    pub credential: String,
    /// The enrollment's participant reference; informational for the adapter.
    pub participant_ref: String,
    /// One DPoP proof header value for this exact request (None for plain Bearer calls).
    pub dpop: Option<String>,
}

#[derive(Debug, thiserror::Error)]
pub enum ExperienceError {
    #[error("the Tangent server could not be reached")]
    Unreachable,
    #[error("authentication was rejected; the stored credential may be expired or revoked")]
    Unauthorized,
    /// RFC 9449 §8 resource-server nonce challenge: the `DPoP-Nonce` value a 401
    /// issued for a DPoP-proved request. The caller embeds it in a fresh proof's
    /// nonce claim and retries once. The nonce is a public server-issued challenge,
    /// not a credential.
    #[error("the resource server challenged for a fresh DPoP nonce")]
    DpopChallenge { nonce: String },
    #[error("{code}: {message}")]
    Application { code: String, message: String },
    #[error("transport problem: {0}")]
    Transport(String),
}

pub trait ExperiencePort: Send + Sync {
    /// GET a read operation (arrival, directories, topic window, updates, receipt lookup).
    fn get(&self, context: &RequestContext, path: &str) -> Result<Value, ExperienceError>;
    /// Submit a mutation (posts, read position, membership, watches).
    fn send(&self, context: &RequestContext, method: &str, path: &str, body: &Value) -> Result<Value, ExperienceError>;
    /// Bounded long wait for activity (server caps near 15 seconds).
    fn wait(&self, context: &RequestContext, path: &str) -> Result<Value, ExperienceError>;
    /// POST the pre-credential enrollment exchange (W2 contract). No Authorization
    /// header: the call happens before any credential exists.
    fn enroll(&self, origin: &str, path: &str, body: &Value) -> Result<Value, ExperienceError>;
    /// GET a pre-credential server document (the atproto service-proof discovery
    /// document). No Authorization header. Error bodies of this surface use an
    /// `error` code field, which the adapter surfaces as `Application`.
    fn discover(&self, origin: &str, path: &str) -> Result<Value, ExperienceError>;
    /// One cheap reachability probe: a short-timeout GET whose body is discarded; any
    /// HTTP answer counts as reachable, only a transport failure does not. Used by
    /// Connect's waiting branch to decide whether a recorded operator page is alive
    /// before popping it.
    fn probe(&self, origin: &str) -> Result<(), ExperienceError>;
    /// POST the bound service-proof exchange (`/mcp/token`): the bearer is the
    /// short-lived proof JWT. Distinct error surface: 503/401/403 bodies carry
    /// `error` codes the hub maps to honest operator wording.
    fn exchange(&self, origin: &str, path: &str, body: &Value, bearer: &str) -> Result<Value, ExperienceError>;
}
