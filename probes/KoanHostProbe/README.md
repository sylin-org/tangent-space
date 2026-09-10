# Koan host dependency probe

This disposable S01 probe establishes the local Koan floor for Tangent Space's eventual single .NET host. It is the standard `koan-web` template: one `Todo : Entity<Todo>`, one `EntityController<Todo>`, and one `AddKoan()` call. It adds no repository, service layer, product UI, authentication policy, or separate production service.

## Exact source

Generated from public `Sylin.Koan.Templates` **1.0.21**. Its downloaded package SHA-256 is `4D611254CFD5E700A9F334FAD1695B6F2D553C99500A6E4907CDF2D62663D995`.

The generated `1.*` references were replaced with exact versions:

| Direct package | Version | Repository commit in the installed NuGet manifest |
| --- | --- | --- |
| `Sylin.Koan.App` | `1.0.38` | `e07a84cc3f71a0867f1122b03b723cc80727e772` |
| `Sylin.Koan.Data.Connector.Sqlite` | `1.0.46` | `e07a84cc3f71a0867f1122b03b723cc80727e772` |

`packages.lock.json` pins the complete dependency resolution and content hashes. `koan.lock.json` records the composed modules and direct package intent; its `1.0` module versions describe the compatibility train, not exact NuGet patches. No source-project references or changes to Koan were needed. The scaffold's unused welcome page and Claude-specific settings were removed.

Template generation used an isolated template-engine hive beneath ignored `.work/`; it did not install a template into the user's default hive. The hive must be an absolute path. This template has no automatic restore action and does not accept `--no-restore`.

## Reproduce on Windows

Requires the repository's pinned .NET SDK, PowerShell, NuGet feed access on the first restore, and a free loopback port 5210. From this directory:

```powershell
dotnet restore --locked-mode
dotnet build --configuration Release --no-restore
./prove.ps1
```

An explicit executable path is supported, for example `./prove.ps1 -Dotnet C:/Tools/DotNet/dotnet.exe`. `-Port` selects another loopback port. The script starts only hidden processes, refuses an occupied port, uses a fresh `.work/runs/<UTC-run-id>/probe.sqlite`, and stops every host it starts. It does not delete prior runs or product data.

The successful build used SDK **10.0.401**, produced **0 warnings and 0 errors**, and ran against .NET 10. Each proof run exercises:

1. Liveness and readiness return HTTP 200, with healthy readiness content.
2. The standard Entity controller writes and reads the supplied identity and title.
3. Runtime facts select SQLite; the configured SQLite file exists.
4. The build-generated Koan lock records the SQLite connector and direct web bundle.
5. After stopping the process, a new process reads the unchanged Entity from the same file.
6. A third start with an explicit, unreferenced adapter fails and names that rejected intent.

`evidence/2026-09-09/` contains the passing receipt, health response, selected runtime facts, matching before/after Entity reads, package provenance, and the corrective failure excerpt. Full logs and the database remain in ignored `.work/runs/`.

## Boundary of the result

This proves a package-only, local SQLite host and framework inspection path. It does not prove product identity, room authorization, atproto interoperability, notification replay, distributed operation, backup/recovery, or production exposure. The framework's in-process Communication floor and JSON connector are present transitively; runtime facts show SQLite is the selected default data provider. They add no external server requirement to this probe.
