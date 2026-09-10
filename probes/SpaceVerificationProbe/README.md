# Native Spaces CAR verification probe

**Result:** an in-process .NET verifier is feasible without a TypeScript runtime
helper or new cryptographic primitives. This probe passed 31 independently
generated cases, including a CAR downloaded from the running test PDS. It is a
small experimental adapter for the pinned alpha format, not a published SDK or
a completed synchronization implementation.

## Run

From the Tangent repository root:

```powershell
dotnet restore probes/SpaceVerificationProbe --locked-mode
dotnet run --project probes/SpaceVerificationProbe -- probes/SpaceVerificationProbe/fixtures probes/SpaceVerificationProbe/evidence.json
```

The checked-in fixtures are sufficient; Docker, a PDS, and Node are unnecessary
for verification. The recorded run used .NET `10.0.12` / SDK `10.0.401` on Windows.
`evidence.json` records all outcomes and the independent primitive comparisons.

To regenerate disposable vectors through the official TypeScript implementation
and capture the current test PDS repo, start the Spaces network and run:

```powershell
./probes/SpaceVerificationProbe/generate-fixtures.ps1
```

Regeneration changes random signatures/keys and the live repository snapshot.
It reads the agent's own repo using a disposable legacy session purely to obtain
verification bytes. It does not modify rooms, records, or memberships. No
passwords, access tokens, or private keys appear in the generated fixture set.

## Minimal integration boundary

```csharp
VerifiedSpaceRepo Verify(
    byte[] car,
    string expectedSpace,
    string expectedAuthor,
    ResolvedAuthorKey independentlyResolvedAuthorKey,
    bool expectValues = true);
```

The result contains verified record bytes, source CIDs, collection/record keys,
revision, and whether the full values were required. The caller supplies the
expected room and author, resolves that author's current repository signing
key independently, and applies Tangent message schema and participation policy
after verification. Keys supplied by the CAR itself must not be trusted.

For this experiment, the official DID resolver obtained the live author's key
from the local PLC; the fixture manifest records the resulting public key.
Native DID resolution/multikey decoding must be connected at the application
boundary before claiming an entirely native live ingestion path. This verifier
accepts either compressed or uncompressed P-256/secp256k1 public-key bytes.

## What is verified

The pinned Spaces CAR contains two roots in order: the signed commit, then an
index mapping `collection/rkey` to CID. Records follow in the index's canonical
map order. This differs from public AT repositories and their MST commits.

The verifier checks bounded framing, explicit CAR version/root shape, CID hashes
for every block, commit version and field shape, context-bound signature/MAC,
the index's aggregate hash, record order, and full record completeness. It uses
canonical string-key ordering and a constrained DAG-CBOR reader. The optional
index-only mode is explicit; a full read rejects omitted records.

Protocol details confirmed against independent upstream vectors:

- Context prefix `atproto-space-v1`, with big-endian uint16 lengths for space,
  author, revision, and IKM.
- Compact 64-byte, low-S ECDSA signatures over SHA-256 of that context.
- **HKDF-Expand only**, using the commit IKM as its PRK and context as info,
  followed by HMAC-SHA256 over the aggregate digest. Extract-and-expand would
  produce the wrong result.
- Each `collection/rkey/cid` expands through BLAKE3 XOF to 2048 bytes. Its 1024
  little-endian uint16 lanes add modulo 65536; SHA-256 digests the final state.

The .NET code supplies protocol framing, validation, and lane aggregation.
Cryptographic hash/XOF, HKDF, HMAC, curve arithmetic, and signature verification
come from established libraries.

## Independent evidence

`generate-fixtures.mjs` creates CARs using the pinned official `serializeRepo`
and obtains each expected outcome from official `verifyRepoCarFull`. The .NET
implementation is not used to produce its expected results.

For each curve, cases include valid/empty/index-only repos; wrong space, author,
and signing key; invalid MAC/signature; a removed or added index entry; omitted,
extra, truncated, or tampered record blocks; and unsupported commit version.
An additional case contains the actual PDS's three-record Workshop snapshot.
The full 2048-byte lane state, final digest, and encoded context also match the
official implementation byte for byte. All 31 cases passed.

The upstream Node image requires the recorded varint ESM import repair described
in `../spaces-network/README.md`. That fix affects fixture generation only; the
native verifier has no dependency on the patched module or on Node.

## Pins, licenses, and limits

| Dependency | Pin | Use | License |
| --- | --- | --- | --- |
| CarpaNet | `1.1.0-alpha.5` | CAR parsing and CID representation | MIT |
| System.Formats.Cbor | `10.0.5` | Structured CBOR decoding | MIT |
| BouncyCastle.Cryptography | `2.7.0` | BLAKE3 XOF and ECDSA curve verification | MIT |
| .NET cryptography | .NET 10 | SHA-256, HKDF-Expand, HMAC, constant-time comparison | MIT |
| Official Spaces format/oracle | `c1d97bbd5c874ae7c2c26ed84586ef15a000b7ae` | Protocol definition and test oracle | MIT option of dual license |

`packages.lock.json` retains package content hashes. Source adaptation attribution
is in `THIRD-PARTY-NOTICES.md`. Bouncy Castle's maintained official package is
[BouncyCastle.Cryptography](https://www.nuget.org/packages/BouncyCastle.Cryptography/2.7.0).

This probe buffers at most 8 MiB and 1024 records, with a 64-level CBOR nesting
limit. It supports DAG-CBOR/SHA-256 CIDs, the two currently supported author
curves, and the tested alpha's commit version 1. The constrained record reader
currently rejects raw-codec CID links, so records with blob references need
additional format coverage before use. It does not implement blob verification,
incremental sync, rollback/freshness detection, DID key-history resolution,
schema enforcement, moderation, or membership policy. A valid old snapshot
still verifies; the caller must keep an accepted revision/read boundary.

CarpaNet's CAR reader itself performs parsing, not block-hash or signature
verification, and does not cap declared allocation sizes. The probe checks frame
lengths against available bytes before passing data to it. A reusable SDK
extension should incorporate these bounds and support cancellable streaming;
the present implementation should receive review before handling production
inputs. No broader production-readiness claim follows from these vectors.

The protocol deliberately separates the context signature from a reader-known
MAC. These are its permissioned-repository verification semantics, not a claim
of independently transferable proof of message authorship.

## Primary format references

- [Official consumer](https://github.com/bluesky-social/atproto/blob/c1d97bbd5c874ae7c2c26ed84586ef15a000b7ae/packages/space/src/sync/consumer.ts)
- [Commit context, signature, and MAC](https://github.com/bluesky-social/atproto/blob/c1d97bbd5c874ae7c2c26ed84586ef15a000b7ae/packages/space/src/repo-commit.ts)
- [Aggregate set hash](https://github.com/bluesky-social/atproto/blob/c1d97bbd5c874ae7c2c26ed84586ef15a000b7ae/packages/space/src/lthash.ts)
- [HKDF mode](https://github.com/bluesky-social/atproto/blob/c1d97bbd5c874ae7c2c26ed84586ef15a000b7ae/packages/crypto/src/hmac.ts)
- [CarpaNet CAR reader](https://github.com/drasticactions/CarpaNet/blob/2bb83297a4198acf3e7f862673eed3eec061688e/src/CarpaNet/Repo/CarReader.cs)
