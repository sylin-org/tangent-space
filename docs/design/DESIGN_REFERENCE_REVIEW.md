# Design reference handoff

Reviewed 9 September 2026. Leo supplied [Design Example](../../refs/Design%20Example/readme.md) after the working-prototype brief, then explicitly selected the idea of each Tangent having a little TCG-like card. The remaining visual treatment is a strong implementation candidate, not a claim that every example behavior is approved.

[Editable reference](../../refs/Design%20Example/Tangent%20Space.dc.html) · [Standalone prototype](../../refs/Design%20Example/Tangent%20Space%20-%20standalone.html) · [Implementation epic](../epics/EPIC-003.md)

## Selected direction: each Tangent has a card

Make the card the community's recognizable face. Attach it to the Tangent, so a Host containing several Tangents gives each its own identity. Carry the same artwork, accent and name into smaller representations throughout the application.

| Representation | Purpose |
| --- | --- |
| Full card | Discover a community, preview an invitation, or celebrate creating a Tangent |
| Compact card | Browse saved/joined Tangents and choose where to return |
| Navigation identity | Recognize the current Tangent and notice a small activity marker while elsewhere |

The full card can carry artwork or a useful fallback, name, a short description, a house rule or motto, audience/join information, and the viewer's relationship to the community. Any counts must respect the actual listing policy and viewer's access. Activity is a live overlay derived from the same participant activity model as Channels, not a second unread system.

An appealing owner-welcome moment is watching the first card take shape as the owner names the Tangent and describes it. Artwork can be added later; setup must not depend on image generation or an external service. This interaction is a recommendation extending Leo's selected card direction.

The card's character should survive its smaller forms: a place that can be recognized by its face, name and accent. Keep the full illustration prominent where choosing or arriving matters, and use the compact form during sustained conversation. Discovery and shared previews may show only deliberately public metadata; invitation-specific information remains audience-controlled.

## What the reference establishes well

The rendered design has a coherent dark foundation, warm house accent, per-community colors, pixel artwork, light display headings and restrained borders. The card gives a Tangent a stronger identity than a plain server name. The return overview and conversation layout share enough visual language to feel like the same place.

The eight demonstration chapters cover visitor arrival, sign-in, owner welcome, return, live conversation, care, an agent counterpart and component/state notes. The source also contains invitation and Post presentations. The distinction between ordinary activity, direct replies, watched activity and unavailable source freshness is useful. Original authors remain visible in conversation, including the agent.

The review opened the standalone HTML and visually inspected Arrival, Return, Live, Agent and First run at the browser's existing desktop viewport. A simulated background message updated the Lounge marker and parent Tangent marker while the selected Workshop and its existing composer value remained. A UX specialist independently reviewed the source against the epic. This is design inspection and simulated interaction evidence, not a native integration or complete accessibility test.

## Corrections to carry into implementation

1. **Account readiness:** the sign-in diagnostics invent a fallback to app-managed rooms. Retain Leo's choice of native Spaces. Distinguish sign-in, community membership, account consent and provider compatibility; the next action differs for each.
2. **Private discovery:** the visitor screen explicitly says a private Channel exists, and the agent example names Seams despite denying access. Omit unauthorized names, counts and existence hints unless a deliberate listing policy permits them.
3. **Read state:** selecting a Channel currently clears all its simulated markers. A real implementation must acknowledge an intended viewed boundary, preserving unseen history. Overview, delivery and read acknowledgement stay separate.
4. **Draft and write identity:** the prototype has a global draft/reply target and hardcoded Workshop writes. Bind author, Tangent, Channel, reply target and operation together. A saved draft does not automatically become a submitted post after renewed authorization; an existing submitted operation retains its original retry receipt.
5. **Live and agent status:** transport connectivity, source freshness, runner connection and operator-controlled execution are different facts. Advertise only supported signals. Use opaque activity continuations and scoped actions in the agent response; its displayed timestamp is an illustration, not a delivery contract.
6. **Publication:** preserve existing discussion audience when presenting a Channel as a Post. Any broader publication selects and previews material explicitly. Search authorization and optional public directory listing are separate controls.
7. **Production navigation:** the chapter/Stage controls explain the design simulation. The application should use ordinary community navigation and real state, with demonstration controls kept in the reference.

The source is candid about stubs. Creation and moderation show confirmations without durable effects; invitation copying changes its label without copying, and the agent response is staged text. Rebuild those behaviors through the existing application operations rather than importing the simulation's state management.

## Remaining design and implementation work

- Complete recipient invitation acceptance, wrong-account handling, expiry/revocation and scoped role selection.
- Carry new-owner choices into a real created Tangent; make setup resumable and second-Tangent creation reachable.
- Complete saved places, permission-filtered search, Post browsing and Series navigation/editing.
- Demonstrate full loss of read access, not only a posting restriction; filter management actions by current actor and scope.
- Give dialogs keyboard focus management, Escape behavior and focus restoration. Batch activity announcements for assistive technology. Validate small text, narrow layouts and long conversations in real browser tests.
- Replace placeholder identities and borrowed example artwork where appropriate. The rendered Tangent design and kit-specific description are the visual reference; the bundled generic Sylin system contains different colors and typography and must not silently override it.

## First implementation demonstration

Start with the selected card identity, real application navigation and a live return loop across two Tangents. Migrate existing rooms under the first Tangent while retaining source references and deep links. Use actual authenticated identity and account readiness, then connect the overview, card markers, Channel markers and WebMCP changes to the shared durable activity model.

The demonstration is: Leo writes a draft in Workshop; an agent posts in a Channel of another Tangent; the corresponding Tangent and Channel markers update; Leo's draft stays with Workshop. Both clients leave and return under the same identities, recovering the same permitted activity. Owner setup, invitations/care and Posts then complete the wider EPIC-003 lifecycle on this foundation.
