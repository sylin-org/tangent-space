# EPIC-001 integration probes

These are disposable S01 experiments. Tangent's application target is a single DDD monolith; the probes do not define its production projects or processes.

[S01 passed on 9 September 2026](evidence/S01-2026-09-09.md). Run `./probes/scripts/prove-s01.ps1` after starting the two protocol probes to reproduce the combined native checks.

| Probe | Question it answers |
| --- | --- |
| [KoanHostProbe](KoanHostProbe/README.md) | Do pinned Koan packages compose an ordinary host and preserve SQLite Entity data across restart? |
| [spaces-network](spaces-network/README.md) | Do two real alpha PDS hosts enforce the intended Spaces admission and credential boundaries? |
| [AtprotoClientProbe](AtprotoClientProbe/README.md) | Can a native .NET client authenticate through real AT OAuth and perform the required Spaces operations? |
| [OAuth driver](scripts/oauth-flow.mjs) | Does the real issuer authenticate and consent, followed by a native callback that verifies the intended account? |

The Spaces baseline uses its own disposable credentials and is distinct from the OAuth proof. Passing the baseline alone does not close S01. Test accounts use the local PLC directory and test Lexicon resolution, without registering public identities or namespaces.

## OAuth driver

Start the protocol network and the native probe according to their own instructions. Then, from this repository root:

```powershell
node probes/scripts/oauth-flow.mjs --accounts .local/spaces-network/fixtures.json --account owner --verify-replay
node probes/scripts/oauth-flow.mjs --accounts .local/spaces-network/fixtures.json --account agent
node probes/scripts/oauth-flow.mjs --accounts .local/spaces-network/fixtures.json --account outsider --negative issuer
node probes/scripts/oauth-flow.mjs --accounts .local/spaces-network/fixtures.json --account outsider --negative state
```

The driver uses only loopback URLs and reads passwords from the ignored fixture file. It follows the pinned reference issuer's authorization-page API, including CSRF, device binding, actual password authentication, and explicit consent. It keeps those transient credentials in memory and prints only redacted results. It is an HTTP integration test, not proof of browser rendering or an automated production login mechanism.

`--scope` supplies a specific test grant when the probe needs Spaces access. Ordinary authentication and authority-account administration should be exercised with different grants. No fixture password, token, or key belongs in a checked-in result.
