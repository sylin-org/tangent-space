# ADR 0010 — Local profile snapshots and background capture

Accepted 12 September 2026.

## Decision

Reading Tangent never waits for upstream profile decoration. The participant profile
service reads a persisted snapshot through a hot memory cache. A missing snapshot
returns the verified local handle and a placeholder immediately; a stale snapshot
remains usable. Both request deduplicated work from one bounded capture queue.

The capture background service resolves the DID/PDS through the existing guarded
atproto transport, fetches the public profile and avatar, stores media atomically,
then saves the snapshot and publishes a change notification. It knows nothing about
browser rendering or SSE. Failures retain the last successful snapshot and delay
another attempt for two minutes. Successful snapshots refresh on access after six
hours; verified sign-in also requests capture. No periodic population scan runs.

The avatar endpoint only serves local media or a non-cacheable placeholder. It can
request capture for an enrolled identity but cannot proxy arbitrary URLs. Real
image URLs include a content hash and are immutable. Snapshots use existing Koan
persistence; images live beneath the existing `/state` bind mount in `profile-media`.
Both survive container replacement and use the existing backup/restore boundary.

The Web consumer subscribes to changes for up to 64 DIDs currently rendered on the
page. An authenticated SSE endpoint replays current snapshots when connecting,
closing the read/subscribe race, and coalesces subsequent changes. It returns only
public AT decoration, never local membership, role information, credentials or
handle-verification claims. The subject need not be signed in. The stream itself
does not initiate arbitrary captures. Names and avatars update in place without
reloading conversations, resetting drafts, or marking anything read.

The verified DID/handle and all permission decisions remain separate from this
rebuildable cache. Deleted avatars are reflected after a successful profile refresh;
previous immutable media is retained in this POC, with no garbage collector yet.

## Sign-in presentation

The primary action is **Log in with Bluesky**, using server-first OAuth so the
provider collects the account selection. Other Atmosphere handles/DIDs remain
available under a disclosure. The Koan contribution preserves required PAR, PKCE,
DPoP, browser correlation and callback DID-to-PDS-to-issuer binding. The fixed
Bluesky entry point is not an arbitrary server URL redirect API.

## Verification

One focused application integration check covers immediate placeholder reads,
persisted snapshot recovery after hot-cache loss, local immutable avatar serving,
and SSE delivery after media is available. The auth contribution has focused PAR
and subject-binding checks. Browser/Docker observations are recorded in CURRENT_STATE.
