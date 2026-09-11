//! The HTTP spoke: a blocking ureq client for the Tangent experience API. Credentials ride
//! only in Authorization headers; redirects are never followed, so a credential can never be
//! forwarded to a different origin.

use std::io::Read;
use std::sync::Arc;
use std::time::Duration;

use serde_json::Value;

use crate::application::ports::{ExperienceError, ExperiencePort, RequestContext};

const RESPONSE_LIMIT: u64 = 512 * 1024;
const READ_TIMEOUT: Duration = Duration::from_secs(30);
const WAIT_TIMEOUT: Duration = Duration::from_secs(25);

pub struct UreqExperience {
    agent: ureq::Agent,
}

impl UreqExperience {
    pub fn new() -> Self {
        Self {
            agent: ureq::AgentBuilder::new()
                .redirects(0)
                .timeout_connect(Duration::from_secs(10))
                .build(),
        }
    }
}

impl Default for UreqExperience {
    fn default() -> Self {
        Self::new()
    }
}

impl ExperiencePort for UreqExperience {
    fn get(&self, context: &RequestContext, path: &str) -> Result<Value, ExperienceError> {
        request(&self.agent, context, "GET", path, None, READ_TIMEOUT)
    }

    fn send(&self, context: &RequestContext, method: &str, path: &str, body: &Value) -> Result<Value, ExperienceError> {
        request(&self.agent, context, method, path, Some(body), READ_TIMEOUT)
    }

    fn wait(&self, context: &RequestContext, path: &str) -> Result<Value, ExperienceError> {
        request(&self.agent, context, "GET", path, None, WAIT_TIMEOUT)
    }

    fn enroll(&self, origin: &str, path: &str, body: &Value) -> Result<Value, ExperienceError> {
        request_anonymous(&self.agent, origin, path, body, READ_TIMEOUT)
    }
}

fn request(
    agent: &ureq::Agent,
    context: &RequestContext,
    method: &str,
    path: &str,
    body: Option<&Value>,
    timeout: Duration,
) -> Result<Value, ExperienceError> {
    let url = format!("{}{}", context.origin, path);
    let request = agent.request(method, &url).timeout(timeout).set("Authorization", &format!("Bearer {}", context.credential));
    dispatch(request, body)
}

/// The pre-credential enrollment exchange: same framing and status handling, no
/// Authorization header.
fn request_anonymous(
    agent: &ureq::Agent,
    origin: &str,
    path: &str,
    body: &Value,
    timeout: Duration,
) -> Result<Value, ExperienceError> {
    let url = format!("{origin}{path}");
    let request = agent.request("POST", &url).timeout(timeout);
    dispatch(request, Some(body))
}

fn dispatch(request: ureq::Request, body: Option<&Value>) -> Result<Value, ExperienceError> {
    let request = match body {
        Some(value) if !value.is_null() => request.set("Content-Type", "application/json"),
        _ => request,
    };
    let outcome = match body {
        Some(value) if !value.is_null() => {
            let serialized = serde_json::to_string(value)
                .map_err(|error| ExperienceError::Transport(format!("cannot encode request body: {error}")))?;
            request.send_string(&serialized)
        }
        _ => request.call(),
    };
    match outcome {
        Ok(response) => parse_response(response),
        Err(ureq::Error::Status(status, response)) => match status {
            401 => Err(ExperienceError::Unauthorized),
            403 | 400 | 404 | 409 => {
                let problem = parse_response(response).ok();
                match problem.and_then(|value| extract_problem(&value)) {
                    Some((code, message)) => Err(ExperienceError::Application { code, message }),
                    None => Err(ExperienceError::Application {
                        code: match status {
                            403 => "permission_denied".into(),
                            409 => "request_conflict".into(),
                            404 => "receipt_expired".into(),
                            _ => "invalid_arguments".into(),
                        },
                        message: format!("the server rejected the request with HTTP {status}"),
                    }),
                }
            }
            // A disabled-redirect surface is a misconfigured or hostile origin.
            301..=308 => Err(ExperienceError::Transport(format!("the server answered HTTP {status} (redirect); refusing to follow"))),
            _ => Err(ExperienceError::Transport(format!("the server answered HTTP {status}"))),
        },
        Err(ureq::Error::Transport(_)) => Err(ExperienceError::Unreachable),
    }
}

fn parse_response(response: ureq::Response) -> Result<Value, ExperienceError> {
    let mut bytes = Vec::new();
    response
        .into_reader()
        .take(RESPONSE_LIMIT + 1)
        .read_to_end(&mut bytes)
        .map_err(|error| ExperienceError::Transport(format!("truncated response: {error}")))?;
    if bytes.len() as u64 > RESPONSE_LIMIT {
        return Err(ExperienceError::Transport("response exceeded 512 KiB".into()));
    }
    serde_json::from_slice(&bytes).map_err(|error| ExperienceError::Transport(format!("invalid JSON response: {error}")))
}

fn extract_problem(value: &Value) -> Option<(String, String)> {
    let code = value.get("code").and_then(Value::as_str)?;
    let message = value.get("message").and_then(Value::as_str).unwrap_or_default();
    Some((code.to_string(), message.to_string()))
}

pub fn shared() -> Arc<dyn ExperiencePort> {
    Arc::new(UreqExperience::new())
}
