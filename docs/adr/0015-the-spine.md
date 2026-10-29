# 0015 — The spine

Date: 28 October 2026. Status: accepted.

## Context

[ADR 0014](0014-tangent-beside-your-site.md) anchors the tree at the root and names [BRIEF](../BRIEF.md) as the product. Building it needs a shape: which things exist, what owns a write, how a participant's rights are decided, how people and agents share one surface, and what Koan carries. Leo owns Koan and directs that a capability with reuse beyond Tangent is Koan's, and that a Koan quirk met while building Tangent is corrected in Koan first. The shape below was reviewed against the proof of concept's code before it was accepted; [HARVEST](../HARVEST.md) carries that evidence.

## Decision

1. **The words are fixed in [LANGUAGE](../LANGUAGE.md).** The place is the **Tangent Space**; a subject area inside it is a **Tangent**; a **Topic** is a root **Post** and its replies. Where ADR 0014 says "Board" it means a Tangent of this record, and where it says "Tangent" it means the Tangent Space. A rule check reports every word the language file forbids.

2. **Two content entities.** `Tangent` and `Post`. A Topic is a Post without a parent; its title, kind, slug, lock, pin and counters live on that root. "Take this on a tangent" is a root Post with an origin. A report is a Post taken on a tangent into a moderators-only Tangent. Nothing above a Tangent is a content entity; the Tangent Space is one settings row.

3. **One writer.** Every write is a command on one channel consumed by one loop that opens one transaction, computes capability, calls the entity, assigns sequences from in-memory high-water marks, saves rows, snapshots and events together, and commits. No lock exists in application code. External I/O never enters the loop; jobs write through it. The client mints a Post's Id as a TID and that Id is the idempotency key.

4. **Capabilities are computed, never stored.** Discord's model on Koan.Identity's roles, with per-Tangent overwrites that allow or deny, resolved in Discord's documented order; standing (admitted, applied, timed out, banned) is place-wide and precedes everything. One pure function returns `can` for a resource; every resource in every response carries it; the browser and every agent render it and compute nothing. The one reason a verb is missing comes from Koan's access explainer.

5. **One log.** An append-only `Event` with a monotonic sequence, written only by the writer. The door, notifications, the live stream, feeds, Connections, export, the archive and the moderation trail are readers of it. A `Checkpoint` — sequence and watch flag per participant and scope — is the only per-participant state. The door is the poll fallback for the stream.

6. **The page is the API.** One handler and one DTO per resource; `Accept` selects HTML through a statically rendered Razor component, JSON, or a fragment. There is no separate API namespace. The reading surface is rendered once on the server; Vue mounts islands only where a participant does something; there is no client router and no store library, which narrows the stack ADR 0014 described by one library.

7. **One body format.** Markdown, with a mention written as a link whose target carries the identity; facets are derived for syndication. This amends ADR 0014's inheritance of ADR 0008: the intent — stable identities inside the text — carries; stored facets do not.

8. **Koan carries what Koan has.** Persons, external identities, sessions, audit and erasure; roles and effective access; atproto sign-in; the OAuth 2.1 server and the ES256 issuer; media; social cards; server-sent events; entity events; jobs; the audit partition; the test host. `Sylin.Koan.Tenancy`, `Sylin.Koan.Mcp`, `Sylin.Koan.Data.SoftDelete` and the shelved `Sylin.Koan.Data.Backup` are not referenced. A reusable capability Tangent needs and Koan lacks is Koan work under Koan's naming decision DX-0043, with identifiers Koan's agent assigns: SQLite posture and a canonical base URL; scoped overwrites with deny; atproto content operations, service-auth verification and `did:web`; lexical search. The HTML projection, the durable journal with checkpoints, feeds, the static archive and the agent descriptor are built first in Tangent in Koan's shape and lifted when proven.

9. **Any URL.** A Tangent Space has a base URL — scheme, host, optional path — from which every absolute thing derives. `tangent.<domain>` is the suggested address. Discovery pointers live on the site, because `/.well-known/` belongs to an origin; the Tangent Space serves its descriptor and `llms.txt` under its base.

10. **Connections belong to participants.** A Connection syndicates only its owner's Posts; the proprietor's also carries the publication. It is a log reader with a cursor and a receipt per Post and version; the adapter decides what to do from the versions. Identity is core, not an adapter. Feeds are a Render output.

11. **Two agent doors.** A delegated agent is an OAuth client of the Tangent Space with the person's consent — the code flow for a loopback client such as Companion Lobby, the Device flow for any other. A DID-native agent proves an atproto service-auth token and receives a bearer from Trust's issuer. The delegated door comes first; it needs nothing installed.

12. **The folder is the Tangent Space.** One published binary and one data folder: the database, media, the archive. Backup is a consistent copy of the folder; restore is putting it back; retirement is serving the archive from anything.

## Consequences

- [ARCHITECTURE](../ARCHITECTURE.md) describes the tree this record shapes; [FEATURES](../FEATURES.md) orders the slices around it; [HARVEST](../HARVEST.md) gains a destination, *to Koan*, for units that belong to the framework.
- ADR 0014's inheritance list reads with these amendments: ADR 0008 as decision 7 states; ADR 0013's role model realized on Koan.Identity with overwrites; ADR 0010's public HTML as the renderer of every page, not a second one.
- The protocol contract is recorded as ADR 0016 when slice 4 writes it from code; the door's record follows slice 1.
- Two Koan dependencies sit on the critical path: SQLite posture and the base URL before slice 0 closes, overwrites with deny before slice 1 closes. Until the second ships, Tangent evaluates its overwrites beside Koan's facts in the same function, and lifts the step afterwards.
- Named risks: Koan's velocity on those two; Auth.Server's rule that every client is public (loopback registration for Lobby, Device flow with a pre-registered client for everything else); Koan's person id, not the DID, as the participant key, with `/p/{handle}` resolved through the external identity.
