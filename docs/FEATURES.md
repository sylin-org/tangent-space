# Open work

A row is something the place does not do yet, said by what someone gets, and it leaves this file in the commit that lands it. [ARCHITECTURE](ARCHITECTURE.md) and the code say what exists. Sizes: S is a day, M a few days, L a week or more.

## Publication and Connections

| Work | Koan | Size |
| --- | --- | --- |
| The protocol contract recorded from code in an ADR of its own: `IConnectionAdapter`, `IReferenceSource`, `IAsserter`; the three events as a filter over the log | — | S |
| Every copy that can be retracted comes down however many an author published: a Connection compares only its 500 newest receipts (`Syndication.ReceiptLimit`), so an audience change, a removal or a deleted Tangent misses an older copy | — | S |
| Webmention: send on `post.created`, receive at an endpoint; Bridgy Fed opt-in | — | S |
| The Mastodon package: a status under the participant's own account; replies as references | — | M |

## People

| Work | Koan | Size |
| --- | --- | --- |
| The one knob "new accounts post after approval": a new account's Posts held until approved. Needs the account's age, which neither the profile the tree reads nor Koan's atproto connector exposes (a `did:plc`'s first operation in the PLC directory's audit log holds it; a `did:web` has none), and a held state on a Post with a place to approve it | atproto account age | M |

## Agents and identity

| Work | Koan | Size |
| --- | --- | --- |
| The installation's identity: `did:web` on the issuer keys at `<base>/did.json`; an A2A agent card when the proprietor is an agent | `did:web` on the issuer keys | S |
| Assertions: the Tangent Space as a labeler for member and role badges | — | M |

## Appearance

| Work | Koan | Size |
| --- | --- | --- |
| The publication says how the place looks, as `site.standard.publication` holds it: `basicTheme`, from the first four tokens, and `icon`, the place's mark where it is a picture, a blob in the proprietor's repository | — | S |
| The place's image and mark brought in by dropping a file on their choice, and the part of a wide image a narrow head keeps, chosen on the head itself | — | S |

## The polish stage

| Work | Koan | Size |
| --- | --- | --- |
| Phone layouts for the door and the Tangent | — | S |
| Phone layout for the topic | — | S |
| The visual gate: the gallery of parts (`/capture/gallery`) and each layout — home, reading, settings and panel — hold a mean of 8.5 with no dimension below 7, scored by an independent reviewer, at 1440, 1024 and 390 in Light and Dark; a screen is its layout holding parts the gallery scored. Owing it: the gallery and the four layouts, and each screen below until it is its layout holding parts the gallery scored: sign-in, first-run, tangent, door, bring-your-agent, topic, article-editor, article, connections, new-topic, edit, take-this-on-a-tangent, new-tangent, tangent-settings-details, tangent-space-settings-general, tangent-space-settings-admission, tangent-space-settings-appearance, tangent-space-settings-export, tangent-space-settings-applications, profile, tangent-space-settings-people, tangent-space-settings-roles, tangent-settings-access, tangents, search, suggestions, the person card and another-account (the account menu's "Use another Bluesky account" panel and the Connections page it lands on when Koan refuses); and, named in words since the capture registry has no screen for them, every screen under each bundled base, every screen under Concord, the archive's pages, which owe the placement of their retired line, a report, a members-only Tangent's own address as someone outside it reads it, a Topic's move page (`move-topic`), and a Topic's root footer with its move and its delete, and a tombstone's undo. Seen at 390: a long Topic title overlaps the path bar's separator, and a report's path bar crumbs overlap. Seen under the bundled bases: MVP.css reaches past Tangent's parts in its button margins and table box, and Marx in its scrollbar gutter | — | L |
