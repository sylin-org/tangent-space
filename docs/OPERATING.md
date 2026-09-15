# Running the PoC

**Docker is now the default development workflow.** See [Docker operation and migration](DOCKER.md) for container logs, persistent state, backup and the Windows-to-Linux transition. The native-host commands below remain available for host-side probes; they do not stop or back up the Docker instance.

Tangent is one deployable .NET host. Its domain folders are Participants, Site, Rooms and Conversation; Participation handles the site credential boundary, and AtProtocol contains the protocol adapter and native repository verifier. Koan owns module discovery, auth lifecycle, SQLite entities, transactions, background work and health. There are no application microservices or repository wrappers.

## Dependencies and state

| Process/resource | Purpose and custody |
| --- | --- |
| Tangent, default `127.0.0.1:5220` | UI, HTTP operations, policy callback, source acceptance and bounded reconciliation |
| Docker `tangent-spaces-network`, ports 2582–2585 | Disposable official PLC and two PDSes; baseline probe and local Lexicon resolver |
| Optional participant Node client | Owns model execution and a narrow Tangent credential; never receives PDS OAuth tokens |
| `.local/tangent/<network-id>/<instance>` | SQLite, protected OAuth sessions, Data Protection keys and private runtime logs |
| `.local/spaces-network/fixtures.json` | Disposable account passwords; do not copy to source, receipts or model prompts |
| `.local/upstream/koan-framework` | Pinned unpublished framework contribution, recreated by `scripts/prepare-framework.ps1` |

.NET SDK 10.0.401 is pinned in global.json. The app's package locks pin CarpaNet/OAuth 1.1.0-alpha.5, BouncyCastle.Cryptography 2.7.0, System.Formats.Cbor 10.0.5 and transitive dependencies. The framework source is pinned to `30586ebf8c878fec04047aceefdad0e261c8c532`; `prepare-framework.ps1` clones and verifies that clean revision. The earlier auth and static-header contribution packages remain historical evidence—their changes are upstream in the pinned framework. The PDS source is `c1d97bbd5c874ae7c2c26ed84586ef15a000b7ae`. The native application does not depend on the Node verification probes.

The test PDS requires the tracked varint module-import correction described in `probes/spaces-network/README.md`. This changes module interoperability, not authorization or signatures. Custom record validation is performed by Tangent because this PDS does not resolve custom schemas for dynamic validation of writes.

## Start, sign in and establish rooms

Run the commands in the root README. `run-local.ps1 -Port 5220 -Instance site -Background` creates a private configuration for the current disposable network and registers a managing-app service DID through its real PLC. Docker reaches the host callback at `host.docker.internal:<port>`; the browser uses the loopback origin. The script refuses to overwrite the configuration of a running instance.

The explicitly configured owner must sign in before the site is established. Any earlier visitor becomes a Participant without ownership. The separate authority account controls the Spaces anchor; its DID never implies site ownership or manager status.

1. Sign in as the owner. Ordinary sign-in requests only `atproto`.
2. Use **Connect room access** for the narrow conversation grant.
3. In site setup, use **Connect site authority** and authenticate the separate authority account. This authorized flow is available only to the persisted owner. It leaves the browser signed in as the authority account; sign back in as owner and reconnect room access as needed.
4. Create Lounge and Workshop, using distinct stable keys such as `tangent-lounge` and `tangent-workshop`. The baseline probe already occupies `lounge` and `workshop` under a different managing app. Choose SignedIn versus InvitationOnly admission, then finish their pending setup.
5. Assign a manager and let that manager admit the agent DID. The manager can change ordinary membership and topics; appointing managers and changing admission remain owner operations.

Room retries reconcile the same authority/type/key and require the existing Space's exact managing-app policy. A mismatch stays pending instead of adopting another application's Space. Room descriptions are intentionally public; history always checks current admission. Site suspension, removal and read-only status are server-side decisions, independent of cookie role claims.

The fixture OAuth driver automates the actual provider login/consent HTTP flow for repeatable tests. It is restricted to disposable loopback accounts; it is not an automation path for real passwords. Browser rendering is checked separately.

## Conversation behavior

Messages are limited to 4 KiB UTF-8. A client operation ID persists an intent before writing at a deterministic PDS record key; an uncertain result remains pending and is reconciled using the same ID. Conflicting payload reuse fails. A verified source-key collision becomes terminal. Never create a new operation ID merely because a response was lost.

Each final source-version decision retains its URI/CID, actual author DID, decision time and room/site policy revisions. Supplied record timestamps cannot backdate permission checks. Missing reply dependencies are deferred and checked against current policy when resolved. Accepted messages remain in the PoC history when their source is removed or their author loses access. Rebuilding projections replays the retained decisions rather than reevaluating past permission.

History returns at most 20 messages and 128 KiB, in acceptance sequence, with a captured upper boundary. Continuations are protected and bound to the participant and room. Acknowledge a displayed/processed page's resume cursor to advance the durable read position. Each page requires current admission, even when its messages are cached.

Koan supervises a 30-second reconciliation worker. It checks a bounded number of rooms/writers, also scanning known arrivals to recover missed write notifications, and retries pending intents. It makes no model calls. Unavailable, catching-up and checked states are explicit. The PoC reads complete repositories with an 8 MiB/1024-record bound; larger repositories need the later incremental synchronization work.

The native verifier validates the pinned Spaces commitment and content format against the current DID key. Source ingestion is tied to a guarded authenticated response from the expected author's PDS. The proof is deliberately non-transferable; it is not an independently transferable authorship certificate. Historical-key recovery, rollback detection, streaming and blobs are outside this verifier's supported scope.

## Restart and restore

Keep the original test network running. Restart the application with the same instance, origin and port:

```powershell
./scripts/stop-local.ps1 -Instance site
./scripts/run-local.ps1 -Instance site -Port 5220 -NoBuild -Background
```

For a consistent backup, stop the application first. The backup includes the database and its retained acceptance decisions, read positions and credential hashes, plus protected OAuth sessions and the Data Protection key ring. It is sensitive operator material even though it contains no plaintext OAuth session files.

```powershell
./scripts/stop-local.ps1 -Instance site
$snapshot = ./scripts/backup-local.ps1 -Instance site
./scripts/restore-local.ps1 -BackupDirectory $snapshot.backupDirectory -Instance recovered
./scripts/run-local.ps1 -Instance recovered -Port 5220 -NoBuild -Background
```

Restore creates a new directory and never replaces existing state. The script enforces the original network and client/service origin. On Windows the keys are DPAPI-protected for the Windows user: copy alone does not recover them under another account or machine. For other hosting arrangements persist and protect the ASP.NET Data Protection key ring explicitly. Back up the participant runner's separate credential and pending/cursor state under operator custody too.

An application backup does not recover PDS contents, the PLC network, account control or the authority's recovery material. Removing membership immediately denies Tangent reads/writes and new Space credentials. A previously issued credential still read the source after removal; the pinned provider's issued lifetime was two hours. Expiry-time denial was not observed by waiting through that interval. This is separate from immediate local denial.

## Evidence and contribution

`docs/evidence/` and `probes/evidence/` contain redacted receipts. Each distinguishes real network behavior from focused unit/oracle tests and scripted fixtures. `contributions/koan-atproto-auth/verify.ps1 -Destination <new-directory> -RunTests` independently applies the auth patch to a fresh pinned checkout and runs its generic regression suites. No upstream publication is required to run this app.

Agents participate through the local connector and the authenticated API ([ADR 0005](adr/0005-experience-api-and-local-mcp.md)); A2A remains a follow-on proof.

