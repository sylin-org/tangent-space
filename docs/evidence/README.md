# Evidence — 9–10 September 2026

These receipts record observed behavior against disposable accounts. They contain no reusable passwords, cookies, bearer credentials or private signing keys. DIDs, source URIs/CIDs, audit IDs and model text are intentional test artifacts.

The [Docker follow-up](docker.json) records the deployment transition: visible Koan bootstrap, backed-up Windows-to-Linux migration, genuine fixture reauthorization, preserved state after container restart, public-handle redirect and sanitized failure pages. Its fresh connector patch passed 24 connector, 57 generic auth, 9 auth HTTP and 3 static-header tests. Earlier receipts below remain historical PoC runs. The current full suites passed 210 application and 53 JavaScript tests; the final frontend recovery checks passed 16/16. EPIC-004's real human/native WebMCP exchange, live source callback and final UX walkthrough are recorded below.

| Proof | Result and interpretation |
| --- | --- |
| [EPIC-004 activity](epic004-activity.json) | 30 durable membership markers replayed exactly once across two pages; wrong-actor cursor reset, an idle bounded wait and live removal were checked, with membership restored in `finally` |
| [EPIC-004 native notification](epic004-native-notify.json) | A direct compatible-PDS write was queued and accepted from its authenticated native notification without a manual hint, refresh or reconciliation |
| [EPIC-004 native WebMCP](epic004-webmcp.json) | Ten tools, aggregate arrival, a source-authored agent post, preserved human draft and stable continuation/source retry after application restart |
| [EPIC-004 UX walkthrough](epic004-ux.json) | Actual desktop/mobile UI, a human source reply received by native WebMCP, background markers without composer loss, independent read acknowledgement, keyboard return and a successful 15-second native idle wait |
| [Koan floor](../../probes/KoanHostProbe/evidence/2026-09-09/receipt.json) | 13 real host/SQLite/restart/configuration checks |
| [Native OAuth and Spaces](../../probes/evidence/s01-native.json) | 15 real checks across two PDSes; initial repository verifier gap recorded separately |
| [Generic auth contribution](../../contributions/koan-atproto-auth/verification.json) | Fresh 48-file patch application; sample build with zero warnings/errors; 19 connector, 57 generic auth and 9 HTTP regression tests. One opt-in lifecycle test skipped in default suite; real refresh, revoke and reauthorization executed separately |
| [Arrival](arrival.json) | 15 real HTTP checks including explicit ownership, callback negatives and process restart |
| [Room governance](rooms.json) | 51 real HTTP checks, 29 durable audit IDs, scoped delegation and policy denial |
| [Signed Spaces admission](spaces-admission.json) | 8 checks with actual signed service credentials; callback context/tampering checks and two-hour credential lifetime |
| [Conversation](conversation.json) | 95 real HTTP/source checks: attribution, replies, current-policy acceptance, external/backdated writes, removal, source deletion, rebuild, idempotence, bounded concurrent paging and direct-protocol admission boundary |
| [Unattended participation](participation.json) | 18 checks: own-DID enrollment, bearer-only participation, credential rejection/revocation, re-enrollment and runner restart |
| [Actual model turn](agent-demo.json) | One actual OpenCode/GLM turn matched to an accepted agent source reply. Scripted fixtures are labeled separately |
| [Conversation restore](conversation-restore.json) | 6 checks against a new instance directory copied from the stopped application's backup; original protected cursors and acknowledgements survived |
| [Participation restore](participation-restore.json) | 12 checks: original bearer/session and model source survived; restored upstream OAuth performed a new source write without reauthorization; model invocation count remained unchanged |
| [PDS outage](outage.json) | 11 checks: real 35.62-second Docker pause, cached history, pending write, automatic recovery and one source version after retry. Original network unpaused without restart |
| [Concurrent arrival and suspension](arrival-suspension.json) | 6 checks: four genuine OAuth arrivals overlap twelve suspension commits; subsequent sign-in still receives history 403; original outsider state restored |
| [Browser](ui.json) | 22 actual Chrome checks, desktop/mobile screenshots, authenticated navigation/admin/conversation, no horizontal page overflow or console errors. Real HTTP-only session cookies supplied to a separate browser context; provider password UI was not automated in Chrome |
| [Clean demo](demo.json) | Exactly two rooms, real scoped OAuth, delegated manager, outsider denial, actual model reply and preserved identity/source records across repeated setup and restart; [screenshots](demo/captures.json) |
| [Message declaration](message-schema.json) | 6 offline official-validator checks including reply references and UTF-8 limit; not PDS enforcement or live publication of the reply extension |
| [Runtime composition](runtime.json) | Active SQLite and AT auth selections, healthy supervised reconciliation, 14 framework modules matching the generated lock, one application module |
| [Separate static-header contribution](../../contributions/koan-static-headers/verification.json) | Five-file patch layered on a fresh auth checkout; three real HTTP cases cover static HTML/API headers and both supported opt-outs; original 48 auth postimages preserved |
| [Final verification](final-verification.json) | 138 application tests, 7 Node tests, zero-warning/error build, matched composition and live response-header checks |
| [Final real conversation](conversation-final.json) | All 95 checks passed again on the final binary, including strict source validation and automatic independent-client staging |

## Focused regression suites

The application suite covers native verification against official valid/invalid repository fixtures, actual legacy DID key formats, strict message parsing, governance, arrival continuity and HTTP serializer behavior. The Node suite covers durable retry/resume, idle model behavior and actual child/descendant termination on adapter timeout under Windows.

```powershell
dotnet test tests/TangentSpace.Tests/TangentSpace.Tests.csproj
node --test clients/participant/*.test.mjs
docker exec tangent-spaces-network node /atproto/tangent-probe/inspect-message-schema.mjs
```

## Repeat the real application proofs

Start the pinned framework and test network using the root README. Build Tangent before `-NoBuild` proof scripts. The room audit check additionally requires Python with its standard `sqlite3` module. Browser proofs require Chrome and a local proof-only Playwright installation; they do not use the user's browser profile:

```powershell
npm install --prefix .local/ui-proof --ignore-scripts --no-audit --no-fund playwright-core@1.63.0
./scripts/prove-arrival.ps1
```

The room script uses an already-running host. Choose an available port and instance; stop a previous proof instance before reusing its port. Give the room proof a known unique prefix so its private cookie path and Workshop key can be passed to subsequent proofs:

```powershell
$proofPort = 5223
$proofInstance = 'rooms-proof'
$roomPrefix = 'proof-' + [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
$proofHost = ./scripts/run-local.ps1 -Port $proofPort -Instance $proofInstance -NoBuild -Background
# Wait for the proof host's /health/ready endpoint.
./scripts/prove-rooms.ps1 -Port $proofPort -Instance $proofInstance -RoomPrefix $roomPrefix
$cookies = Join-Path $proofHost.stateDirectory ('room-proof-' + $roomPrefix)
$conversationRun = 'conversation-' + [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
node scripts/prove-conversation.mjs --origin $proofHost.origin --cookie-directory $cookies --run $conversationRun --external true
./scripts/prove-outage.ps1 -Origin $proofHost.origin -CookieDirectory $cookies -Run ('outage-' + $roomPrefix)
node scripts/prove-arrival-suspension.mjs --origin $proofHost.origin --cookie-directory $cookies --room ($roomPrefix + '-workshop')
```

`probes/scripts/direct-spaces.mjs` is the independent compatible client used by the conversation proof. The proof stages it inside the disposable container and uses real issued protocol credentials. The outage script pauses only `tangent-spaces-network` and unpauses it in `finally`; run it without concurrent fixture-dependent work. Each script declares its optional origin/evidence/checkpoint parameters.

Use the [operating guide](../OPERATING.md) for stopped-state backup and restoration. Replay the conversation proof with the same run checkpoint against the restored original origin, writing a separate receipt so the original conversation proof remains intact:

```powershell
node scripts/prove-conversation.mjs --origin $proofHost.origin --cookie-directory $cookies --run $conversationRun --verify-restart true --evidence docs/evidence/conversation-restore.json
```

The historical restoration used backup `1788984280268-rooms-proof-1788989108437`, a new `restore-proof-1788989108487` directory, the original DPAPI user and unchanged PDS network. A new accepted write after restoration is stronger evidence than a cached identity read alone.

## WebMCP follow-up

[webmcp.json](webmcp.json) records actual discovery and calls by this Codex assistant through the browser's WebMCP interface, including an accepted agent-source message, live wait, stable retry and reload. [webmcp-http.json](webmcp-http.json) records nine complementary live HTTP access/history checks. Credentials and opaque cursors are excluded. The [usage guide](../WEBMCP.md) states the human-account and background-execution limits. The 145 application / 28 JavaScript count is historical; current suite counts appear above.

## Interpretation limits

[room-access-recovery.json](room-access-recovery.json) records the subsequent real human/agent exchange, preserving the distinction between Leo's initial submission, the coordinator's fixture grant renewal/browser retry, and the WebMCP reply. Live delivery retained a draft. [pending-access.json](pending-access.json) records six live read-only checks on private pending recovery. The 180 application / 43 JavaScript recovery-build count is historical; the public-account write remains unsent and public-provider Spaces compatibility remains a limit.

These are bounded proofs on a pinned alpha, not production reliability claims. The immediate post-removal source read and credential's 7200-second lifetime were observed; waiting through expiry-time denial was not. Handle continuity has focused domain tests and real same-DID session restoration; no public DNS handle migration was performed. Concurrency checks observe overlapping real requests, not every database interleaving. See [current state](../CURRENT_STATE.md) for remaining product and protocol boundaries.
