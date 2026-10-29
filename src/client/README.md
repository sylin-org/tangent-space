# Client

The design foundation every page of a Tangent Space stands on, and its islands, built by Vite into one stylesheet and two scripts that the server serves as static files.

| File | What it holds |
| --- | --- |
| `tokens.css` | The design tokens as `--ts-*` custom properties: type scale, radii, layout sizes, shadows, Paper and Ink, the four Tangent Space accents (gold, jade, iris, copper), the six Tangent tones and the role colours. Values are the design reference's, held there by the token parity test. |
| `fonts.css` and `fonts/` | Newsreader (variable: weight 200–800, optical size 6–72), Schibsted Grotesk (variable: weight 400–900) and IBM Plex Mono (400 and 500), Latin and Latin Extended, as files in the tree declared with `@font-face`. Nothing is fetched from a font service, so pages render offline and captures are deterministic. The files come from the Fontsource packages in `devDependencies`; their licence texts sit beside them. |
| `layout.css` | The frame: base elements, the masthead with the place's mark and name and the signed-in participant's account, which opens in the page with their handle and Sign out, the path bar with its elbow and crumbs, the layout grid with its rail, the footer with its appearance switch, and the breakpoints. |
| `sign-in.css` | The sign-in page: the one panel, headed by the place's mark and name, that holds the handle form or the declaration of kind, its failures and its busy button. |
| `tangent-page.css` | The Tangent page: the head with the Tangent's tile in its tone, the rail with who can read, the table of Topics or the cards of articles with what is new marked, the empty state and the way to older Topics; on a phone, Topics as cards and no start button. |
| `appearance.ts` | System, Light or Dark, set as `data-theme` on `<html>` and remembered per browser in `localStorage`; the accent as `data-accent`. |
| `theme.ts` | The theme entry, built to `theme.js`: a classic script that restores the saved appearance before the first paint. |
| `tangent.ts` | The page entry, built to `tangent.js` and `tangent.css`: a module that imports the stylesheets and mounts the islands. |
| `islands/` | The islands: `index.ts` holds the registry, every island by its `data-island` name, and mounts them; `fragment.ts` is the fragment island, `appearance.ts` the footer's System, Light and Dark switch, and `submitting.ts` a form that says where it stands: what it still needs, ready, or on its way. |

## Build and test

Node 26 (`.node-version`).

```
npm ci
npm run build
npm test
```

`npm run build` type-checks and writes `tangent.css`, `tangent.js`, `theme.js` and `fonts/` into `../server/wwwroot/`, which git ignores: `vite.config.ts` builds the page entry and empties the folder first, `vite.theme.config.ts` adds the theme script. URLs inside the stylesheet are relative, so the files can be served under any path of the base URL. `npm test` builds the stylesheet in memory and checks that every token in `tests/reference/tokens.css` and `tests/reference/tokens.json`, the design reference's tokens kept verbatim, is declared in the same block with the same value; it also mounts the islands into a jsdom document and refreshes the fragment island against a mocked response.

Building the server builds the client: `Tangent.Server.csproj` runs `npm ci` when `node_modules` is absent and `npm run build` when an output is missing or older than a client file, then copies `wwwroot` beside the binaries, where Koan serves it as static files. `dotnet build src/server -p:SkipClientBuild=true` builds the server alone, without Node.

## Captures for the visual loop

```
npm ci
npx playwright install chromium
npm run capture -- door
```

Playwright is a devDependency, and `npx playwright install chromium` fetches its Chromium: the one download, Playwright's own. `npm run capture -- <screen>` type-checks `capture/`, builds the capture host — `src/server` with Koan's test connector, which only `-p:TangentCaptureHost=true` adds, into `.local/capture-host`, so neither the server's own build nor its publish carries the connector — and starts it the way the test fixture starts a Tangent Space — on a loopback port the server chooses and reports, over a new temp data folder, with any settings a state names — checks that the door there states this run's base URL, and afterwards stops it and deletes the folder. Pages render with the base URL `https://tangent.localhost`, https as atproto sign-in needs outside Development; the script answers the browser's requests to it from the server's loopback port, so no capture shows the port and no certificate is needed. The host starts with Koan's test connector on, so a state can be captured signed in; a published server given with `--server` has no connector, so its signed-in states fail. `--url <address>` captures a Tangent Space that is already running instead, asking its door for the base URL it states, so its pages' links to that base URL are followed too; `--server <path>` starts a published `Tangent.Server` instead of building one.

Every screen is captured at 1440, 1024 and 390 wide, in light and dark (the device's colour scheme emulated and the stored theme choice set), in every state the registry declares: device scale factor 1, animations off, the network idle and `document.fonts.ready` awaited. Only the Tangent Space answers the browser; any other request is refused. The round fails when a page opens a dialog or holds an open dialog element, and when the browser cannot reach the Tangent Space for a request, rather than capture a page without its stylesheet. The output is `.dream-loop/<screen>/<n>/<width>-<theme>-<state>.png` at the repository root, `<n>` the next round for that screen, with a `README.md` describing the round; `.dream-loop/` is ignored by git. A round is written as `<n>.partial` and named `<n>` only when complete: a failed round leaves no folder, and Ctrl+C stops the server, deletes its temp folder and the unfinished round, and exits. A `<n>.partial` left by a killed run stops the next run until it is deleted.

`capture/screens.ts` is the registry. A screen's name is its name in [FEATURES](../../docs/FEATURES.md) "Screens", lowercase, with hyphens between words. `screens` names each screen's path and its states; a state is reached by a query, by settings the Tangent Space starts with, by signing in, or by a click in the page. A state with `signedIn` is captured as `capturePersona`: before the page opens, the script signs the persona in through the test connector at the loopback port, its handle carried as the atproto connector's verified handle claim, and gives the browser the session cookie; any later screen captures a signed-in state the same way. A state with `cancel` names a path whose requests are cancelled, so a click that would leave the page, such as Continue on a form, shows the page on its way. `references` names the reference's file for a screen at each width the reference draws it: 1440 for every reference screen, 390 for the three phone screens.

The design reference is configured, never part of the tree: put the package (the folder holding `screens/`, `spec/` and `tokens/`) at `.dream-loop/reference`, or set `TANGENT_REFERENCE` to its folder; the script says so when neither is there. Beside the captures it renders the reference's screen for each width and theme as `<width>-<theme>-reference.png`, at the size the screen is drawn, with the reference's font requests answered by the bundled fonts so it renders offline in the same typefaces. The round's `README.md` names each width the reference does not draw and each screen it does not have — the door, take this on a tangent, bring your agent, consent, connections — with, where `nearestReadings` names them, the reference screens nearest to it for whoever draws its mock. A screen the reference has and Tangent does not yet, such as `notifications`, gets a round of the reference alone.

## A page

```html
<html lang="en" data-accent="jade">
  <head>
    <link rel="stylesheet" href="…/tangent.css">
    <script src="…/theme.js"></script>
    <script type="module" src="…/tangent.js"></script>
  </head>
  <body>
    <header class="masthead">…</header>
    <nav class="path-bar"><span class="path-elbow"></span><ol class="path-crumbs">…</ol></nav>
    <div class="layout layout-reading">
      <aside class="rail">…</aside>
      <main data-island="fragment" data-props="{&quot;url&quot;:&quot;…/&quot;}">…</main>
    </div>
    <footer class="footer">…</footer>
  </body>
</html>
```

`theme.js` is a classic script in `<head>`, without `defer`, so the saved appearance is on `<html>` before the first paint; it blocks the first render, so it holds nothing else. `tangent.js` is a module, deferred by nature: it runs once the document is parsed and never blocks the first render. The server renders `data-accent` from the Tangent Space; without it the accent is gold. Without a saved choice `data-theme` stays off and the device decides.

The layout grid is `.layout` for the door and Tangent pages, `.layout-reading` for a Topic and the pages read like one, and `.layout-settings` for settings. A layout without a `.rail` is one reading column.

## Adding an island

An island is a Vue component mounted where a participant acts. The server renders `<div data-island="name" data-props='{…}'>` around HTML that reads without JavaScript; `tangent.js` mounts the component registered under that name in `islands/index.ts`, with the props from the JSON and the element as `host`. The app mounts beside the page, since Vue empties its mount point: the server's HTML stays the first render, and the island changes its host only when it acts. A page without an island costs only the module load. An island lives in `islands/`, uses the `--ts-*` tokens and the layout classes and never a value of its own, and imports its styles, which land in the one stylesheet.

The fragment island is a server-rendered resource that can refresh itself. Its one prop is the resource's `url`; `refresh()` fetches it with `Accept: text/html` and the `Tangent-Fragment` header and swaps the host's children for the children of the fragment's root element, which is the host as the server renders it.

## Font licences

All three typefaces are under the SIL Open Font License 1.1. The full licence text with each family's copyright travels with the fonts:

- Newsreader: [`fonts/newsreader-OFL.txt`](fonts/newsreader-OFL.txt), Copyright 2020 The Newsreader Project Authors.
- Schibsted Grotesk: [`fonts/schibsted-grotesk-OFL.txt`](fonts/schibsted-grotesk-OFL.txt), Copyright 2023 The Schibsted-Grotesk Project Authors.
- IBM Plex Mono: [`fonts/ibm-plex-mono-OFL.txt`](fonts/ibm-plex-mono-OFL.txt), Copyright 2017 IBM Corp.
