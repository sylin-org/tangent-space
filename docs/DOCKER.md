# Docker development

Use the root README's setup commands. The app runs as the image's non-root user in the `tangent-space` Compose project. Its URL is `http://127.0.0.1:5220`; container port 8080 is bound to host loopback. `AddKoan()` is the host entry point. The bootstrap report is on stdout, followed by live application and framework logs.

```powershell
docker compose ps
docker compose logs -f tangent
docker compose restart tangent
docker compose stop tangent
docker compose up -d --wait tangent
```

For code changes, run `Build.bat`, then `Launch.bat` (or `./scripts/start-docker.ps1 -Build`). The build publishes for Linux inside the pinned .NET SDK image; the runtime image contains the published output and a curl health check. The build context excludes private application state, OAuth files and keys. SourceLink is disabled for the source archive build, which excludes Git internals; the pinned Koan contribution and base image identify the source.

`Launch.bat` creates a missing configuration with an unclaimed owner, public AT sign-in and durable SQLite under `/state`. Ordinary sign-in requests only the `atproto` identity scope. Publishing a selected Post to Bluesky starts a separate consent request for `repo:app.bsky.feed.post`. An existing `appsettings.json` is retained byte-for-byte. Older local configurations that declare only `atproto` in both the provider `Scopes` and localhost `ClientId` query must be updated together before the separate publishing consent can work. The public origin comes from `Tangent__Space__PublicOrigin` in `compose.yaml`.

## State

The app binds `.local/docker/site` to `/state`, its persistent root: `appsettings.json`, `storage.json`, the selected file database (SQLite defaults to `tangent.sqlite`, including WAL and SHM files), `oauth/`, and Data Protection keys. Koan loads `storage.json` before opening repositories, so a tested and activated database choice survives restart. Compose sets `Koan__Discovery__DisableAutomatic=true` so referenced but inactive adapters do not search the network at startup. The setup API still lists every installed adapter, and Test probes a submitted candidate without changing the active source. An activated external provider needs a concrete endpoint in its saved settings. Activate saves the choice for the next restart. Selection is limited to first installation, before the Space is claimed; moving existing data requires a separate transfer. In-memory databases are rejected. `/health/ready` drives Docker health. Logs rotate at three 10 MB files; view them in Docker Desktop or with `docker compose logs -f tangent`.

On Windows/WSL2, SQLite on the bind-mounted host directory can have high `fsync` latency, especially for write-heavy conversations. The bind mount remains the development default because the current backup and restore scripts operate on `.local/docker/site`. For a production installation, prefer a native Linux volume or an external durable database, then adapt and verify backup/restore before switching; moving an existing SQLite file also requires a controlled stopped migration. External provider data is **not** included in the local `/state` backup.

The Linux Data Protection key ring is persisted without an additional XML key encryptor. Treat the state directory and its backups as sensitive local operator material. This setup is for local development; production hosting needs its own encrypted key custody and deployment policy.

## Build, launch and reset

- `Build.bat`: prepares the pinned Koan contribution, builds the Tangent image and compiles the local connector. It does not stop the running app.
- `Launch.bat`: creates a missing configuration, preserves an existing one byte-for-byte, and starts only the Tangent service. Add `-Build` to rebuild first.
- `Wipe.bat`: prints the resolved state directory and requires typing `WIPE`. It stops and removes only the app container, then deletes `.local/docker/site`: configuration, SQLite and journals, OAuth state and Data Protection keys. `Wipe.bat -WhatIf` previews the operation; `-Force` skips the prompt for scripts. Backups are retained.

- `full.bat`: wipe, build, launch and open the app, with no prompt anywhere. It is for serial deploy tests, where a first run is exercised over and over, so it takes **no backup** and destroys `.local/docker/site` outright. Use `Backup.bat` first if the install holds anything worth keeping, or the three scripts separately when you mean only one of them.

After a wipe, run Build and Launch. A blank `OwnerDid` lets the first verified account claim ownership; the browser then offers the first-Tangent setup. Alternatively, set an explicit owner DID in `.local/docker/site/appsettings.json` before the first sign-in and restart the app. Once claimed, ownership is persisted in SQLite and cannot be transferred by changing configuration.

Sign-in resolves identities through the public `https://plc.directory`. Missing or unresolvable identifiers return a sanitized retry page. On an unclaimed server with a blank `OwnerDid`, a verified sign-in opens owner confirmation; an explicit `OwnerDid` reserves it for that account.

Run `pwsh -File scripts/test-server-lifecycle.ps1` for isolated safety and configuration-preservation checks. It uses disposable scratch paths and mocked Docker commands, never your current app state.

## Backup and restore

```powershell
./Backup.bat                          # Timestamped copy under .local/backups
./Backup.bat "E:/backups/tangent-one" # Optional new destination
./Restore.bat "E:/backups/tangent-one"
./Launch.bat
```

Backup briefly stops the app for a consistent SQLite copy and resumes it if it was running. The snapshot includes the entire mounted state and a file-hash manifest. Restore verifies the snapshot, asks for `RESTORE`, stops and removes only the app container, saves the displaced state under `.local/backups/before-restore-*`, and copies the snapshot into `.local/docker/site`. It leaves the app stopped for Launch. Both support `-WhatIf`.

These scripts snapshot only `/state`. With a selected external provider, they preserve Tangent configuration, OAuth sessions and keys but do not snapshot the provider's content; use that provider's coordinated backup procedure. A restored `storage.json` can point to an external database in a different state, so validate the provider backup and connection before restoring.

## Public deployment configuration

The Compose port and generated OAuth client are loopback-only development defaults. For a public DNS name such as `https://tangent.example`, terminate HTTPS at a reverse proxy and set `Tangent__Space__PublicOrigin=https://tangent.example` (the canonical origin, without a trailing path). Configure `Koan:Web:Auth:Providers:atproto:ClientId` as `https://tangent.example/.well-known/oauth-client-metadata`, with provider `Scopes` containing `atproto` and `repo:app.bsky.feed.post`. Koan serves that metadata route and derives callback `https://tangent.example/auth/atproto/callback`; both must be reachable over the public origin. The development `http://localhost?...` ClientId is invalid outside Development and must not be carried into a public deployment. Replacing the ClientId invalidates existing local AT sessions, so users must sign in again.

Keep the app bound to loopback behind the proxy or an equivalent private network path. Test DNS, HTTPS metadata, callback, and explicit Bluesky publishing consent from a real browser before opening access. The repository does not include a public deployment topology or tested external-provider backup plan.
