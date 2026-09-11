//! The connector hub: the single application orchestrator for every model-requested
//! operation and background check. Intakes (the stdio MCP edge and the command line) are
//! spokes in the same shape: each translates its input model into the closed [`Operation`]
//! vocabulary and calls [`ConnectorHub::execute`]; the channel is recorded for attribution
//! and never changes a domain outcome. Adapters (HTTP client, poller, store) are the
//! remaining spokes; none of them talks to another directly.

use std::collections::BTreeMap;
use std::sync::{Arc, Mutex};

use serde_json::{json, Value};

use crate::adapters::store::StateStore;
use crate::application::bus::EventBus;
use crate::application::contract::{self, ExperienceDto};
use crate::application::operations::{decode, Operation, ViewMode};
use crate::application::ports::{ExperienceError, ExperiencePort, RequestContext};
use crate::domain::events::DomainEvent;
use crate::domain::identity::{valid_handle, CallerId, CompanionEntry, LocalContext};
use crate::domain::intake::IntakeChannel;
use crate::domain::refs;
use crate::domain::writes::PendingWrite;
use crate::domain::{attention::AttentionState, now_millis};
use crate::presentation::perspective::Perspective;
use crate::presentation::{render, RenderInput};

pub const DELIVERY_MODE: &str = "tool_response_only";

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

pub struct ConnectorHub {
    port: Arc<dyn ExperiencePort>,
    store: Mutex<StateStore>,
    events: Arc<EventBus>,
    caller: CallerId,
}

impl ConnectorHub {
    pub fn new(port: Arc<dyn ExperiencePort>, store: StateStore, events: Arc<EventBus>, caller: CallerId) -> Self {
        Self { port, store: Mutex::new(store), events, caller }
    }

    pub fn events(&self) -> Arc<EventBus> {
        self.events.clone()
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
        };
        let raw = self.port.get(&context, "/api/v1/experience").map_err(|error| match error {
            ExperienceError::Unreachable => "the server could not be reached".to_string(),
            ExperienceError::Unauthorized => {
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

    // ---------- identities and the client allowlist (operator surface) ----------

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

    pub fn client_rules(&self) -> Vec<crate::domain::identity::ClientRule> {
        let store = self.lock_store().expect("state lock");
        store.client_rules().to_vec()
    }

    pub fn set_client_rules(&self, rules: Vec<crate::domain::identity::ClientRule>) -> Result<(), String> {
        self.attributed("operator.set_allowlist", || {
            for rule in &rules {
                if rule.client_name.trim().is_empty() || rule.client_name.chars().count() > 100 {
                    return Err("a client name is 1-100 characters".to_string());
                }
                if let Some(local_id) = &rule.local_id {
                    let store = self.lock_store()?;
                    if store.identity(local_id).is_none() {
                        return Err(format!("allowlist names unknown identity '{local_id}'"));
                    }
                }
            }
            let mut store = self.lock_store()?;
            store.set_client_rules(rules);
            store.save()
        })
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
        let outcome = self.dispatch(operation);
        self.events.publish(DomainEvent::ToolCompleted {
            channel,
            tool: tool.to_string(),
            status: outcome.status.clone(),
            text_bytes: outcome.text.len(),
        });
        outcome
    }

    fn dispatch(&self, operation: Operation) -> ToolOutcome {
        match operation {
            Operation::SelectCompanion { moniker } => self.select_companion(moniker.as_deref()),
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
    /// moniker the only permitted source is the connecting client's allowlist rule: an
    /// unlisted MCP client (and the CLI, always) resolves nothing — never a guess, never
    /// machine-wide, even when exactly one identity exists.
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
                let Some(client) = self.caller.mcp_client_name() else {
                    return instruction(
                        "This intake never auto-selects an identity. Pass a moniker: an identity handle, or an enrollment's name, handle or participant reference."
                        .to_string(),
                    );
                };
                let Some(rule) = store.client_rule(client) else {
                    return instruction(format!(
                        "MCP client '{client}' is not in the connector's identity allowlist. Ask the operator to assign it an identity in the operator page (tangent-connector operator), or pass a moniker."
                    ));
                };
                let Some(local_id) = rule.local_id.as_deref() else {
                    return instruction(format!(
                        "MCP client '{client}' is listed without an identity. Ask the operator to choose one in the operator page, or pass a moniker."
                    ));
                };
                let Some(identity) = store.identity(local_id) else {
                    return instruction(
                        "The allowlist names an identity that no longer exists. Ask the operator to fix the allowlist, or pass a moniker.".to_string(),
                    );
                };
                self.select_enrollment_of(&store, &identity)
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
        let request = RequestContext { origin: companion.origin.clone(), credential: session, participant_ref: companion.participant_ref.clone() };
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
        let request = RequestContext { origin: companion.origin.clone(), credential: session, participant_ref: companion.participant_ref.clone() };
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
            request: RequestContext { origin: companion.origin.clone(), credential: session, participant_ref: companion.participant_ref.clone() },
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
                    ExperienceError::Unauthorized => "authentication was rejected".to_string(),
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
        let request = RequestContext { origin: companion.origin.clone(), credential: session, participant_ref: companion.participant_ref.clone() };
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
            ExperienceError::Unauthorized => ("needs_operator_connection".to_string(), "Authentication was rejected; the operator must renew this enrollment's session.".to_string()),
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
        ExperienceError::Unauthorized => "the enrollment endpoint rejected the request".to_string(),
        ExperienceError::Application { code, message } => format!("{code}: {message}"),
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
