# 0017 — Appearance is layered CSS over meaningful markup

Date: 3 November 2026. Status: proposed. It records Leo's direction of 3 November 2026: a place's look is purely CSS that anyone reading it understands, and it is combined from existing stylesheets "like legos".

## Context

Leo looked at his own door and couldn't tell three things: where to manage the place, how to create a Tangent or start a Topic, or whether Moderators was a Tangent or a link. A design search followed, with two rounds of generated proposals. Proposals specified tightly came out alike; proposals left open came out gaudy. The lesson is to divide the work. Tangent owns what a page *means*, and the proprietor owns how it *looks*.

Today a place's Appearance is its Accent, one of four, plus its Slots ([LANGUAGE](../LANGUAGE.md)). Every page is a record, rendered to HTML through one view per record (`TangentModule.cs:66-89`). An agent reads the same record as JSON. The markup underneath doesn't say what things are:

- `main` sits inside a `div`.
- A Post's meta and actions are `div.post-head` and `div.post-actions` (`Render/TopicPost.razor`).

No standard defines themes for HTML. Several existing collections fit the work anyway:

- **Classless stylesheets.** These style plain semantic HTML with no classes, and their only contract is the HTML element set. One catalogue lists 47 of them ([dbohdan/classless-css](https://github.com/dbohdan/classless-css)), and one switcher previews 110 ([dohliam/dropin-minimal-css](https://github.com/dohliam/dropin-minimal-css)). Most are MIT. [Simple.css](https://github.com/kevquirk/simple.css), [MVP.css](https://github.com/andybrewer/mvp), [Marx](https://github.com/mblode/marx) and [Sakura](https://github.com/oxalorg/sakura) are maintained. Pico is archived. All of them are single-column document styles: none styles rails, tiles, meta lines or action rows.
- **Palettes that name each colour's job.**
  - [Catppuccin](https://github.com/catppuccin/catppuccin/blob/main/docs/style-guide.md) names background, surfaces, text, links and errors.
  - [Radix Colors](https://www.radix-ui.com/colors/docs/palette-composition/understanding-the-scale) guarantees contrast between its steps.
  - [base16 and base24](https://github.com/tinted-theming/schemes) hold about 600 schemes, but guarantee no contrast.
  - Bootstrap 5.3 exposes each Bootswatch theme's colours as variables.
  - [Material's colour utilities](https://github.com/material-foundation/material-color-utilities) derive a light and a dark scheme from one seed colour.
- **[Bear Blog](https://docs.bearblog.dev/styling/)** shows the arrangement: fixed, documented markup, and owner CSS over it that may pull in a classless stylesheet.
- **The first four tokens follow atproto.** They are already named as `site.standard.theme.basic` names them, and publishing them is planned ([FEATURES](../FEATURES.md)).

## Decision

1. **The markup is the contract.**
   - Native elements carry the structure: `header`, `nav`, `main`, `article`, `aside`, `section`, `footer`, `form`, `button` and `details`.
   - `main` sits directly under `body`. A Post is an `article` whose meta line is its `header` and whose actions are its `footer`.
   - Any classless stylesheet therefore styles every page as it stands, and assistive technology reads the same structure.
   - A page says everything it means, as far as is ergonomically sensible, and the stylesheet chooses what to show and how. Three rules keep that sound:
     - **A stylesheet chooses what shows, never what a reader may see.** Everything in a page reaches its reader, hidden or not, so what a reader may see decides what goes into the page.
     - **Text is for reading, attributes are for facts.** A classless stylesheet shows every element it meets. So a fact a page carries for styling, such as an exact time, a count or an edit, goes in an attribute: `<time datetime>`, `<data value>` or `data-*`. A stylesheet that wants it brings it into view with `attr()`.
     - **The page's order is its reading order.** Assistive technology and the keyboard follow the page, whatever a stylesheet moves. So the markup comes in the order that makes sense, and a stylesheet rearranges it from there.

2. **Classes name three things, in the product's words.**
   - **Shape:** how a thing is built. `chip`, `card`, `row`, `list`, `tile`, `badge`, `panel`, `bar`, `path`, `menu`. These follow Bootstrap's names where Bootstrap has the same idea.
   - **Subject:** what a thing is about. `place`, `tangent`, `topic`, `post`, `participant`, `report`, `application`, `notification`, `offer`. A form also carries the name of its tool, with hyphens (`start-topic`, `reply`, `create-tangent`, `seen-notifications`), so a form, its route and its tool share one name ([ADR 0016](0016-a-standard-client-at-the-tools-door.md)).
   - **State:** how a thing relates to the reader, or where it stands. `is-` describes the thing itself: `is-you`, `is-agent`, `is-new`, `is-private`, `is-locked`, `is-removed`, `is-pinned`, `is-waiting`, `is-current`. `by-` describes who made it: `by-you`, `by-agent`.

   Classes combine and never compound. A chip showing you is `chip participant is-you`, and the same three words style a card or a row. A state names only a fact the record holds, never a look. Each word is defined once in LANGUAGE, beside the record field and the JSON name that share it. About thirty words make up the whole theming surface.

3. **Appearance is five cascade layers, in a fixed order.**

   ```css
   @layer base, shapes, tangent, palette, owner;
   ```

   | Layer | What it styles | What can fill it |
   |---|---|---|
   | `base` | plain elements | nothing, or one bundled classless stylesheet |
   | `shapes` | the shape classes | Tangent's own shapes, or Bootstrap's components |
   | `tangent` | subjects and states | Tangent's own |
   | `palette` | the `--ts-*` values, in Light and Dark | Tangent's palette for the Accent, a role-named palette, a Bootswatch theme's colours, or one derived from the proprietor's own |
   | `owner` | anything | the proprietor's own CSS |

   - Each layer holds at most one stylesheet, and an empty layer adds nothing. With every layer left at its default, the place wears Tangent's own design.
   - The order decides which layer wins, whatever a selector's specificity, so layers never fight.
   - Whoever holds `ManageSpace` picks a stylesheet for each layer at `/settings/appearance` and sees it on a preview of the place's own door before saving. One action returns a layer to its default.
   - An agent holding the bit does the same through the tool of the same route.

4. **The variables join the layers.**
   - Every layer reads the `--ts-*` tokens.
   - Each bundled classless stylesheet comes with an adapter of a few lines that maps its own variables to `--ts-*`. Any palette then colours any base.
   - A palette maps its named roles to the tokens, for Light and for Dark.

5. **The place offers the proprietor's own look.** Where the proprietor's atproto repository holds a `site.standard.theme.basic`, the place offers it as its palette, once.

6. **Bootstrap comes in as components and colours only.**
   - The `shapes` layer can hold Bootstrap's components, built without its reset, its utility classes or its script.
   - A Bootswatch theme comes in through `palette`, from the variables Bootstrap 5.3 exposes. Its reset would restyle the plain elements the `base` layer owns, and its utility classes would turn the markup back into presentation.

7. **Management pages leave out the owner layer.** Every page under `/settings` renders with the first four layers only. No stylesheet can hide the way back.

## What does not exist

- A template language.
- Script of the proprietor's own.
- Bootstrap's utilities or its script.
- Two stylesheets in one layer.
- A stylesheet for one Tangent or one Topic.
- A gallery or marketplace beyond the stylesheets Tangent bundles and the owner's own.
- A visual stylesheet editor. The proprietor's agent writes CSS through the Appearance tool.

## Consequences

- **The markup changes.** Every view moves to the contract: native structure, `main` under `body`, a Post's `header` and `footer`, and the three kinds of class. Captures show each page under Tangent's default appearance and under one bundled classless stylesheet with its adapter.
- **LANGUAGE gains the contract.** It holds the shape, subject and state words and the five layers, and *Appearance* comes to mean all of it. A stylesheet reading `article.post.by-agent > header` says what it targets, in words the glossary defines.
- **The words are kept.** A word is added freely and changed only rarely, because stylesheets rely on it.
- **The visual gate applies.** At the polish stage it scores the default appearance, the one most places wear.
- **Agents are unaffected.** They read records. Whatever a stylesheet does to a page, the tools and their answers stay the same.
- **Koan stays out of it.** Koan writes no page (ARCH-0143), so the markup contract and the layers are Tangent's. If another Koan application ever needs the same thing, it lifts out then.

## Open

- Whether `base` defaults to empty, so Tangent's own design carries every element, or to a bundled classless stylesheet that Tangent's layers refine.
- Which classless stylesheets ship first. Candidates: Simple.css, MVP.css, Marx and Sakura, each with its adapter and licence.
