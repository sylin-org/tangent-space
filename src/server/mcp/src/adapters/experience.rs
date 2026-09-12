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
/// The reachability probe's whole budget: a loopback operator page answers in
/// milliseconds, so anything slower is honestly treated as not running.
const PROBE_TIMEOUT: Duration = Duration::from_millis(1500);

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

    fn discover(&self, origin: &str, path: &str) -> Result<Value, ExperienceError> {
        atproto_request(&self.agent, "GET", &format!("{origin}{path}"), None, None)
    }

    fn probe(&self, origin: &str) -> Result<(), ExperienceError> {
        let outcome = self.agent.request("GET", &format!("{origin}/")).timeout(PROBE_TIMEOUT).call();
        match outcome {
            // Any answer — including an HTTP error status — proves the page is running;
            // the body is deliberately unread. Only a transport failure is unreachable.
            Ok(_) | Err(ureq::Error::Status(_, _)) => Ok(()),
            Err(ureq::Error::Transport(_)) => Err(ExperienceError::Unreachable),
        }
    }

    fn exchange(&self, origin: &str, path: &str, body: &Value, bearer: &str) -> Result<Value, ExperienceError> {
        atproto_request(&self.agent, "POST", &format!("{origin}{path}"), Some(bearer), Some(body))
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
    let mut request = agent.request(method, &url).timeout(timeout).set("Authorization", &format!("Bearer {}", context.credential));
    if let Some(proof) = &context.dpop {
        // RFC 9449 resource requests: the access token stays in Authorization, the
        // proof rides its own header, bound to this exact method/URI and token.
        request = request.set("DPoP", proof);
    }
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

/// One request on the atproto service-proof surface (discovery GET or the `/mcp/token`
/// exchange POST). Error bodies here use an `error` code field (ASP-style problem
/// shapes also accepted); the meaningful statuses — 503 unconfigured audience, 401
/// invalid/replayed proof, 403 suspended — surface as `Application` so the hub can map
/// them to honest operator wording. The bearer, when present, is the ephemeral proof
/// JWT; it rides only in the Authorization header.
fn atproto_request(
    agent: &ureq::Agent,
    method: &str,
    url: &str,
    bearer: Option<&str>,
    body: Option<&Value>,
) -> Result<Value, ExperienceError> {
    let mut request = agent.request(method, url).timeout(READ_TIMEOUT);
    if let Some(bearer) = bearer {
        request = request.set("Authorization", &format!("Bearer {bearer}"));
    }
    let outcome = match body {
        Some(value) if !value.is_null() => {
            let serialized = serde_json::to_string(value)
                .map_err(|error| ExperienceError::Transport(format!("cannot encode request body: {error}")))?;
            request.set("Content-Type", "application/json").send_string(&serialized)
        }
        _ => request.call(),
    };
    match outcome {
        Ok(response) => parse_response(response),
        Err(ureq::Error::Status(status, response)) => {
            let problem = parse_response(response).ok().and_then(|value| extract_error_code(&value));
            match status {
                401 => Err(problem
                    .map(|(code, message)| ExperienceError::Application { code, message })
                    .unwrap_or(ExperienceError::Unauthorized)),
                400 | 403 | 404 | 409 | 500 | 503 => Err(problem
                    .map(|(code, message)| ExperienceError::Application { code, message })
                    .unwrap_or_else(|| ExperienceError::Application {
                        code: format!("http_{status}"),
                        message: format!("the server rejected the request with HTTP {status}"),
                    })),
                // A disabled-redirect surface is a misconfigured or hostile origin.
                301..=308 => Err(ExperienceError::Transport(format!("the server answered HTTP {status} (redirect); refusing to follow"))),
                _ => Err(ExperienceError::Transport(format!("the server answered HTTP {status}"))),
            }
        }
        Err(ureq::Error::Transport(_)) => Err(ExperienceError::Unreachable),
    }
}

/// Error extraction for the atproto surfaces: `{ "error": "code", "message": "…" }`
/// (the documented shape), with the experience API's `{ "code": …, "message": … }`
/// accepted as a fallback.
fn extract_error_code(value: &Value) -> Option<(String, String)> {
    let code = value.get("error").or_else(|| value.get("code")).and_then(Value::as_str)?;
    let message = value.get("message").and_then(Value::as_str).unwrap_or_default();
    Some((code.to_string(), message.to_string()))
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
