//! The atproto OAuth client (the bind flow's outbound spoke): identity resolution
//! (handle → DID → DID document → PDS), authorization-server discovery, a Pushed
//! Authorization Request with PKCE (S256), the code exchange and the silent refresh —
//! all under the local-client profile the atproto OAuth spec defines for loopback
//! tools: `client_id` exactly `http://localhost`, loopback `http://127.0.0.1[:port]/`
//! redirect, `atproto` scope, no `nonce` parameter (the pushed request_uri binding
//! replaces it — verified against the public authorization server 2026-09-11), DPoP
//! (ES256) proofs on the PAR and token requests per RFC 9449.
//!
//! Compact JWS (the DPoP proof) is hand-rolled in the house style: bounded, strict
//! parsing, no JWT crate. The cryptographic primitives come from pure-Rust crates
//! (p256, sha2, rand_core/getrandom — the same entropy source uuid already ships).
//! Nothing here is ever logged, journaled or rendered: tokens and keys are session
//! state (cookie-jar posture), handed only to the store and the wire.

use std::collections::HashMap;
use std::io::Read;
use std::sync::Mutex;
use std::time::Duration;

use p256::ecdsa::{signature::Signer, Signature, SigningKey};
use rand_core::{OsRng, RngCore};
use serde_json::Value;
use sha2::{Digest, Sha256};

use crate::domain::refs;

/// The atproto local-client id: a literal origin, no port, no metadata document.
pub const CLIENT_ID: &str = "http://localhost";
/// The scope the implicit localhost client metadata declares (the only one).
pub const SCOPE: &str = "atproto";
/// How long a started bind stays completable: the authorization server parks pushed
/// requests for ~5 minutes; ten minutes of operator attention is the whole budget.
pub const FLIGHT_TTL_MS: i64 = 10 * 60 * 1000;

const RESPONSE_LIMIT: u64 = 256 * 1024;
const READ_TIMEOUT: Duration = Duration::from_secs(30);
/// DPoP proof lifetime (RFC 9449 advises short): issued now, valid this long.
const PROOF_LIFETIME_SECS: i64 = 120;
/// Refresh margin: a token with less than this left is refreshed before use.
const REFRESH_MARGIN_SECS: i64 = 60;
/// Bounded string discipline for the values this module handles.
const TOKEN_LIMIT: usize = 4096;

/// The atproto OAuth client. One instance per process; safe to share.
pub struct AtprotoOauth {
    agent: ureq::Agent,
    /// The public PLC directory origin (did:plc document resolution).
    plc_directory: String,
    /// The handle-resolution origin (the `com.atproto.identity.resolveHandle` XRPC —
    /// createSession-equivalent server-side resolution, per the owner direction).
    handle_resolver: String,
    /// Per-authorization-server DPoP nonces (RFC 9449 §8), so the retry is the exception.
    dpop_nonces: Mutex<HashMap<String, String>>,
}

/// Everything a started bind needs to finish: returned by [`AtprotoOauth::start`], kept
/// in flight by the hub, consumed by [`AtprotoOauth::exchange`].
pub struct BindStart {
    /// The authorize URL the operator's browser is redirected to.
    pub authorize_url: String,
    /// The OAuth `state` value: the flight's key and the CSRF check.
    pub state: String,
    /// The PKCE code verifier (S256 challenge was pushed in the PAR).
    pub verifier: String,
    /// The DPoP ES256 private key, base64url SEC1 bytes (cookie-jar session state).
    pub dpop_key: String,
    /// The authorization server origin (token and refresh endpoint host).
    pub authserver: String,
    /// The account's PDS origin (service-auth requests go here).
    pub pds: String,
    /// The DID identity resolution produced (the exchange must agree).
    pub did: String,
    /// The canonical handle (the DID document's `alsoKnownAs` when it names one).
    pub handle: String,
}

/// Tokens an exchange or refresh produced.
pub struct OAuthTokens {
    pub access_token: String,
    pub refresh_token: Option<String>,
    /// The subject DID the authorization server reports (the code exchange carries it).
    pub sub: Option<String>,
}

impl AtprotoOauth {
    /// The production client: the public PLC directory and the public handle resolver.
    pub fn new() -> Self {
        Self::with_origins("https://plc.directory", "https://public.api.bsky.app")
    }

    /// A client pointed at explicit resolution origins (tests point both at their fake).
    pub fn with_origins(plc_directory: &str, handle_resolver: &str) -> Self {
        Self {
            agent: ureq::AgentBuilder::new().redirects(0).timeout_connect(Duration::from_secs(10)).build(),
            plc_directory: plc_directory.to_string(),
            handle_resolver: handle_resolver.to_string(),
            dpop_nonces: Mutex::new(HashMap::new()),
        }
    }

    /// Runs the outbound half of starting a bind: identity resolution, authorization
    /// server discovery, the pushed authorization request. The redirect URI must be the
    /// loopback shape the local-client profile allows (`http://127.0.0.1[:port]/`).
    pub fn start(&self, handle_input: &str, redirect_uri: &str) -> Result<BindStart, String> {
        let handle = normalize_handle(handle_input)?;
        let redirect = normalize_redirect(redirect_uri)?;
        let (did, canonical_handle) = self.resolve_identity(&handle)?;
        let (pds, from_doc) = self.pds_of(&did)?;
        let handle = canonical_handle.unwrap_or_else(|| from_doc.unwrap_or(handle));
        let authserver = self.discover_authorization_server(&pds)?;
        let metadata = self.authorization_server_metadata(&authserver)?;

        let state = random_token(32);
        let verifier = random_token(32);
        let challenge = base64url(&sha256_bytes(verifier.as_bytes()));
        let key = SigningKey::random(&mut OsRng);
        let par_form = vec![
            ("client_id", CLIENT_ID.to_string()),
            ("redirect_uri", redirect.clone()),
            ("response_type", "code".to_string()),
            ("scope", SCOPE.to_string()),
            ("state", state.clone()),
            ("code_challenge", challenge),
            ("code_challenge_method", "S256".to_string()),
        ];
        let par_url = metadata.par_endpoint.clone();
        let (_, body) = self.form_post(&par_url, &par_form, Some(&key), &authserver)?;
        let request_uri = body
            .get("request_uri")
            .and_then(Value::as_str)
            .filter(|uri| !uri.is_empty() && uri.len() <= 1024)
            .ok_or_else(|| "authorization_server: the pushed request returned no request_uri".to_string())?
            .to_string();
        let authorize_url = format!(
            "{}?client_id={}&request_uri={}",
            metadata.authorize_endpoint,
            percent_encode(CLIENT_ID),
            percent_encode(&request_uri)
        );
        Ok(BindStart {
            authorize_url,
            state,
            verifier,
            dpop_key: encode_key(&key),
            authserver,
            pds,
            did,
            handle,
        })
    }

    /// Exchanges the authorization code for tokens (the loopback callback's outbound
    /// half): PKCE verifier, DPoP proof, nonce retry. `flight` is the started bind.
    pub fn exchange(&self, flight: &BindStart, code: &str, redirect_uri: &str) -> Result<OAuthTokens, String> {
        if code.is_empty() || code.len() > TOKEN_LIMIT {
            return Err("invalid_code: the authorization server returned no usable code".to_string());
        }
        let redirect = normalize_redirect(redirect_uri)?;
        let metadata = self.authorization_server_metadata(&flight.authserver)?;
        let key = decode_key(&flight.dpop_key)?;
        let form = vec![
            ("grant_type", "authorization_code".to_string()),
            ("code", code.to_string()),
            ("redirect_uri", redirect),
            ("client_id", CLIENT_ID.to_string()),
            ("code_verifier", flight.verifier.clone()),
        ];
        let (_, body) = self.form_post(&metadata.token_endpoint, &form, Some(&key), &flight.authserver)?;
        tokens_of(&body)
    }

    /// Silent refresh: a new access token (and rotated refresh token, when the server
    /// rotates) from the stored refresh token, DPoP-proved with the session's key.
    pub fn refresh(&self, authserver: &str, refresh_token: &str, dpop_key: &str) -> Result<OAuthTokens, String> {
        if refresh_token.is_empty() || refresh_token.len() > TOKEN_LIMIT {
            return Err("invalid_refresh_token: the stored refresh token is unusable; re-bind the identity".to_string());
        }
        let metadata = self.authorization_server_metadata(authserver)?;
        let key = decode_key(dpop_key)?;
        let form = vec![
            ("grant_type", "refresh_token".to_string()),
            ("refresh_token", refresh_token.to_string()),
            ("client_id", CLIENT_ID.to_string()),
        ];
        let (_, body) = self.form_post(&metadata.token_endpoint, &form, Some(&key), authserver)?;
        tokens_of(&body)
    }

    // ---------- identity resolution ----------

    /// Handle (or DID) → (DID, canonical handle when the DID document names one).
    fn resolve_identity(&self, handle: &str) -> Result<(String, Option<String>), String> {
        if handle.starts_with("did:") {
            if handle.len() > 512 || !handle.chars().all(|c| c.is_ascii_alphanumeric() || c == ':') {
                return Err(format!("invalid_did: '{handle}' is not a usable DID"));
            }
            return Ok((handle.to_string(), None));
        }
        // The resolver XRPC first (createSession-equivalent server-side resolution);
        // the DID well-known is the custom-domain fallback.
        let lookup = format!(
            "{}/xrpc/com.atproto.identity.resolveHandle?handle={}",
            self.handle_resolver,
            percent_encode(handle)
        );
        if let Ok((status, body)) = self.get_json(&lookup) {
            if (200..300).contains(&status) {
                if let Some(did) = body.get("did").and_then(Value::as_str) {
                    if did.starts_with("did:") && did.len() <= 512 {
                        return Ok((did.to_string(), None));
                    }
                }
                return Err("identity_resolution_failed: the handle resolver returned no usable DID".to_string());
            }
            if status != 404 {
                return Err(format!("identity_resolution_failed: the handle resolver answered HTTP {status}"));
            }
        }
        // The HTTPS well-known fallback: only for plausible hostnames.
        if !handle.chars().all(|c| c.is_ascii_lowercase() || c.is_ascii_digit() || c == '.' || c == '-') || !handle.contains('.') {
            return Err("identity_resolution_failed: no resolver knows that handle".to_string());
        }
        let well_known = format!("https://{handle}/.well-known/atproto-did");
        match self.agent.get(&well_known).timeout(READ_TIMEOUT).call() {
            Ok(response) => {
                let mut text = String::new();
                let _ = response.into_reader().take(1024).read_to_string(&mut text);
                let did = text.trim().to_string();
                if did.starts_with("did:") && did.len() <= 512 {
                    Ok((did, None))
                } else {
                    Err("identity_resolution_failed: the handle's DID well-known returned no usable DID".to_string())
                }
            }
            Err(_) => Err("identity_resolution_failed: no resolver knows that handle (check the spelling)".to_string()),
        }
    }

    /// DID → (PDS origin, canonical handle from the document), per the DID method.
    fn pds_of(&self, did: &str) -> Result<(String, Option<String>), String> {
        let url = if let Some(id) = did.strip_prefix("did:plc:") {
            if id.is_empty() || id.len() > 240 {
                return Err(format!("invalid_did: '{did}' is not a usable DID"));
            }
            format!("{}/{}", self.plc_directory, did)
        } else if let Some(location) = did.strip_prefix("did:web:") {
            if location.is_empty() || location.len() > 240 {
                return Err(format!("invalid_did: '{did}' is not a usable DID"));
            }
            // did:web:example.com → https://example.com/did.json (path form per spec).
            let path = location.replace(':', "/");
            format!("https://{path}/did.json")
        } else {
            return Err(format!("unsupported_did_method: '{did}' (did:plc and did:web only)"));
        };
        let (status, body) = self.get_json(&url).map_err(|error| format!("did_resolution_failed: {error}"))?;
        if !(200..300).contains(&status) {
            return Err(format!("did_resolution_failed: the DID directory answered HTTP {status}"));
        }
        let pds = body
            .get("service")
            .and_then(Value::as_array)
            .and_then(|services| {
                services
                    .iter()
                    .find(|service| {
                        let id = service.get("id").and_then(Value::as_str).unwrap_or_default();
                        let kind = service.get("type").and_then(Value::as_str).unwrap_or_default();
                        id.ends_with("#atproto_pds") || kind == "AtprotoPds"
                    })
                    .or_else(|| services.first())
            })
            .and_then(|service| service.get("serviceEndpoint").and_then(Value::as_str))
            .and_then(refs::acceptable_origin)
            .ok_or_else(|| "did_resolution_failed: the DID document names no usable PDS".to_string())?;
        let handle = body
            .get("alsoKnownAs")
            .and_then(Value::as_array)
            .and_then(|known| {
                known
                    .iter()
                    .filter_map(|entry| entry.as_str())
                    .find_map(|entry| entry.strip_prefix("at://"))
                    .map(str::to_string)
            })
            .filter(|handle| !handle.is_empty() && handle.len() <= 253);
        Ok((pds, handle))
    }

    /// PDS → authorization server (OAuth protected resource metadata).
    fn discover_authorization_server(&self, pds: &str) -> Result<String, String> {
        let url = format!("{pds}/.well-known/oauth-protected-resource");
        let (status, body) = self.get_json(&url).map_err(|error| format!("authorization_server: {error}"))?;
        if !(200..300).contains(&status) {
            return Err(format!("authorization_server: the PDS answered HTTP {status}"));
        }
        body.get("authorization_servers")
            .and_then(Value::as_array)
            .and_then(|servers| servers.first())
            .and_then(Value::as_str)
            .and_then(refs::acceptable_origin)
            .ok_or_else(|| "authorization_server: the PDS names no usable authorization server".to_string())
    }

    /// The authorization server's metadata document, strictly: the endpoints we need
    /// must exist and live on that origin, and the profile we speak must be supported.
    fn authorization_server_metadata(&self, authserver: &str) -> Result<AsMetadata, String> {
        let url = format!("{authserver}/.well-known/oauth-authorization-server");
        let (status, body) = self.get_json(&url).map_err(|error| format!("authorization_server: {error}"))?;
        if !(200..300).contains(&status) {
            return Err(format!("authorization_server: the metadata document answered HTTP {status}"));
        }
        if let Some(issuer) = body.get("issuer").and_then(Value::as_str) {
            if refs::acceptable_origin(issuer).as_deref() != Some(authserver) {
                return Err("authorization_server: the metadata issuer does not match its origin".to_string());
            }
        }
        let endpoint = |name: &str| {
            body.get(name)
                .and_then(Value::as_str)
                .filter(|url| url.starts_with(&format!("{authserver}/")))
                .map(str::to_string)
        };
        let metadata = AsMetadata {
            authorize_endpoint: endpoint("authorization_endpoint")
                .ok_or_else(|| "authorization_server: no authorization endpoint in the metadata".to_string())?,
            par_endpoint: endpoint("pushed_authorization_request_endpoint")
                .ok_or_else(|| "authorization_server: no pushed-authorization endpoint in the metadata (PAR is required)".to_string())?,
            token_endpoint: endpoint("token_endpoint")
                .ok_or_else(|| "authorization_server: no token endpoint in the metadata".to_string())?,
        };
        if let Some(scopes) = body.get("scopes_supported").and_then(Value::as_array) {
            if !scopes.is_empty() && !scopes.iter().any(|scope| scope.as_str() == Some(SCOPE)) {
                return Err(format!("authorization_server: the '{SCOPE}' scope is not supported there"));
            }
        }
        if let Some(algorithms) = body.get("dpop_signing_alg_values_supported").and_then(Value::as_array) {
            if !algorithms.is_empty() && !algorithms.iter().any(|algorithm| algorithm.as_str() == Some("ES256")) {
                return Err("authorization_server: ES256 DPoP is not supported there".to_string());
            }
        }
        Ok(metadata)
    }

    // ---------- the wire ----------

    /// One form-encoded POST with a DPoP proof (RFC 9449), one nonce retry when the
    /// server demands one. Returns the final status and parsed JSON body.
    fn form_post(
        &self,
        url: &str,
        form: &[(&str, String)],
        key: Option<&SigningKey>,
        authserver: &str,
    ) -> Result<(u16, Value), String> {
        let mut nonce = self.dpop_nonces.lock().ok().and_then(|cache| cache.get(authserver).cloned());
        for attempt in 0..2 {
            let proof = key.map(|key| dpop_proof(key, "POST", &htu_of(url), nonce.as_deref()));
            let mut request = self.agent.post(url).timeout(READ_TIMEOUT);
            if let Some(proof) = &proof {
                request = request.set("DPoP", proof);
                if let Some(nonce) = &nonce {
                    request = request.set("DPoP-Nonce", nonce);
                }
            }
            let pairs: Vec<(&str, &str)> = form.iter().map(|(name, value)| (*name, value.as_str())).collect();
            let outcome = request.send_form(&pairs);
            let (status, fresh_nonce, body) = read_outcome(outcome)?;
            if let Some(fresh) = fresh_nonce {
                if let Ok(mut cache) = self.dpop_nonces.lock() {
                    cache.insert(authserver.to_string(), fresh.clone());
                }
                nonce = Some(fresh);
            }
            let refused = body.get("error").and_then(Value::as_str).unwrap_or_default().to_string();
            if (refused == "use_dpop_nonce" || refused == "invalid_dpop_nonce") && attempt == 0 && nonce.is_some() {
                continue;
            }
            if (200..300).contains(&status) {
                return Ok((status, body));
            }
            let description = body.get("error_description").and_then(Value::as_str).unwrap_or_default();
            return Err(format!(
                "authorization_server: the request was refused (HTTP {status}, {refused}): {description}"
            ));
        }
        Err("authorization_server: the nonce challenge did not settle".to_string())
    }

    /// One bounded anonymous GET; the caller interprets the status.
    fn get_json(&self, url: &str) -> Result<(u16, Value), String> {
        let outcome = self.agent.get(url).timeout(READ_TIMEOUT).call();
        let (status, _, body) = read_outcome(outcome)?;
        Ok((status, body))
    }
}

impl Default for AtprotoOauth {
    fn default() -> Self {
        Self::new()
    }
}

/// The endpoints this client uses from one authorization server's metadata.
struct AsMetadata {
    authorize_endpoint: String,
    par_endpoint: String,
    token_endpoint: String,
}

/// Reads one ureq outcome into (status, fresh DPoP-Nonce header, JSON body). Any status
/// is data — the caller maps refusals to honest wording. Only transport failures and
/// oversize/broken bodies are errors here.
fn read_outcome(outcome: Result<ureq::Response, ureq::Error>) -> Result<(u16, Option<String>, Value), String> {
    let response = match outcome {
        Ok(response) => response,
        Err(ureq::Error::Status(_, response)) => response,
        Err(ureq::Error::Transport(error)) => {
            return Err(format!("the authorization server could not be reached: {error}"));
        }
    };
    let status = response.status();
    let nonce = response.header("DPoP-Nonce").map(str::to_string);
    let mut bytes = Vec::new();
    response
        .into_reader()
        .take(RESPONSE_LIMIT + 1)
        .read_to_end(&mut bytes)
        .map_err(|error| format!("truncated response: {error}"))?;
    if bytes.len() as u64 > RESPONSE_LIMIT {
        return Err("the authorization server's response exceeded 256 KiB".to_string());
    }
    let body: Value = serde_json::from_slice(&bytes).unwrap_or(Value::Null);
    Ok((status, nonce, body))
}

// ---------- tokens ----------

fn tokens_of(body: &Value) -> Result<OAuthTokens, String> {
    let access_token = body
        .get("access_token")
        .and_then(Value::as_str)
        .filter(|token| !token.is_empty() && token.len() <= TOKEN_LIMIT)
        .ok_or_else(|| "authorization_server: the token response carried no access token".to_string())?
        .to_string();
    let refresh_token = body
        .get("refresh_token")
        .and_then(Value::as_str)
        .filter(|token| !token.is_empty() && token.len() <= TOKEN_LIMIT)
        .map(str::to_string);
    let sub = body
        .get("sub")
        .and_then(Value::as_str)
        .filter(|sub| sub.starts_with("did:") && sub.len() <= 512)
        .map(str::to_string);
    Ok(OAuthTokens { access_token, refresh_token, sub })
}

/// The `exp` (epoch seconds) inside an access token's payload, when it is a JWT-shaped
/// token. Pure parsing of our own session state — no verification, bounded strictly.
pub fn access_expiry(token: &str) -> Option<i64> {
    if token.len() > TOKEN_LIMIT {
        return None;
    }
    let mut parts = token.split('.');
    let header = parts.next()?;
    let payload = parts.next()?;
    let signature = parts.next()?;
    if parts.next().is_some() || header.is_empty() || payload.is_empty() || signature.is_empty() {
        return None;
    }
    let bytes = base64url_decode(payload)?;
    // The signature segment must be well-formed base64url too — strictness costs nothing.
    base64url_decode(signature)?;
    let value: Value = serde_json::from_slice(&bytes).ok()?;
    value.get("exp").and_then(Value::as_i64)
}

/// Whether an access token is stale enough to refresh first (margin included). A token
/// without a readable expiry is treated as still fresh: the PDS refuses it honestly if
/// it is not, and that refusal maps to the documented re-bind path.
pub fn access_needs_refresh(access_token: &str, now_epoch_secs: i64) -> bool {
    match access_expiry(access_token) {
        Some(expiry) => expiry - now_epoch_secs <= REFRESH_MARGIN_SECS,
        None => false,
    }
}

// ---------- the local-client profile helpers ----------

/// Handle discipline for the bind input: one label, trimmed, `@` stripped, lowercased.
/// A DID passes through untouched (its own validation happens at resolution).
fn normalize_handle(input: &str) -> Result<String, String> {
    let handle = input.trim().trim_start_matches('@').to_ascii_lowercase();
    if handle.starts_with("did:") {
        return Ok(handle);
    }
    if handle.chars().count() < 3
        || handle.chars().count() > 253
        || handle.chars().any(|c| c.is_whitespace() || c == '/' || c == ':' || c == '#' || c == '?')
        || !handle.contains('.')
    {
        return Err("invalid_handle: an atproto handle looks like someone.example.org (or a DID)".to_string());
    }
    Ok(handle)
}

/// Redirect discipline: `http://127.0.0.1[:port]/` or `http://[::1][:port]/` — the
/// loopback shape the local-client profile allows (path exactly `/`, port free).
fn normalize_redirect(input: &str) -> Result<String, String> {
    let refused =
        || "invalid_redirect: the bind redirect must be loopback HTTP with an empty path (http://127.0.0.1:port/)".to_string();
    let rest = input.strip_prefix("http://").ok_or_else(refused)?;
    let (authority, path) = rest.split_once('/').unwrap_or((rest, ""));
    if !path.is_empty() {
        return Err(refused());
    }
    // Split an optional port, IPv6-bracket aware.
    let (host, port) = if let Some(bracketed) = authority.strip_prefix('[') {
        let (inner, tail) = bracketed.split_once(']').ok_or_else(refused)?;
        (format!("[{inner}]"), tail.strip_prefix(':').unwrap_or(""))
    } else {
        match authority.split_once(':') {
            Some((host, port)) => (host.to_string(), port),
            None => (authority.to_string(), ""),
        }
    };
    if !matches!(host.as_str(), "127.0.0.1" | "[::1]" | "localhost") {
        return Err(refused());
    }
    let parsed = port.parse::<u32>().unwrap_or(u32::MAX);
    if !port.is_empty() && (port.len() > 5 || !port.chars().all(|c| c.is_ascii_digit()) || parsed > 65535) {
        return Err(refused());
    }
    Ok(format!("http://{authority}/"))
}

/// The DPoP proof JWT (RFC 9449): ES256 over a compact header+payload, the public key
/// in the header, the request bound by method and URI, the nonce when the server issued
/// one. Hand-rolled in the house style — there is no JWT crate in this workspace.
fn dpop_proof(key: &SigningKey, htm: &str, htu: &str, nonce: Option<&str>) -> String {
    let point = key.verifying_key().to_encoded_point(false);
    let coordinates = point.as_bytes();
    let header = format!(
        "{{\"alg\":\"ES256\",\"jwk\":{{\"crv\":\"P-256\",\"kty\":\"EC\",\"x\":\"{}\",\"y\":\"{}\"}},\"typ\":\"dpop+jwt\"}}",
        base64url(&coordinates[1..33]),
        base64url(&coordinates[33..65])
    );
    let now = now_secs();
    let mut payload = format!(
        "{{\"exp\":{},\"iat\":{},\"htm\":\"{}\",\"htu\":\"{}\",\"jti\":\"{}\"",
        now + PROOF_LIFETIME_SECS,
        now,
        htm,
        htu,
        random_token(16)
    );
    if let Some(nonce) = nonce {
        payload.push_str(&format!(",\"nonce\":\"{}\"", nonce.escape_default()));
    }
    payload.push('}');
    let signing_input = format!("{}.{}", base64url(header.as_bytes()), base64url(payload.as_bytes()));
    let signature: Signature = key.sign(signing_input.as_bytes());
    format!("{}.{}", signing_input, base64url(&signature.to_bytes()))
}

/// The `htu` claim: the URL without query or fragment (RFC 9449 §4.3).
fn htu_of(url: &str) -> String {
    url.split(['?', '#']).next().unwrap_or(url).to_string()
}

fn encode_key(key: &SigningKey) -> String {
    base64url(key.to_bytes().as_ref())
}

fn decode_key(encoded: &str) -> Result<SigningKey, String> {
    let refused = || "invalid_dpop_key: the stored DPoP key is unusable; re-bind the identity".to_string();
    let bytes = base64url_decode(encoded).ok_or_else(refused)?;
    let field: [u8; 32] = bytes.try_into().map_err(|_| refused())?;
    SigningKey::from_bytes(&field.into()).map_err(|_| refused())
}

/// Random URL-safe token of `len` bytes (43 chars for 32 bytes — the PKCE minimum).
fn random_token(len: usize) -> String {
    let mut bytes = vec![0u8; len];
    OsRng.fill_bytes(&mut bytes);
    base64url(&bytes)
}

// ---------- bounded encoding helpers (house style: no encoding crates) ----------

pub fn sha256_bytes(data: &[u8]) -> [u8; 32] {
    let mut hasher = Sha256::new();
    hasher.update(data);
    hasher.finalize().into()
}

/// Standard base64url (RFC 4648 §5, unpadded).
pub fn base64url(bytes: &[u8]) -> String {
    const ALPHABET: &[u8] = b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
    let mut out = String::with_capacity(bytes.len().div_ceil(3) * 4);
    for chunk in bytes.chunks(3) {
        let b0 = chunk[0] as u32;
        let b1 = *chunk.get(1).unwrap_or(&0) as u32;
        let b2 = *chunk.get(2).unwrap_or(&0) as u32;
        let triple = (b0 << 16) | (b1 << 8) | b2;
        out.push(ALPHABET[(triple >> 18) as usize & 63] as char);
        out.push(ALPHABET[(triple >> 12) as usize & 63] as char);
        if chunk.len() > 1 {
            out.push(ALPHABET[(triple >> 6) as usize & 63] as char);
        }
        if chunk.len() > 2 {
            out.push(ALPHABET[triple as usize & 63] as char);
        }
    }
    out
}

/// Strict base64url decode (unpadded alphabet only). `None` on any foreign character.
pub fn base64url_decode(text: &str) -> Option<Vec<u8>> {
    const TABLE: &[u8] = b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
    let mut out = Vec::with_capacity(text.len() * 3 / 4);
    let mut buffer: u32 = 0;
    let mut bits = 0u32;
    for byte in text.bytes() {
        let value = TABLE.iter().position(|candidate| *candidate == byte)? as u32;
        buffer = (buffer << 6) | value;
        bits += 6;
        if bits >= 8 {
            bits -= 8;
            out.push((buffer >> bits) as u8);
        }
    }
    // Trailing bits must be zero padding, not data loss.
    if bits >= 6 {
        return None;
    }
    Some(out)
}

/// Percent-encoding for one query component (same table as the hub's).
pub fn percent_encode(value: &str) -> String {
    let mut out = String::new();
    for byte in value.bytes() {
        match byte {
            b'A'..=b'Z' | b'a'..=b'z' | b'0'..=b'9' | b'-' | b'_' | b'.' | b'~' => out.push(byte as char),
            _ => out.push_str(&format!("%{byte:02X}")),
        }
    }
    out
}

fn now_secs() -> i64 {
    std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|value| value.as_secs() as i64)
        .unwrap_or(0)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn redirect_normalization_accepts_only_the_loopback_profile() {
        assert_eq!(normalize_redirect("http://127.0.0.1:5219/").unwrap(), "http://127.0.0.1:5219/");
        assert_eq!(normalize_redirect("http://127.0.0.1:5219").unwrap(), "http://127.0.0.1:5219/");
        assert_eq!(normalize_redirect("http://127.0.0.1/").unwrap(), "http://127.0.0.1/");
        assert_eq!(normalize_redirect("http://[::1]:5219/").unwrap(), "http://[::1]:5219/");
        assert!(normalize_redirect("http://127.0.0.1:5219/bind/ox/atproto/callback").is_err(), "the public profile refuses paths");
        assert!(normalize_redirect("https://127.0.0.1:5219/").is_err(), "HTTPS is not the loopback profile");
        assert!(normalize_redirect("http://example.com/").is_err(), "off-loopback is refused");
    }

    #[test]
    fn handle_normalization_is_strict() {
        assert_eq!(normalize_handle("@Lumen.Example").unwrap(), "lumen.example");
        assert!(normalize_handle("no-dot").is_err());
        assert!(normalize_handle("a b.example").is_err());
        assert!(normalize_handle("did:plc:ok").is_ok(), "DIDs pass through untouched here");
    }

    #[test]
    fn base64url_round_trips_strictly() {
        for bytes in [&b""[..], b"f", b"fo", b"foo", b"foob", b"fooba", b"foobar", &[0xff, 0x00, 0x81]] {
            let encoded = base64url(bytes);
            assert_eq!(base64url_decode(&encoded).unwrap(), bytes);
        }
        assert_eq!(base64url(b"foobar"), "Zm9vYmFy");
        assert!(base64url_decode("Zm9vYmFy=").is_none(), "padded input is refused");
        assert!(base64url_decode("Zm9vYmFy!").is_none(), "foreign characters are refused");
    }

    #[test]
    fn access_expiry_reads_only_well_formed_jwts() {
        let payload = base64url(br#"{"exp":1234567890}"#);
        let token = format!("{}.{}", base64url(b"{\"alg\":\"ES256\"}"), payload);
        let with_sig = format!("{token}.{}", base64url(&[0u8; 64]));
        assert_eq!(access_expiry(&token), None, "a two-part string is not a JWS");
        assert_eq!(access_expiry(&with_sig), Some(1234567890));
        assert_eq!(access_expiry("sat_plain_token"), None);
        assert_eq!(access_expiry(&format!("{}.{}.{}", base64url(b"x"), payload, "unsig!ned")), None);
    }

    #[test]
    fn refresh_margin_decides_staleness() {
        let payload = base64url(format!("{{\"exp\":{}}}", 1_000_100).as_bytes());
        let token = format!("h.{payload}.{}", base64url(&[0u8; 64]));
        assert!(access_needs_refresh(&token, 1_000_100), "expired");
        assert!(access_needs_refresh(&token, 1_000_100 - 59), "inside the margin");
        assert!(!access_needs_refresh(&token, 1_000_100 - 61), "comfortably fresh");
        assert!(!access_needs_refresh("sat_plain", 0), "opaque tokens are never force-refreshed");
    }

    #[test]
    fn dpop_proofs_carry_the_profile_claims() {
        let key = SigningKey::random(&mut OsRng);
        let proof = dpop_proof(&key, "POST", "https://as.example/oauth/token", Some("nonce-1"));
        let segments: Vec<&str> = proof.split('.').collect();
        assert_eq!(segments.len(), 3);
        let header = String::from_utf8(base64url_decode(segments[0]).unwrap()).unwrap();
        let payload = String::from_utf8(base64url_decode(segments[1]).unwrap()).unwrap();
        assert!(header.contains("\"typ\":\"dpop+jwt\""));
        assert!(header.contains("\"alg\":\"ES256\""));
        assert!(header.contains("\"kty\":\"EC\""));
        assert!(payload.contains("\"htm\":\"POST\""));
        assert!(payload.contains("\"htu\":\"https://as.example/oauth/token\""));
        assert!(payload.contains("\"nonce\":\"nonce-1\""));
        assert_eq!(base64url_decode(segments[2]).unwrap().len(), 64, "raw r||s signature");
    }

    #[test]
    fn dpop_keys_survive_their_session_encoding() {
        let key = SigningKey::random(&mut OsRng);
        let encoded = encode_key(&key);
        let back = decode_key(&encoded).expect("round trip");
        assert_eq!(key.to_bytes(), back.to_bytes());
        assert!(decode_key("!!!not-base64url!!!").is_err());
    }
}
