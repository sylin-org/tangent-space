# Docker development and identity resolution repair

Task: run the existing AddKoan monolith in Docker with visible bootstrap/logs; repair public identity discovery that incorrectly used the fixture PLC and escaped as HTTP 500.

Application intent: the operator runs Compose and can see the application, health and logs in Docker Desktop; public sign-in discovers the real provider while explicit fixture identities retain their original DID/PDS URLs.

Public expression: unchanged AddKoan host, Dockerfile, compose.yaml and a preparation script writing ignored instance configuration. One app container uses the existing disposable protocol container. Durable SQLite is copied only after stopping and backing up the Windows process. DPAPI sessions/keys stay in that backup and are not copied into Linux; genuine OAuth reauthorization preserves DID continuity.

Guarantee/correction: public PLC remains the default. A development-only alternate PLC applies exclusively to explicitly mapped fixture identities, and an optional development connection host routes only exact allowed origins without rewriting URL/Host/DPoP. Production rejects these options. Failed challenges return a sanitized retry page instead of unhandled exceptions. PDS Space capability remains separate from sign-in.

Docs/code read: root product/current state and operation guide establish local governance and custody; Koan AGENTS/CLAUDE/explore establish adapter ownership and standing authorization; connector EXPLORATION/README/TECHNICAL document the existing protocol/transport boundaries. AtprotoOptions, AtprotoProtocol, AtprotoSessions, AtprotoHttp and authentication handler are the decision owners. Program already uses the standard AddKoan grammar. Runtime logs prove composition and bootstrap exist but were redirected by the background launcher.

Reusing: typed options, DevelopmentOnly gate, exact allowed origins, guarded socket connection, native OAuth, session validation, standard middleware and existing persistent domain records. No new domain/service/repository or application process.

Creating: DevelopmentPlcDirectory and DevelopmentConnectHost on connector AtprotoOptions; selector at the existing identity resolver and host choice at the guarded Connect callback; sanitized challenge failure at the existing handler. Dockerfile/Compose own image/runtime topology; scripts own stopped-state migration and configuration. Focused tests cover isolation and production rejection.

Coalescence: keep the connector as the one owner of protocol mechanics. Fixture network topology is explicit configuration, never fallback from public resolution. Keep product policy out of generic auth. No override of localhost DNS, no proxy bypass, no changing source DIDs, no migration of protected Windows keys.

Ergonomics/constraints: one Compose app, ordinary logs/health, stable localhost browser address, persistent host-mounted data for the local PoC; no extra layer in domain code. Existing isolated contribution will be regenerated and verified without touching the original sibling checkout. Current user directions authorize Docker migration and the reported login repair; no new approval is required.

Risks to verify: Linux native SQLite assets/build targets; Docker Desktop host routing for fixture loopback ports; public provider acceptance of the declared development client; explicit reauthorization after the platform change.
