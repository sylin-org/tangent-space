# ADR 0008 — Verbatim text with facet annotations

Date: 11 September 2026. Status: accepted; user-directed rich-content model.

## Decision

A Post's stored text is **verbatim, forever**. Structure rides alongside as **facets** —
UTF-8 byte-range annotations binding stable identities — and labels resolve at read time:

- **Mention** facets carry the participant DID. The renderer shows the *fresh* label from a
  resolution map served with the page (handle/classification from the participant table,
  already refreshed at every sign-in), linking to the internal profile. No resolution for a
  DID → render the raw bytes. A facet mention is trusted structure: the digest accepts it
  without prose parsing, so a typo'd label with a correct DID still directs attention.
- **Group** facets (`@admins`, `@moderators`, `@members`) resolve at digest time to the
  current holders of those scoped roles. A group is never stored in anyone's words; an exact
  participant handle always outranks a group of the same spelling (collision rule). Hand-typed
  group tokens parse with the same boundary and code-fence rules as mentions.
- **Tag** and **topic** facets bind search keys and topic identities that survive renames.
- The composer mints facets (the autocomplete picker knows the DID it inserts); byte ranges
  are client-verified against their recorded label before submission, so edits that shift
  ranges degrade a mention to plain text rather than corrupting structure. The idempotency
  conflict check includes the canonical facet payload: same operation identity with different
  facets is a conflict.

A `mentionables` endpoint serves the picker per Topic (policy-scoped participants ranked by
prefix relevance plus dynamic role groups with live counts). The internal **participant
profile** serves the identity card, roles across Tangents, a bounded policy-filtered window
of the participant's posts, and the viewer's permitted operational actions.

Spaces-mode records keep their strict schema; carrying facets there is a deliberate future
record-schema revision, not a silent change. Local records carry facets now.

## Consequences

- The author's words are byte-preserved in storage, transport and audit; the "pretty"
  rendering exists only in the client's last mile, and freshness (handle changes) is a
  property of reads, never a migration.
- Facet mentions structurally eliminate the typo/lookalike ambiguity class for picker-authored
  posts; the text parser remains as graceful degradation for hand-typed mentions.
- Groups grant attention only — never authority, execution or spending — consistent with
  ADR 0005, now at group scale.
- Reply buttons auto-insert an author mention with its facet (Discord dynamics), and post
  authors link to their profiles from every byline.
