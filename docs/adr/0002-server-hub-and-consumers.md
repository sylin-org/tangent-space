# ADR 0002 — One server hub, registered consumers

Status: Accepted, 10 September 2026. Partly superseded by [ADR 0011](0011-realigned-server-architecture.md): a command pipeline replaces the `TangentServer` service holder; consumer registrations remain.

## Decision

`TangentServer` is the singleton application entry point for the Web and inbound MCP consumers. It exposes the existing domain services for server settings, Tangents, participants, Topics, Posts and activity. It also exposes the existing source adapter and readiness service. These references are immutable and resolve to the same singleton instances for both consumers.

There is one execution path for each operation: authenticate the request, check its transport grants, enter the relevant domain operation, check current permissions, apply the transition, confirm any required native source write, commit, and signal activity. Existing domain services own these steps and their durable operation receipts. The hub does not duplicate their rules, introduce a generic workflow engine, or put unrelated work into another global queue. Existing transaction and policy gates remain unchanged.

Small singleton `IRegistration` implementations select each consumer's authentication scheme. `WebRegistration` selects Koan's browser session when no Authorization header exists. `McpConsumerRegistration` selects the participant credential handler when that header exists, including for bearer-based WebMCP calls. An invalid bearer must never fall back to an otherwise valid browser cookie. MCP's endpoint continues to require a participant credential explicitly.

`ConsumerRegistrations` stores the registered adapters once and selects exactly one per request. It only chooses the scheme: ASP.NET/Koan's existing handlers perform credential verification. Registrations neither appoint a domain role nor bypass the permission checks in the shared services.

The Koan module remains the composition root, through `.AddKoan()`. The server, registrations, domain services, source clients and activity services are long-lived singletons. HTTP controllers and authentication handlers retain their framework-managed lifetimes because they carry request state. Actor identity, cancellation, entities, transactions and response state belong to the individual operation and must not be retained on singleton fields. Credentials remain in the existing protected stores.

Web renders the shared results and consumes activity through SSE; MCP formats them into its BBS context segments. Presentation remains in the spokes. Neither gets a separate policy implementation.

## Scope

This is a composition refactor of the working prototype, preserving API contracts, persisted data and native Spaces behavior. Service-DID adoption and public Topic publication remain follow-on work from ADR 0001. The incomplete exploratory additions for those features were removed before integrating this change.

Validation is a Docker build and a focused check of browser and MCP access to the same running server, including rejection of invalid bearer credentials. No new test framework or deployment is introduced.
