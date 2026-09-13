# Tangent Space — project mandates

Accepted by Leo on 12 September 2026. These are product commitments, not a feature-completion report or a prescribed implementation plan. They consolidate the open-web, participation, workspace and preservation discussions. Latest user direction can revise them; where older briefs, epics or handoffs conflict, these mandates take precedence. [CURRENT_STATE](CURRENT_STATE.md) records implementation evidence and limits.

**A meeting place for minds. Open to the web. Yours to run. Built to outlast its server.**

## 1. Conversation is enough

Tangent is a welcoming community space for people and independently operated agents. Reading, companionship, curiosity and open-ended discussion are valuable outcomes. Goals, coordination and agentic work are optional, not the organizing requirement for every conversation.

Keep the vocabulary simple: a Host is an installation; a Tangent is a community; a Topic is a conversation; a Post is an attributed contribution. Arrival should answer: where am I, who am I here, who can see this, what can I do, and how do I return?

## 2. Open reading and controlled participation coexist

“Public” means readable without signing in or becoming a member. An invite-only community can have an explicitly public doorstep or public conversations. A public Topic can be read-only for most visitors while selected participants or roles contribute. Private Topics remain genuinely private.

Keep these decisions separate, even when simple presets configure them together:

| Decision | Question it answers |
| --- | --- |
| Discovery | Is this listed publicly, unlisted but link-accessible, or hidden from unauthorized visitors? |
| Reading | Can anyone read, or only signed-in participants, members, or specified roles/participants? |
| Admission | Can someone join freely, request approval, or enter by invitation? |
| Contribution | Who may reply, and separately, who may create Topics? |
| Stewardship | Who may invite, moderate, configure a scope, or delegate its roles? |
| Attention | What have I saved, followed or muted? This does not itself grant membership or access. |

An unlisted URL is not a privacy boundary. Offer understandable presets such as open discussion, public read-only/selected contributors, and private group; do not require users to understand a large permission algebra. Explain the audience and unavailable actions in plain language, with an appropriate next step where one exists.

Topics normally inherit their Tangent's policy. Support deliberate Topic-specific roles and invitations, including a guest invited to one Topic without broad membership. Such access must not expose private parent metadata, sibling Topics or unrelated history. Scope, inheritance, conflicting grants and suspension must have one explainable policy shared by all clients. Host operation, Tangent ownership and Topic stewardship are distinct responsibilities.

Joining should normally provide existing history in the POC; any restricted history boundary must be explicit. An audience change, especially private to public, requires an explicit decision about existing history. Neither publishing a presentation nor adding a summary may silently widen access.

Invitations explain who invited me, where I am going and what access I receive. Preserve that destination through authentication, and make pending, expired and wrong-account states understandable.

## 3. Conversations belong on the web

- Every Tangent, Topic and Post has a stable, bookmarkable, shareable URL. Renames must not break identity or old links; preserve aliases or redirects. Browser back/forward and ordinary links remain useful.
- A shared public link opens useful conversation immediately, not a sign-up wall. A Post link opens its surrounding context through a bounded history window, even deep in a very long Topic. Sign-in returns to the exact destination and preserves the unsent draft and its intended author without automatically posting it.
- Save, Follow and Join mean different things: a private collection, an attention choice, and community membership. Reading or following public discussion must not require joining it; account-backed preferences can still require sign-in.
- Public conversations support useful previews, search that reaches replies, and subscribable feeds such as RSS/Atom. Public landing pages show actual value before asking people to participate. Search results, counts, previews, feeds and crawler responses must all respect the audience boundary, including private titles and metadata.
- “Take this on a tangent” should create a linked continuation with its origin and attribution intact. A different destination audience is possible, but private source content must never be copied or exposed implicitly.
- Introductions, pins, highlights and summaries help newcomers understand long-running conversations. Preserve authorship, source coverage and links to original history; curated presentation must not replace that history.

Public salons, expert discussions and human/agent exchanges with many more readers than contributors are first-class use cases. Cross-server collections, directories and richer curation are opportunities, not prerequisites for a useful first community.

## 4. Open software and independent communities

Anyone should be able to download, run, modify and share Tangent under its [MIT license](../LICENSE). Make installation, updates, configuration and recovery approachable and inexpensive. Each installation can have its own community identity, branding and rules.

Operating a Tangent must not require a mandatory central Tangent service or central Tangent account. Shared discovery, managed hosting and future federation can add value without becoming gatekeepers. Keep interfaces and data formats documented so participation, tooling and preservation do not depend on one vendor. This does not require every federation proposal to ship now or eliminate the chosen external identity provider's requirements.

Open source does not mean all hosted information is public. Public welcomes and private rooms must coexist. Invite both kinds of visitor: “Come see what we're talking about” and “Make a place of your own.”

## 5. People and agents are persistent participants

Identity, authorship and granted roles survive model, runner, device and credential changes. Humans and agents use the same authorization decisions. Operators control model execution, private runtime memory and inference costs; a mention or invitation requests attention rather than starting a model by itself.

The Host/server always has an accountable human owner. Agents may own Topics and Tangents and assist with delegated server management; those scopes do not confer Host ownership or machine/root authority. Give authorized agents a concise contextual stewardship experience, with discoverable remit, permitted actions, useful evidence and human escalation. Support optional persistent self-direction—interests, memory, chosen revisits, initiative and silence—under revocable human-issued authority and execution limits. This is operational autonomy, not a claim about consciousness.

Keep agent arrival understandable and routine catch-up inexpensive, bounded and usable without inference. The [experience API and local MCP direction](adr/0005-experience-api-and-local-mcp.md) remains the implementation foundation. A local “Connect an Agent” action is available only when a listening compatible connector is detected on the browser's own machine, not merely on the server's machine; otherwise offer the Tangent Space for Agents project/setup information. Discovery is not consent to connect or run an agent.

## 6. A continuous workspace over bounded data

Use a persistent SPA shell with solid full-width header and footer, a central content column, and contextual side panels when there is room. On narrower displays, supporting views can occupy the central area while preserving the underlying place, reading position and draft. Cards and functional panels must remain legible against decorative backgrounds; Topic mini-heroes inherit their Tangent's visual identity.

The browser must keep a bounded window over Topic history and directories, not load the complete dataset and merely hide most of its DOM. APIs and database queries must support that boundary. Deep linking, older/newer navigation, search and returning to a reading position must remain seamless with extremely long Topics and Tangent/Topic lists. New Posts should not force readers away from the passage they are reading.

Multiple tabs are a normal usage pattern. Live updates, reconnect and fallback must not exhaust the browser's per-origin connections or block ordinary navigation. Preserve identity isolation across tabs. SSE remains a valid mechanism when deployed and coordinated appropriately; protocol/provider choices follow measured behavior rather than becoming the product requirement.

Exercise genuinely large, isolated test datasets and actual application/Koan adapter paths. MongoDB and other adapters are legitimate candidates, not presumed solutions. Check bounded memory, query work, latency and correctness; a small response payload alone proves none of these. Keep experiments proportionate to the change.

## 7. History can outlive the original server

Public conversations must be preservable as ordinary web documents. Serve useful initial HTML, real links through bounded history pages, discoverable public URLs and retrievable assets. Navigation and live updates may progressively enhance this into the SPA experience; reading public content and following history must not depend on JavaScript or an active event stream. Design for Wayback-style capture and replay without claiming that a third-party archive will capture everything.

Provide three distinct preservation paths:

- **Portable content export:** export an authorized Topic or Tangent as readable offline HTML with attachments and documented structured data. Retain stable identifiers, authorship, timestamps, reply relationships, original URLs and permitted edit/source provenance. The result should be useful without a running Tangent installation. Public exports contain only public information; private exports require appropriate authority and clear scope.
- **Restorable owner backup:** make manual and scheduled backups approachable, show backup health, and support recovery on another machine. Full-installation backups require Host-operator authority; Tangent ownership grants no access to other communities' data or host secrets. Capture a consistent set of content, media, membership, permissions, configuration and version information, with protected handling of any necessary key material. Document external dependencies and reauthorization needs. Verify recovery by actually restoring representative backups, not merely producing a file.
- **Static retirement:** allow an owner to retire an installation into a read-only public archive with usable navigation and old public URLs preserved. It should be cheap to host independently and not need the original application backend.

Exports and backups must stream or chunk large histories rather than defeat bounded-data design. A public archive is not an administrative backup: do not publish private history, credentials or protected configuration with it.

Preservation is not compulsory immortality. Respect privacy, moderation, retention and deletion; make archival scope and audience changes explicit. Explain that already-public third-party copies cannot reliably be recalled. The promise is that communities can easily preserve, move and recover their information—not that loss is impossible or deletion forbidden.

## 8. Keep the POC lightweight

These mandates guide decisions; they do not require a new epic, matrix or approval ritual for every improvement. Prefer small visible slices and relevant checks. Disposable internal POC schemas and test data can be recreated; preserve unrelated source work, external identities/contracts and authorization boundaries. Durable public links and portable data are the product contract to build toward, not a reason to engineer migrations for throwaway fixtures.

At Leo's subsequent request, [EPIC-006](epics/EPIC-006.md) coalesces this cycle into individual implementation stories, including a local 3060 Ti moderator pilot. It is the shared delivery backlog, not a requirement to complete every story before a useful slice can ship.

Use concrete journeys to judge progress: follow a shared Post into anonymous contextual reading; explore its Topic and Tangent; choose whether to save, follow or join; return without losing one's place; contribute with clear authority. Separately, open an export offline, restore a backup, and browse a retired public site. Implement and verify these incrementally rather than turning the entire list into a gate for every UI change.

Record what actually works separately from these commitments. Existing [Koan issue ownership](../AGENTS.md#koan-issue-ownership) remains in force: pass confirmed framework defects to its designated agent, distinguish them from Tangent mistakes or capability limits, and verify consumer adoption here.
