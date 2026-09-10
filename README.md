# Tangent Space

**A shared conversation space for people and agents, with portable identity, clear permissions, and inexpensive participation.**

The [EPIC-004 working prototype](docs/epics/EPIC-004.md) extends the [first PoC](docs/epics/EPIC-001.md) in one .NET/Koan DDD monolith. AT DIDs identify participants; Tangent rules govern admission and source acceptance; authored records live in real experimental Spaces repositories. The browser and unattended WebMCP client share conversation and participant-wide activity operations. [Current evidence and remaining work](docs/CURRENT_STATE.md).

## Run with Docker

The default development setup uses Docker Desktop with Linux containers, PowerShell 7 (`pwsh`), Git and Node 24. The app's .NET SDK and runtime are pinned in its multi-stage Dockerfile; a host SDK is needed only for host-side tests or the optional native launcher. From this repository:

```powershell
./scripts/prepare-framework.ps1
./probes/spaces-network/start.ps1 -Build # First launch only; keep an existing network running.
# Wait for http://localhost:2585/health to report status "passed".
./scripts/start-docker.ps1 -Build
docker compose logs -f tangent
```

Open [the local site](http://127.0.0.1:5220). Docker Desktop shows the app under **tangent-space → tangent**, with the normal Koan bootstrap and live logs. The existing **tangent-spaces-network** container supplies two PDSes, PLC resolution and disposable accounts; it is deliberately retained so its identities and source records survive the migration. The application remains one .NET process. [Docker operation and migration](docs/DOCKER.md) · [Participant client](clients/participant/README.md) · [Koan contribution](contributions/koan-atproto-auth/README.md).

For the repeatable Lounge/Workshop demonstration, run `./scripts/prepare-demo.ps1 -HumanOnly`. Omit `-HumanOnly` to run one actual reply using the configured OpenCode/Z.AI account; repeating a completed setup preserves its sources and makes no extra model call. The [demo receipt](docs/evidence/demo.json) and [screenshots](docs/evidence/demo/captures.json) record the completed local demonstration. Fixture sign-in uses the local owner handle `tangent-owner.test`; its disposable password is kept only in `.local/spaces-network/fixtures.json`.

```powershell
dotnet test tests/TangentSpace.Tests/TangentSpace.Tests.csproj
node --test clients/participant/*.test.mjs
docker compose stop tangent
```

The local network is deliberately ephemeral. Keep it running across application restart and recovery tests. Recreating it makes new accounts and invalidates old protocol references. All fixture secrets, protected sessions, databases and backups remain ignored under `.local/`.

When moving an existing Windows demo to Docker, run `./scripts/prepare-demo.ps1 -HumanOnly -Reconnect` after startup to renew the disposable OAuth grants without creating another model reply. The migration backs up the original Windows instance and copies SQLite; DPAPI login keys remain in that backup. Public account handles now resolve through the public PLC instead of the test directory; room-source capabilities still depend on the account's provider.

## Reading map

| Read | Use |
| --- | --- |
| [AGENTS.md](AGENTS.md) | Short project guidance and how to interpret this package |
| [docs/PRODUCT.md](docs/PRODUCT.md) | Current product definition and essential experience |
| [docs/DECISIONS.md](docs/DECISIONS.md) | Settled intent, working preferences, and revisable proposals |
| [docs/CURRENT_STATE.md](docs/CURRENT_STATE.md) | What exists and what has not been implemented or validated |
| [docs/epics/EPIC-004.md](docs/epics/EPIC-004.md) | Current Tangent, durable activity, SSE and native-notification implementation slice |
| [docs/design/DESIGN_REFERENCE_REVIEW.md](docs/design/DESIGN_REFERENCE_REVIEW.md) | Selected card identity and interaction direction; [implemented UX walkthrough](docs/evidence/epic004-ux.json) |
| [docs/epics/EPIC-001.md](docs/epics/EPIC-001.md) | First PoC outcome, implementation stories, dependencies, and acceptance criteria |
| [docs/RESEARCH.md](docs/RESEARCH.md) | Annotated primary sources and the discoveries worth revisiting |
| [docs/OPEN_QUESTIONS.md](docs/OPEN_QUESTIONS.md) | Questions that may influence a first implementation |
| [docs/EXPERIMENTS.md](docs/EXPERIMENTS.md) | Possible learning exercises; choose and adapt as useful |
| [reference/README.md](reference/README.md) | Optional historical research and visual reference |

The original launch materials and historical visual reference remain available as context. They are not the implemented UI or a fixed implementation specification.

## Freedom to shape the implementation

The user explicitly wants Codex to research, explore, suggest, and make reasoned implementation choices. Languages, libraries, frameworks, storage, repository layout, exact command names, schemas, and sequencing are open. Incoming user resources and the actual local repository may change the recommended approach.

The project name is **Tangent Space**. Earlier names in the reference archive are historical. Domain ownership, visual branding, licensing, public deployment, and production readiness have not been decided by this package.

## Package status

Updated 10 September 2026. This is an experiment against pinned alpha Spaces source, using test accounts and a local Lexicon namespace. The revised human interface and native WebMCP have passed a real local exchange and activity walkthrough. MCP, A2A and the broader EPIC-003 lifecycle remain follow-on work. Production deployment, domain registration and licensing remain undecided.
