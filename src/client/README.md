# Client

The design foundation every page of a Tangent Space stands on, and its islands, built by Vite into a stylesheet for each layer of the place's appearance, the bundled bases, and two scripts, which the server serves as static files.

| File | What it holds |
| --- | --- |
| `layers.css` | The order of the five layers, `@layer base, shapes, tangent, palette, owner;`, stated once; every page links it first. |
| `public/art/` | The covers a place or a Tangent may wear as its image, served at `art/<name>.webp`. |
| `base.css`, `tangent.css`, `palette.css` | Tangent's own stylesheet for each layer: `base.css` its fonts as `--ts-font-*` and the plain elements, the base layer's default; `tangent.css` its components, `tokens.css` and `frame.css` in the shapes layer and `parts.css` in the tangent layer, which every page loads; `palette.css` the colours of Paper and Ink and the four accents, the palette layer's default. Each is built to the file of its name. |
| `tokens.css` | The scale and the subjects' colours as `--ts-*` custom properties, in the shapes layer: type scale, radii, layout sizes, shadows, the place's first four tokens named as `site.standard.theme.basic` names them (`--ts-background`, `--ts-foreground`, `--ts-accent`, `--ts-accent-foreground`) as they follow from the palette, the six Tangent tones, the role colours, the agent's marks, danger, and the reader's theme as the page's colour scheme. With `base.css` and `palette.css` the values are the design reference's, held there by the token parity test. |
| `elements.css` | The plain elements as Tangent draws them — the page's colour and type, headings, links, buttons, fields, selection and focus — which Tangent's own base and Concord each bring into the base layer. |
| `bases/` | Concord, `bases/concord.css`: Tangent's own arrangement of the place in a chat app's look, which brings `elements.css` into the base layer in the system's font and arranges Tangent's parts in the tangent layer, scoped by `data-base="concord"`, beside its own palette. And the classless stylesheets the base layer can hold instead of Tangent's own — Simple.css, MVP.css, Marx, Sakura, Water.css and new.css — each vendored verbatim from its package in `devDependencies` into `bases/<name>/` beside its licence; `bases/<name>.css`, its adapter, imports it into the base layer and has its own variables read the tokens and the tokens read its fonts; `bases/<name>.palette.css` is its own palette, the `--ts-*` values taken from its colours with `light-dark()`, which the palette layer holds when it is left to the base. The build writes each with its licence under `bases/`. |
| `fonts.css` and `fonts/` | Newsreader (variable: weight 200–800, optical size 6–72), Schibsted Grotesk (variable: weight 400–900) and IBM Plex Mono (400 and 500), Latin and Latin Extended, as files in the tree declared with `@font-face`. Nothing is fetched from a font service, so pages render offline and captures are deterministic. The files come from the Fontsource packages in `devDependencies`; their licence texts sit beside them. |
| `frame.css` | In the shapes layer, after the tokens: the frame the shell draws around every page — the page's box, which keeps every part Tangent names with a class in the browser's own box whichever base styles the plain elements, the masthead with the place's mark and name, the search box and the account menu or the Sign in button, the path bar with its elbow, its crumbs and the Tangent switcher, each layout's page, the footer with its appearance switch, and the breakpoints — then the shapes several parts draw alike: buttons, chips, badges, bars, rows, marks, lists, cards, panels and menus. |
| `parts.css` | In the tangent layer: one block per part, named for the part, in the order a page first meets them, each reading the classes its component writes; an island's block styles what its script adds to the part it takes over. |
| `appearance.ts` | A reader's theme: System, Light or Dark, set as `data-theme` on `<html>` and remembered per browser in `localStorage`. |
| `theme.ts` | The theme entry, built to `theme.js`: a classic script that restores the saved appearance before the first paint. |
| `tangent.ts` | The page entry, built to `tangent.js` and `tangent.css`: a module that imports Tangent's components' stylesheet and mounts the islands. |
| `composer.ts` | The composer's rules apart from any page: drafts in `localStorage` by participant and scope with each send kept beside them by its TID until it is settled, a send that says whether the server confirmed the Post, refused it or left it uncertain, the reconciliation of an uncertain send by a `GET` of the Post's address for its TID, and TIDs minted in the page. |
| `live.ts` | The live updater's transport apart from any page: one connection that follows the log from a position, as the stream while it can, as the poll at the same address when the stream fails and as the door when the poll fails too, trying the stream again every minute; every request guarded by a watchdog, what it reads capped, the stream rotated, failures backed off with jitter, paused while the page is hidden; the position advances only once the page has accepted the rows. |
| `article/` | The article editor and what it reads: `article/editor.ts` is the article editor, TipTap over the form's own body field, in a chunk only its pages load; `article/markdown-roundtrip.ts` says whether the visual view holds a body's meaning; `article/extensions.ts` what it can hold, as a body is read; `article/bringing.ts` sends an image in. |
| `islands/` | The islands: `index.ts` holds the registry, every island by its `data-island` name, and mounts them; `fragment.ts` is the fragment island, `live.ts` the live updater, one per page, which patches a Topic, a Tangent's list or the door as the log moves, `appearance.ts` the footer's System, Light and Dark switch, `preview.ts` the frame on the appearance settings that draws the door in the choices as they change, `seen.ts` the Topic's seen control, which posts itself once the end of the window is on screen, `unseen.ts` the masthead's count of what is for the reader and new, raised from the rows the live updater dispatches on the document, `search.ts` the suggestions under the masthead's search box, `submitting.ts` a form that shows it is on its way, `composer.ts` the composer on the reply and new-topic forms, and `article.ts` the island that loads the article editor. |

## Build and test

Node 26 (`.node-version`).

```
npm ci
npm run build
npm test
```

`npm run build` type-checks and writes `base.css`, `tangent.css`, `palette.css`, `bases/`, `tangent.js`, `theme.js` and `fonts/` into `../server/wwwroot/`, which git ignores: `vite.config.ts` builds the page entry and empties the folder first, `vite.theme.config.ts` adds the theme script. URLs inside the stylesheet are relative, so the files can be served under any path of the base URL. `npm test` builds the stylesheets in memory and checks that every token in `tests/reference/tokens.css` and `tests/reference/tokens.json`, the design reference's tokens kept verbatim, is declared in Tangent's own stylesheets in the same block with the same value, whichever layer holds it; it also runs the built theme script over a saved choice, mounts the islands into a jsdom document, refreshes the fragment island against a mocked response, drives the composer against mocked answers, follows a mocked log with the live updater, and raises the masthead's count from delivered rows.

Building the server builds the client: `Tangent.Server.csproj` runs `npm ci` when `node_modules` is absent and `npm run build` when an output is missing or older than a client file, then copies `wwwroot` beside the binaries, where Koan serves it as static files. `dotnet build src/server -p:SkipClientBuild=true` builds the server alone, without Node.

## Captures for the visual loop

```
npm ci
npx playwright install chromium
npm run capture -- door
```

Playwright is a devDependency, and `npx playwright install chromium` fetches its Chromium: the one download, Playwright's own. `npm run capture -- <screen>` type-checks `capture/`, builds the capture host — `src/server` with Koan's test connector, which only `-p:TangentCaptureHost=true` adds, into `.local/capture-host`, so neither the server's own build nor its publish carries the connector — and starts it the way the test fixture starts a Tangent Space — on a loopback port the server chooses and reports, over a new temp data folder, with any settings a state names — checks that the door there states this run's base URL, and afterwards stops it and deletes the folder. Pages render with the base URL `https://tangent.localhost`, https as atproto sign-in needs outside Development; the script answers the browser's requests to it from the server's loopback port, so no capture shows the port and no certificate is needed. The host starts with Koan's test connector on, so a state can be captured signed in; a published server given with `--server` has no connector, so its signed-in states fail. `--url <address>` captures a Tangent Space that is already running instead, asking its door for the base URL it states, so its pages' links to that base URL are followed too; `--server <path>` starts a published `Tangent.Server` instead of building one.

Every screen is captured at 1440, 1024 and 390 wide, in light and dark (the device's colour scheme emulated and the stored theme choice set), in every state the registry declares: device scale factor 1, animations off, the network idle and `document.fonts.ready` awaited. Only the Tangent Space answers the browser; any other request is refused. The round fails when a page opens a dialog or holds an open dialog element, and when the browser cannot reach the Tangent Space for a request, rather than capture a page without its stylesheet. The output is `.dream-loop/<screen>/<n>/<width>-<theme>-<state>.png` at the repository root, `<n>` the next round for that screen, with a `README.md` describing the round; `.dream-loop/` is ignored by git. A round is written as `<n>.partial` and named `<n>` only when complete: a failed round leaves no folder, and Ctrl+C stops the server, deletes its temp folder and the unfinished round, and exits. A `<n>.partial` left by a killed run stops the next run until it is deleted.

`capture/screens.ts` is the registry. A screen's name is its key in `screens`, lowercase words joined by hyphens, and the visual gate row of [FEATURES](../../docs/FEATURES.md) names each registry screen by that key. `screens` names each screen's path and its states; a state is reached by a path of its own or a query, by settings the Tangent Space starts with, by a seed, by signing in, or by a click in the page. A state with `seed` names the rows its Tangent Space holds: once the capture host listens, the script posts to `/capture/seed/<name>` and waits until the host has written them through its writer; the seeds live in `src/server/Capture/`, which only the capture host compiles, and are fixed in names, words, times and TIDs, so rounds repeat. States with the same seed and settings share one Tangent Space. A state with `signedIn` is captured as `capturePersona`: before the page opens, the script signs the persona in through the test connector at the loopback port, its handle carried as the atproto connector's verified handle claim, and gives the browser the session cookie; any later screen captures a signed-in state the same way. A state with `cancel` names a path whose requests are cancelled, so a click that would leave the page, such as Continue on a form, shows the page on its way, and a post an island sends by itself, such as the Topic's seen post, changes nothing. A state with `values` sets hidden fields before any fill, such as a form's TID, so a state that writes repeats one Post rather than adding another in each width and theme; one with `reload` opens the page again once its fields are filled, so it shows what the page keeps of them, such as a draft; one with `waitFor` names what the page shows once its click is answered in place or by a navigation the page starts itself, such as a form an island sends. `references` names the reference's file for a screen at each width the reference draws it: 1440 for every reference screen, 390 for the three phone screens. `referenceVariants` names, for a screen the reference draws as several screens (the Tangent page as discussions and as articles), the entry of `references` for each variant; its reference captures are `<width>-<theme>-reference-<variant>.png`.

The design reference is configured, never part of the tree: put the package (the folder holding `screens/`, `spec/` and `tokens/`) at `.dream-loop/reference`, or set `TANGENT_REFERENCE` to its folder; the script says so when neither is there. Beside the captures it renders the reference's screen for each width and theme as `<width>-<theme>-reference.png`, at the size the screen is drawn, with the reference's font requests answered by the bundled fonts so it renders offline in the same typefaces. The round's `README.md` names each width the reference does not draw and each screen it does not have — the door, take this on a tangent, connections — with, where `nearestReadings` names them, the reference screens nearest to it for whoever draws its mock. A screen the reference has and Tangent does not yet gets a round of the reference alone.

## A page

```html
<html lang="en" data-accent="jade">
  <head>
    <link rel="stylesheet" href="…/layers.css">
    <link rel="stylesheet" href="…/tangent.css">
    <link rel="stylesheet" href="…/base.css">
    <link rel="stylesheet" href="…/palette.css">
    <style>…the place's own CSS, in its layer…</style>
    <script src="…/theme.js"></script>
    <script type="module" src="…/tangent.js"></script>
  </head>
  <body>
    <header class="masthead bar place">…</header>
    <nav class="path"><span class="path-elbow"></span><ol class="path-crumbs">…</ol></nav>
    <main class="layout layout-reading">
      <aside class="rail">…</aside>
      <div class="layout-main">…</div>
    </main>
    <footer class="footer bar">…</footer>
  </body>
</html>
```

`theme.js` is a classic script in `<head>`, without `defer`, so the saved appearance is on `<html>` before the first paint; it blocks the first render, so it holds nothing else. `tangent.js` is a module, deferred by nature: it runs once the document is parsed and never blocks the first render. The server renders `data-accent`, the place's accent, on every page, and `data-base` where the place chose a base other than Tangent's own; it links Tangent's components, then the place's base, so the arrangement a base such as Concord brings follows Tangent's own in the tangent layer, then its palette; the place's own CSS is the `owner` layer, left out of a management page. Without a saved choice `data-theme` stays off and the device decides; with no script too, and the switch does not show.

`main` stands directly under `body`. The layout grid is `.layout` for the door and Tangent pages, `.layout-reading` for a Topic and the pages read like one, and `.layout-settings` for settings. A layout without a `.rail` is one reading column.

## Adding an island

An island is a Vue component mounted where a participant acts. The server renders `<div data-island="name" data-props='{…}'>` around HTML that reads without JavaScript; `tangent.js` mounts the component registered under that name in `islands/index.ts`, with the props from the JSON and the element as `host`. The app mounts beside the page, since Vue empties its mount point: the server's HTML stays the first render, and the island changes its host only when it acts. A page without an island costs only the module load. An island lives in `islands/`, uses the `--ts-*` tokens and the layout classes and never a value of its own, and imports its styles, which land in Tangent's components' stylesheet, `tangent.css`.

The fragment island is a server-rendered resource that can refresh itself. Its one prop is the resource's `url`; `refresh()` fetches it with `Accept: text/html` and the `Tangent-Fragment` header and swaps the host's children for the children of the fragment's root element, which is the host as the server renders it.

## Font licences

All three typefaces are under the SIL Open Font License 1.1. The full licence text with each family's copyright travels with the fonts:

- Newsreader: [`fonts/newsreader-OFL.txt`](fonts/newsreader-OFL.txt), Copyright 2020 The Newsreader Project Authors.
- Schibsted Grotesk: [`fonts/schibsted-grotesk-OFL.txt`](fonts/schibsted-grotesk-OFL.txt), Copyright 2023 The Schibsted-Grotesk Project Authors.
- IBM Plex Mono: [`fonts/ibm-plex-mono-OFL.txt`](fonts/ibm-plex-mono-OFL.txt), Copyright 2017 IBM Corp.
