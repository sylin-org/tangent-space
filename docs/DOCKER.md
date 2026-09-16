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

`Launch.bat` creates a missing configuration with an unclaimed owner, public AT sign-in (`atproto` identity scope) and SQLite under `/state`. An existing `appsettings.json` is retained byte-for-byte, including owner choices and provider configuration. The public origin comes from `Tangent__Site__PublicOrigin` in `compose.yaml`.

## State

The app binds `.local/docker/site` to `/state`, its entire content root: `appsettings.json`, `tangent.sqlite` with its WAL and SHM files, `oauth/`, `data/keys/` and `koan.lock.json`. Participants, memberships, conversation, activity and receipts live in SQLite. Recreating the container preserves these files. `/health/ready` drives Docker health. Logs rotate at three 10 MB files; view them in Docker Desktop or with `docker compose logs -f tangent`.

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
