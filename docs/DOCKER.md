# Docker development

Use the root README's setup commands. The app runs as the image's non-root user in the `tangent-space` Compose project. Its URL stays `http://127.0.0.1:5220`; container port 8080 is bound to host loopback. `AddKoan()` remains the standard host entry point. The usual bootstrap report is on stdout, followed by live application and framework logs.

```powershell
docker compose ps
docker compose logs -f tangent
docker compose restart tangent
docker compose stop tangent
docker compose up -d --wait tangent
```

For code changes, use `./scripts/start-docker.ps1 -Build`. The build publishes for Linux inside the pinned .NET SDK image; the runtime image contains published output and a curl health check. Build context excludes private application state, OAuth files, keys and fixture passwords. SourceLink is disabled for the source archive build, which intentionally excludes Git internals. The separately verified Koan contribution and pinned base identify the source.

The `tangent-spaces-network` container is existing external development infrastructure. Its ports 2582–2585 and disposable identities are intentionally preserved; Compose does not recreate or stop it. A fresh checkout starts it using `probes/spaces-network/start.ps1 -Build` before the app. It is not a durable PDS deployment: restarting it creates new accounts. Do not restart it to restart Tangent.

## State and migration

The app binds `.local/docker/site` to `/state`. It holds SQLite, instance configuration, protected OAuth storage and the persistent Linux Data Protection key ring. `/health/ready` drives Docker health. Logs rotate at three 10 MB files and can be viewed directly in Docker Desktop.

On first migration, `start-docker.ps1` stops the known Windows `site` process, creates its consistent backup under `.local/backups`, and copies its SQLite database and any journal files into the new Docker state directory. It never overwrites an existing Docker database. Participants, ownership, room rules, source decisions, conversation projections, credential hashes and durable read positions are retained. `migration.json` records the original backup. The original Windows directory and DPAPI keys remain intact.

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

The reported `leo.sylin.org` challenge now reaches the real Bluesky authorization page with HTTP 302. Password entry and consent remain with the user/provider; a complete public-account sign-in is not claimed by that redirect test. Missing or unresolvable identifiers return a sanitized retry page rather than HTTP 500. Signing in does not establish a public provider's experimental Spaces support or confer site ownership.
