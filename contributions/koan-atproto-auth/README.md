# Native AT Protocol authentication contribution

This is an unpublished, reviewable Koan source contribution against
`e07a84cc3f71a0867f1122b03b723cc80727e772` from
<https://github.com/sylin-org/koan-framework>. No upstream commit, PR, or package
publication was performed. The live sibling Koan checkout was not changed.

The Docker follow-up added isolated
development PLC/socket routing and sanitized challenge failures. Public handles
continue to use the public PLC while explicitly mapped fixtures retain their
local directory. The callback permission correction now rejects incomplete
requested grants before replacing a working session and explains missing access
on a sanitized HTTP 400 page. Current verification includes 30 connector tests and real
container sign-in/restart checks; `verification-initial.json` retains the earlier
PoC receipt. See [Docker evidence](../../docs/evidence/docker.json).

`koan-atproto-auth.patch` contains exactly the 49 files listed in `files.txt`.
`manifest.json` records the base, patch SHA-256, and Git blob for every resulting
file. The package includes:

- The protocol-neutral `IAuthProtocol` seam, compiled provider validation and
  ordinary ASP.NET custom-scheme activation, with existing OAuth/OIDC regression
  coverage. The shared auth controller remains unchanged.
- A native AT connector: browser correlation, PKCE/PAR/DPoP through CarpaNet,
  verified DID/issuer/PDS binding, staged protected sessions, guarded HTTP/DNS,
  refresh, checked revocation, and fixed trusted metadata/callback URLs.
- A generic `AddKoan()` Web/Identity/SQLite sample, module docs/exploration card,
  focused tests and redacted local-provider lifecycle receipts.

The patch excludes Tangent domain code, disposable-network code, fixture
passwords, session files, cookies, keys, build output and unrelated Koan changes.
The separate native Spaces verifier is outside this contribution.

## Apply and check

Use Git and .NET SDK 10; validation here used 10.0.401. The verifier creates a new
checkout, verifies the patch hash and base, runs `git apply --check`, applies the
patch and compares all 49 resulting file blobs with the manifest. It refuses an
existing destination and never resets or deletes a checkout.

```powershell
./contributions/koan-atproto-auth/verify.ps1 -Destination C:/work/koan-atproto-review -RunTests
```

An existing local Git repository can supply the base objects without a network
clone; it remains read-only:

```powershell
./contributions/koan-atproto-auth/verify.ps1 -Destination C:/work/koan-atproto-review -SourceRepository C:/work/koan-source -RunTests
```

For manual application in a separate clean checkout:

```powershell
git checkout --detach e07a84cc3f71a0867f1122b03b723cc80727e772
git apply --check C:/path/to/koan-atproto-auth.patch
git apply C:/path/to/koan-atproto-auth.patch
git diff --check
dotnet build samples/AtprotoIdentity/AtprotoIdentity.csproj
dotnet test tests/Koan.Web.Auth.Atproto.Tests/Koan.Web.Auth.Atproto.Tests.csproj
dotnet test tests/Koan.Web.Auth.Tests/Koan.Web.Auth.Tests.csproj
dotnet test tests/Suites/Auth/Koan.Web.Auth.Integration.Tests/Koan.Web.Auth.Integration.Tests.csproj
```

## Evidence and limits

`verification.json` distinguishes checks executed on the fresh patch application
from source-checkout evidence. The sample's module `evidence/` directory records
real local-provider browser authentication, wrong issuer/state/missing correlation
denial, replay rejection, and cookie survival across an actual process restart.
The provider was the official experimental permissioned-data branch at
`c1d97bbd5c874ae7c2c26ed84586ef15a000b7ae`, with disposable local accounts.

The opt-in lifecycle test independently restored the sample's protected session,
rotated real access and refresh tokens, authenticated `getSession`, performed
checked upstream revocation, and verified that the retained revoked refresh token
failed with `invalid_grant`. Explicit provider password authentication/consent and
successful restored refresh after reauthorization also passed. Only local expiry
metadata was adjusted to trigger a real refresh; tokens were never fabricated.
Follow `samples/AtprotoIdentity/README.md` after applying the patch to reproduce
this proof. Default tests deliberately skip that grant-revoking test.

This is a bounded public-client integration, not production certification. It
uses CarpaNet.OAuth/CarpaNet 1.1.0-alpha.5 (NuSpec source
`a24d54bf6a9ce3bbf7c1961d37ab099abe1d1a65`) and DnsClient 1.8.0. Sessions are
protected and durable for one process; operators must persist and protect the
ASP.NET Data Protection key ring. Distributed session coordination and the
confidential-client profile are not implemented. No A2A/MCP/WebMCP or Spaces
repository-verification claim is made by this auth package.

Declared scopes are an upper bound; a first ordinary sign-in requests only
`atproto`. If that DID already has a validated broader durable grant for the
same PDS, issuer and client within the current ceiling, ordinary sign-in requests
the established grant again so an identity refresh cannot silently discard a
working room connection.
Trusted server code can request additional exact declared strings. The connector
requires every exact requested scope in callback grants, rejects token scope
expansion, and does not interpret `include:` permission sets
or experimental Spaces defaults. Concrete, provider-canonical resource scopes
must be configured explicitly; in the pinned Spaces provider, spell out the
authority DID and collections to avoid issuer-time `self`/Lexicon expansion.
HTTP loopback origins and fixture handle maps require the DevelopmentOnly gate;
there is no production exception switch.

A provider returning only `atproto` for an explicitly requested broader connection
now fails before committing the staged grant. The old session survives that failure,
including reopening its protected store. Ordinary sign-in and restored sessions
remain subject to the declared ceiling without requiring all optional client scopes.
No automatic revocation of a rejected callback grant is attempted, because provider
revocation could affect a pre-existing authorization.

SDK contribution candidates remain clearly separated: callback issuer/subject
checks, mandatory PAR behavior, and checked revocation. This adapter retains its
guards until corresponding upstream behavior is verified, regardless of method
names or future SDK version claims.
