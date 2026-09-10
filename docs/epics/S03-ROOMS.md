# S03 — Room governance

Status: implemented and verified through the real host. The room aggregate, coordinator, HTTP boundary, and welcome list are integrated; the real Space transport is owned by the AT integration.

**Task:** Persist room ownership, scoped room administration, admission, and auditable policy decisions in the single Tangent host.

**Application intent:** The persisted site owner creates Lounge and Workshop, appoints a room manager, and that manager admits or restricts ordinary participants and changes the topic. Removal overrides signed-in admission. Every later operation observes current policy.

**Public expression:** Domain folders in `src/TangentSpace`. A singleton `RoomGovernance` exposes `Create`, `SetMembership`, `SetTopic`, `SetAdmission`, `SetSuspension`, `BeginProvisioning`, and `MapSpace`; attribute-routed controllers and actual Spaces provisioning remain at the application boundary. `WithCurrentPolicy` executes bounded local acceptance work under the same host-owned gate and Entity transaction as policy changes. Applications must never perform PDS/network calls inside that callback.

**Guarantee/correction:** Room creation requires the persisted site owner, supplied through a verified acting DID. A manager may administer ordinary memberships and topics, but cannot appoint managers, change admission, transfer ownership, or demote another manager or owner. Read-only participants cannot speak. Explicit removal denies access even in signed-in rooms. A pending room never pretends that a real Space exists. Mapping requires the stable local key and expected policy revision; the caller first verifies the returned Space URI against configured authority/type/key. Accepted and denied administrative operations commit actor, target, room, outcome, and selected policy revision to an audit Entity.

**Complete intent surface:** `Room` owns rules and revision changes. `RoomMembership` represents both ordinary membership and a scoped manager grant. `RoomAudit` retains decisions. `RoomPolicy` is an immutable per-operation snapshot with current content/manage permissions, Space state, and room/site revisions. `RoomGovernance` reloads persisted site/room/grant/participant state on every operation; cookie roles are not input. Thin HTTP endpoints call these operations. There is no site ownership transfer, local message substitute, or distributed locking claim.

**Public concepts:** Room, admission (`SignedIn`/`InvitationOnly`), explicit role (`Manager`/`Member`/`Reader`/`Removed`), Space state (`Pending`/`Ready`), audit, and a current policy snapshot. The owner remains implicit in persisted site/room ownership; clients cannot submit an `Owner` role. Expected policy revisions prevent stale provisioning completion.

**Docs read:** Root AGENTS, EPIC-001 (especially S03–S05), and S03-ARRIVAL establish verified site ownership, scoped delegation, durable first-acceptance decisions, one DDD monolith, and single-host limits. The existing Koan contributor/build guidance and Entity/transaction contracts determine the persistence shape.

**Code read:** `TangentSite`, `Arrival`, `Participant`, `TangentModule`, `TangentConstants`, and the existing arrival tests establish current ownership, verified DID validation, explicit commit, singleton operations, and test conventions. Koan `Entity<T>` statics and `EntityContext.Transaction` provide the existing persistence/transaction path; no repository abstraction is needed.

**Reusing:** Persisted `TangentSite.IsOwner`, verified DID syntax validation already used by Participant, Koan Entity statics and explicit commit, standard singleton DI, `TimeProvider`, and a `SemaphoreSlim` owned by that singleton. State updates and future acceptance use that exact same operation instance.

**Creating new:** `src/TangentSpace/Rooms/**` contains the aggregate, membership/audit Entities, bounded policy/result values, enums, coordinator, and HTTP boundary. `tests/TangentSpace.Tests/RoomRulesTests.cs` exercises authority, demotion/removal, suspension, isolation, revisions, pending/ready rules, and exact origin handling. The welcome DTO includes a bounded current room list. `scripts/prove-rooms.ps1` exercises the real host and writes `docs/evidence/rooms.json`. The root integration owns registration and real source/provisioning.

**Coalescence:** Keep site ownership with `TangentSite`; put room invariants with `Room`; use one `RoomGovernance` only to coordinate the related Entities, transaction, and shared concurrency gate. Keep AT URI authority verification/networking in the real Spaces integration. No interface-per-class, repository, command bus, generic services, or extra project is added.

**Ergonomics:** Consumers call an explicit room operation with the verified acting DID. Denials are typed results with useful reasons, not cookie role checks. Source acceptance receives the selected current policy revision even when content is denied, so its decision remains attributable and replayable.

**Constraints satisfied:** Entity-first persistence; current persisted policy on every operation; audited denials commit before returning; unknown/invalid targets fail closed; pending mapping is explicit; manager boundaries remain room-specific; one host-owned gate is shared with acceptance; no inline routes or local protocol replacement.

**Risks:** The in-process gate supports one Tangent writer host, not multiple replicas. The callback passed to `WithCurrentPolicy` must stay local and bounded. All policy writers must use the same registered coordinator; bypassing it with raw Entity saves bypasses application invariants. The real-host proof confirms SQLite audit durability; it does not establish multi-process serialization. A durable mapping is not itself proof of external Space creation; the real transport verifies and reconciles the authority/type/key URI before `MapSpace`.

## Authorized extension

After the first 13 domain checks passed, the coordinator assigned the room HTTP boundary and welcome list here as
well. Attribute-routed room endpoints call these same operations; cookie mutations require an exact same-origin
`Origin` and JSON content. Actor identity comes only from the authenticated connector DID claim. `SpacesService`
is the separately implemented real provisioning transport. Lists are bounded to 100 rooms per page and refresh
persisted policy for each request. The root agent owns singleton registration and later bearer authentication.

Site suspension is also included: a persisted `Participant.IsSuspended` flag, owner-only audited `SetSuspension`,
and current policy checks under the same gate. Suspension advances the site policy revision while retaining room
membership, so restoration restores the previous room role. It does not create an unverified Participant or suspend
the owner. The room proof records HTTP policy changes and the matching durable SQLite audit entries.

## Implemented boundary and evidence

`GET /api/rooms` and `GET /api/rooms/{key}` expose current metadata and capabilities; `/api/site` includes the first
100-room page. Room reads accept the participation `read` grant, and authenticated welcome accepts `welcome`.
All responses use `Cache-Control: no-store`. Cookie administration requires JSON, an exact same-origin `Origin`,
and no `Authorization` header; credentials cannot inherit a cookie's administrative authority. Mutation DTOs use
Koan MVC's actual Newtonsoft serializer, reject unknown fields, and require their declared fields explicitly:
an omitted role, admission, or suspension value must never turn into an administrative default.

Creation is `POST /api/rooms`; `POST /api/rooms/{key}/provision` performs the separately implemented real Space
provisioning. Membership, topic, and admission use `PUT /api/rooms/{key}/members/{did}`, `/topic`, and `/admission`.
Site suspension uses `PUT /api/site/participants/{did}/suspension`. The current persisted owner alone creates rooms,
changes admission, provisions, or suspends. A room manager handles ordinary membership and topic changes.

Observed validation on 2026-09-09:

- `dotnet test tests/TangentSpace.Tests/TangentSpace.Tests.csproj --no-restore --filter FullyQualifiedName~RoomRulesTests -v minimal`: 29 passed, including actual Newtonsoft valid/unknown/omitted/null request-body cases.
- `dotnet build src/TangentSpace/TangentSpace.csproj --no-restore -v minimal`: passed with zero warnings/errors at completion of this slice.
- `pwsh -NoProfile -File scripts/prove-rooms.ps1 -Port 5223 -Instance rooms-proof`: 51 checks passed, including real OAuth for all fixture roles, two distinct real Space mappings, authority boundaries, immediate policy changes with existing cookies, removal, suspension/restoration, and 29 matching durable audit receipts (20 accepted, 9 denied). The redacted receipt is `docs/evidence/rooms.json`.

The separate disposable JSON-lines client in `probes/scripts/direct-spaces.mjs` uses real legacy fixture sessions,
the provider's `getServiceAuth`, and official `JoseKey`/`createDpopProof`; tokens and keys stay in the process. It is
staged beside the pinned source packages at `/atproto/tangent-client/direct-spaces.mjs`, because the network's
`/atproto/tangent-probe` directory is a different, read-only fixture mount. Its `check` command targets only the
registered `http://host.docker.internal:{port}` callback, with optional audience/method overrides or a tampered
signature for negative tests. It does not accept an arbitrary destination. The redacted eight-check receipt is
`docs/evidence/spaces-admission.json`.

After the real PLC key-format fix, direct observations were: current member allowed (200), outsider and unknown
room denied (200 with `authorized: false`), and wrong issuer, audience, method, or signature rejected (401). A real
member Space credential was issued with a 7,200-second lifetime. These are bounded callback/client observations;
the independent conversation proof owns source acceptance and the old-credential window after removal. The room
proof itself does not fetch source records or issue credentials, and its recorded limits remain unchanged.

## Arrival and policy concurrency correction

A returning sign-in writes the whole Participant record. Its former private arrival semaphore could overlap the
room coordinator's suspension transaction and overwrite a freshly changed suspension flag from a stale read.
The correction is one DI-owned `PolicyGate`, injected into both operations. Arrival, policy administration, and
source acceptance reload with `EntityContext.NoCache` and commit while holding that same non-reentrant local gate.
The gate is owned and disposed by the host; neither operation creates an independent lock. This preserves the
single-writer-host boundary and introduces no repository or distributed coordination layer.

The combined `ArrivalRulesTests|RoomRulesTests` filter passes 38 cases after this correction. The dedicated
`scripts/prove-arrival-suspension.mjs` uses genuine disposable outsider OAuth flows overlapping audited owner
suspension writes, then checks a new sign-in and restores the outsider. Its receipt explicitly distinguishes
observed concurrent HTTP behavior from a forced database interleaving. The real-host run passed all six checks:
four concurrent OAuth arrivals completed alongside twelve suspension commits; a fifth subsequent real sign-in
retained suspension and could not read history; restoration retained the outsider's prior removed-room decision.
The receipt is `docs/evidence/arrival-suspension.json` and includes all thirteen audit receipt IDs.

The real headless browser proof (`scripts/prove-ui.mjs`) passes 22 checks against the restored host and records
`docs/evidence/ui.json` plus six desktop/mobile screenshots in `docs/evidence/ui/`. It uses the earlier real OAuth
cookie in a fresh nonpersistent browser context, performs no API interception, and writes only to a newly created
UI room. It also renders the existing human/GLM conversation read-only. Real provisioning, topic changes, compose,
source identity, reply linkage, anonymous denial, 1440px/390px overflow, and browser errors are checked.
