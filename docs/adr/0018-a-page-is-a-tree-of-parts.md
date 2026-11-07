# 0018 — A page is a tree of parts

Date: 5 November 2026. Status: proposed. It records Leo's direction of 5 November 2026, given after his door printed the place's name three times: pages are a hierarchy of components — the person, the Tangent entry, the list of Tangents — and "the mechanism enforces adherence naturally, no bespoke content."

## Context

Every page wrote its own markup and its own path. A person was drawn by three separate functions (the masthead's, the Topic row's and the profile's) and carried by four records; a Tangent in a list by three; the door wrote its own Topic rows and copied a Tangent's head, which is why it named the place three times. Twenty-eight views wrote their own crumbs, twelve of them starting with the place. The one model of where a page sits, `Here`, was read by access alone. Koan renders no HTML (ARCH-0143 §5), so the mechanism is Tangent's.

## Decision

1. **A part is a record that says what a thing is, never how it looks** (`IPart`): a person, a Tangent entry, a Topic row, a Post, the place's head, a shelf, and the parts that hold others, such as a list of Tangents or who is around. Each part type has exactly one Razor component in `src/server/Render/Parts`, whose `Model` is the part. `Draw` draws any part by its type; a part that holds another chooses how it is drawn, in LANGUAGE's shapes (`Shape`): a person is a chip in a Post's head and among who is around, a row in a list of people, a name in a sentence. The start stops on a part type with no component or with two, and on a component of the parts folder that draws no part (`PartComponents.Check`).
2. **A page is a resource record whose properties are parts**, each placed in a slot of its layout — head, rail or column — by declaration (`[Slot]`), in declaration order; a null part is left out. The record names its layout (`[Page]`: home, reading, settings or panel), or chooses it where its layout is its own (`ILaidOut`, as a page that writes an article in the article's layout and anything else in a panel). One page component draws every page from that (`Page`), and a registration names no component (`ResourceComponents.Add<TPage>`). A refusal, Koan's `Problem`, is drawn as its page (`RefusalPage`).
3. **The page's JSON stays the record serialized.** Facts a person and an agent read alike stay where they were; a slot may hold a part projected from them, which the JSON leaves out, so the JSON says each fact once and the page draws it from the same record. The page's version hashes the record as its JSON says it (`PageVersion`).
4. **A form is a part.** `Form` draws every write: each field says what it is drawn as (`Form.Field.As`: a line, a few lines, a choice, a value the page gives), its choices and its bound, which its JSON says too; a destructive form confirms in the page.
5. **Where a page sits is `Here`**, which the Doorway resolves from its address for every page that declares its bit. The path bar is `Here`'s Tangent and Topic followed by the page's own crumbs (`Frame.Under`); the switcher hangs from the Tangent's crumb; the place is never a crumb, since the masthead is the root. The door, a profile, the place's settings, a Tangent's settings and access, the Tangents and a new Tangent declare `[Needs]` and read what the Doorway found.
6. **Each part owns its classes** — LANGUAGE's shape and subject — and a container sizes what it holds by where it stands, so a stylesheet meets one element per thing. `scripts/check-rules.ps1` refuses a `.razor` file outside the parts and the shell.

## What does not exist

- Markup in a page record or a controller.
- A page component per page.
- A crumb that names the place.
- A second drawing of a person, a Tangent entry or a Topic row.
- A part whose JSON member is typed by an interface: Koan answers such a page as guarded JSON whatever the request prefers, so a part held that way stays out of the JSON.

## Consequences

- **The visual gate scores the gallery and each layout**, not each screen: the capture host's gallery of parts (`/capture/gallery`) shows every part in its states, and a screen is its layout holding parts the gallery already scored.
- **JSON field names moved where records became parts**: a participant is `Person` wherever one stands, a listed Tangent `TangentEntry`, a listed Topic `TopicEntry`. Tangent shapes nothing for Companion Lobby.
- **Export and the archive draw the same parts**, standing alone, and hand the handlers they call the place as the Doorway finds it for a visitor.
