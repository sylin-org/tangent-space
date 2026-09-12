# Docker development

Use the root README's setup commands. The app runs as the image's non-root user in the `tangent-space` Compose project. Its URL stays `http://127.0.0.1:5220`; container port 8080 is bound to host loopback. `AddKoan()` remains the standard host entry point. The usual bootstrap report is on stdout, followed by live application and framework logs.

```powershell
docker compose ps
docker compose logs -f tangent
docker compose restart tangent
docker compose stop tangent
docker compose up -d --wait tangent
```

For code changes, run `Build.bat`, then `Launch.bat`. The existing `./scripts/start-docker.ps1 -Build` entry point is also retained. The build publishes for Linux inside the pinned .NET SDK image; the runtime image contains published output and a curl health check. Build context excludes private application state, OAuth files, keys and fixture passwords. SourceLink is disabled for the source archive build, which intentionally excludes Git internals. The separately verified Koan contribution and pinned base identify the source.

`Launch.bat` starts standalone Tangent with Local conversation storage. It does not read Spaces fixtures, contact port 2585, or require a test network. Missing configuration is created with an unclaimed owner, public AT sign-in (`atproto` identity scope), and SQLite under `/state`. Existing `appsettings.json` is retained byte-for-byte, including any owner choices and provider configuration.

Experimental Spaces testing is explicit: `Launch.bat -UseFixtureNetwork` (or `-FixtureFile path/to/fixtures.json`). Only that mode validates the fixture health and network marker and registers a managing application for fresh configuration. Start the disposable network separately with `probes/spaces-network/start.ps1 -Build` when needed. Its ports 2582–2585 and identities are external to Tangent; the launcher never recreates or stops it. Restarting that network creates new accounts. Selecting fixture mode does not rewrite an existing standalone configuration.

## State and migration

The app binds `.local/docker/site` to `/state`. It holds SQLite, instance configuration, protected OAuth storage and the persistent Linux Data Protection key ring. `/health/ready` drives Docker health. Logs rotate at three 10 MB files and can be viewed directly in Docker Desktop.

Only with explicit `start-docker.ps1 -UseFixtureNetwork -MigrateWindowsState`, the launcher stops the known Windows `site` process, creates its consistent backup under `.local/backups`, and copies its SQLite database and any journal files into the new Docker state directory. It never overwrites an existing Docker database. Participants, ownership, room rules, source decisions, conversation projections, credential hashes and durable read positions are retained. `migration.json` records the original backup. The original Windows directory and DPAPI keys remain intact.

DPAPI-protected Windows login keys cannot be used by the Linux container. Sign in again and reconnect the authority/room grants. For the disposable demo:

```powershell
./scripts/prepare-demo.ps1 -HumanOnly -Reconnect
```

This preserves the existing messages and model reply, renews the real fixture OAuth connections, and performs no model inference. Existing site bearer credentials are retained in SQLite. Old opaque read cursors protected by the Windows key ring must be discarded; retain any pending write operation and resume from the server's durable read position. Ordinary subsequent Docker restarts retain the Linux key ring, cookies and OAuth grants.

The Linux development key ring is persisted without an additional XML key encryptor. Treat the entire state directory and its backups as sensitive local operator material. This setup is for the local PoC; production hosting needs its own encrypted key custody and deployment policy.

For a consistent Docker backup, stop the app and copy the entire state directory, then start it again:

```powershell
docker compose stop tangent
$backupPath = '.local/backups/docker-' + [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
Copy-Item -LiteralPath .local/docker/site -Destination $backupPath -Recurse
docker compose up -d --wait tangent
```

Restore only while the app is stopped, to a new state directory and against the original PDS network. Point the Compose bind mount at that directory. App backup does not recover the external PDSes or control of their accounts. The original `backup-local.ps1` and `restore-local.ps1` remain specifically for Windows-hosted instances.

## Sign-in and development routing

Public accounts use `https://plc.directory`. Only DIDs and handles explicitly listed in `DevelopmentHandles` use `DevelopmentPlcDirectory`. In Docker, the exact fixture origins connect through `host.docker.internal`, while their URLs, issuer identities, HTTP Host and DPoP audiences stay unchanged. Public destinations keep guarded public HTTPS transport. These routing options are rejected outside Development.

The reported `leo.sylin.org` challenge now reaches the real Bluesky authorization page with HTTP 302. Password entry and consent remain with the user/provider; a complete public-account sign-in is not claimed by that redirect test. Missing or unresolvable identifiers return a sanitized retry page rather than HTTP 500. Signing in does not establish a public provider's experimental Spaces support. On an unclaimed server with blank OwnerDid, a verified sign-in opens owner confirmation; an explicit OwnerDid reserves it for that account.

## Build, launch and reset

- `Build.bat`: prepares the pinned Koan contribution and builds the Tangent image. It does not stop the running app.
- `Launch.bat`: creates missing standalone configuration, preserves existing configuration byte-for-byte, and starts only the Tangent service. Test-network validation requires explicit `-UseFixtureNetwork` or `-FixtureFile`.
- `Wipe.bat`: prints the resolved state directory and requires typing `WIPE`. It stops/removes only the app container, then deletes `.local/docker/site`, including config, SQLite and journals, OAuth state, Data Protection keys and network marker. `Wipe.bat -WhatIf` previews the operation. Backups, source fixtures and the external network are retained.

After a wipe, run Build and Launch. Blank OwnerDid lets the first verified account claim ownership; the browser then offers the existing name/channel setup. Alternatively, edit `.local/docker/site/appsettings.json` before the first sign-in and restart the app with an explicit owner DID. Once claimed, ownership is persisted in SQLite and cannot be transferred by changing configuration. Legacy Windows data is never migrated automatically after a reset.

The app's entire content root is the Windows bind mount: `.local/docker/site` → `/state`. This includes `appsettings.json`, `tangent.sqlite` and WAL/SHM, `oauth/`, `data/keys/`, `koan.lock.json`, and, for fixture mode, `network.json`. Durable Participants, memberships, local configuration entities, activity, MCP companion/context handles and receipts live in SQLite. Recreating the app container preserves these files. Docker stdout logs rotate independently; use Docker Desktop or `docker compose logs -f tangent`.

The source test PDS/PLC processes remain disposable infrastructure. Their host evidence files are not a durable backup of source accounts or repositories. The lifecycle scripts deliberately leave this network running. App-state persistence does not make the test network production storage.

Run `pwsh -File scripts/test-server-lifecycle.ps1` for isolated safety and configuration-preservation checks. It uses disposable scratch paths and mocked Docker commands, never your current app state.

## Copy state out and back in

```powershell
./Backup.bat                         # Timestamped copy under .local/backups
./Backup.bat "E:/backups/tangent-one" # Optional new destination
./Restore.bat "E:/backups/tangent-one"
./Launch.bat
```

Backup briefly stops the app for a consistent SQLite copy and resumes it if it was running. The snapshot includes the entire mounted state and a file-hash manifest. Restore verifies that snapshot, asks for `RESTORE`, stops/removes only the app container, saves the displaced state under `.local/backups/before-restore-*`, and copies the snapshot into `.local/docker/site`. It leaves the app stopped for Launch. Both support `-WhatIf`. For Topics using experimental Spaces storage, restore also requires their original source network; these copies do not recreate its PDS/PLC accounts. Standalone Local Topics have no test-network dependency.

A real backup was created at `.local/backups/docker-before-final-mcp`; the app resumed healthy. Current user state was not wiped or restored. No second Tangent instance was created.
