# ADR 0012 — Realigned connector architecture

Date: 15 September 2026. Status: accepted. Leo accepted every recommendation of the [connector assessment](../ASSESSMENT_2026-09-15-CONNECTOR.md); [EPIC-007](../epics/EPIC-007.md) delivers them together with the server realignment of [ADR 0011](0011-realigned-server-architecture.md).

## Context

The connector grew by addition, as the server did. It has three enrollment tiers and two account-binding paths, of which one each serves real users; one hub that holds every use case behind ten mutexes; a state file rewritten whole by processes that do not coordinate; a copy of the server contract in its tests that has drifted from the server; and sessions that expire every seven days with nothing to renew them.

## Decision

The connector follows ADR 0011's rules and concepts: a DDD monolith with clear separation of concerns, the fewest meaningful moving parts, the product's words, and nothing deprecated left behind.

1. **One enrollment path.** A companion enrolls only through the account-bound proof exchange, after its atproto account is bound through OAuth. The unbound tier, the app-password binding and session import are removed, together with the server's person-scoped `/api/participation/credentials` endpoints that only import used.
2. **Sessions renew themselves.** The connector repeats the exchange before a session expires or after the server refuses it. A person is asked only when the account session itself cannot be refreshed.
3. **One transactional state store.** Every change is a short read-modify-write under an OS file lock, and every process uses it; reads take no lock. The long-lived lockfile, `--force` and the one-process rule are removed, and receipts live in the state instead of an append-only journal.
4. **Use cases instead of one hub.** The application is a set of use cases over one typed problem. An interface exists only where a second real implementation does, so the Tangent client is a concrete type.
5. **One way in for a model.** `Connect` resolves the companion, enrolls, arrives, and asks for a person when one is needed. `SelectCompanion`, `Arrive` and `OpenRegistration` are removed.
6. **The connector uses the product's words**, as listed in the [connector glossary](../ARCHITECTURE.md#connector-glossary), and its crate moves from `src/server/mcp` to `src/connector`. The enrollment discovery document, exchange route and exchange method lose their `mcp` names in the server and connector together.
7. **The real server is the contract.** The connector's journeys run against the real server; its fake keeps only failures the server cannot produce on demand.
8. **The companion manager is a hardened loopback page.** Writes are JSON only, requests must name the loopback host and come from the page's own origin, and other processes detect the page through its discovery document.
9. **Code without a caller is removed**, and each attention item is rendered once.

Leo's standing rules apply unchanged: cleanup of deprecated content is mandatory, and code reads greenfield. `scripts/check-greenfield.ps1` covers the connector.

## Consequences

- [ARCHITECTURE](../ARCHITECTURE.md#connector) describes the connector's shape, modules, glossary and shared components.
- This decision partly supersedes [ADR 0009](0009-deployment-postures-and-admission.md): agents enroll only with an atproto account in every posture, so its identity-strength knob and the Local posture's minted agent identities have no connector path.
- A `Connect` that waits for the operator still finishes by itself once the operator signs in; the Connect use case owns it.
- Renaming the exchange method changes the OAuth consent scope, so every companion binds its account again once. Under the standing wipe rule that costs nothing; after a release it would.
- The server and connector continue to ship as a matched pair.
