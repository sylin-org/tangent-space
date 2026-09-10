# Disposable Spaces protocol network

This is S01 infrastructure, not the Tangent deployment architecture. It runs two
instances of the official alpha PDS, its local PLC server, a tiny authenticated
managing-app callback, and a port forward to the native .NET probe. No public
accounts or data are involved.

The first real baseline passed on 9 September 2026. Retained results are in
[evidence/baseline-2026-09-09.json](evidence/baseline-2026-09-09.json), with the
commands and build details in [evidence/run-2026-09-09.md](evidence/run-2026-09-09.md).

## Pinned inputs

- AT Protocol source: `bluesky-social/atproto`, `permissioned-data`, commit
  `c1d97bbd5c874ae7c2c26ed84586ef15a000b7ae`.
- PDS package in that checkout: `@atproto/pds` `0.5.32`. The commit is the actual
  pin; that version alone does not identify the unpublished Spaces code.
- Official fixture helpers: `@atproto/dev-env` `0.6.5`.
- Package manager: `pnpm` `11.11.0`; dependency graph from the pinned lockfile.
- Node base: `node:24-bookworm-slim` digest
  `sha256:ba849c60be29959425b8734d57b8b4b7d56f98edd9504c9af091d5281095a71e`.
- Spaces Lexicons: the `lexicons/com/atproto/space` and `simplespace` directories
  from the same AT Protocol commit.
- One recorded upstream compatibility patch: [upstream-varint.patch](upstream-varint.patch).
  Its default import repairs CommonJS `varint` loading in native Node ESM. The
  startup preload [patch-varint.mjs](patch-varint.mjs) applies that exact change
  to source and compiled equivalents before loading the PDS. All CAR, MST,
  signature, OAuth, and Space authorization checks remain enabled.

The image is a source development fixture and includes build tools. It is not a
production image. Debian build packages are resolved during image build; retain
the resulting image ID in evidence when exact binary reproduction matters.

## Start and inspect

From the Tangent repository root, with Docker Desktop's Linux engine available:

```powershell
./probes/spaces-network/start.ps1 -Build
Invoke-RestMethod http://localhost:2585/health
Get-Content .local/spaces-network/baseline-evidence.json
```

The first build downloads the upstream dependency graph and compiles generated
Lexicons and the OAuth sign-in UI. The script rejects a dirty or incorrectly
pinned upstream checkout. It restores one tracked upstream symlink inside the
Linux build because Git for Windows may materialize it as plain text.

Allow roughly ten minutes for a cold build on this machine; the legacy client
generator alone took 372 seconds. Reusing the image starts the network in seconds.

The container starts asynchronously. Wait for `/health` to report `passed` or
`failed`; `docker logs tangent-spaces-network` reports startup problems. Do not
dump the credentials file into a terminal transcript.

| Host origin | Purpose |
| --- | --- |
| `http://localhost:2582` | Local PLC DID documents and signed operations |
| `http://localhost:2583` | PDS1; authority, owner, manager, Lexicon authority |
| `http://localhost:2584` | PDS2; agent and outsider |
| `http://localhost:2585` | Redacted health/evidence and managing-app callback |
| `http://localhost:5180` | Native .NET OAuth probe; started separately |

Docker publishes only to host loopback. Inside the network process,
`localhost:5180` forwards bytes to `host.docker.internal:5180`, so the same native
probe metadata URLs can be reached from the PDS and host browser. This forward
does not terminate OAuth, modify traffic, own tokens, or implement app logic.

`/.well-known/oauth-authorization-server` and
`/.well-known/oauth-protected-resource` are served by each PDS. The network
process must remain running throughout the native OAuth experiment.

## Fixture files and lifetime

`.local/spaces-network/fixtures.json` contains generated disposable passwords,
account DIDs, origins, local Lexicon authority, and room URIs. Its `accounts`
array has `role`, `handle`, `email`, `password`, `did`, and `pds` properties.
`baseline-evidence.json` contains only redacted observed results.

PDS files live in disposable container temporary directories. The official PLC
test server uses its in-memory database. Stopping and removing this network
destroys its identities; starting again creates new DIDs. This fixture does not
prove restart continuity or backup recovery. Keep it running while testing
restart of the Tangent application or runner.

```powershell
./probes/spaces-network/stop.ps1
```

The stop script checks the fixture label and removes only this named container.
Saved evidence and the local source checkout remain.

## What the baseline proves

`network.mjs` creates Lounge and Workshop as separate real Spaces. They use the
real `simplespace` managing-app policy and a service DID registered with the
local PLC. The callback verifies the authority's service JWT signature,
audience, method, expiry, and relationship to the requested room. Unknown users
and rooms deny admission; unsigned callbacks receive HTTP 401.

It writes a record as the agent on PDS2, exchanges a delegation token issued to
the manager on PDS1 for an ES256 DPoP-bound Space credential, and reads the source
record from PDS2. It then checks outsider credential denial, rejection of a
Lounge credential when reading Workshop, and old-versus-new credentials after
membership removal. Recorded evidence states the observed outcomes.

**The baseline authenticates with legacy password sessions.** This deliberately
isolates Spaces server behavior and does not satisfy the epic's OAuth gate.
The separate .NET probe must repeat relevant operations with real OAuth grants
and verify the grant scopes. Nothing here bypasses PDS identity, ownership,
Space credential, DPoP, or service-authentication checks.

The in-process callback membership list is a protocol test fixture, not Tangent
policy storage or a proposed separate production service.

The actual granular OAuth attempt uncovered a separate upstream runtime issue:
Lexicon CAR verification failed with `varint.decode is not a function`. The
one-line patch above fixed module loading; the official resolver then verified
both local Lexicon records and the upstream example. Run the retained probes:

```powershell
docker exec tangent-spaces-network node /atproto/tangent-probe/inspect-lexicon.mjs
docker exec tangent-spaces-network node /atproto/tangent-probe/inspect-record-validation.mjs
```

The second probe distinguishes validated Lexicon resolution/client validation
from PDS record validation. With `validate:true`, this PDS rejects even a valid
custom message as an unknown Lexicon type. The resolved message schema itself
accepts the valid record and rejects a malformed record through the official
Lexicon validator. Tangent must enforce that schema on write and ingestion.

## Local development differences

This harness follows upstream `TestPds`, `TestPlc`, and the multi-PDS developer
entry point. It uses upstream development mode: HTTP localhost origins, local
SSRF allowance, and handle resolution routed to the corresponding PDS. These
transport/DNS substitutions enable an isolated network; they are not production
DNS, HTTPS, or host-hardening validation. DID records and signatures are still
resolved and verified by the real implementations.

`local.tangent.room` and `local.tangent.message` are published as Lexicon records
in a disposable authority account. Each PDS explicitly uses that account for
Lexicon resolution, so there is no claim to a globally registered namespace.
At this alpha revision, third-party record validation may report `unknown` even
though the space declaration resolves for OAuth scopes. Tangent must validate
its own accepted record schema.

## Primary references

- [Official source revision](https://github.com/bluesky-social/atproto/tree/c1d97bbd5c874ae7c2c26ed84586ef15a000b7ae)
- [Multi-PDS developer entry point](https://github.com/bluesky-social/atproto/blob/c1d97bbd5c874ae7c2c26ed84586ef15a000b7ae/packages/dev-env/src/bin-multi-pds.ts)
- [Space integration test helpers](https://github.com/bluesky-social/atproto/blob/c1d97bbd5c874ae7c2c26ed84586ef15a000b7ae/packages/pds/tests/_space.ts)
- [Managing-app authorization](https://github.com/bluesky-social/atproto/blob/c1d97bbd5c874ae7c2c26ed84586ef15a000b7ae/packages/pds/src/simplespace/manager.ts)
