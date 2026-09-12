//! The connector hub: the single application orchestrator for every model-requested
//! operation and background check. Intakes (the stdio MCP edge and the command line) are
//! spokes in the same shape: each translates its input model into the closed [`Operation`]
//! vocabulary and calls [`ConnectorHub::execute`]; the channel is recorded for attribution
//! and never changes a domain outcome. Adapters (HTTP client, poller, store) are the
//! remaining spokes; none of them talks to another directly.

use std::collections::{BTreeMap, HashMap, HashSet};
use std::sync::atomic::{AtomicI64, Ordering};
use std::sync::{Arc, Mutex};

use serde_json::{json, Value};

use crate::adapters::atproto_oauth::{self, AtprotoOauth, BindStart};
use crate::adapters::operator::DEFAULT_PAGE_URL;
use crate::adapters::store::StateStore;
use crate::application::bus::EventBus;
use crate::application::contract::{self, ExperienceDto};
use crate::application::operations::{decode, Operation, ViewMode};
use crate::application::ports::{ExperienceError, ExperiencePort, RequestContext};
use crate::domain::events::DomainEvent;
use crate::domain::identity::{valid_handle, AtprotoSession, CallerId, CompanionEntry, Identity, LocalContext};
use crate::domain::intake::IntakeChannel;
use crate::domain::refs;
use crate::domain::writes::PendingWrite;
use crate::domain::{attention::AttentionState, now_millis};
use crate::presentation::perspective::Perspective;
use crate::presentation::{render, RenderInput};

pub const DELIVERY_MODE: &str = "tool_response_only";
/// The exact exchange method of the atproto service-proof profile. Fixed by protocol;
/// the server's discovery document must agree, or the enrollment refuses honestly.
/// One source with the bind's OAuth rpc permission (the oauth module owns it).
pub const EXCHANGE_LXM: &str = atproto_oauth::EXCHANGE_LXM;
/// Proof lifetime requested from the PDS; the server's accepted window is now+120 s.
const PROOF_EXPIRY_SECONDS: i64 = 120;
/// The public PDS used when the operator does not name one explicitly; the authoritative
/// origin from the account's DID document replaces it when the PDS reports one.
pub const DEFAULT_PDS: &str = "https://bsky.social";
/// How long a waiting-for-operator connect stays resumable before it ages out honestly.
/// Live coordination state, not durable enrollment state — ten minutes of operator
/// attention is the whole budget.
pub const PENDING_CONNECT_TIMEOUT_MS: i64 = 10 * 60 * 1000;
/// In-flight OAuth binds parked at once, across identities. A small bound: each pins
/// one DPoP key and one pushed request; an operator drives at most a few tabs.
const BIND_FLIGHT_LIMIT: usize = 8;

/// One started, not-yet-completed OAuth bind (in memory only — live coordination
/// state, like a pending connect). Keyed by its OAuth `state`; single use.
struct BindFlight {
    local_id: String,
    created_at: i64,
    start: BindStart,
}
/// Age-out scan cadence: one shared sweeper thread wakes at this period and drops
/// expired pendings, so a looping model's repeated Connects (each refreshing the
/// pending) can never pile up one sleeping thread per call.
const PENDING_CONNECT_SWEEP_MS: u64 = 15 * 1000;

/// A completed tool invocation: deterministic view text plus canonical structured facts.
pub struct ToolOutcome {
    pub is_error: bool,
    pub status: String,
    pub text: String,
    pub structured: Value,
}

/// Read-only per-enrollment attention/pending-write snapshot for the operator page.
pub struct EnrollmentStatus {
    pub companion_id: String,
    pub identity_local_id: String,
    pub origin: String,
    pub waiting: i64,
    pub pending_attention: usize,
    pub unresolved_writes: usize,
}

/// Read-only atproto binding status for the operator page: what is bound and how old
/// the session is — never the access token.
pub struct AtprotoBinding {
    pub did: String,
    pub handle: String,
    pub pds: String,
    pub obtained_at: i64,
}

impl ToolOutcome {
    /// Terminal status to process exit code. Zero means the server confirmed the outcome;
    /// pending and unknown outcomes never exit zero, so scripts do not replay effects.
    pub fn exit_code(&self) -> i32 {
        match self.status.as_str() {
            "ok" if !self.is_error => 0,
            "ok" => 3,
            "pending" => 2,
            "blocked" => 3,
            _ => 4,
        }
    }
}

/// Everything an intake-scoped operation needs after the context binding resolves.
pub struct CallFrame {
    pub request: RequestContext,
    pub companion: CompanionEntry,
    pub context_id: String,
}

/// One waiting-for-operator handshake the connector resumes by itself once the operator
/// completes the binding (owner addendum). In-memory only: live coordination, never
/// durable state — a restart simply asks the model to connect again.
#[derive(Clone)]
struct PendingConnect {
    local_id: String,
    handle: String,
    origin: String,
    initiator: String,
    recorded_at: i64,
}

pub struct ConnectorHub {
    port: Arc<dyn ExperiencePort>,
    store: Mutex<StateStore>,
    events: Arc<EventBus>,
    caller: CallerId,
    /// The loopback operator page URL this process hosts (serve mode), set once at
    /// startup so `OpenRegistration` can construct the browser target internally.
    operator_page_url: Mutex<Option<String>>,
    /// The identity whose sign-in a `Connect` popped last (R3): routes the next
    /// `OpenRegistration` to that identity's bind anchor instead of identity creation.
    pending_bind: Mutex<Option<String>>,
    /// Browser targets already opened by this process (F2): a looping model must not
    /// spawn one tab per retry. Keyed by the full target URL, so distinct anchors stay
    /// distinct.
    opened_pages: Mutex<HashSet<String>>,
    /// Waiting-for-operator connects (A3), shared with the one age-out sweeper.
    pending_connects: Arc<Mutex<Vec<PendingConnect>>>,
    /// The atproto OAuth client (the `/bind` flow's outbound spoke). Replaceable before
    /// serving (tests point the resolution origins at their fake).
    atproto_oauth: Arc<Mutex<Arc<AtprotoOauth>>>,
    /// Started OAuth binds, keyed by their OAuth `state` value.
    bind_flights: Mutex<Vec<BindFlight>>,
    /// How long a parked bind stays completable; [`atproto_oauth::FLIGHT_TTL_MS`] by
    /// default. A pub test seam shortens it so the TTL refusal is assertable without
    /// waiting out ten real minutes.
    bind_flight_ttl_ms: AtomicI64,
    /// Per-identity refresh serialization (R5): one small mutex per identity so a MCP
    /// Connect and the operator auto-resume can never double-refresh one session. The
    /// map itself grows one entry per identity that ever refreshes.
    refresh_locks: Mutex<HashMap<String, Arc<Mutex<()>>>>,
    /// Arms the single sweeper thread on the first recorded pending connect.
    sweep_once: std::sync::Once,
}

impl ConnectorHub {
    pub fn new(port: Arc<dyn ExperiencePort>, store: StateStore, events: Arc<EventBus>, caller: CallerId) -> Self {
        Self {
            port,
            store: Mutex::new(store),
            events,
            caller,
            operator_page_url: Mutex::new(None),
            pending_bind: Mutex::new(None),
            opened_pages: Mutex::new(HashSet::new()),
            pending_connects: Arc::new(Mutex::new(Vec::new())),
            atproto_oauth: Arc::new(Mutex::new(Arc::new(AtprotoOauth::new()))),
            bind_flights: Mutex::new(Vec::new()),
            bind_flight_ttl_ms: AtomicI64::new(atproto_oauth::FLIGHT_TTL_MS),
            refresh_locks: Mutex::new(HashMap::new()),
            sweep_once: std::sync::Once::new(),
        }
    }

    /// Shortens how long a parked bind stays completable (milliseconds). A test seam
    /// for the TTL refusal; production always runs [`atproto_oauth::FLIGHT_TTL_MS`].
    pub fn set_bind_flight_ttl_ms(&self, milliseconds: i64) {
        self.bind_flight_ttl_ms.store(milliseconds, Ordering::Relaxed);
    }

    /// Replaces the atproto OAuth client (tests point its resolution origins at a fake
    /// authorization server). Call before serving; the bind flow reads it per request.
    pub fn set_atproto_oauth(&self, client: AtprotoOauth) {
        if let Ok(mut slot) = self.atproto_oauth.lock() {
            *slot = Arc::new(client);
        }
    }

    /// The current atproto OAuth client (cloned out — never held across network I/O).
    fn oauth(&self) -> Arc<AtprotoOauth> {
        self.atproto_oauth
            .lock()
            .map(|slot| slot.clone())
            .unwrap_or_default()
    }

    pub fn events(&self) -> Arc<EventBus> {
        self.events.clone()
    }

    /// Records this process's own operator page URL in memory. The URL never enters
    /// model-visible output; only the internal browser open uses it. The full recording
    /// (memory + durable state for cross-process Connects) is
    /// [`ConnectorHub::announce_operator_page`].
    pub fn set_operator_page_url(&self, url: &str) {
        if let Ok(mut slot) = self.operator_page_url.lock() {
            *slot = Some(url.to_string());
        }
    }

    /// Records the operator page URL this process hosts: in memory for this process's
    /// own opens, and in durable state so a Connect in ANY process (the CLI one-shots)
    /// can pop this page at the sign-in anchor. Cookie-jar class by design — same
    /// exposure class as the per-enrollment sessions.
    pub fn announce_operator_page(&self, url: &str) {
        self.set_operator_page_url(url);
        if let Ok(mut store) = self.lock_store() {
            store.set_operator_page_url(url);
            let _ = store.save();
        }
    }

    /// Clears the persisted operator page URL on clean shutdown, so later Connects are
    /// not pointed at a page that died with this process.
    pub fn clear_persisted_operator_page(&self) {
        if let Ok(mut store) = self.lock_store() {
            store.clear_operator_page_url();
            let _ = store.save();
        }
    }

    /// The browser target `OpenRegistration` opens. Never rendered into a view, a tool
    /// response or the journal; this accessor exists for the internal open and tests.
    /// The anchor is routed (R3): a pending sign-in popped by `Connect` wins over the
    /// default identity-creation view.
    pub fn registration_target_url(&self) -> Option<String> {
        let page = self.operator_page_url.lock().ok()?.clone()?;
        let pending = self.pending_bind.lock().ok().and_then(|slot| slot.clone());
        let anchor = match pending.as_deref() {
            Some(local_id) => bind_anchor(local_id),
            None => "#create-identity".to_string(),
        };
        Some(registration_target(&page, &anchor))
    }

    /// The browser target a sign-in pop for this identity would open (this process's
    /// own page, or a recorded reachable one, at the identity's bind route).
    /// Test-visible mirror of the internal resolution, so the target is assertable
    /// under the no-browser guard without spawning anything.
    pub fn sign_in_target_url(&self, local_id: &str) -> Option<String> {
        let (page, _) = self.sign_in_page()?;
        Some(format!("{page}{}", bind_anchor(local_id)))
    }

    /// Opens one browser target once per process (F2). Returns whether THIS call is the
    /// first to open it — a repeated target answers `false` without spawning, so a
    /// looping model cannot pile up tabs. First-ness is independent of the no-browser
    /// guard: a guarded first call is still the one that "opened" the page.
    fn open_page_once(&self, target: &str) -> bool {
        let fresh = self
            .opened_pages
            .lock()
            .map(|mut opened| opened.insert(target.to_string()))
            .unwrap_or(false);
        if !fresh {
            return false;
        }
        let _ = crate::adapters::browser::open_guarded(target);
        true
    }

    /// Manual enrollment: import an existing session token, verify the participant
    /// reference and origin against the server's own identity response, and record the
    /// binding. Import never broadens the session's grants. When `identity` names no
    /// existing identity, a fresh one is minted with `name` as its handle.
    pub fn enroll(&self, name: &str, origin: &str, session: &str, auto_check: bool) -> Result<CompanionEntry, String> {
        self.enroll_as(name, None, origin, session, auto_check)
    }

    /// Manual enrollment bound to an explicit identity (handle or local id).
    pub fn enroll_as(
        &self,
        name: &str,
        identity: Option<&str>,
        origin: &str,
        session: &str,
        auto_check: bool,
    ) -> Result<CompanionEntry, String> {
        let canonical = refs::acceptable_origin(origin)
            .ok_or_else(|| "Use one HTTPS server origin, or explicit loopback HTTP for development".to_string())?;
        let context = RequestContext {
            origin: canonical.clone(),
            credential: session.to_string(),
            participant_ref: String::new(),
            dpop: None,
        };
        let raw = self.port.get(&context, "/api/v1/experience").map_err(|error| match error {
            ExperienceError::Unreachable => "the server could not be reached".to_string(),
            ExperienceError::Unauthorized | ExperienceError::DpopChallenge { .. } => {
                "the session was rejected; it may be expired, revoked or from another server".to_string()
            }
            ExperienceError::Application { code, message } => format!("{code}: {message}"),
            ExperienceError::Transport(detail) => detail,
        })?;
        let experience = contract::parse(&raw).map_err(|error| format!("{error}; is this a Tangent experience API?"))?;
        let identity_view = experience.identity.ok_or_else(|| "the server did not confirm a participant identity".to_string())?;
        if identity_view.participant_ref.is_empty() {
            return Err("the server did not confirm a participant reference".to_string());
        }
        let mut store = self.lock_store()?;
        let bound_identity = match identity {
            Some(handle_or_id) => store.identity_by_moniker(handle_or_id).ok_or_else(|| {
                format!("no local identity matches '{handle_or_id}'; create one first in the operator page")
            })?,
            None => match store.identity_by_handle(name) {
                Some(existing) => existing,
                None => {
                    if !valid_handle(name) {
                        return Err(format!("'{name}' cannot become an identity handle; use 2-253 characters without spaces"));
                    }
                    let minted = crate::domain::identity::Identity {
                        local_id: crate::adapters::store::new_local_id(),
                        handle: name.to_string(),
                        display_name: None,
                        bound_did: None,
                        created_at: now_millis(),
                    };
                    store.upsert_identity(minted.clone())?;
                    minted
                }
            },
        };
        if let Some(existing) = store.enrollment_at(&bound_identity.local_id, &canonical) {
            return Err(format!(
                "identity '{}' already holds an enrollment at {canonical} ({}); forget it first if you mean to replace it",
                bound_identity.handle, existing.companion_id
            ));
        }
        // The imported session is stored per enrollment, keyed by its fresh companion id:
        // one identity at two servers keeps two distinct sessions.
        let companion_id = format!("cmp_{}", crate::adapters::store::short_uuid());
        let entry = CompanionEntry {
            companion_id,
            local_id: bound_identity.local_id.clone(),
            name: name.to_string(),
            origin: canonical,
            participant_ref: identity_view.participant_ref,
            did: identity_view.did,
            display_name: Some(identity_view.display_name),
            handle: identity_view.handle,
            enrolled_at: now_millis(),
            auto_check,
        };
        store.upsert_companion(entry.clone());
        store.set_session(&entry.companion_id, session);
        store.save()?;
        drop(store);
        self.events.publish(DomainEvent::CompanionSelected { companion_id: entry.companion_id.clone() });
        Ok(entry)
    }

    /// Unbound enrollment per the frozen W2 contract: ask the server to enroll this
    /// identity, store the returned session in connector state, and create the
    /// enrollment. The token never renders, logs or journals.
    pub fn enroll_unbound(&self, local_id: &str, origin: &str) -> Result<CompanionEntry, String> {
        self.attributed("operator.enroll_unbound", || {
            let canonical = refs::acceptable_origin(origin)
                .ok_or_else(|| "Use one HTTPS server origin, or explicit loopback HTTP for development".to_string())?;
            let (identity, already) = {
                let store = self.lock_store()?;
                let identity = store.identity(local_id).ok_or_else(|| "no local identity matches that id".to_string())?;
                let already = store.enrollment_at(local_id, &canonical).is_some();
                (identity, already)
            };
            if already {
                return Err(
                    "already_enrolled: this identity already holds a session for that server. Use the existing enrollment, or forget it first to re-enroll."
                        .to_string(),
                );
            }
            let mut body = json!({
                "client": { "localId": identity.local_id, "handle": identity.handle },
                "serverRef": { "label": "tangent-connector" },
            });
            if let Some(display) = &identity.display_name {
                body["client"]["displayName"] = json!(display);
            }
            let raw = self
                .port
                .enroll(&canonical, "/api/v1/experience/identities/enroll", &body)
                .map_err(|error| enroll_transport_error(&error))?;
            let response: contract::EnrollResponseDto =
                serde_json::from_value(raw).map_err(|error| format!("malformed enrollment response: {error}"))?;
            match response.status.as_str() {
                "ok" => {
                    let participant = response
                        .participant
                        .ok_or_else(|| "the server confirmed enrollment without a participant".to_string())?;
                    if participant.participant_ref.is_empty() {
                        return Err("the server did not confirm a participant reference".to_string());
                    }
                    let granted = response
                        .credential
                        .ok_or_else(|| "the server confirmed enrollment without a session".to_string())?;
                    if granted.token.is_empty() {
                        return Err("the server returned an empty session".to_string());
                    }
                    let mut store = self.lock_store()?;
                    // Re-check under the write lock: a concurrent intake may have enrolled
                    // this (identity, origin) while the exchange was in flight. The new
                    // session is then discarded — the state save below is the only write,
                    // so nothing needs rolling back — and the honest answer is
                    // already_enrolled with the existing enrollment intact.
                    if let Some(existing) = store.enrollment_at(local_id, &canonical) {
                        return Err(format!(
                            "already_enrolled: identity '{}' gained an enrollment at {canonical} during the exchange ({}); use it, or forget it first to re-enroll",
                            identity.handle, existing.companion_id
                        ));
                    }
                    // Session storage is keyed by the per-enrollment companion id: two
                    // servers, two distinct sessions (never one overwriting the other).
                    let companion_id = format!("cmp_{}", crate::adapters::store::short_uuid());
                    let entry = CompanionEntry {
                        companion_id,
                        local_id: identity.local_id.clone(),
                        name: identity.handle.clone(),
                        origin: canonical,
                        participant_ref: participant.participant_ref,
                        did: participant.did,
                        display_name: identity.display_name.clone().or(participant.best_label.clone()),
                        handle: participant.best_label,
                        enrolled_at: now_millis(),
                        auto_check: true,
                    };
                    store.upsert_companion(entry.clone());
                    store.set_session(&entry.companion_id, &granted.token);
                    store.save()?;
                    drop(store);
                    self.events.publish(DomainEvent::CompanionSelected { companion_id: entry.companion_id.clone() });
                    Ok(entry)
                }
                _ => {
                    let problem = response.problem.unwrap_or_default();
                    Err(enroll_blocked_error(&problem.code, &problem.message))
                }
            }
        })
    }

    /// Removes one enrollment and its stored session. Enrollment state (attention,
    /// checkpoints, contexts) cascades; the server side is untouched.
    pub fn forget_enrollment(&self, companion_id: &str) -> Result<(), String> {
        self.attributed("operator.forget_enrollment", || {
            let mut store = self.lock_store()?;
            store.companion(companion_id).ok_or_else(|| "no enrollment matches that id".to_string())?;
            store.remove_companion(companion_id);
            store.save()?;
            Ok(())
        })
    }

    // ---------- atproto binding (operator surface) ----------

    /// Binds one identity to an atproto account: the operator supplies a handle and an
    /// app password, the connector exchanges them for a PDS session (`createSession`)
    /// and records it in connector state (cookie-jar posture) with the identity's
    /// `bound_did`. The app password exists in memory for exactly this one request —
    /// it is never persisted, logged or echoed. Binding again replaces the session
    /// (the documented re-bind path on expiry); existing enrollments keep their own
    /// Tangent sessions untouched. A successful bind also clears any pending sign-in
    /// routing (R3) and resumes every waiting-for-operator connect for this identity
    /// (A3) — the handshake finishes connector-side, no model involved.
    pub fn bind_atproto(
        &self,
        local_id: &str,
        handle: &str,
        app_password: &str,
        pds: Option<&str>,
    ) -> Result<Identity, String> {
        let outcome = self.attributed("operator.bind_atproto", || {
            let supplied = handle.trim().trim_start_matches('@');
            if supplied.is_empty() || supplied.chars().count() > 253 || supplied.chars().any(|c| c.is_whitespace()) {
                return Err("invalid_handle: an atproto handle is 1-253 characters without whitespace".to_string());
            }
            if app_password.is_empty() || app_password.len() > 1024 {
                return Err("invalid_password: an app password is 1-1024 bytes".to_string());
            }
            let pds_origin = refs::acceptable_origin(pds.unwrap_or(DEFAULT_PDS)).ok_or_else(|| {
                "invalid_pds: the PDS origin must be HTTPS, or explicit loopback HTTP for development".to_string()
            })?;
            {
                let store = self.lock_store()?;
                store.identity(local_id).ok_or_else(|| "no local identity matches that id".to_string())?;
            }
            // The one and only journey of the app password: the createSession body. It
            // leaves scope when this closure returns.
            let body = json!({ "identifier": supplied, "password": app_password });
            let raw = self
                .port
                .enroll(&pds_origin, "/xrpc/com.atproto.server.createSession", &body)
                .map_err(|error| pds_signin_error(&error))?;
            let session: contract::CreateSessionDto = serde_json::from_value(raw)
                .map_err(|error| format!("malformed createSession response: {error}"))?;
            if session.did.is_empty() || !session.did.starts_with("did:") {
                return Err("the PDS confirmed the sign-in without a usable DID".to_string());
            }
            if session.access_jwt.is_empty() {
                return Err("the PDS confirmed the sign-in without a session".to_string());
            }
            // The DID document names the authoritative PDS; when it does not (or is not
            // an acceptable origin), the endpoint we just used stands.
            let authoritative = session.pds_endpoint().and_then(refs::acceptable_origin).unwrap_or(pds_origin);
            let updated = self.store_atproto_session(
                local_id,
                AtprotoSession {
                    did: session.did,
                    handle: session.handle.trim().trim_start_matches('@').to_string(),
                    access_jwt: session.access_jwt,
                    refresh_jwt: None,
                    pds: authoritative,
                    authserver: None,
                    client_id: None,
                    dpop_key: None,
                    obtained_at: now_millis(),
                },
            )?;
            Ok(updated)
        });
        if outcome.is_ok() {
            self.clear_pending_bind(local_id);
            self.resume_pending_connects(local_id);
        }
        outcome
    }

    /// Stores (or replaces) one identity's atproto session and mirrors the DID onto the
    /// identity's `bound_did`. The shared write tail of both binding paths — the
    /// app-password fallback and the OAuth `/bind` flow — so enrollments and reads see
    /// one consistent shape. Existing enrollments keep their own Tangent sessions.
    fn store_atproto_session(&self, local_id: &str, session: AtprotoSession) -> Result<Identity, String> {
        let mut store = self.lock_store()?;
        let mut updated = store.identity(local_id).ok_or_else(|| "no local identity matches that id".to_string())?;
        updated.bound_did = Some(session.did.clone());
        store.upsert_identity(updated.clone())?;
        store.set_atproto_session(local_id, session);
        store.save()?;
        Ok(updated)
    }

    /// Clears one identity's atproto session and `bound_did`. Existing enrollments and
    /// their Tangent sessions are untouched — those are per enrollment, not per binding.
    pub fn unbind_atproto(&self, local_id: &str) -> Result<crate::domain::identity::Identity, String> {
        self.attributed("operator.unbind_atproto", || {
            let mut store = self.lock_store()?;
            let mut identity = store.identity(local_id).ok_or_else(|| "no local identity matches that id".to_string())?;
            identity.bound_did = None;
            store.upsert_identity(identity.clone())?;
            store.remove_atproto_session(local_id);
            store.save()?;
            Ok(identity)
        })
    }

    // ---------- atproto OAuth binding (the /bind route) ----------

    /// Starts one identity's OAuth bind (the `/bind` route's GET — no interstitial, the
    /// owner correction). Without a handle it goes straight to the default
    /// authorization server (`TANGENT_CONNECTOR_AUTHSERVER`, else the public Bluesky
    /// one) and parks no pre-declared account: the exchange's mandatory `sub` claim
    /// will name the bound DID. With a handle (the self-hosted escape hatch) it runs
    /// the discovery first — handle → DID → DID document → PDS → authorization server —
    /// and the exchange must then agree with the resolved DID. Either way the answer is
    /// the authorize URL the operator's browser is redirected to (a 302); the
    /// provider's own UI handles account selection and sign-in. Starting a new bind
    /// for the same identity replaces its in-flight one; different identities bind
    /// concurrently. All network I/O happens outside every guard (W2-A). A flight
    /// that cannot be parked is an honest failure — never a dangling redirect.
    pub fn begin_atproto_bind(&self, local_id: &str, handle: Option<&str>, redirect_uri: &str) -> Result<String, String> {
        self.attributed("operator.atproto_bind_start", || {
            {
                let store = self.lock_store()?;
                store.identity(local_id).ok_or_else(|| "no local identity matches that id".to_string())?;
            }
            let start = self.oauth().start(handle, redirect_uri)?;
            let authorize_url = start.authorize_url.clone();
            let mut flights = self
                .bind_flights
                .lock()
                .map_err(|_| "bind_unparkable: the connector's bind state is unavailable; restart the connector and try again".to_string())?;
            let now = now_millis();
            let ttl = self.bind_flight_ttl_ms.load(Ordering::Relaxed);
            // Age out, then keep one bind at a time per identity.
            flights.retain(|flight| now.saturating_sub(flight.created_at) < ttl);
            flights.retain(|flight| flight.local_id != local_id);
            flights.push(BindFlight { local_id: local_id.to_string(), created_at: now, start });
            while flights.len() > BIND_FLIGHT_LIMIT {
                flights.remove(0);
            }
            Ok(authorize_url)
        })
    }

    /// Completes one OAuth bind (the loopback callback): validates the state and the
    /// issuer (`iss` is mandatory — RFC 9207 — and must be the authorization server the
    /// flight started with), exchanges the code for tokens, and binds exactly the
    /// account the exchange's `sub` names — the account the operator authenticated as;
    /// on the `?handle=` discovery path a differing `sub` is the honest
    /// account_mismatch refusal. The PDS and canonical handle come from the bound
    /// DID's document when the flight parked none. The session — refresh token and
    /// DPoP key included, cookie-jar posture — is stored, and the waiting-connect hook
    /// runs on success, exactly like the app-password path.
    pub fn complete_atproto_bind(&self, state: &str, code: &str, issuer: Option<&str>, redirect_uri: &str) -> Result<String, String> {
        let mut bound_local: Option<String> = None;
        let outcome = self.attributed("operator.bind_atproto", || {
            // The issuer comes first (RFC 9207): without `iss` the callback does not
            // even name which authorization server answered, so nothing else is
            // trustworthy enough to try.
            let issuer = issuer.filter(|iss| !iss.is_empty()).ok_or_else(|| {
                "invalid_callback: the authorization server's callback carried no issuer (iss); start the bind again".to_string()
            })?;
            let flight = {
                let mut flights = match self.bind_flights.lock() {
                    Ok(flights) => flights,
                    Err(_) => return Err("state lock poisoned".to_string()),
                };
                let position = flights
                    .iter()
                    .position(|flight| flight.start.state == state)
                    .ok_or_else(|| {
                        "state_mismatch: no started bind matches that state (it may have expired or already completed); start the bind again".to_string()
                    })?;
                let flight = flights.remove(position);
                let ttl = self.bind_flight_ttl_ms.load(Ordering::Relaxed);
                if now_millis().saturating_sub(flight.created_at) >= ttl {
                    return Err("bind_expired: this bind started too long ago; start it again".to_string());
                }
                flight
            };
            if refs::acceptable_origin(issuer).as_deref() != Some(flight.start.authserver.as_str()) {
                return Err(format!(
                    "issuer_mismatch: the callback claims issuer {issuer}, but the bind started with {}",
                    flight.start.authserver
                ));
            }
            let tokens = self.oauth().exchange(&flight.start, code, redirect_uri)?;
            // The binding IS the authenticated account: `sub` (mandatory) names the
            // DID. Only the ?handle= discovery path pre-declared one to disagree with.
            if let Some(declared) = flight.start.did.as_deref() {
                if tokens.sub != declared {
                    return Err(
                        "account_mismatch: the authorized account resolves to a different DID than the bind started with; start the bind again".to_string(),
                    );
                }
            }
            let (pds, discovered_handle) = match (flight.start.pds.as_deref(), flight.start.handle.as_deref()) {
                (Some(pds), handle) => (pds.to_string(), handle.map(str::to_string)),
                (None, _) => {
                    let (pds, doc_handle) = self.oauth().resolve_account(&tokens.sub)?;
                    (pds, doc_handle)
                }
            };
            // The bound label: the DID document's canonical handle when one is known,
            // else the DID itself (honest, never a guess).
            let handle = discovered_handle.unwrap_or_else(|| tokens.sub.clone());
            let local_id = flight.local_id.clone();
            self.store_atproto_session(
                &local_id,
                AtprotoSession {
                    did: tokens.sub.clone(),
                    handle: handle.clone(),
                    access_jwt: tokens.access_token,
                    refresh_jwt: tokens.refresh_token,
                    pds,
                    authserver: Some(flight.start.authserver.clone()),
                    client_id: Some(flight.start.client_id.clone()),
                    dpop_key: Some(flight.start.dpop_key.clone()),
                    obtained_at: now_millis(),
                },
            )?;
            bound_local = Some(local_id);
            Ok(handle)
        });
        if outcome.is_ok() {
            if let Some(local_id) = bound_local {
                self.clear_pending_bind(&local_id);
                self.resume_pending_connects(&local_id);
            }
        }
        outcome
    }

    /// The per-identity refresh mutex (R5). Leaf lock: taken only around one
    /// identity's refresh, never while holding the store lock (the refresh takes the
    /// store inside, briefly, in its own scopes).
    fn refresh_lock_of(&self, local_id: &str) -> Arc<Mutex<()>> {
        match self.refresh_locks.lock() {
            Ok(mut locks) => locks
                .entry(local_id.to_string())
                .or_insert_with(|| Arc::new(Mutex::new(())))
                .clone(),
            // A poisoned registry lock must not brick refreshes: an unsynchronized
            // refresh is still correct (rotation just converges on the last writer).
            Err(_) => Arc::new(Mutex::new(())),
        }
    }

    /// Silent refresh before use (the OAuth bind's promise): an access token inside its
    /// refresh margin is renewed from the stored refresh token with the session's DPoP
    /// key, and the renewed session is stored before the caller proceeds. One small
    /// per-identity mutex serializes this (R5), so a MCP Connect and the operator
    /// auto-resume can never double-refresh — the later waiter re-reads the session
    /// and finds the earlier one's renewal. The refreshed `sub` must be the same
    /// account (R3, mandatory now) or the honest re-bind error. App-password sessions
    /// carry no refresh material and pass through; a token without a readable expiry
    /// is used as-is (the PDS refuses it honestly if stale). A store failure AFTER a
    /// rotation keeps the rotated tokens in the live store (R5): the call proceeds on
    /// them and the next save persists them, rather than bricking on the stale disk
    /// copy. A failed refresh is the honest expired-session error — never a silent
    /// unbound fallback.
    fn refresh_atproto_if_stale(&self, local_id: &str) -> Result<AtprotoSession, String> {
        // Serialization first; the session is re-read under the lock so a concurrent
        // refresh's result is seen instead of duplicated.
        let serializer = self.refresh_lock_of(local_id);
        let _serial = serializer.lock().unwrap_or_else(|poisoned| poisoned.into_inner());
        let session = {
            let store = self.lock_store()?;
            store.atproto_session(local_id)
        };
        let Some(session) = session else {
            return Err(
                "atproto_session_missing: the atproto session is gone (it may have expired). Re-bind the identity on the operator page.".to_string(),
            );
        };
        let (Some(refresh), Some(authserver), Some(key)) = (&session.refresh_jwt, &session.authserver, &session.dpop_key) else {
            return Ok(session);
        };
        if !atproto_oauth::access_needs_refresh(&session.access_jwt, now_millis() / 1000) {
            return Ok(session);
        }
        // The client id the grant lives under: the scope-declaring form for new
        // binds, the bare localhost origin for pre-scope-era sessions.
        let client_id = session
            .client_id
            .clone()
            .unwrap_or_else(|| atproto_oauth::CLIENT_ID.to_string());
        match self.oauth().refresh(authserver, refresh, key, &client_id) {
            Ok(tokens) => {
                if tokens.sub != session.did {
                    return Err(
                        "atproto_session_expired: the refresh returned a different account. Re-bind the identity on the operator page.".to_string(),
                    );
                }
                let mut renewed = session;
                renewed.access_jwt = tokens.access_token;
                if let Some(rotated) = tokens.refresh_token {
                    renewed.refresh_jwt = Some(rotated);
                }
                renewed.obtained_at = now_millis();
                if self.store_atproto_session(local_id, renewed.clone()).is_err() {
                    // Persistence failed, not the session: the rotated tokens are
                    // already in the live store, so the next attempt (and the next
                    // save anywhere) reuses them instead of bricking.
                }
                Ok(renewed)
            }
            Err(_) => Err(
                "atproto_session_expired: the PDS session could not be renewed (it may have expired or been revoked). Re-bind the identity on the operator page.".to_string(),
            ),
        }
    }

    /// Read-only atproto binding status (did, handle, PDS, session age) — never the
    /// access token.
    pub fn atproto_binding(&self, local_id: &str) -> Option<AtprotoBinding> {
        let store = self.lock_store().ok()?;
        store.atproto_session(local_id).map(|session| AtprotoBinding {
            did: session.did,
            handle: session.handle,
            pds: session.pds,
            obtained_at: session.obtained_at,
        })
    }

    /// The operator page's whole identity table in one store guard: every identity with
    /// its atproto binding status (never the access token) and its enrollment count.
    /// This is the ONLY shape the page should read identities through — a caller that
    /// instead walks the store directly and then asks per-identity questions re-enters
    /// the store lock and deadlocks the whole hub (the live popped-page freeze).
    pub fn identity_inventory(&self) -> Vec<(crate::domain::identity::Identity, Option<AtprotoBinding>, usize)> {
        let store = self.lock_store().expect("state lock");
        store
            .identities()
            .iter()
            .map(|identity| {
                let binding = store.atproto_session(&identity.local_id).map(|session| AtprotoBinding {
                    did: session.did,
                    handle: session.handle,
                    pds: session.pds,
                    obtained_at: session.obtained_at,
                });
                (identity.clone(), binding, store.companions_of(&identity.local_id).len())
            })
            .collect()
    }

    /// Shared discovery step of the bound handshake: reads the server's own
    /// `/.well-known/tangent-mcp` document and validates that it offers exactly the
    /// service-proof exchange this connector speaks. The proof audience comes from the
    /// server, never hardcoded. Used by `enroll_bound` and by `Connect`'s pre-flight
    /// (an unusable server is an honest error before any operator attention is asked).
    fn discover_proof_spec(&self, canonical: &str) -> Result<contract::ServiceProofDto, String> {
        let raw = self
            .port
            .discover(canonical, "/.well-known/tangent-mcp")
            .map_err(|error| discovery_error(&error))?;
        let discovery: contract::DiscoveryDto = serde_json::from_value(raw)
            .map_err(|error| format!("malformed discovery document: {error}"))?;
        let proof_spec = discovery.service_proof.ok_or_else(|| {
            "no_service_proof: this server offers no service-proof enrollment; use the unbound enrollment path".to_string()
        })?;
        if proof_spec.method != EXCHANGE_LXM {
            return Err(format!(
                "exchange_method_mismatch: the server expects '{}', but this connector speaks only '{EXCHANGE_LXM}'",
                proof_spec.method
            ));
        }
        if !proof_spec.audience.starts_with("did:") || proof_spec.audience.len() > 512 {
            return Err("invalid_audience: the server's proof audience is not a DID".to_string());
        }
        Ok(proof_spec)
    }

    /// One PDS `getServiceAuth` call. OAuth sessions ride a DPoP proof under the DPoP
    /// auth scheme (RFC 9449 §7.1) carrying the PDS's OWN nonce: the first ask uses
    /// the cached one when it is fresh, otherwise none; a 401 answering with a
    /// `DPoP-Nonce` header is honored exactly ONCE — the nonce is cached per PDS and
    /// the retried proof embeds it. A second challenge, or any other refusal, maps
    /// through [`service_auth_error`]. App-password sessions carry no key: plain
    /// Bearer, no proof, no retry.
    fn pds_service_auth(&self, atproto: &AtprotoSession, auth_path: &str) -> Result<Value, String> {
        let Some(key) = atproto.dpop_key.as_deref() else {
            let context = RequestContext {
                origin: atproto.pds.clone(),
                credential: atproto.access_jwt.clone(),
                participant_ref: String::new(),
                dpop: None,
            };
            return self.port.get(&context, auth_path).map_err(|error| service_auth_error(&error));
        };
        let htu = format!("{}{}", atproto.pds, "/xrpc/com.atproto.server.getServiceAuth");
        let mut nonce = self.oauth().resource_nonce(&atproto.pds);
        for attempt in 0..2 {
            let proof = self.oauth().resource_proof(key, "GET", &htu, &atproto.access_jwt, nonce.as_deref()).map_err(|_| {
                "atproto_session_expired: the session's DPoP key is unusable. Re-bind the identity on the operator page.".to_string()
            })?;
            let context = RequestContext {
                origin: atproto.pds.clone(),
                credential: atproto.access_jwt.clone(),
                participant_ref: String::new(),
                dpop: Some(proof),
            };
            match self.port.get(&context, auth_path) {
                Ok(value) => return Ok(value),
                Err(ExperienceError::DpopChallenge { nonce: fresh }) if attempt == 0 => {
                    self.oauth().remember_resource_nonce(&atproto.pds, &fresh);
                    nonce = Some(fresh);
                }
                Err(error) => return Err(service_auth_error(&error)),
            }
        }
        // The retried proof was challenged again: retrying further cannot settle it.
        Err(service_auth_error(&ExperienceError::DpopChallenge { nonce: String::new() }))
    }

    /// Bound enrollment — the primary path: verify the server's discovery document,
    /// have the identity's PDS mint a service-auth proof for exactly that audience, and
    /// exchange the proof at `/mcp/token` for a Tangent session stored per enrollment.
    /// The proof JWT is ephemeral (created and consumed here); the audience comes from
    /// the server, never hardcoded; the token never renders, logs or journals.
    pub fn enroll_bound(&self, local_id: &str, origin: &str) -> Result<CompanionEntry, String> {
        self.attributed("operator.enroll_bound", || {
            let canonical = refs::acceptable_origin(origin)
                .ok_or_else(|| "Use one HTTPS server origin, or explicit loopback HTTP for development".to_string())?;
            let (identity, atproto, already) = {
                let store = self.lock_store()?;
                let identity = store.identity(local_id).ok_or_else(|| "no local identity matches that id".to_string())?;
                let atproto = store.atproto_session(local_id);
                let already = store.enrollment_at(local_id, &canonical).is_some();
                (identity, atproto, already)
            };
            if already {
                return Err(
                    "already_enrolled: this identity already holds a session for that server. Use the existing enrollment, or forget it first to re-enroll."
                        .to_string(),
                );
            }
            let Some(bound_did) = identity.bound_did.clone() else {
                return Err(
                    "atproto_binding_required: bind this identity to an atproto account on the operator page first".to_string(),
                );
            };
            let Some(atproto) = atproto else {
                return Err(
                    "atproto_session_missing: the atproto session is gone (it may have expired). Re-bind the identity on the operator page."
                        .to_string(),
                );
            };
            if atproto.did != bound_did {
                return Err(
                    "atproto_binding_stale: the bound DID and the stored atproto session disagree. Re-bind the identity on the operator page."
                        .to_string(),
                );
            }
            // Silent refresh before use: an OAuth access token inside its margin renews
            // here, outside every guard (W2-A) and under the identity's refresh mutex;
            // the renewed session is already stored.
            let atproto = self.refresh_atproto_if_stale(local_id)?;

            // Step 1 — discovery: the proof audience comes from the server's own document.
            let proof_spec = self.discover_proof_spec(&canonical)?;

            // Step 2 — the PDS mints the proof: the discovery audience, the exact
            // exchange method, and an expiry inside the server's accepted window.
            // Percent-encoding the parameters means a crafted audience or origin can
            // never inject query structure into the request. OAuth sessions carry a
            // DPoP proof on this resource request (R2): htm/htu of this exact call,
            // `ath` binding the access token, and the PDS's OWN nonce — never the
            // authorization server's (RFC 9449 §8 gives each server its own nonce
            // context).
            let exp = now_millis() / 1000 + PROOF_EXPIRY_SECONDS;
            let auth_path = format!(
                "/xrpc/com.atproto.server.getServiceAuth?aud={}&lxm={}&exp={}",
                encode(&proof_spec.audience),
                encode(EXCHANGE_LXM),
                exp
            );
            let raw = self.pds_service_auth(&atproto, &auth_path)?;
            let auth: contract::ServiceAuthDto = serde_json::from_value(raw)
                .map_err(|error| format!("malformed getServiceAuth response: {error}"))?;
            if auth.token.is_empty() {
                return Err("the PDS returned no service-auth token".to_string());
            }

            // Step 3 — the exchange. Grants are bounded to welcome/read/post; manage is
            // explicitly never requested.
            let body = json!({ "name": identity.handle, "lifetimeDays": 7, "grants": ["welcome", "read", "post"] });
            let raw = self
                .port
                .exchange(&canonical, "/mcp/token", &body, &auth.token)
                .map_err(|error| exchange_error(&error))?;
            let exchanged: contract::BoundExchangeDto = serde_json::from_value(raw)
                .map_err(|error| format!("malformed exchange response: {error}"))?;
            if exchanged.token.is_empty() {
                return Err("the server confirmed the exchange without a session".to_string());
            }
            let credential = exchanged
                .credential
                .ok_or_else(|| "the server confirmed the exchange without a credential view".to_string())?;
            if credential.participant_id.is_empty() {
                return Err("the server did not confirm a participant reference".to_string());
            }

            let mut store = self.lock_store()?;
            // Re-check under the write lock: a concurrent intake may have enrolled this
            // (identity, origin) while the exchange was in flight. The freshly issued
            // session is then discarded server-side untouched, and the honest answer is
            // already_enrolled with the existing enrollment intact.
            if let Some(existing) = store.enrollment_at(local_id, &canonical) {
                return Err(format!(
                    "already_enrolled: identity '{}' gained an enrollment at {canonical} during the exchange ({}); use it, or forget it first to re-enroll",
                    identity.handle, existing.companion_id
                ));
            }
            // The Tangent session is stored per enrollment, keyed by its fresh companion
            // id — one identity at two servers keeps two distinct sessions, and the
            // identity-level atproto session is a third, separate thing.
            let companion_id = format!("cmp_{}", crate::adapters::store::short_uuid());
            let entry = CompanionEntry {
                companion_id,
                local_id: identity.local_id.clone(),
                name: identity.handle.clone(),
                origin: canonical,
                participant_ref: credential.participant_id,
                did: Some(bound_did),
                display_name: identity.display_name.clone().or(Some(atproto.handle.clone())),
                handle: Some(atproto.handle.clone()),
                enrolled_at: now_millis(),
                auto_check: true,
            };
            store.upsert_companion(entry.clone());
            store.set_session(&entry.companion_id, &exchanged.token);
            store.save()?;
            drop(store);
            self.events.publish(DomainEvent::CompanionSelected { companion_id: entry.companion_id.clone() });
            Ok(entry)
        })
    }

    // ---------- the on-the-fly handshake (Connect) ----------

    /// `Connect { serverUrl, identity? }` — the on-the-fly handshake (owner-directed):
    /// the model says "connect to server X" and enrollment is a consequence, not a
    /// ceremony. (a) the acting identity resolves by behavior — an explicit `identity`
    /// argument (exact match), or exactly one local identity, for every intake alike;
    /// (b) discovery against the operator-supplied origin; (c) with no usable atproto
    /// binding the handshake pops the operator page (this process's own, or a recorded
    /// reachable one) at that identity's sign-in anchor and returns honestly — NEVER a
    /// silent unbound fallback; (d) with a binding, enrollment runs only when no usable
    /// enrollment/session exists for the origin, and the handshake exits through
    /// `Arrive`'s orientation view led by the "You are … — session …" line (P2).
    /// `SelectCompanion` + `Arrive` stay the explicit path.
    fn connect(&self, server_url: &str, identity_arg: Option<&str>, initiator: &str) -> ToolOutcome {
        let Some(canonical) = refs::acceptable_origin(server_url) else {
            return self.problem_outcome(
                "Connect",
                "invalid_arguments",
                "Use one HTTPS server origin, or explicit loopback HTTP for development.",
                None,
            );
        };
        let identity = match self.resolve_connect_identity(identity_arg) {
            Ok(identity) => identity,
            Err(reason) => {
                self.connect_failed(&canonical, "", "identity_selection_required", initiator);
                return self.identity_question(&reason);
            }
        };
        // P5a coalescing: a live pending for this (identity, origin) means the waiting
        // state is already narrated on the feed — a looping caller's repeated Connects
        // refresh the pending but stay silent until state changes (sign-in, age-out, a
        // different identity or origin).
        let repeated = self.touch_pending_connect(&identity.local_id, &canonical);
        if !repeated {
            self.events.publish(DomainEvent::ConnectStarted { origin: canonical.clone(), initiator: initiator.to_string() });
            self.events.publish(DomainEvent::ConnectResolved {
                origin: canonical.clone(),
                identity: identity.handle.clone(),
                initiator: initiator.to_string(),
            });
        }
        // Discovery before any operator attention is requested: a server that cannot
        // do the proof exchange is an honest error, never a popped page.
        if let Err(error) = self.discover_proof_spec(&canonical) {
            let outcome = self.enrollment_problem("Connect", &error);
            self.connect_failed(&canonical, &identity.handle, &problem_code_of(&outcome), initiator);
            return outcome;
        }
        if !self.usable_binding(&identity.local_id) {
            return self.pop_sign_in(&identity, &canonical, repeated, initiator);
        }
        self.clear_pending_bind(&identity.local_id);
        // The wait (if any) is over: this connect finishes model-side, so its pending
        // must not linger into a duplicate auto-resume.
        self.drop_pending_connect(&identity.local_id, &canonical);
        self.connect_finish(&identity, &canonical, initiator)
    }

    /// Identity resolution is behavior, not configuration (owner correction): an explicit
    /// argument resolves exactly (handle or local id) with an honest miss; otherwise
    /// exactly one local identity resolves automatically for every intake — the MCP
    /// edge, the CLI and the operator channel alike — while zero or several resolve
    /// nothing, honestly, even when a choice would seem obvious.
    fn resolve_connect_identity(&self, identity_arg: Option<&str>) -> Result<Identity, String> {
        let store = self.lock_store().expect("state lock");
        if let Some(argument) = identity_arg {
            return store
                .identity_by_moniker(argument)
                .ok_or_else(|| format!("No local identity matches '{argument}'"));
        }
        let identities = store.identities();
        match identities.len() {
            0 => Err("No local identity exists yet".to_string()),
            1 => Ok(identities[0].clone()),
            _ => Err("Multiple local identities exist".to_string()),
        }
    }

    /// The honest unresolvable-identity answer: why resolution failed, which identities
    /// exist, and the explicit way forward. Never a guess, never machine-wide.
    fn identity_question(&self, reason: &str) -> ToolOutcome {
        let handles: Vec<String> = self.identities().iter().map(|identity| identity.handle.clone()).collect();
        let message = if handles.is_empty() {
            format!(
                "{reason}. Ask the operator to create one (OpenRegistration opens the operator page), then connect again."
            )
        } else {
            format!(
                "{reason}. Available identities: {}. Ask which one is yours, then connect again with identity set to one of them.",
                handles.join(" · ")
            )
        };
        self.problem_outcome("Connect", "identity_selection_required", &message, None)
    }

    /// Whether the identity holds an atproto binding usable for the proof exchange: a
    /// `bound_did` with a matching stored session. This is local staleness only — the
    /// PDS may still refuse the session, which the exchange maps honestly.
    fn usable_binding(&self, local_id: &str) -> bool {
        let Ok(store) = self.lock_store() else { return false };
        let Some(identity) = store.identity(local_id) else { return false };
        match store.atproto_session(local_id) {
            Some(session) => identity.bound_did.as_deref() == Some(session.did.as_str()),
            None => false,
        }
    }

    fn clear_pending_bind(&self, local_id: &str) {
        if let Ok(mut slot) = self.pending_bind.lock() {
            if slot.as_deref() == Some(local_id) {
                *slot = None;
            }
        }
    }

    fn page_url(&self) -> Option<String> {
        self.operator_page_url.lock().ok()?.clone()
    }

    /// The operator page a sign-in pop should open, and whether THIS process hosts it.
    /// Order (P4): this process's own page first (trusted — it is in-process and alive);
    /// otherwise the page URL the current long-running process recorded in state, but
    /// only after one cheap reachability probe, so a stale record from an unclean
    /// shutdown points no one at a dead port. The fixed-port world adds one last
    /// honest probe: with the deterministic default URL, a host that is running can be
    /// found even when no record survived.
    fn sign_in_page(&self) -> Option<(String, bool)> {
        if let Some(page) = self.page_url() {
            return Some((page, true));
        }
        let recorded = {
            let store = self.lock_store().ok()?;
            store.operator_page_url()
        };
        for candidate in recorded.into_iter().chain([DEFAULT_PAGE_URL.to_string()]) {
            if let Some(origin) = page_origin(&candidate) {
                if self.port.probe(&origin).is_ok() {
                    return Some((candidate, false));
                }
            }
        }
        None
    }

    /// The waiting-for-operator branch (c): pops the operator page at this identity's
    /// sign-in anchor (guarded, once per target per process), records the pending
    /// connect so the handshake auto-resumes when the operator completes the binding,
    /// narrates it on the feed, and returns the honest outcome. No enrollment side
    /// effect happens on this branch. `repeated` (P5a) means an identical pending is
    /// already narrated: the pending refreshes but the feed stays quiet.
    fn pop_sign_in(&self, identity: &Identity, canonical: &str, repeated: bool, initiator: &str) -> ToolOutcome {
        if let Ok(mut slot) = self.pending_bind.lock() {
            *slot = Some(identity.local_id.clone());
        }
        if !repeated {
            self.record_pending_connect(identity, canonical, initiator);
            self.events.publish(DomainEvent::ConnectWaitingForOperator {
                origin: canonical.to_string(),
                identity: identity.handle.clone(),
                needed: format!("sign in identity '{}': its atproto account (the provider's sign-in page opens in the browser)", identity.handle),
                initiator: initiator.to_string(),
            });
        }
        let page = self.sign_in_page();
        let opened = page.as_ref().map(|(url, in_process)| {
            let target = format!("{url}{}", bind_anchor(&identity.local_id));
            let fresh = self.open_page_once(&target);
            (fresh, *in_process)
        });
        let message = match opened {
            Some((true, true)) => format!(
                "operator action needed — page opened to sign in identity '{}'; ask the operator, then connect again. The connect also finishes by itself once the sign-in is done.",
                identity.handle
            ),
            Some((true, false)) => format!(
                "operator action needed — page opened to sign in identity '{}'; ask the operator, then connect again.",
                identity.handle
            ),
            Some((false, _)) => format!(
                "operator action needed — page already opened to sign in identity '{}'; ask the operator, then connect again.",
                identity.handle
            ),
            None => format!(
                "operator action needed — no reachable operator page is running. Ask the operator to start tangent-connector operator to sign in identity '{}', then connect again.",
                identity.handle
            ),
        };
        let code = if opened.is_some() { "operator_action_needed" } else { "operator_page_unavailable" };
        self.problem_outcome("Connect", code, &message, None)
    }

    /// The (d) step shared by `Connect` and the auto-resume: with a usable binding,
    /// ensure an enrollment with a live session for exactly this origin, then arrive.
    /// Enrollment runs only when no usable enrollment/session exists — a session-less
    /// enrollment is unusable for every operation, so the honest re-enroll path is
    /// forget + bound exchange (the same one the CLI documents). An existing enrollment
    /// (of either tier) with its session intact is used as-is.
    fn connect_enroll(&self, identity: &Identity, canonical: &str, initiator: &str) -> Result<String, String> {
        let mut forget: Option<String> = None;
        let mut ready: Option<String> = None;
        {
            let store = self.lock_store()?;
            if let Some(entry) = store.enrollment_at(&identity.local_id, canonical) {
                if store.has_session(&entry.companion_id) {
                    ready = Some(entry.companion_id);
                } else {
                    forget = Some(entry.companion_id);
                }
            }
        }
        if let Some(broken) = forget {
            let _ = self.forget_enrollment(&broken);
        }
        if let Some(ready) = ready {
            return Ok(ready);
        }
        let entry = self.enroll_bound(&identity.local_id, canonical)?;
        self.events.publish(DomainEvent::ConnectEnrolled {
            origin: canonical.to_string(),
            identity: identity.handle.clone(),
            initiator: initiator.to_string(),
        });
        Ok(entry.companion_id)
    }

    /// Enroll-if-needed then arrive, publishing the handshake's live tail events and
    /// answering with `Arrive`'s orientation outcome led by the P2 line — "You are
    /// {handle} — session {contextId}": the session id IS the context handle later
    /// calls carry. Shared by the model-facing connect and the service-side
    /// auto-resume so both narrate identically. A PDS session that died mid-flight
    /// pops the sign-in page again (just-in-time re-bind).
    fn connect_finish(&self, identity: &Identity, canonical: &str, initiator: &str) -> ToolOutcome {
        match self.connect_enroll(identity, canonical, initiator) {
            Ok(companion_id) => {
                let mut outcome = self.arrive(&companion_id, canonical);
                if outcome.is_error {
                    self.connect_failed(canonical, &identity.handle, &problem_code_of(&outcome), initiator);
                } else {
                    let context_id = outcome
                        .structured
                        .pointer("/connector/contextId")
                        .and_then(Value::as_str)
                        .unwrap_or_default()
                        .to_string();
                    outcome.text = format!("You are {} — session {context_id}\n{}", identity.handle, outcome.text);
                    if let Some(connector) = outcome.structured.get_mut("connector").and_then(Value::as_object_mut) {
                        connector.insert("identityHandle".into(), json!(identity.handle));
                    }
                    self.events.publish(DomainEvent::ConnectArrived {
                        origin: canonical.to_string(),
                        identity: identity.handle.clone(),
                        initiator: initiator.to_string(),
                    });
                }
                outcome
            }
            Err(error) if error.starts_with("atproto_session_expired") => {
                self.pop_sign_in(identity, canonical, false, initiator)
            }
            Err(error) => {
                let outcome = self.enrollment_problem("Connect", &error);
                self.connect_failed(canonical, &identity.handle, &problem_code_of(&outcome), initiator);
                outcome
            }
        }
    }

    /// Whether an identical waiting connect is already pending (P5a coalescing), and if
    /// so refreshes it: the looping caller keeps the auto-resume window open while its
    /// repeated Connects stay silent on the feed.
    fn touch_pending_connect(&self, local_id: &str, origin: &str) -> bool {
        self.pending_connects
            .lock()
            .map(|mut pendings| {
                let now = now_millis();
                let mut found = false;
                for entry in pendings.iter_mut() {
                    if entry.local_id == local_id && entry.origin == origin {
                        entry.recorded_at = now;
                        found = true;
                    }
                }
                found
            })
            .unwrap_or(false)
    }

    /// Drops the pending for one (identity, origin): the connect finished model-side,
    /// so a later binding must not resume it a second time.
    fn drop_pending_connect(&self, local_id: &str, origin: &str) {
        if let Ok(mut pendings) = self.pending_connects.lock() {
            pendings.retain(|entry| !(entry.local_id == local_id && entry.origin == origin));
        }
    }

    /// Records one waiting-for-operator connect (A3) and arms its honest age-out: if
    /// the operator never completes (or abandons) the sign-in, the pending connect is
    /// dropped after [`PENDING_CONNECT_TIMEOUT_MS`] with a feed event — never silently.
    /// The age-out runs on ONE shared sweeper thread (armed here on the first pending):
    /// a looping model's repeated Connects each refresh the pending but spawn nothing,
    /// and the sweeper touches the pending list only briefly, once per sweep — it can
    /// never contend with the store, the operator page, or a Connect in flight.
    fn record_pending_connect(&self, identity: &Identity, canonical: &str, initiator: &str) {
        let pending = PendingConnect {
            local_id: identity.local_id.clone(),
            handle: identity.handle.clone(),
            origin: canonical.to_string(),
            initiator: initiator.to_string(),
            recorded_at: now_millis(),
        };
        {
            let mut pendings = match self.pending_connects.lock() {
                Ok(pendings) => pendings,
                Err(_) => return,
            };
            // One pending per (identity, origin): a repeated connect refreshes it.
            pendings.retain(|entry| !(entry.local_id == pending.local_id && entry.origin == pending.origin));
            pendings.push(pending);
        }
        let pendings = self.pending_connects.clone();
        let events = self.events();
        self.sweep_once.call_once(|| {
            let _ = std::thread::Builder::new()
                .name("tangent-connect-timeout".into())
                .spawn(move || loop {
                    std::thread::sleep(std::time::Duration::from_millis(PENDING_CONNECT_SWEEP_MS));
                    let expired: Vec<PendingConnect> = pendings
                        .lock()
                        .map(|mut pendings| {
                            let now = now_millis();
                            let mut expired = Vec::new();
                            pendings.retain(|entry| {
                                if now.saturating_sub(entry.recorded_at) >= PENDING_CONNECT_TIMEOUT_MS {
                                    expired.push(entry.clone());
                                    false
                                } else {
                                    true
                                }
                            });
                            expired
                        })
                        .unwrap_or_default();
                    for entry in expired {
                        events.publish(DomainEvent::ConnectFailed {
                            origin: entry.origin,
                            identity: entry.handle,
                            code: "operator_timeout: the pending connect aged out waiting for the operator".into(),
                            initiator: entry.initiator,
                        });
                    }
                });
        });
    }

    /// The auto-resume (A3), armed right after an operator completed a binding: every
    /// fresh pending connect for that identity finishes by itself — no model involved —
    /// through the same enroll-and-arrive steps with the same live progress. The
    /// model's next Connect or Arrive simply finds the enrollment and session ready.
    ///
    /// Lock discipline: the pending list is drained under its own lock and RELEASED
    /// before any hub work; the enroll-and-arrive steps take the store lock only in
    /// their own short scopes, with all network I/O outside it (W2-A). The resume runs
    /// on the operator connection thread that performed the bind and holds no lock
    /// across its whole journey.
    fn resume_pending_connects(&self, local_id: &str) {
        let resumes: Vec<PendingConnect> = {
            let Ok(mut pendings) = self.pending_connects.lock() else { return };
            let now = now_millis();
            let (due, keep): (Vec<_>, Vec<_>) = pendings
                .drain(..)
                .partition(|entry| entry.local_id == local_id && now.saturating_sub(entry.recorded_at) < PENDING_CONNECT_TIMEOUT_MS);
            pendings.extend(keep);
            due
        };
        for pending in resumes {
            let Some(identity) = self.identity(&pending.local_id) else { continue };
            self.events.publish(DomainEvent::ConnectOperatorCompleted {
                origin: pending.origin.clone(),
                identity: identity.handle.clone(),
                // The resume is always armed by an operator action on the page.
                initiator: "operator (page)".to_string(),
            });
            let _ = self.connect_finish(&identity, &pending.origin, "operator (page)");
        }
    }

    fn connect_failed(&self, origin: &str, identity: &str, code: &str, initiator: &str) {
        self.events.publish(DomainEvent::ConnectFailed {
            origin: origin.to_string(),
            identity: identity.to_string(),
            code: code.to_string(),
            initiator: initiator.to_string(),
        });
    }

    /// Maps an enrollment-engine `Err(String)` (its own "code: message" discipline)
    /// onto the honest tool problem outcome — the same split the operator API applies.
    fn enrollment_problem(&self, tool: &str, error: &str) -> ToolOutcome {
        let (code, message) = match error.split_once(": ") {
            Some((code, message)) => (code.to_string(), message.to_string()),
            None => ("blocked".to_string(), error.to_string()),
        };
        self.problem_outcome(tool, &code, &message, None)
    }

    // ---------- identities (operator surface) ----------

    pub fn create_identity(&self, handle: &str, display_name: Option<&str>) -> Result<crate::domain::identity::Identity, String> {
        self.attributed("operator.create_identity", || {
            if !valid_handle(handle) {
                return Err("a handle is 2-253 characters without whitespace or ':'".to_string());
            }
            let identity = crate::domain::identity::Identity {
                local_id: crate::adapters::store::new_local_id(),
                handle: handle.to_string(),
                display_name: display_name.map(str::to_string),
                bound_did: None,
                created_at: now_millis(),
            };
            let mut store = self.lock_store()?;
            store.upsert_identity(identity.clone())?;
            store.save()?;
            Ok(identity)
        })
    }

    /// Updates handle and/or display name. `display_name`: `None` leaves it unchanged,
    /// `Some(None)` clears it, `Some(Some(v))` sets it. The local id never changes.
    pub fn update_identity(
        &self,
        local_id: &str,
        handle: Option<&str>,
        display_name: Option<Option<&str>>,
    ) -> Result<crate::domain::identity::Identity, String> {
        self.attributed("operator.update_identity", || {
            let mut store = self.lock_store()?;
            let mut identity = store.identity(local_id).ok_or_else(|| "no identity matches that id".to_string())?;
            if let Some(handle) = handle {
                if !valid_handle(handle) {
                    return Err("a handle is 2-253 characters without whitespace or ':'".to_string());
                }
                identity.handle = handle.to_string();
            }
            if let Some(display) = display_name {
                identity.display_name = display.map(str::to_string);
            }
            store.upsert_identity(identity.clone())?;
            store.save()?;
            Ok(identity)
        })
    }

    /// Deletes an identity. Refuses while enrollments exist unless `cascade` forgets them
    /// (and their sessions) first.
    pub fn delete_identity(&self, local_id: &str, cascade: bool) -> Result<(), String> {
        self.attributed("operator.delete_identity", || {
            let mut store = self.lock_store()?;
            let identity = store.identity(local_id).ok_or_else(|| "no identity matches that id".to_string())?;
            let enrollments = store.companions_of(local_id);
            if !enrollments.is_empty() && !cascade {
                return Err(format!(
                    "identity '{}' still holds {} enrollment(s); forget them first, or confirm a cascade delete",
                    identity.handle,
                    enrollments.len()
                ));
            }
            for entry in &enrollments {
                store.remove_companion(&entry.companion_id);
            }
            store.remove_identity(local_id);
            store.save()?;
            Ok(())
        })
    }

    pub fn identities(&self) -> Vec<crate::domain::identity::Identity> {
        let store = self.lock_store().expect("state lock");
        store.identities().to_vec()
    }

    pub fn identity(&self, local_id: &str) -> Option<crate::domain::identity::Identity> {
        let store = self.lock_store().expect("state lock");
        store.identity(local_id)
    }

    pub fn enrollments_of(&self, local_id: &str) -> Vec<CompanionEntry> {
        let store = self.lock_store().expect("state lock");
        store.companions_of(local_id)
    }

    /// Every enrollment, oldest first, with its session availability (never the token).
    pub fn enrollment_inventory(&self) -> Vec<(CompanionEntry, bool)> {
        let store = self.lock_store().expect("state lock");
        store.companions().iter().map(|entry| (entry.clone(), store.has_session(&entry.companion_id))).collect()
    }

    /// Read-only attention/pending-write state per enrollment, for the operator page.
    pub fn enrollment_statuses(&self) -> Vec<EnrollmentStatus> {
        let store = self.lock_store().expect("state lock");
        let unsettled = store.unsettled_writes();
        store
            .companions()
            .iter()
            .map(|entry| EnrollmentStatus {
                companion_id: entry.companion_id.clone(),
                identity_local_id: entry.local_id.clone(),
                origin: entry.origin.clone(),
                waiting: store.waiting_count(&entry.companion_id),
                pending_attention: store
                    .attention_records(&entry.companion_id)
                    .iter()
                    .filter(|record| record.state != AttentionState::Delivered)
                    .count(),
                unresolved_writes: unsettled.iter().filter(|write| write.companion_id == entry.companion_id).count(),
            })
            .collect()
    }

    /// Attribution wrapper for operator-page mutations: the same invoked/completed pair
    /// every intake records, with the `Operator` channel.
    fn attributed<T>(&self, action: &str, run: impl FnOnce() -> Result<T, String>) -> Result<T, String> {
        self.events.publish(DomainEvent::ToolInvoked { channel: IntakeChannel::Operator, tool: action.to_string() });
        let result = run();
        let status = if result.is_ok() { "ok" } else { "error" };
        self.events.publish(DomainEvent::ToolCompleted {
            channel: IntakeChannel::Operator,
            tool: action.to_string(),
            status: status.into(),
            text_bytes: 0,
        });
        result
    }

    /// Direct store access for intakes rendering their own views (the CLI, the tray).
    ///
    /// LOCK RULE — the store mutex is a leaf lock. While holding it, only pure
    /// `StateStore` reads and writes are allowed: never a hub method that takes the
    /// store again (`std::sync::Mutex` is not re-entrant — the second `lock()` on the
    /// same thread blocks forever while still holding the mutex, freezing every other
    /// intake), and never network I/O (W2-A: exchange outside the lock, re-check after).
    /// Intakes that need composed facts should ask the hub for a batched read (see
    /// [`ConnectorHub::identity_inventory`]) instead of walking the store themselves.
    pub fn store(&self) -> &Mutex<StateStore> {
        &self.store
    }

    pub fn caller(&self) -> &CallerId {
        &self.caller
    }

    /// The shared entry point for every intake. Decodes and executes one tool invocation.
    pub fn invoke(&self, channel: IntakeChannel, tool: &str, arguments: &Value) -> ToolOutcome {
        let operation = match decode(tool, arguments) {
            Ok(operation) => operation,
            Err(error) => {
                self.events.publish(DomainEvent::ToolInvoked { channel, tool: tool.to_string() });
                self.events.publish(DomainEvent::ToolCompleted {
                    channel,
                    tool: tool.to_string(),
                    status: "error".into(),
                    text_bytes: 0,
                });
                return self.problem_outcome(tool, "invalid_arguments", &error, None);
            }
        };
        self.execute(channel, operation)
    }

    pub fn execute(&self, channel: IntakeChannel, operation: Operation) -> ToolOutcome {
        let tool = operation.tool_name();
        self.events.publish(DomainEvent::ToolInvoked { channel, tool: tool.to_string() });
        let initiator = self.initiator_label(channel);
        let outcome = self.dispatch(operation, &initiator);
        self.events.publish(DomainEvent::ToolCompleted {
            channel,
            tool: tool.to_string(),
            status: outcome.status.clone(),
            text_bytes: outcome.text.len(),
        });
        outcome
    }

    /// The feed's initiator label (P5b): who started this call. MCP tool calls are the
    /// model acting through a named client; the command line and the operator page are
    /// the operator.
    fn initiator_label(&self, channel: IntakeChannel) -> String {
        match channel {
            IntakeChannel::Mcp => format!("model (via {})", self.caller.mcp_client_name().unwrap_or("stdio")),
            IntakeChannel::Cli => "operator (CLI)".to_string(),
            IntakeChannel::Operator => "operator (page)".to_string(),
        }
    }

    fn dispatch(&self, operation: Operation, initiator: &str) -> ToolOutcome {
        match operation {
            Operation::SelectCompanion { moniker } => self.select_companion(moniker.as_deref()),
            Operation::OpenRegistration => self.open_registration(),
            Operation::Connect { server_url, identity } => self.connect(&server_url, identity.as_deref(), initiator),
            Operation::Arrive { companion_id, server_url } => self.arrive(&companion_id, &server_url),
            Operation::ListTangents { context_id, cursor } => {
                self.with_context("ListTangents", &context_id, ViewMode::Compact, |frame| {
                    let path = match &cursor {
                        Some(value) => format!("/api/v1/experience/tangents?cursor={}", encode(value)),
                        None => "/api/v1/experience/tangents".to_string(),
                    };
                    self.port.get(&frame.request, &path)
                })
            }
            Operation::ListTopics { context_id, tangent_ref, cursor } => {
                self.with_context("ListTopics", &context_id, ViewMode::Compact, |frame| {
                    let Some(tangent) = refs::tangent_key(&frame.request.origin, &tangent_ref) else {
                        return Err(invalid_ref("Tangent"));
                    };
                    let path = match &cursor {
                        Some(value) => format!("/api/v1/experience/tangents/{tangent}/topics?cursor={}", encode(value)),
                        None => format!("/api/v1/experience/tangents/{tangent}/topics"),
                    };
                    self.port.get(&frame.request, &path)
                })
            }
            Operation::ReadTopic { context_id, topic_ref, cursor, around_post_ref, view, limit } => {
                self.with_context("ReadTopic", &context_id, view, |frame| {
                    let Some((_tangent, room)) = refs::topic_keys(&frame.request.origin, &topic_ref) else {
                        return Err(invalid_ref("Topic"));
                    };
                    let mut path = format!("/api/v1/experience/topics/{room}");
                    let mut query = Vec::new();
                    if let Some(value) = &cursor {
                        query.push(format!("cursor={}", encode(value)));
                    }
                    if let Some(value) = &around_post_ref {
                        query.push(format!("aroundPostRef={}", encode(value)));
                    }
                    if let Some(value) = limit {
                        query.push(format!("limit={value}"));
                    }
                    if !query.is_empty() {
                        path.push('?');
                        path.push_str(&query.join("&"));
                    }
                    self.port.get(&frame.request, &path)
                })
            }
            Operation::CreatePost { context_id, topic_ref, request_id, text, reply_to, view } => {
                self.with_context("CreatePost", &context_id, view, |frame| {
                    let Some((_tangent, room)) = refs::topic_keys(&frame.request.origin, &topic_ref) else {
                        return Err(invalid_ref("Topic"));
                    };
                    let mut body = json!({ "requestId": request_id, "text": text });
                    if let Some(reference) = &reply_to {
                        body["replyTo"] = json!(reference);
                    }
                    self.journaled_send(frame, "CreatePost", &topic_ref, &request_id, Route::TopicPosts(room.to_string()), &body)
                })
            }
            Operation::GetUpdates { context_id, view, cursor, scope_ref } => {
                self.get_updates(&context_id, view, cursor, scope_ref)
            }
            Operation::MarkRead { context_id, topic_ref, read_cursor, request_id, view } => {
                self.with_context("MarkRead", &context_id, view, |frame| {
                    let Some((_tangent, room)) = refs::topic_keys(&frame.request.origin, &topic_ref) else {
                        return Err(invalid_ref("Topic"));
                    };
                    let request_id = request_id.unwrap_or_else(|| format!("mark-{}", crate::adapters::store::short_uuid()));
                    let body = json!({ "requestId": request_id, "readCursor": read_cursor });
                    self.journaled_send(frame, "MarkRead", &topic_ref, &request_id, Route::TopicReadPosition(room.to_string()), &body)
                })
            }
            Operation::JoinTangent { context_id, tangent_ref, request_id, invite_ref, view } => {
                self.with_context("JoinTangent", &context_id, view, |frame| {
                    let Some(tangent) = refs::tangent_key(&frame.request.origin, &tangent_ref) else {
                        return Err(invalid_ref("Tangent"));
                    };
                    let mut body = json!({ "requestId": request_id });
                    if let Some(reference) = &invite_ref {
                        body["inviteRef"] = json!(reference);
                    }
                    self.journaled_send(frame, "JoinTangent", &tangent_ref, &request_id, Route::Membership(tangent.to_string()), &body)
                })
            }
            Operation::LeaveTangent { context_id, tangent_ref, request_id, view } => {
                self.with_context("LeaveTangent", &context_id, view, |frame| {
                    let Some(tangent) = refs::tangent_key(&frame.request.origin, &tangent_ref) else {
                        return Err(invalid_ref("Tangent"));
                    };
                    self.journaled_send(
                        frame,
                        "LeaveTangent",
                        &tangent_ref,
                        &request_id,
                        Route::Leave(tangent.to_string(), request_id.clone()),
                        &Value::Null,
                    )
                })
            }
            Operation::SetWatch { context_id, scope_ref, mode, request_id, view } => {
                self.with_context("SetWatch", &context_id, view, |frame| {
                    if refs::tangent_key(&frame.request.origin, &scope_ref).is_none()
                        && refs::topic_keys(&frame.request.origin, &scope_ref).is_none()
                    {
                        return Err(invalid_ref("Topic or Tangent"));
                    }
                    let request_id = request_id.unwrap_or_else(|| format!("watch-{}", crate::adapters::store::short_uuid()));
                    let body = json!({ "requestId": request_id, "scopeRef": scope_ref, "mode": mode });
                    self.journaled_send(frame, "SetWatch", &scope_ref, &request_id, Route::Watches, &body)
                })
            }
            Operation::GetOperation { context_id, request_id, view } => {
                self.with_context("GetOperation", &context_id, view, |frame| {
                    self.port
                        .get(&frame.request, &format!("/api/v1/experience/operations/{}", encode(&request_id)))
                })
            }
        }
    }

    /// Selection resolves an identity first, then one of its enrollments. Without a
    /// moniker the acting identity resolves by behavior — exactly one local identity is
    /// used (every intake alike); zero or several resolve nothing, honestly, and the
    /// answer is a question — never a guess, never machine-wide.
    fn select_companion(&self, moniker: Option<&str>) -> ToolOutcome {
        let store = self.lock_store().expect("state lock");
        match moniker {
            Some(moniker) => {
                if let Some(identity) = store.identity_by_moniker(moniker) {
                    return self.select_enrollment_of(&store, &identity);
                }
                match store.find_companion(moniker) {
                    Some(companion) => selected_outcome(&companion),
                    None => self.problem_outcome("SelectCompanion", "companion_unavailable",
                        "No enrolled companion or identity matches that moniker. Ask the operator to enroll one.", None),
                }
            }
            None => {
                let instruction = |detail: String| {
                    self.problem_outcome("SelectCompanion", "identity_selection_required", &detail, None)
                };
                let identities = store.identities();
                match identities.len() {
                    0 => instruction(
                        "No local identity exists yet. Ask the operator to create one (the operator page), or pass a moniker."
                            .to_string(),
                    ),
                    1 => self.select_enrollment_of(&store, &identities[0]),
                    _ => instruction(format!(
                        "Multiple local identities exist: {}. Ask which one is yours, or pass a moniker (an identity handle).",
                        identities.iter().map(|identity| identity.handle.clone()).collect::<Vec<_>>().join(" · ")
                    )),
                }
            }
        }
    }

    fn select_enrollment_of(&self, store: &StateStore, identity: &crate::domain::identity::Identity) -> ToolOutcome {
        let enrollments = store.companions_of(&identity.local_id);
        match enrollments.len() {
            1 => selected_outcome(&enrollments[0]),
            0 => self.problem_outcome("SelectCompanion", "companion_unavailable",
                &format!("Identity '{}' has no enrollment yet. Ask the operator to enroll it on a server first.", identity.handle), None),
            _ => self.problem_outcome("SelectCompanion", "identity_selection_needed",
                &format!(
                    "Identity '{}' is enrolled at {} servers. Select one explicitly with its companion id: {}",
                    identity.handle,
                    enrollments.len(),
                    enrollments.iter().map(|entry| format!("{} ({})", entry.companion_id, entry.origin)).collect::<Vec<_>>().join(", ")
                ), None),
        }
    }

    /// Attention, not execution (ADR 0009 invariant): browser-open the operator page
    /// so the human operator can create an identity or complete a pending sign-in. The
    /// anchor is routed (R3): after a `Connect` popped sign-in for one identity, this
    /// opens that identity's bind anchor; the default is the identity-creation view.
    /// The URL is constructed internally; it never renders into the tool response or
    /// any view. Nothing auto-runs: signing in and enrolling remain operator actions on
    /// that page.
    /// Once per process (F2): a second call answers honestly instead of spawning
    /// another tab for a looping model.
    fn open_registration(&self) -> ToolOutcome {
        let Some(target) = self.registration_target_url() else {
            return self.problem_outcome(
                "OpenRegistration",
                "operator_page_unavailable",
                "The local operator page is not running in this process. Ask the operator to start tangent-connector (serve or the operator verb) with its operator page.",
                None,
            );
        };
        let (text, registration) = if self.open_page_once(&target) {
            (
                "Opened the local operator page for the operator to create or bind an identity; ask the operator when done.",
                "operator_page",
            )
        } else {
            (
                "The operator page is already open for the operator to create or bind an identity; ask the operator when done.",
                "operator_page_already_open",
            )
        };
        ToolOutcome {
            is_error: false,
            status: "ok".into(),
            text: text.into(),
            structured: json!({
                "experience": null,
                "problem": null,
                "connector": {
                    "view": "compact",
                    "deliveryMode": DELIVERY_MODE,
                    "registration": registration,
                }
            }),
        }
    }

    fn arrive(&self, companion_id: &str, server_url: &str) -> ToolOutcome {
        let (companion, canonical_check) = {
            let store = self.lock_store().expect("state lock");
            (store.companion(companion_id), refs::acceptable_origin(server_url))
        };
        let Some(companion) = companion else {
            return self.problem_outcome("Arrive", "companion_unavailable",
                "That companion is not enrolled in this connector.", None);
        };
        // The destination must be the companion's enrolled canonical origin; a different
        // URL never silently rebinds the session.
        if canonical_check.as_deref() != Some(companion.origin.as_str()) {
            return self.problem_outcome("Arrive", "unreachable",
                &format!("That destination does not match this companion's server ({}).", companion.origin),
                Some((&companion, None)));
        }
        let session = match self.session_of(&companion) {
            Ok(session) => session,
            Err(error) => return self.problem_outcome("Arrive", "needs_operator_connection", &error, Some((&companion, None))),
        };
        let request = RequestContext { origin: companion.origin.clone(), credential: session, participant_ref: companion.participant_ref.clone(), dpop: None };
        let raw = match self.port.get(&request, "/api/v1/experience") {
            Ok(raw) => raw,
            Err(error) => return self.transport_problem("Arrive", &companion, None, &error),
        };
        let parsed = match contract::parse(&raw) {
            Ok(parsed) => parsed,
            Err(error) => return self.problem_outcome("Arrive", "unreachable", &error, Some((&companion, None))),
        };
        let bound = {
            let mut store = self.lock_store().expect("state lock");
            let bound = store.bind_context(&self.caller, &companion, now_millis());
            self.sync_attention(&mut store, &companion.companion_id, &parsed);
            let _ = store.save();
            bound
        };
        self.events.publish(DomainEvent::ContextArrived { context_id: bound.context_id.clone(), origin: companion.origin.clone() });
        self.finish("Arrive", &companion, Some(&bound), ViewMode::Orientation, raw, parsed, false)
    }

    fn get_updates(&self, context_id: &str, view: ViewMode, cursor: Option<String>, scope_ref: Option<String>) -> ToolOutcome {
        let binding = {
            let store = self.lock_store().expect("state lock");
            store.context(context_id)
        };
        let Some(context_binding) = binding else {
            return self.context_expired("GetUpdates");
        };
        let Some(companion) = self.companion_of(&context_binding.companion_id) else {
            return self.context_expired("GetUpdates");
        };
        let session = match self.session_of(&companion) {
            Ok(session) => session,
            Err(error) => return self.problem_outcome("GetUpdates", "needs_operator_connection", &error, Some((&companion, Some(&context_binding)))),
        };
        let request = RequestContext { origin: companion.origin.clone(), credential: session, participant_ref: companion.participant_ref.clone(), dpop: None };
        let mut query = Vec::new();
        match &cursor {
            // A supplied cursor continues the previous page sequence.
            Some(value) => query.push(format!("pageCursor={}", encode(value))),
            None => {
                let store = self.lock_store().expect("state lock");
                if let Some(checkpoint) = store.checkpoint(&companion.companion_id) {
                    query.push(format!("checkpoint={}", encode(&checkpoint)));
                }
            }
        }
        if let Some(scope) = &scope_ref {
            query.push(format!("scopeRef={}", encode(scope)));
        }
        let path = if query.is_empty() {
            "/api/v1/experience/updates".to_string()
        } else {
            format!("/api/v1/experience/updates?{}", query.join("&"))
        };
        let raw = match self.port.get(&request, &path) {
            Ok(raw) => raw,
            Err(error) => return self.transport_problem("GetUpdates", &companion, Some(&context_binding), &error),
        };
        let parsed = match contract::parse(&raw) {
            Ok(parsed) => parsed,
            Err(error) => return self.problem_outcome("GetUpdates", "unreachable", &error, Some((&companion, Some(&context_binding)))),
        };
        let unchanged = {
            let mut store = self.lock_store().expect("state lock");
            let previous = store.revision(&companion.companion_id);
            let unchanged = previous.as_deref() == Some(parsed.attention.revision.as_str());
            self.sync_attention(&mut store, &companion.companion_id, &parsed);
            // The recovery checkpoint always names the page start, not a page continuation.
            if parsed.continuation.activity_checkpoint.is_some() && cursor.is_none() {
                if let Some(checkpoint) = &parsed.continuation.activity_checkpoint {
                    store.set_checkpoint(&companion.companion_id, checkpoint);
                }
            }
            let _ = store.save();
            unchanged
        };
        self.finish("GetUpdates", &companion, Some(&context_binding), view, raw, parsed, unchanged)
    }

    /// Shared path for every context-scoped operation: resolve and validate the binding,
    /// run the use case, synchronize attention, then render with the delivery segment.
    fn with_context(
        &self,
        tool: &str,
        context_id: &str,
        view: ViewMode,
        run: impl FnOnce(&CallFrame) -> Result<Value, ExperienceError>,
    ) -> ToolOutcome {
        let binding = {
            let store = self.lock_store().expect("state lock");
            store.context(context_id)
        };
        let Some(context_binding) = binding else {
            return self.context_expired(tool);
        };
        let Some(companion) = self.companion_of(&context_binding.companion_id) else {
            return self.context_expired(tool);
        };
        if !context_binding.belongs_to(&self.caller, &companion.companion_id) || context_binding.origin != companion.origin {
            return self.context_expired(tool);
        }
        let session = match self.session_of(&companion) {
            Ok(session) => session,
            Err(error) => return self.problem_outcome(tool, "needs_operator_connection", &error, Some((&companion, Some(&context_binding)))),
        };
        let frame = CallFrame {
            request: RequestContext { origin: companion.origin.clone(), credential: session, participant_ref: companion.participant_ref.clone(), dpop: None },
            companion: companion.clone(),
            context_id: context_binding.context_id.clone(),
        };
        let raw = match run(&frame) {
            Ok(raw) => raw,
            Err(error) => return self.transport_problem(tool, &companion, Some(&context_binding), &error),
        };
        let parsed = match contract::parse(&raw) {
            Ok(parsed) => parsed,
            Err(error) => return self.problem_outcome(tool, "unreachable", &error, Some((&companion, Some(&context_binding)))),
        };
        let unchanged = {
            let mut store = self.lock_store().expect("state lock");
            let previous = store.revision(&companion.companion_id);
            let unchanged = previous.as_deref() == Some(parsed.attention.revision.as_str());
            self.sync_attention(&mut store, &companion.companion_id, &parsed);
            let _ = store.save();
            unchanged
        };
        self.finish(tool, &companion, Some(&context_binding), view, raw, parsed, unchanged)
    }

    /// Mutation path: the full tuple is journaled before the request leaves, and settled from
    /// the returned receipt. A lost response keeps the entry unsettled for reconciliation.
    fn journaled_send(
        &self,
        frame: &CallFrame,
        operation: &str,
        target_ref: &str,
        request_id: &str,
        route: Route,
        body: &Value,
    ) -> Result<Value, ExperienceError> {
        let write = PendingWrite {
            request_id: request_id.to_string(),
            context_id: frame.context_id.clone(),
            companion_id: frame.companion.companion_id.clone(),
            origin: frame.request.origin.clone(),
            operation: operation.to_string(),
            target_ref: target_ref.to_string(),
            payload: body.clone(),
            registered_at: now_millis(),
            settled: false,
            state: None,
        };
        if let Ok(store) = self.lock_store() {
            let _ = store.journal_register(&write);
        }
        self.events.publish(DomainEvent::WriteRegistered {
            request_id: request_id.to_string(),
            operation: operation.to_string(),
        });
        let (method, path) = route.build();
        let result = self.port.send(&frame.request, method, &path, body);
        // A lost or failed response has an unknown outcome: the journaled tuple is the
        // recovery path, so the request id stays visible to the caller.
        if let Err(error) = &result {
            if !matches!(error, ExperienceError::Application { code, .. } if code == "request_conflict") {
                let reason = match error {
                    ExperienceError::Unreachable => "the server could not be reached".to_string(),
                    ExperienceError::Unauthorized | ExperienceError::DpopChallenge { .. } => {
                        "authentication was rejected".to_string()
                    }
                    ExperienceError::Application { message, .. } => message.clone(),
                    ExperienceError::Transport(detail) => detail.clone(),
                };
                return Err(ExperienceError::Transport(format!(
                    "The request did not complete ({reason}). Keep the request id {request_id}: retrying with the same id and payload reconciles safely, or recover it with GetOperation."
                )));
            }
        }
        match &result {
            Ok(raw) => {
                if let Ok(experience) = contract::parse(raw) {
                    let state = experience
                        .result
                        .receipt
                        .as_ref()
                        .map(|receipt| receipt.state.clone())
                        .unwrap_or_else(|| experience.status.clone());
                    if let Ok(store) = self.lock_store() {
                        let _ = store.journal_settle(request_id, &state);
                    }
                    self.events.publish(DomainEvent::WriteSettled { request_id: request_id.into(), state });
                }
            }
            Err(ExperienceError::Application { code, .. }) if code == "request_conflict" => {
                if let Ok(store) = self.lock_store() {
                    let _ = store.journal_settle(request_id, "conflict");
                }
            }
            _ => {}
        }
        result
    }

    fn finish(
        &self,
        tool: &str,
        companion: &CompanionEntry,
        binding: Option<&LocalContext>,
        view: ViewMode,
        raw: Value,
        parsed: ExperienceDto,
        unchanged: bool,
    ) -> ToolOutcome {
        let (text, delivered_ids, unresolved) = {
            let mut store = self.lock_store().expect("state lock");
            let context_id = binding.map(|binding| binding.context_id.clone()).unwrap_or_default();
            let perspective = Perspective {
                participant_ref: companion.participant_ref.clone(),
                did: companion.did.clone(),
                display: companion.display_name.clone().unwrap_or_else(|| companion.name.clone()),
            };
            let mut canonical_refs = Vec::new();
            collect_refs(&parsed, &mut canonical_refs);
            let pending = store.attention_records(&companion.companion_id);
            for record in &pending {
                canonical_refs.push(record.source_ref.clone());
            }
            let mut alias_map: BTreeMap<String, String> = BTreeMap::new();
            for reference in canonical_refs {
                if !reference.is_empty() && !alias_map.contains_key(&reference) {
                    let alias = store.alias_for(&context_id, &reference);
                    alias_map.insert(reference, alias);
                }
            }
            let lookup = |reference: &str| alias_map.get(reference).cloned().unwrap_or_else(|| truncate_reference(reference));
            let pending_undelivered: Vec<_> = pending
                .iter()
                .filter(|record| record.state != AttentionState::Delivered)
                .cloned()
                .collect();
            let unresolved = store.unsettled_writes();
            let input = RenderInput {
                experience: Some(&parsed),
                mode: view,
                perspective: &perspective,
                aliases: &lookup,
                pending: &pending_undelivered,
                waiting_known: Some(store.waiting_count(&companion.companion_id)),
                unchanged,
            };
            let mut text = render(&input);
            if view == ViewMode::Orientation && !unresolved.is_empty() {
                let ids = unresolved.iter().map(|write| write.request_id.as_str()).take(3).collect::<Vec<_>>().join(", ");
                text.push_str(&format!(
                    "\n{} saved action(s) awaiting reconciliation ({ids}); use GetOperation with the request id.",
                    unresolved.len()
                ));
            }
            // Delivery for tool-response-only hosts happens now: these previews are in the
            // response. Persist the delivery before returning.
            let delivered: Vec<String> = pending_undelivered.iter().map(|record| record.id.clone()).take(5).collect();
            store.mark_attention_delivered(&companion.companion_id, &delivered);
            let _ = store.save();
            (text, delivered, unresolved)
        };
        if !delivered_ids.is_empty() {
            self.events.publish(DomainEvent::AttentionDelivered {
                companion_id: companion.companion_id.clone(),
                item_count: delivered_ids.len(),
                delivery_mode: DELIVERY_MODE.into(),
            });
        }
        let context_id = binding.map(|binding| binding.context_id.clone());
        let aliases_exposed: BTreeMap<String, String> = {
            let store = self.lock_store().expect("state lock");
            context_id
                .as_deref()
                .map(|context| store.aliases(context))
                .unwrap_or_default()
                .into_iter()
                .map(|(canonical, alias)| (alias, canonical))
                .collect()
        };
        let is_error = matches!(parsed.status.as_str(), "blocked" | "error");
        let _ = tool;
        ToolOutcome {
            is_error,
            status: parsed.status.clone(),
            text,
            structured: json!({
                "experience": raw,
                "problem": null,
                "connector": {
                    "companionId": companion.companion_id,
                    "contextId": context_id,
                    "view": view.as_str(),
                    "deliveryMode": DELIVERY_MODE,
                    "aliases": aliases_exposed,
                    "unresolvedWrites": unresolved.iter().map(|write| write.request_id.clone()).take(10).collect::<Vec<_>>(),
                }
            }),
        }
    }

    // ---------- background checks ----------

    /// One ordinary background check: fetch the digest, persist state, publish events. Runs no
    /// model, invokes no host; delivery remains a separate policy decision.
    pub fn background_check(&self, companion_id: &str) -> Result<String, String> {
        let Some(companion) = self.companion_of(companion_id) else {
            return Err("unknown companion".to_string());
        };
        let session = self.session_of(&companion)?;
        let request = RequestContext { origin: companion.origin.clone(), credential: session, participant_ref: companion.participant_ref.clone(), dpop: None };
        let checkpoint = {
            let store = self.lock_store().map_err(|_| "state lock poisoned")?;
            store.checkpoint(&companion.companion_id)
        };
        let path = match checkpoint {
            Some(value) => format!("/api/v1/experience/updates?checkpoint={}", encode(&value)),
            None => "/api/v1/experience/updates".to_string(),
        };
        let raw = self.port.get(&request, &path).map_err(|error| format!("check failed: {error}"))?;
        let parsed = contract::parse(&raw).map_err(|error| error.to_string())?;
        let summary = {
            let mut store = self.lock_store().map_err(|_| "state lock poisoned")?;
            self.sync_attention(&mut store, &companion.companion_id, &parsed);
            if let Some(checkpoint) = &parsed.continuation.activity_checkpoint {
                store.set_checkpoint(&companion.companion_id, checkpoint);
            }
            store.save()?;
            format!(
                "revision {} · waiting {} · activity {}",
                parsed.attention.revision,
                parsed.attention.waiting_count.describe(),
                parsed.attention.new_activity_count.describe()
            )
        };
        self.events.publish(DomainEvent::PollCompleted {
            companion_id: companion.companion_id.clone(),
            revision: parsed.attention.revision.clone(),
            waiting: parsed.attention.waiting_count.value.unwrap_or(0),
            activity: parsed.attention.new_activity_count.value.unwrap_or(0),
        });
        // Reconcile unsettled writes through receipt lookup; never re-execute.
        let unsettled = {
            let store = self.lock_store().map_err(|_| "state lock poisoned")?;
            store.unsettled_writes()
        };
        for write in unsettled
            .iter()
            .filter(|write| write.companion_id.is_empty() || write.companion_id == companion.companion_id)
            .take(10)
        {
            if let Ok(raw) = self.port.get(&request, &format!("/api/v1/experience/operations/{}", encode(&write.request_id))) {
                if let Ok(experience) = contract::parse(&raw) {
                    if let Some(receipt) = &experience.result.receipt {
                        if matches!(receipt.state.as_str(), "completed" | "rejected") {
                            if let Ok(store) = self.lock_store() {
                                let _ = store.journal_settle(&write.request_id, &receipt.state);
                            }
                            self.events.publish(DomainEvent::WriteSettled {
                                request_id: write.request_id.clone(),
                                state: receipt.state.clone(),
                            });
                        }
                    }
                }
            }
        }
        Ok(summary)
    }

    // ---------- attention synchronization ----------

    fn sync_attention(&self, store: &mut StateStore, companion_id: &str, parsed: &ExperienceDto) {
        let now = now_millis();
        let (fresh, coalesced) = store.upsert_attention(companion_id, &parsed.attention.items, &parsed.attention.revision, now);
        if let Some(waiting) = parsed.attention.waiting_count.value {
            store.set_waiting_count(companion_id, waiting);
        }
        let complete = !parsed.attention.more
            && parsed
                .attention
                .waiting_count
                .value
                .map(|waiting| {
                    waiting
                        == parsed
                            .attention
                            .items
                            .iter()
                            .filter(|item| {
                                matches!(item.relationship.as_deref(), Some("addressed_to_you") | Some("replies_to_you"))
                            })
                            .count() as i64
                })
                .unwrap_or(false);
        store.resync_attention(companion_id, &parsed.attention.items, parsed.attention.waiting_count.value, complete);
        store.set_revision(companion_id, &parsed.attention.revision);
        if !fresh.is_empty() {
            self.events.publish(DomainEvent::AttentionObserved {
                companion_id: companion_id.to_string(),
                new_items: fresh,
                coalesced,
            });
        }
    }

    // ---------- helpers ----------

    fn companion_of(&self, companion_id: &str) -> Option<CompanionEntry> {
        let store = self.lock_store().ok()?;
        store.companion(companion_id)
    }

    /// The enrollment's bearer session, from the per-enrollment session map. A missing
    /// session is an honest 're-enroll' state (legacy rows are dropped at load; the
    /// session map may also lag a hand-edited state file).
    fn session_of(&self, companion: &CompanionEntry) -> Result<String, String> {
        let store = self.lock_store()?;
        store.session(&companion.companion_id).ok_or_else(|| "the stored session is missing; re-enroll this enrollment".to_string())
    }

    /// Takes the store lock. Leaf-lock discipline applies for the whole guard scope:
    /// no hub method that locks the store again (the mutex is not re-entrant), no
    /// network I/O, no process spawn. Composed reads belong in batched hub methods.
    fn lock_store(&self) -> Result<std::sync::MutexGuard<'_, StateStore>, String> {
        self.store.lock().map_err(|_| "state lock poisoned".to_string())
    }

    fn context_expired(&self, tool: &str) -> ToolOutcome {
        self.problem_outcome(tool, "context_expired",
            "That context is unknown to this connector. Select your companion and Arrive again; keep any saved request ids.", None)
    }

    fn transport_problem(
        &self,
        tool: &str,
        companion: &CompanionEntry,
        binding: Option<&LocalContext>,
        error: &ExperienceError,
    ) -> ToolOutcome {
        let (code, message) = match error {
            ExperienceError::Unreachable => ("unreachable".to_string(), "The Tangent server could not be reached. Saved actions and cursors remain available.".to_string()),
            ExperienceError::Unauthorized | ExperienceError::DpopChallenge { .. } => ("needs_operator_connection".to_string(), "Authentication was rejected; the operator must renew this enrollment's session.".to_string()),
            ExperienceError::Application { code, message } => (code.clone(), message.clone()),
            ExperienceError::Transport(detail) => ("unreachable".to_string(), detail.clone()),
        };
        self.problem_outcome(tool, &code, &message, Some((companion, binding)))
    }

    fn problem_outcome(
        &self,
        tool: &str,
        code: &str,
        message: &str,
        companion: Option<(&CompanionEntry, Option<&LocalContext>)>,
    ) -> ToolOutcome {
        let mut text = String::new();
        if let Some((companion, _)) = &companion {
            let name = companion.display_name.clone().unwrap_or_else(|| companion.name.clone());
            text.push_str(&format!("You: {name}\n"));
        }
        text.push_str(&format!("Blocked [{code}]: {message}"));
        let (companion_id, context_id) = companion
            .map(|(entry, binding)| {
                (
                    Some(entry.companion_id.clone()),
                    binding.map(|binding| binding.context_id.clone()),
                )
            })
            .unwrap_or((None, None));
        ToolOutcome {
            is_error: true,
            status: "blocked".into(),
            text,
            structured: json!({
                "experience": null,
                "problem": { "code": code, "message": message, "operation": tool },
                "connector": {
                    "companionId": companion_id,
                    "contextId": context_id,
                    "view": "compact",
                    "deliveryMode": DELIVERY_MODE,
                }
            }),
        }
    }
}

/// Mutation routes: method + path construction stays in one place.
enum Route {
    TopicPosts(String),
    TopicReadPosition(String),
    Membership(String),
    Leave(String, String),
    Watches,
}

impl Route {
    fn build(self) -> (&'static str, String) {
        match self {
            Self::TopicPosts(room) => ("POST", format!("/api/v1/experience/topics/{room}/posts")),
            Self::TopicReadPosition(room) => ("POST", format!("/api/v1/experience/topics/{room}/read-position")),
            Self::Membership(tangent) => ("PUT", format!("/api/v1/experience/tangents/{tangent}/membership")),
            Self::Leave(tangent, request_id) => {
                ("DELETE", format!("/api/v1/experience/tangents/{tangent}/membership?requestId={}", encode(&request_id)))
            }
            Self::Watches => ("PUT", "/api/v1/experience/watches".to_string()),
        }
    }
}

fn invalid_ref(kind: &str) -> ExperienceError {
    ExperienceError::Application {
        code: "invalid_arguments".into(),
        message: format!("Copy a {kind} reference returned by this server."),
    }
}

/// The ok outcome of a resolved selection: one enrollment, one server to arrive at.
fn selected_outcome(companion: &CompanionEntry) -> ToolOutcome {
    let text = format!(
        "Selected {}. Continue with Arrive using this server: {}.",
        companion.display_name.clone().unwrap_or_else(|| companion.name.clone()),
        companion.origin
    );
    ToolOutcome {
        is_error: false,
        status: "ok".into(),
        text,
        structured: json!({
            "experience": null,
            "problem": null,
            "connector": {
                "companionId": companion.companion_id,
                "identityId": companion.local_id,
                "contextId": null,
                "serverUrl": companion.origin,
                "view": "compact",
                "deliveryMode": DELIVERY_MODE,
            }
        }),
    }
}

fn enroll_transport_error(error: &ExperienceError) -> String {
    match error {
        ExperienceError::Unreachable => "the server could not be reached".to_string(),
        ExperienceError::Unauthorized | ExperienceError::DpopChallenge { .. } => {
            "the enrollment endpoint rejected the request".to_string()
        }
        ExperienceError::Application { code, message } => format!("{code}: {message}"),
        ExperienceError::Transport(detail) => detail.clone(),
    }
}

/// The browser target of one operator-page anchor. Pure construction, so tests can
/// assert the URL under the no-browser guard without spawning anything. The anchor
/// carries its own sigil: `#create-identity` (a fragment) or `bind/{localId}/atproto`
/// (the connector-served bind route, a path).
pub fn registration_target(page_url: &str, anchor: &str) -> String {
    format!("{page_url}{anchor}")
}

/// The per-identity sign-in target on the operator page (R2): the connector-served
/// `/bind` page `bind/{localId}/atproto` — a path, not a fragment, since the bind flow
/// is its own route now.
pub fn bind_anchor(local_id: &str) -> String {
    format!("bind/{local_id}/atproto")
}

/// The origin (`scheme://host:port`) of a page URL, for the reachability probe. Pure
/// construction; a URL without the expected shape answers `None` (honestly unprobeable).
fn page_origin(url: &str) -> Option<String> {
    let (scheme, rest) = url.split_once("://")?;
    if scheme != "http" && scheme != "https" {
        return None;
    }
    let authority = rest.split('/').next()?;
    if authority.is_empty() {
        return None;
    }
    Some(format!("{scheme}://{authority}"))
}

/// The honest problem code of a tool outcome, for feed narration. Outcomes without a
/// problem (or unrenderable ones) report the generic blocked shape.
fn problem_code_of(outcome: &ToolOutcome) -> String {
    outcome
        .structured
        .pointer("/problem/code")
        .and_then(Value::as_str)
        .unwrap_or("blocked")
        .to_string()
}

/// Honest operator wording when the PDS refuses the app-password sign-in.
fn pds_signin_error(error: &ExperienceError) -> String {
    match error {
        ExperienceError::Unreachable => "the PDS could not be reached; check the PDS origin and the network".to_string(),
        ExperienceError::Unauthorized | ExperienceError::DpopChallenge { .. } => "the PDS rejected the handle or app password".to_string(),
        ExperienceError::Application { code, message } => format!("the PDS refused the sign-in ({code}): {message}"),
        ExperienceError::Transport(detail) => detail.clone(),
    }
}

/// Honest operator wording for a refused discovery document. The 503 family on this
/// surface all means one thing: the server has no proof audience configured.
fn discovery_error(error: &ExperienceError) -> String {
    match error {
        ExperienceError::Unreachable => "the server could not be reached".to_string(),
        ExperienceError::Unauthorized | ExperienceError::DpopChallenge { .. } => {
            "the discovery document refused the request".to_string()
        }
        ExperienceError::Application { code, message } => match code.as_str() {
            "exchange_unconfigured" | "public_origin_unconfigured" | "exchange_unavailable" => {
                "exchange_unavailable: this server has no proof audience configured (service-proof enrollment is off there)".to_string()
            }
            other => format!("the discovery document was refused ({other}): {message}"),
        },
        ExperienceError::Transport(detail) => detail.clone(),
    }
}

/// Honest operator wording when the PDS refuses the service-auth request. A rejection
/// here is almost always an expired PDS session: the message says to re-bind.
fn service_auth_error(error: &ExperienceError) -> String {
    match error {
        ExperienceError::Unreachable => "the PDS could not be reached; re-try, or re-bind the identity if the PDS moved".to_string(),
        ExperienceError::Unauthorized => {
            "atproto_session_expired: the PDS session was rejected (it may have expired). Re-bind the identity on the operator page.".to_string()
        }
        ExperienceError::DpopChallenge { .. } => {
            "atproto_session_expired: the PDS kept challenging the DPoP proof for a fresh nonce. Re-try, or re-bind the identity on the operator page.".to_string()
        }
        ExperienceError::Application { code, message } => match code.as_str() {
            // The reference PDS's granular-scope refusal: a session bound before the
            // rpc permission existed cannot mint service proofs. One re-bind (the
            // updated consent screen shows the permission) extends the grant.
            "ScopeMissingError" => format!(
                "atproto_scope_missing: the bound account's OAuth grant lacks the service-proof permission this PDS demands ({message}). Re-bind the identity on the operator page to grant it."
            ),
            _ => format!("the PDS refused the service-auth request ({code}): {message}"),
        },
        ExperienceError::Transport(detail) => detail.clone(),
    }
}

/// Honest operator wording for the `/mcp/token` exchange outcomes: 503 → no proof
/// audience configured; 401 → the proof was rejected (invalid or replayed); 403 → the
/// participant is suspended there.
fn exchange_error(error: &ExperienceError) -> String {
    match error {
        ExperienceError::Unreachable => "the server could not be reached".to_string(),
        ExperienceError::Unauthorized | ExperienceError::DpopChallenge { .. } => {
            "invalid_service_proof: the server rejected the proof (invalid or already used); enroll again".to_string()
        }
        ExperienceError::Application { code, message } => match code.as_str() {
            "exchange_unavailable" | "exchange_unconfigured" | "public_origin_unconfigured" => {
                format!("exchange_unavailable: this server has no proof audience configured ({message})")
            }
            "participant_suspended" => format!("participant_suspended: the server reports this identity is suspended there ({message})"),
            "invalid_service_proof" | "service_proof_required" => {
                format!("invalid_service_proof: the server rejected the proof ({message}); enroll again")
            }
            other => format!("the server blocked the exchange ({other}): {message}"),
        },
        ExperienceError::Transport(detail) => detail.clone(),
    }
}

/// Honest operator-facing wording for the contract's blocked enrollment codes.
fn enroll_blocked_error(code: &str, message: &str) -> String {
    match code {
        "already_enrolled" => format!(
            "already_enrolled: the server reports this client identity is already enrolled there and returned no new session ({message}). Use the existing enrollment, or forget it first to re-enroll."
        ),
        "unbound_enrollment_disabled" => format!(
            "unbound_enrollment_disabled: that server has unbound enrollment switched off ({message})."
        ),
        "invalid_handle" => format!("invalid_handle: the server rejected this identity's handle ({message})."),
        "suspended_participant" => format!("suspended_participant: the server declined to re-enroll this identity ({message})."),
        "request_conflict" => format!("request_conflict: this identity already maps to a different participant there ({message})."),
        other => format!("the server blocked enrollment ({other}): {message}"),
    }
}

/// Collects the canonical references a response exposes, for alias assignment.
fn collect_refs(experience: &ExperienceDto, refs_out: &mut Vec<String>) {
    if let Some(place) = &experience.place.server_ref {
        refs_out.push(place.clone());
    }
    if let Some(reference) = &experience.place.tangent_ref {
        refs_out.push(reference.clone());
    }
    if let Some(reference) = &experience.place.topic_ref {
        refs_out.push(reference.clone());
    }
    for item in &experience.attention.items {
        refs_out.push(item.source_ref.clone());
        refs_out.push(item.scope_ref.clone());
    }
    for action in &experience.actions {
        refs_out.push(action.target_ref.clone());
    }
    for key in ["postRef", "throughPostRef", "resultRef"] {
        if let Some(reference) = experience.result.data.get(key).and_then(Value::as_str) {
            refs_out.push(reference.to_string());
        }
    }
    if let Some(receipt) = &experience.result.receipt {
        if let Some(reference) = &receipt.result_ref {
            refs_out.push(reference.clone());
        }
    }
    if let Some(posts) = experience.result.data.get("posts").and_then(Value::as_array) {
        for post in posts {
            if let Some(reference) = post.get("ref").and_then(Value::as_str) {
                refs_out.push(reference.to_string());
            }
        }
    }
}

fn encode(value: &str) -> String {
    let mut out = String::new();
    for byte in value.bytes() {
        match byte {
            b'A'..=b'Z' | b'a'..=b'z' | b'0'..=b'9' | b'-' | b'_' | b'.' | b'~' => out.push(byte as char),
            _ => out.push_str(&format!("%{byte:02X}")),
        }
    }
    out
}

fn truncate_reference(reference: &str) -> String {
    let tail = reference.rsplit("::").next().unwrap_or(reference);
    tail.chars().take(12).collect()
}
