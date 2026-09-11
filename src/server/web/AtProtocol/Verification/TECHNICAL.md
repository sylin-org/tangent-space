# Native Spaces verification integration

This bounded integration moves the S01 verification experiment into one in-process
component. `SpacesVerifier` resolves the expected author's DID through Koan's
guarded AT transport, selects the document-controlled `#atproto` signing key, and
verifies the CAR against the expected Space and author. A reusable resolved key
also verifies compact low-S ECDSA signatures for the managing-app JWT boundary;
its caller remains responsible for the JWT algorithm and claims.

The format is pinned to bluesky-social/atproto
`c1d97bbd5c874ae7c2c26ed84586ef15a000b7ae`, including its two-root CAR,
`atproto-space-v1` context, HKDF-Expand, HMAC, and BLAKE3 LtHash composition.
CarpaNet 1.1.0-alpha.5 supplies CAR/CID utilities;
BouncyCastle.Cryptography 2.7.0 supplies maintained BLAKE3 and ECDSA primitives;
System.Formats.Cbor 10.0.5 and .NET supply CBOR, SHA-256, HKDF, and HMAC.
These dependencies and the official atproto source are MIT licensed. CarpaNet's
base58 helpers are private; a bounded base58btc conversion decodes the 35-byte
Multikey envelope or legacy 33/65-byte point, and BouncyCastle validates its curve
point. The legacy `EcdsaSecp256k1VerificationKey2019` and
`EcdsaSecp256r1VerificationKey2019` types follow the pinned official
`packages/identity/src/did/atproto-data.ts`; the live test PLC emits this format.

**Proof scope:** the signature covers the Space/author/revision/IKM context, while
the record digest is protected by a symmetric MAC derived from that IKM. This is
the upstream format's deliberate non-transferability: a recipient who has the
CAR can recompute its digest and MAC. A CAR is not independent proof that an
author wrote its records. Ingestion must bind the bytes to an authenticated
expected author/PDS response; do not accept an arbitrary uploader's CAR as
transferable authorship evidence.

The resolver uses the **current** DID document only. It does not validate key
history, accept previous signing keys, detect rollback, establish freshness, or
decide membership. A valid signature authenticates the context under that key,
not authorization or record-schema validity. Callers must supply their expected
Space and author from authenticated context and perform those other checks.

The first implementation accepts P-256 and secp256k1 Multikeys and those exact
legacy 2019 types, compact low-S
signatures, bounded CARs (8 MiB / 1,024 indexed records), and finite, canonical
DAG-CBOR with at most 64 nesting levels. Blob links and streaming/incremental
synchronization are outside this integration. Malformed, unsupported, incomplete,
misbound, and tampered input must fail before records are returned.

Tests reference the official-generator probe fixtures as test content; production
has no probe dependency. They retain both curves, real local-PDS output, wrong
context/key/author, altered commitments, additions/deletions, and framing cases.
Integration adds DID key selection/decoding and signature tests, plus duplicate
record-value coverage against the pinned official serializer's CAR behavior.
That serializer writes one block per indexed path even when CIDs repeat; a
deduplicated CAR is incomplete under this version and must be rejected.
