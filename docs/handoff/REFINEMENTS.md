# Refinement backlog — session handoff

Enumerated 11 September 2026 after shipping ADRs 0005–0008 end to end (experience API,
local Rust connector, standalone storage, atomic upserts, facets/profiles). Baseline: suite
**351/351**, live server running standalone storage at `http://127.0.0.1:5220`, working
human/agent exchange verified in The Lobby / Lounge (`@leo.sylin.org` ↔ `@tangent-agent.test2`
via the connector, artifacts under `.local/live-exchange/`). Items are ordered by expected
value; each names the surfaces it touches.

## 1. Connector mints facets (agent-side picker parity)
The connector's `CreatePost` tool vocabulary has no `facets` argument; agent posts are plain
text the parser resolves. Add the argument (schema + decode + journaled body), and optionally
render facets in the connector's expanded views using the experience API's `resolved` map.
Surfaces: `src/server/mcp/src/application/operations.rs`, `hub.rs`, `src/adapters/mcp.rs`.

## 2. Facet recompute on edit (replacing the drop)
Edits currently drop facets (honest but lossy — ADR 0008 records recompute as the refinement).
On a text-changing edit, re-run detection over the new text (mention facets from the
mentionables resolution at authoring time cannot be reconstructed, but handle/DID/group/tag
tokens can) and store fresh ranges. Surfaces: `ConversationService.PostChanges.cs`, tests.

## 3. Composer-simplicity cleanup (the "EntityController" direction)
Composer visibility derives purely from the topic response; the Spaces-only readiness probe,
reconnect link and consent copy move behind the `spaceState === 'Ready'` branch so the Local
path has zero bespoke state (the bug class that produced the disappearing composer).
Later: adopt Koan `EntityAccess<T>` + `GET /new` verb projection for read surfaces
(mentionables already has the right shape). Surfaces: `rooms.js`, optionally controllers.

## 4. Search over facets
`author=did`, `tag=value`, `mention=did` queries indexed off `Message.Facets`; powers profile
"all posts" pagination (`morePosts` exists) and tag/topic navigation. Surfaces: conversation
queries, profile page, `#tag` rendering links.

## 5. Group semantics refinement
`@moderators` currently resolves to the same holders as `@admins` (owner + Tangent admins).
Distinguish per-Topic moderators (Room managers) as their own group; expose distinct counts
in the popup. Consider `@members` including readers. Surfaces: `ExperienceDigest.HoldsAnyRole`,
`ExperienceService.Mentionables.MembersOf`, tests.

## 6. Roster panel and presence
A "who's here" panel reusing the mentionables endpoint (participants + classifications +
badges). Presence needs a presence *concept* (SSE exists; do not fake it from connections);
typing indicators ride the same decision. Surfaces: new UI panel, optional server presence.

## 7. E10 — modest-model walkthrough with a named host
Still open from IMPLEMENT_LOCAL_MCP: attach the connector to a real agent host (Claude Code
channels noted incompatible with 2026-07-28 negotiation; verify the installed host), run the
select → arrive → read → respond → resume loop, record errors/sizes/calls. Also: second
companion + second server live (E02/E12 live variants).

## 8. Spaces record schema v2
Carry facets in CBOR records (lexicon bump: `MessageContent.FromCbor` is strict 3-or-4
fields); stamp facets at `Accept` for source-authored history so Spaces topics render
identically. Deliberate change, not silent. Surfaces: `MessageContent`, `Acceptance.cs`.

## 9. Automated service-DID lifecycle (Spaces mode)
The manual `tangent-authority.test` connection is the stand-in for the recorded "verified
service DID" idea (ADR 0001 direction): automate acquisition/renewal of the server's own
identity when Spaces mode is enabled. Large; needs its own ADR.

## 10. Connector polish
`check --wait` using the experience `wait` endpoint (long-poll instead of fixed interval);
an `inspect` CLI for operator visibility into state/attention/pending writes; background
check already reconciles receipts.

## 11. Known v1 characteristics (not bugs, worth measuring before scaling)
Digest assembly scans per response; mentionables queries per keystroke (30 s cache client
side); renderer decorates ranges without verifying segment text (guarded by composer-side
verification and the edit-drop rule).
