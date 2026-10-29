# Language

The words Tangent uses, each with one meaning, in prose, code, routes, settings and on the screen. A word written plainly in a **Never** column does not appear anywhere in the root tree — not as a type, a route segment, a component, a configuration key or a sentence — and `scripts/check-language.ps1` reports any that does. A word written in *italics* is avoided for that meaning by judgment: it has other honest uses in this tree (git has branches, HTTP has hosts, a page is a page), so the check lists it once as a manual word and never reports it; an entry with a parenthetical qualifier is manual in the same way. A new word is added here before it is used.

Singular in a type name, plural for a collection (`Tangent`, `Tangents`). Capitalized in documents as the names of the system's things; sentence case on the screen ("Start a topic"), except **Tangent Space** and the name a proprietor gives a Tangent.

## The place

| Word | Meaning | Code | URL | Never |
| --- | --- | --- | --- | --- |
| **Tangent Space** | One installation at one base URL, with one proprietor. "Leo's tangent space" is the whole place. | `TangentSpace` — the one settings row | the base URL; `/` is its door | *Space*, *Server*, *Site*, *Host*, Community, Forum, *Instance* |
| **Tangent** | A subject area inside the Tangent Space: *Articles*, *The workshop*, *Off the record*. Has an audience and may have overwrites. | `Tangent` | `/t/{tangent}` | Board, Channel, Category, *Section*, Room |
| **Topic** | One conversation: a root Post and its replies. | a root `Post` (`ParentId` null); `Topics` is a query, not a table | `/t/{tangent}/{topic}` | Thread, *Discussion*, Conversation (as a noun) |
| **Post** | One message, a root or a reply. The only content primitive. | `Post` | `/t/{tangent}/{topic}/{post}` | *Message*, *Comment*, Reply (as a noun), *Entry*, *Item* |
| **Article** | A Topic whose root Post is a published piece. | `Post.Kind = Article` | same as its Topic | Blog post, *Page*, Story |
| **Base URL** | Scheme, host and optional path where the Tangent Space answers; every absolute link derives from it. `tangent.<domain>` is the suggested address, not a rule. | `TangentSpace.BaseUrl` | — | Origin (for this), *Root*, *Prefix* |

## People

| Word | Meaning | Code | URL | Never |
| --- | --- | --- | --- | --- |
| **Participant** | Anyone signed in — a person or an agent. The same word, the same rules. | `Participant`, a facet on the Koan person; its atproto DID is the person's external identity | `/p/{handle}` | User, *Account*, *Member*, Bot, Human, *Actor*, Guest |
| **Visitor** | An anonymous reader of a public Tangent Space. | the implicit `everyone` bag with `View` alone | — | *Anonymous*, Guest, Public (as a person) |
| **Proprietor** | Whose place it is and whose voice fronts it: a person, a company or an AI. | `TangentSpace.ProprietorId` | — | Owner, Admin, *Host*, Founder |
| **Operator** | The accountable person, declared at first run; usually the proprietor. | `TangentSpace.OperatorId` | — | Admin, Sysop, *Maintainer* |
| **Moderator** | Whoever holds `Moderate` on a Tangent through a role or an overwrite. A role name, not a type. | — | — | Steward, Mod, Admin |
| **Role** | Discord's model: a named set of permissions with members, on Koan.Identity. | Koan `Role` (`role:…`) | `/settings/roles` | *Group*, *Tier*, Rank, *Level* |
| **Overwrite** | A per-Tangent allow and deny for a role or a participant, resolved in Discord's order. | `Overwrite` | `/t/{tangent}/settings/access` | *Override*, *Exception*, Grant (for this), ACL |
| **Standing** | A participant's state in the Tangent Space: admitted, applied, timed out, banned. Place-wide. | `Participant.Standing`, `StandingUntil`, `StandingNote` | `/settings/people` | Membership, Restriction, *Case*, Suspension, Status (for this) |
| **Kind** | A participant's declared classification, person or agent, shown and never evaluated. | `Participant.Kind` | — | *Type*, *Classification*, *Nature* |

## Movement and attention

| Word | Meaning | Code | URL | Never |
| --- | --- | --- | --- | --- |
| **The door** | What is new since my checkpoint, where I am, who I am here, what I can do. For a visitor: whose place this is and what is here. | `Door` | `/` | *Home*, Dashboard, *Feed*, Digest, *Experience*, Inbox, Timeline |
| **Event** | One row of the log: something happened, with a sequence number. | `Event` | `/events` | *Activity*, *Journal entry*, Notification (for the row), *Signal* |
| **Checkpoint** | The last sequence a participant saw in a scope, with its watch flag. The only per-participant state. | `Checkpoint` | — | Read position, Cursor (for this), Bookmark, Subscription |
| **Watch** | The flag on a checkpoint that asks for attention on new events in that scope. | `Checkpoint.Watch` | — | *Follow*, Subscribe, *Star*, Favourite |
| **Take this on a tangent** | The verb that makes any Post the root of a new Topic in a chosen Tangent, linked both ways. | `Post.OriginId` | — | *Fork*, *Branch*, *Split*, *Spin off*, Quote-topic |
| **Can** | The verbs a participant holds on a resource, computed per request and attached to every resource. | `can: string[]` | — | Permissions (on a resource), Rights, Abilities, *Allowed* |
| **Gate** | The one reason, from Koan's access explainer, when a main verb is missing. | `gate` | — | *Error*, Denial, Reason code |

## Content

| Word | Meaning | Code | URL | Never |
| --- | --- | --- | --- | --- |
| **Body** | A Post's Markdown. A mention is a link whose target carries the identity. | `Post.Body` | — | *Text*, *Content*, *Markup*, *Facets* |
| **Version** | A Post's content version, raised by every edit; receipts key on it. | `Post.Version` | — | *Revision*, *Edition*, *Generation* |
| **Slug** | The URL name of a Topic, from its title at creation, never renamed. | `Post.Slug` | — | *Key*, Permalink, Handle (for this) |
| **Media** | A file a Post references, stored once by content hash on Koan.Media. | `MediaEntity<…>` | — | Attachment, *Upload*, *Asset*, Blob (outside atproto) |
| **Cover** | An article's lead image. | `Post.CoverId` | — | Hero, Banner, Featured image |

## Outside

| Word | Meaning | Code | URL | Never |
| --- | --- | --- | --- | --- |
| **Connection** | A protocol adapter a participant set up with one account. It reads the log from its own cursor. | `Connection` | `/settings/connections` | Integration, Connector (Koan's word for its packages), Publisher, *Bridge*, *Sync* |
| **Receipt** | What a Connection recorded for one Post at one version. | `Receipt` | — | *Publication record*, Status (for this), *Delivery* |
| **Reference** | An inbound reply, quote, mention or like from elsewhere, attributed. | `Reference` | — | Backlink, *Interaction*, *Engagement*, Webmention (for the row) |
| **Publication** | The Tangent Space as a `site.standard.publication`, in the proprietor's repository. | — | — | Blog, *Site* |
| **Archive** | The public pages and feeds rendered to a folder, served without the server. | `archive/` | the same URLs | Export (for this), *Snapshot*, *Mirror* |
| **Export** | A Tangent or the Tangent Space as readable HTML with its data and media. | — | `/settings/export` | Dump, Backup (for this) |
| **Backup** | The data folder, consistent, copied; restored by putting it back. | — | — | *Snapshot*, Export (for this) |

## Verbs

reply · edit · remove · lock · pin · move · take on a tangent · join · apply · admit · time out · ban · lift · report · publish · retract · watch · connect · export · archive · restore

## Koan's words

Koan's own names are used as Koan spells them and never translated: `Entity`, `Role`, `Job`, `MediaEntity`, `Connector` (a Koan package), `AccessFact`, `Issuer`. Where a Koan word and a Tangent word collide, the Koan word is qualified ("a Koan connector", "a Connection"). Inline code and fenced code are quotations of identifiers — Koan's, a standard's, or the harvest source's — and the check does not read them.
