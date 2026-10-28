# First product slice — arrive at the site

Status: implementation in progress. This is the first vertical slice of S03, alongside S02's reusable connector. The remaining room and conversation work stays in EPIC-001.

**Task:** Start Tangent as one DDD monolith and persist an arriving participant and explicitly established site owner.

**Application intent:** I sign in with my AT account and arrive at a named site that recognizes me and its owner on my next visit.

**Public expression:** One `src/TangentSpace` web application, one `AddKoan()` bootstrap, domain-owned `Participant` and `TangentSite` Entities, and a welcome controller. The operator supplies `Tangent:Site:Name`, `Tangent:Site:OwnerDid`, the reusable AT connector's trusted client metadata/callback configuration, and a SQLite connection. The temporary source dependency path points at the isolated, pinned Koan contribution; a preparation script/patch must make this reproducible before delivery. No independently deployed domain service is introduced.

**Guarantee/correction:** Only a verified AT DID can become a Participant. Only the configured DID can establish initial ownership. Repeated sign-in preserves identity and existing ownership; changing configuration cannot silently transfer a persisted site. Missing/invalid configuration fails with a specific setting to correct. The welcome reports observed identity and ownership, with only currently implemented actions.

**Complete intent surface:** Configure the site and connector, start one process, open its welcome, enter a handle/DID, consent at the account provider and return. SQLite and the protected key/session state must survive application restart. The local demonstration uses explicitly configured test identities, local PLC and a gated DNS fixture map.

**Public concepts:** Participant owns DID continuity; Site owns local ownership; Arrival coordinates their durable sign-in operation. HTTP DTOs describe the welcome. No repositories, event bus, command bus, separate Domain/Application/Infrastructure assemblies, or interface-per-class are added.

**Docs read:** The root project guidance and PRODUCT/DECISIONS establish conversation-first purpose and the DDD monolith. EPIC-001 establishes verified ownership and persistent identity. Koan's AGENTS/CLAUDE, architecture principles, capability map and build skill establish package intent, domain operations, controller routes and a small first journey. Web Auth's technical contract establishes the existing sign-in lifecycle; SQLite's contract establishes the selected store and transaction boundary.

**Code read:** Koan's GoldenJourney `ReviewRequest` demonstrates domain-owned behavior on `Entity<T>`. `EntityContext` supplies explicit transaction commit/rollback. `IKoanAuthFlowHandler`, `AuthSignInContext` and `IdentityAuthFlowHandler` supply durable sign-in and cookie hooks. `KoanWebOptions`/`KoanWebStartupFilter` already serve static assets and controllers. The AT connector's claim contract supplies a verified DID separately from Koan's canonical person ID.

**Reusing:** Entity statics, SQLite, explicit transactions, standard DI/options, Koan auth lifecycle, durable Koan identity/session reconciliation, static-file hosting and controller mapping. Constants/options/contracts were searched in Koan before selecting these paths. The public package IDs are `Sylin.Koan.Web`, `Sylin.Koan.Data.Connector.Sqlite`, `Sylin.Koan.Identity`; the new connector's proposed ID is `Sylin.Koan.Web.Auth.Connector.Atproto` and is not yet published.

**Creating new:**

| Code | Location | Why |
| --- | --- | --- |
| Single host/bootstrap | `src/TangentSpace/TangentSpace.csproj`, `Program.cs` | One deployment unit, source references during contribution development. |
| Participant | `Participants/Participant.cs` | Verified DID, mutable handle and arrival continuity. |
| Site and typed options | `Site/TangentSite.cs`, `Site/SiteOptions.cs` | Ownership and explicit bootstrap configuration are site decisions. |
| Arrival operation | `Site/Arrival.cs` | Coordinates the two durable records with one host-owned mutation lock and transaction. |
| Sign-in integration | `Participants/ParticipantSignIn.cs` | Translates verified AT claims into the domain operation; rejects an invalid identity before cookie issuance. |
| Welcome/controller | `Web/WelcomeController.cs`, `Web/SiteWelcome.cs` | Attribute-routed, bounded domain projection; no generic mutable Entity endpoint. |
| Composition/constants | `Infrastructure/TangentModule.cs`, `Infrastructure/TangentConstants.cs` | Registers real options/operations and validates ownership at startup. |
| Welcome UI | `wwwroot/` | Plain static assets in the same host, no separate frontend runtime. |
| Domain/runtime checks | `tests/TangentSpace.Tests/`, `scripts/` | Verify owner invariants, genuine authentication, persistence and corrective failures. |

**Coalescence:** Keep identity/protocol mechanics in Koan's connector and lifecycle. Keep site ownership in Tangent. `Arrival` is a domain operation across two records, not a generic repository/service layer. Room and conversation folders are added only when their behavior is implemented. No discarded prototype runtime is carried into the application.

**Ergonomics:** The source tree follows product nouns. The operator runs one host. A reader can follow callback → verified claim → Arrival → Participant/Site → welcome without navigating separate assemblies or generic dispatch machinery.

**Constraints satisfied:** Controller-only routes; first-class Entity statics; typed configuration and constants; no empty domain modules; no large unbounded reads; existing auth cookie lifecycle preserved; single-host transaction/lock limits explicit; reusable connector owns AT networking and cryptography.

**Risks:** The source build needs the pending Koan contribution. An application restart is not a PDS/authority recovery test. SQLite transactions must be verified through actual behavior. Koan's identity reconciliation may rewrite its canonical subject, so Tangent uses the connector's separately verified DID claim. The welcome slice must not advertise room operations before they exist.
