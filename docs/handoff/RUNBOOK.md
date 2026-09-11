# Local operation and continuation runbook

## Paths and dependencies

| Item | Location / value |
| --- | --- |
| Working repository | `E:\repo\github\sylin-org\tangent-space` |
| Shell | PowerShell; wrappers require PowerShell 7 (`pwsh`) |
| Application | http://127.0.0.1:5220/ |
| Agent page | http://127.0.0.1:5220/agent.html |
| Compose project / service | `tangent-space` / `tangent` |
| App container / image | `tangent-space-tangent-1` / `tangent-space:dev` |
| App host state | `.local/docker/site` bind-mounted to `/state` |
| App binaries / static files in container | `/app` / `/app/wwwroot` |
| Source test container | `tangent-spaces-network` — external to app Compose |
| Source test health | http://localhost:2585/health |
| Source test network ID at earlier proof | `1788984280268`; confirm current fixture marker before using old receipts |
| Pinned Koan checkout | `.local/upstream/koan-framework`, base `e07a84cc3f71a0867f1122b03b723cc80727e772` plus local contribution |
| Pinned AT source | `.local/upstream/atproto`, revision `c1d97bbd5c874ae7c2c26ed84586ef15a000b7ae` |
| Host Node for client/probes | Node 24 |
| App SDK / runtime | Docker pins .NET SDK 10.0.401 / runtime 10.0.12 by digest |
| Visual reference | `refs/Design Example`; sibling `E:\repo\github\gposingway\gposingway-org` |

Use [README](../../README.md), [DOCKER](../DOCKER.md) and actual scripts for full options. Some older Docker paragraphs describe manual migration/alternate restore directories; the current user-selected path is the root Backup/Restore pair copying the same mounted state in/out.

## First checks on takeover

```powershell
git status --short
docker compose ps
docker ps --filter name=tangent-spaces-network --format '{{.Names}} {{.Status}}'
Invoke-RestMethod http://127.0.0.1:5220/health/ready
Invoke-RestMethod http://127.0.0.1:5220/api/server |
  Select-Object setupRequired, ownerDid, canManage, backgroundScene, backgroundMouseSpotlight
```

Anonymous `canManage: false` is expected even after an owner exists. `setupRequired` and the authoritative welcome/onboarding response distinguish unclaimed setup. At handoff, `ownerDid` was empty and `setupRequired` true. Do not assume seed communities, old room links or old app-issued tokens survived the user's wipe.

Use an existing browser tab if available, but rediscover it: old tab IDs and agent tool handles are session artifacts, not durable setup. `/` initially returns an HTML shell; the unclaimed redirect occurs in browser JavaScript. A plain HTTP 200 at `/` is not evidence that the redirect is broken.

## Build and launch the application

```powershell
./Build.bat
./Launch.bat
docker compose logs -f tangent
```

Build prepares/verifies the pinned Koan contribution and builds the image. Launch creates only missing local configuration, retains existing config, checks the source-network marker and starts the app. For an already prepared checkout, the recent direct iteration path was:

```powershell
docker compose build tangent *> .local/docker/server-build.log
if ($LASTEXITCODE -eq 0) {
  docker compose up -d --no-deps --wait tangent
} else {
  Get-Content .local/docker/server-build.log -Tail 30
}
```

No source-network restart is involved. Docker restart without a rebuild does not publish changed source/static files. Update the existing asset-version query strings when browser caching would hide a frontend revision; the atmosphere build at handoff used `20260910-18` in index.html. Avoid leaving a static `docker cp` hotfix that the next image build loses.

On a genuinely new machine with **no existing source test network**, follow `probes/spaces-network/start.ps1 -Build` and wait for health before Launch. Do not run that as a routine takeover/restart command here: source startup regenerates fixture identities. Check actual state before acting.

## What persists

The entire app content root is host-mounted. It includes `appsettings.json`, `tangent.sqlite` and WAL/SHM, `oauth/`, `data/keys/`, `koan.lock.json` and `network.json`. Persisted entities cover participants, memberships, server/Tangent/Topic policy, accepted history, source decisions, activity, hashed credentials, MCP selections/contexts and receipts.

The Linux key ring persists on the mount; in this development configuration it has no additional XML key encryptor. Treat the mount/backups as sensitive operator state. Historical Windows-native state used DPAPI under the same Windows user and cannot simply reuse those keys inside Linux. Reauthorization was required for that older migration. Ordinary container replacement now retains Linux keys.

**App backup is not source backup.** The disposable PDS/PLC accounts, source repositories and account control have a separate lifetime. Files recording source fixtures on the host do not turn the network into a durable production PDS.

## Backup, restore and clean start

```powershell
./Backup.bat
./Backup.bat "E:/backups/tangent-one"
./Restore.bat "E:/backups/tangent-one"
./Launch.bat
```

Backup stops the app briefly for a consistent full-mount copy and resumes it if previously running. Restore verifies the manifest, asks for RESTORE, displaces current app state into a backup, copies the snapshot into the existing mount and leaves the app stopped for Launch. Neither needs another instance or edits to the Compose mount. See `scripts/docker-state.ps1`.

Only when deliberately testing a new server:

```powershell
./Wipe.bat -WhatIf
./Wipe.bat
# Read the displayed resolved path and type WIPE.
./Build.bat
./Launch.bat
```

Wipe clears app configuration, DB, OAuth state, key ring and marker under `.local/docker/site`, preserving backups and the separate source container. It does not migrate old Windows state back in. A blank `Tangent:Site:OwnerDid` allows explicit confirmed ownership; a configured DID reserves the claim. Changing config after ownership is persisted does not transfer ownership.

Never recursively delete/move a computed Windows path without verifying that its resolved absolute destination is inside the intended target. Use native PowerShell literal-path operations or the existing guarded script. Do not improvise cross-shell cleanup commands.

## Identity and test accounts

`tangent-owner.test` is a compatible local owner fixture; local fixture details are under ignored `.local/spaces-network/fixtures.json`. Use existing scripts/connection flows rather than printing passwords or tokens into tool output. Historical proofs used additional agent/manager/outsider fixtures; discover current mappings before using them.

Leo's public account is `leo.sylin.org`. He offered Lumen's public identity `lumen-bubbles.bsky.social`, but its credentials were not requested or used in the recorded flow. Do not assume a Lumen connection exists. Keep reusable PDS credentials out of model context and never request them casually in chat.

If a write is pending or denied, separate these questions:

1. Is the participant authenticated under the intended DID?
2. Does the current Tangent/Topic policy permit this action?
3. Has that account granted native source write consent?
4. Does its provider actually implement the required Spaces capability, and is the source reachable?

Owner permission answers only part of this. Historical public consent offered identity permission only and writes returned ScopeMissingError. The user explicitly chose to retain Spaces and use compatible test accounts. Saved pending operations must retain their original key/body/actor through consent recovery.

## Focused verification

Use the smallest relevant check, not all of these commands every time:

```powershell
# Changed browser code:
node --check src/server/web/wwwroot/atmosphere.js
node --check src/server/web/wwwroot/ascii-scenes.js

# For a relevant .NET behavior, choose an existing focused filter:
dotnet test tests/TangentSpace.Tests/TangentSpace.Tests.csproj --filter 'FullyQualifiedName~RelevantExistingTest'
```

The test filter above is a placeholder; choose an actual existing test. For CSS/UI changes, prefer a real browser check at desktop and the affected narrow width. Do not add tests that merely mirror a reversible style change. For MCP wire/auth changes, the existing SDK/inbound probes are more meaningful than a synthetic schema pass, but they may enroll identities or post content: read before running.

Useful historical receipts: `epic004-ux.json`, `epic004-webmcp.json`, `epic004-native-notify.json`, `room-access-recovery.json`, `mcp-inbound.json`, `mcp-sdk.json`, `mcp-workflows.json`, `mcp-companion-context.json`, `server-roles.json`, `server-hub.json`. They document old runs, not fresh runtime state. Generated `examples.json`/STORYBOOK/explorer are explicitly synthetic.

The latest atmosphere browser pass checked all eight scenes and viewport grids at 390×844, 1920×1080 and 3840×2160 (2,508 / 15,622 / 42,120 cells), no horizontal overflow, pause and zero intensity. Mouse Spotlight was checked on frozen Tidal Lines, then off and across reload. Owner-persisted atmosphere writes remain unexercised on the current unclaimed server.

## Koan contributions and troubleshooting

`contributions/koan-atproto-auth/` holds the reusable AT auth connector patch/sample/tests; `contributions/koan-static-headers/` holds a separate middleware ordering fix. They were verified against the pinned isolated checkout and have not been published upstream. `scripts/prepare-framework.ps1` reconstructs/verifies that setup. Preserve the original sibling framework repository.

Koan MVC uses Newtonsoft in relevant inbound paths. A real native notification failure was previously caused by using a System.Text.Json JsonElement in its request DTO; don't reintroduce serializer assumptions when editing callbacks. Requests from public DIDs resolve through public PLC; only explicitly configured development identities use the local fixture resolver. Do not globally route public accounts through the fixture PLC to repair a test.

No useful work is pending in a required background agent at handoff. Current app containers should stay running. When continuing, update CURRENT_STATE/ADRs only for material changes and make any final claims match the checks actually performed.
