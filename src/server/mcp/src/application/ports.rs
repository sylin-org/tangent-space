//! Outbound port to a Tangent server's experience API. The hub speaks only this trait; the
//! ureq adapter implements it. `RequestContext` carries the credential for one call and is
//! never logged, journaled or rendered.

use serde_json::Value;

/// Per-call routing context: canonical origin, scoped credential and the participant
/// reference it belongs to.
pub struct RequestContext {
    pub origin: String,
    pub credential: String,
    /// The enrollment's participant reference; informational for the adapter.
    pub participant_ref: String,
}

#[derive(Debug, thiserror::Error)]
pub enum ExperienceError {
    #[error("the Tangent server could not be reached")]
    Unreachable,
    #[error("authentication was rejected; the stored credential may be expired or revoked")]
    Unauthorized,
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
}
