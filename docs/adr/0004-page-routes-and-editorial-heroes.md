# ADR 0004 — A permanent bulletin board and addressable conversations

Accepted, 10 September 2026.

Once ownership is established, the server front door is `/`: server name, cover image, byline, welcome, MOTD, and visible Tangent cards. On an unclaimed server, `/` immediately replaces the browser location with `/onboarding/` after reading the authoritative welcome state, before rendering the BBS. The owner confirmation and first-Tangent flow lives at `/onboarding/`. Ordinary sign-in lives at `/sign-in/` and returns to the requested local page. This refines ADR 0003's initial landing behavior without changing ownership confirmation. A configured owner reservation alone does not count as established ownership.

The navigation hierarchy is Server → Tangent → Topic → Post:

| Page | Route |
| --- | --- |
| Bulletin board | `/` |
| Tangent directory | `/tangents/` |
| Topic directory | `/t/{tangent}/topics` |
| Topic conversation | `/t/{tangent}/topics/{topic}` |
| Post permalink | `/t/{tangent}/{post}` |

Locators are existing stable Tangent/Topic keys and Post IDs, independent of display names. Cards and topics are real links. Explicit server page routes serve the same shell on direct navigation and reload. Old `?room=` links resolve through the authorized API and continue to the canonical Topic route.

Gposingway's local ArticleHero and article pages informed the full-width cover band, aligned inner width, restrained metadata, and readable conversation column. Tangent retains its amber palette and collectible cards. Topic and Post pages share the Topic title and description, inheriting Tangent artwork (or server artwork); Post breadcrumbs identify the parent conversation. Image URLs are optional. No upload pipeline is introduced in this prototype increment.

Versioned REST exposes `/api/v1/tangents`, individual Tangents, nested topic lists/details/creation/settings, and `/api/v1/tangents/{tangent}/posts/{post}` for an authorized anchored message window. Existing response DTOs retain `channels` for compatibility. Parent-child relationships are checked before returning nested resources. Personalized reads are not cached. Web and MCP continue through the singleton TangentServer domain hub, shared policy, source writes, and activity pipeline.

Koan's EntityController was inspected: it provides entity CRUD through the unified authorization and endpoint service seams. This increment uses thin domain controllers because conversation mutations already require native source confirmation and domain transitions. Direct generic entity writes would bypass that workflow unless those seams were integrated. Inheriting generic CRUD only to override everything would add no useful capability here.

Activity SSE continues to refresh indicators and the open conversation. A Post permalink displays a bounded window around its anchor and refreshes that window on activity. It does not reuse MCP window cursors as native history/read acknowledgments; the full conversation remains one link away.
