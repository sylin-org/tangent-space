# Native AT Protocol capability probe

Disposable S01 evidence for EPIC-001. This is one .NET process using an existing
OAuth SDK. It is not a Tangent service boundary or a finished Koan authentication
provider. Only use the disposable accounts from the local Spaces test network.

## Pins and selection

| Component | Pin |
| --- | --- |
| .NET SDK used to build | 10.0.401 |
| CarpaNet.OAuth and CarpaNet | 1.1.0-alpha.5, locked in `packages.lock.json` |
| Package's NuSpec repository commit | `a24d54bf6a9ce3bbf7c1961d37ab099abe1d1a65` |
| Official Spaces PDS/schema source | `bluesky-social/atproto`, `c1d97bbd5c874ae7c2c26ed84586ef15a000b7ae` |

Both [CarpaNet](https://github.com/drasticactions/carpanet) and
[idunno.Bluesky](https://github.com/blowdart/idunno.Bluesky) appear in the
[official SDK index](https://atproto.com/sdks). NuGet was checked on 9 September
2026: CarpaNet.OAuth latest stable was 1.0.3, latest preview 1.1.0-alpha.5;
idunno.AtProto was 6.0.0. This is a deliberately pinned experimental candidate,
not a claim that an alpha library has production support.

CarpaNet was selected because its OAuth client accepts a configurable identity
resolver, custom HTTP client, state/session stores, and explicit app state. Its
public DPoP handler and key/proof methods also cover custom Spaces requests without
writing PKCE, PAR, OAuth token exchange, ES256 signing, or access-token hashing.
The inspected idunno source has generic XRPC/header support and endpoint options,
but the OAuth flow delegates discovery to Duende with no obvious exposed local
HTTP policy configuration. That candidate was inspected, not built or executed;
the choice does not establish that idunno cannot work.

Relevant exact SDK source:

- [OAuthSession](https://github.com/drasticactions/carpanet/blob/a24d54bf6a9ce3bbf7c1961d37ab099abe1d1a65/src/CarpaNet.OAuth/OAuthSession.cs)
- [DPoPTokenProvider](https://github.com/drasticactions/carpanet/blob/a24d54bf6a9ce3bbf7c1961d37ab099abe1d1a65/src/CarpaNet.OAuth/DPoPTokenProvider.cs)
- [DPoP HTTP handler](https://github.com/drasticactions/carpanet/blob/a24d54bf6a9ce3bbf7c1961d37ab099abe1d1a65/src/CarpaNet.OAuth/ATProtoDPoPAuthHandler.cs)
- [DPoP key/proof implementation](https://github.com/drasticactions/carpanet/blob/a24d54bf6a9ce3bbf7c1961d37ab099abe1d1a65/src/CarpaNet.OAuth/DPoPKeyPair.cs)

## Run

Start the repository's disposable Spaces network first. It provides PLC on
`http://localhost:2582`, PDSes on ports 2583 and 2584, and test Lexicon resolution.
The network also forwards container port 5180 to this host. No public identities,
public Lexicons, DNS changes, TLS exceptions, or third-party PDS access are used.
This probe allowlists the two local PDS origins and their authorization endpoints.

```powershell
dotnet build probes/AtprotoClientProbe/AtprotoClientProbe.csproj -p:RestoreLockedMode=true
dotnet run --project probes/AtprotoClientProbe/AtprotoClientProbe.csproj --no-build
```

The listener is `http://localhost:5180`. Its client ID uses the AT OAuth profile's
special `http://localhost?redirect_uri=...&scope=...` form, with redirect URI
`http://127.0.0.1:5180/oauth/callback`. The scopes in the client ID match the
authorization request. The SDK's unrelated port-based loopback client-ID helper
is not used.

The root-owned driver `probes/scripts/oauth-flow.mjs` exercises the reference
provider's real password, CSRF, consent, and callback APIs. It does not test
browser rendering. Examples:

```powershell
node probes/scripts/oauth-flow.mjs --accounts .local/spaces-network/fixtures.json --account owner --verify-replay
node probes/scripts/oauth-flow.mjs --accounts .local/spaces-network/fixtures.json --account agent --scope 'atproto space:local.tangent.room?authority=*&action=read&action=create&collection=local.tangent.message'
```

Only the site-authority fixture should receive the separate `manage=create` /
`manage=update` grant when exercising space management. Ordinary sign-in defaults
to `atproto`, requiring no email or Bluesky profile. The local administration API
accepts an explicit scope so the probe can test denial as well as success.

## API

All endpoints accept only loopback connections. These are test controls, with no
application login cookie or role system.

| Endpoint | JSON input / result |
| --- | --- |
| `POST /oauth/start` | `{did,pds,scope?}` -> `{authorizationUrl}` |
| `GET /oauth/callback` | Standard OAuth callback query -> verified `{authenticated,did,pds,sdk}` |
| `GET /sessions` | DID/PDS/issuer/scope/expiry/refresh-presence only |
| `POST /sessions/refresh` | `{did,expireForProbe?}` -> whether the access token actually rotated |
| `POST /xrpc` | `{did,nsid,method,parameters?,body?}` -> HTTP status and redacted response |
| `POST /spaces/read` | `{did,space,repo?,nsid?,parameters?}` -> delegation/credential/read stage and result |

`/xrpc` permits only `com.atproto.space.*` and `com.atproto.simplespace.*`, using
the authenticated participant's PDS. `/spaces/read` obtains a delegation token
there, exchanges it on the Space authority's PDS with a fresh SDK-generated
DPoP key, and reads the requested repository's PDS with the bound SpaceCredential.
It supports `getRecord`, `listRecords`, `listRepos`, and `getRepo`; the default is
`getRecord`. Record parameters are ordinary `collection` and `rkey` properties.
Space credentials, delegation tokens, refresh/access tokens, and keys are never
returned. No direct OAuth access token is sent to another participant's PDS.

`expireForProbe:true` changes the local SDK session's expiry to the past, retaining
the real refresh token. It tests the SDK's actual refresh request rather than
calling its no-op path for a still-valid access token.

## Checks and explicit gaps

The integration validates the requested DID against its resolved PDS, callback
`iss` against the stored authorization server, the token subject against the
requested DID, and a fresh resolution of the token subject against the original
PDS and issuer. Invalid issuer callbacks consume their state; the SDK consumes
successful state atomically. Stored sessions are checked again on restore.
These guards compensate for missing callback/subject consistency checks in the
pinned SDK. They should become focused upstream SDK tests and contributions.

This is API-level verification, not browser sign-in. S02 still needs browser
correlation cookies, protected return destinations, account/cookie lifecycle, and
Koan integration. State alone does not bind a login callback to its initiating
browser. The probe does not claim that feature.

State, PKCE verifiers, DPoP private keys, and SDK sessions are protected through
ASP.NET Data Protection. Windows protects the key ring with current-user DPAPI.
The ignored `.state` directory survives restarts under the same user. On other
platforms the key ring has filesystem protection only. Storage is atomically
replaced under an in-process lock and is not multi-instance coordination.

The current Spaces repository format is a concrete SDK gap. Its CAR has two
roots: a signed commitment and a DRISL index, with records after them. The
commitment combines a context signature, HKDF/HMAC integrity, and a BLAKE3-based
LtHash over the record set. The SDK's public-repository reader expects a v3
commit and MST root; it does not verify this format. The `getRepo` probe returns
CAR and parser diagnostics with `verified:false`, never accepts parser success
as proof of repository integrity. A future in-process verifier should reuse
established primitives and match the official implementation's test vectors.
No TypeScript OAuth/token service is required by this transport gap.

- [Current getRepo format](https://github.com/bluesky-social/atproto/blob/c1d97bbd5c874ae7c2c26ed84586ef15a000b7ae/lexicons/com/atproto/space/getRepo.json)
- [Official commitment verification](https://github.com/bluesky-social/atproto/blob/c1d97bbd5c874ae7c2c26ed84586ef15a000b7ae/packages/space/src/repo-commit.ts)
- [SDK public repository parser](https://github.com/drasticactions/carpanet/blob/a24d54bf6a9ce3bbf7c1961d37ab099abe1d1a65/src/CarpaNet/Repo/Repository.cs)

Observed on 9 September 2026: .NET 10 build passed with zero warnings/errors,
lock-file restore succeeded, and the loopback host returned the pinned SDK
identity. The agent on PDS2 then passed real PAR, provider password authentication,
consent, native token exchange, DID verification, and callback replay rejection.
Expiring the local access-token timestamp and calling SDK refresh produced a real
token rotation (`refreshed:true`). A subsequent Spaces delegation request under
only `atproto` was correctly denied with HTTP 403 `ScopeMissingError`.

The first granular Spaces authorization failed with `invalid_scope: Unable to
retrieve space declarations`. The network probe isolated an upstream Node 24
CommonJS import incompatibility in the CAR reader, fixed the import without
removing proof verification, and recreated its disposable fixtures. Granular
authorization then passed. A native `createRecord` and native two-hop
delegation/credential/read round trip both returned HTTP 200 with the same CID.
The PDS reports the third-party record's `validationStatus` as `unknown`; test
Lexicon declaration resolution and record schema validation are distinct checks.

An actual process stop/restart restored the prior agent's protected session and
rotated its real token successfully. That test caught and corrected an overly
strict integration comparison: the SDK stores the PDS root URI with a trailing
slash after refresh, while the DID document uses the equivalent origin without
one. Callback issuer comparison remains strict.

The root-owned `probes/scripts/prove-s01.ps1` records the complete sequence,
including an independent participant's cross-PDS read and outsider denial.
Individual lifecycle observations are retained in this directory's `evidence`.
