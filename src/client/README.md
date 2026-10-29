# Client

The design foundation every page of a Tangent Space stands on, built by Vite into one stylesheet and one script.

| File | What it holds |
| --- | --- |
| `tokens.css` | The design tokens as `--ts-*` custom properties: type scale, radii, layout sizes, shadows, Paper and Ink, the four Tangent Space accents (gold, jade, iris, copper), the six Tangent tones and the role colours. Values are the design reference's, held there by the token parity test. |
| `fonts.css` and `fonts/` | Newsreader (variable: weight 200–800, optical size 6–72), Schibsted Grotesk (variable: weight 400–900) and IBM Plex Mono (400 and 500), Latin and Latin Extended, as files in the tree declared with `@font-face`. Nothing is fetched from a font service, so pages render offline and captures are deterministic. The files come from the Fontsource packages in `devDependencies`; their licence texts sit beside them. |
| `layout.css` | The frame: base elements, the masthead, the path bar with its elbow, the layout grid with its rail, the footer and the breakpoints. |
| `appearance.ts` | System, Light or Dark, set as `data-theme` on `<html>` and remembered per browser in `localStorage`; the accent as `data-accent`. |
| `main.ts` | The entry: imports the three stylesheets and restores the saved appearance. |

## Build and test

Node 26 (`.node-version`).

```
npm ci
npm run build
npm test
```

`npm run build` type-checks and writes `dist/tangent.css`, `dist/tangent.js` and `dist/fonts/`. URLs inside the stylesheet are relative, so the files can be served under any path of the base URL. `npm test` builds the stylesheet in memory and checks that every token in `tests/reference/tokens.css` and `tests/reference/tokens.json`, the design reference's tokens kept verbatim, is declared in the same block with the same value.

## Captures for the visual loop

```
npm ci
npx playwright install chromium
npm run capture -- door
```

Playwright is a devDependency, and `npx playwright install chromium` fetches its Chromium: the one download, Playwright's own. `npm run capture -- <screen>` type-checks `capture/`, builds `src/server` and starts it the way the test fixture starts a Tangent Space — on a free loopback port over a new temp data folder, with any `Tangent:*` settings a state names — then stops it and deletes the folder. Pages render with the base URL `http://tangent.localhost`, which the browser maps to that port, so no capture shows the port. `--url <base URL>` captures a Tangent Space that is already running instead; `--server <path>` starts a published `Tangent.Server` instead of building one.

Every screen is captured at 1440, 1024 and 390 wide, in light and dark (the device's colour scheme emulated and the stored theme choice set), in every state the registry declares: device scale factor 1, animations off, the network idle and `document.fonts.ready` awaited. Only the Tangent Space answers the browser; any other request is refused. A page that opens a dialog fails the round. The output is `.dream-loop/<screen>/<n>/<width>-<theme>-<state>.png` at the repository root, `<n>` the next round for that screen, with a `README.md` describing the round; `.dream-loop/` is ignored by git, and a failed round leaves no folder.

`capture/screens.ts` is the registry. `screens` names each screen's path and its states; a state is reached by a query, by settings the Tangent Space starts with, or by a click in the page. `references` names the reference's file for a screen at each width the reference draws it: 1440 for every reference screen, 390 for the three phone screens.

The design reference is configured, never part of the tree: put the package (the folder holding `screens/`, `spec/` and `tokens/`) at `.dream-loop/reference`, or set `TANGENT_REFERENCE` to its folder; the script says so when neither is there. Beside the captures it renders the reference's screen for each width and theme as `<width>-<theme>-reference.png`, at the size the screen is drawn, with the reference's font requests answered by the bundled fonts so it renders offline in the same typefaces. The round's `README.md` names each width the reference does not draw and each screen it does not have — the door, take this on a tangent, bring your agent, consent, connections. A screen the reference has and Tangent does not yet, such as `sign-in`, gets a round of the reference alone.

## A page

```html
<html lang="en" data-accent="jade">
  <head>
    <link rel="stylesheet" href="…/tangent.css">
    <script src="…/tangent.js"></script>
  </head>
  <body>
    <header class="masthead">…</header>
    <nav class="path-bar"><span class="path-elbow"></span><ol class="path-crumbs">…</ol></nav>
    <div class="layout layout-reading">
      <aside class="rail">…</aside>
      <main>…</main>
    </div>
    <footer class="footer">…</footer>
  </body>
</html>
```

The script is a classic script in `<head>`, without `defer`, so the saved appearance is on `<html>` before the first paint. That has two consequences for everything the entry grows to hold: code that touches the page must wait for the parsed document, and the whole script blocks the first render on a first visit, before the browser has cached it. Whether the theme step splits from the islands into a script of its own is the islands bootstrap's decision. The server renders `data-accent` from the Tangent Space; without it the accent is gold. Without a saved choice `data-theme` stays off and the device decides.

The layout grid is `.layout` for the door and Tangent pages, `.layout-reading` for a Topic and the pages read like one, and `.layout-settings` for settings. A layout without a `.rail` is one reading column.

## Adding an island

An island is a Vue component mounted where a participant acts. It lives in this folder beside the foundation, uses the `--ts-*` tokens and the layout classes and never a value of its own, and is reached from `main.ts`, the one entry. Anything that touches the page runs after the document is parsed, since the entry runs in `<head>`. Its styles are imported by the island and land in the one stylesheet.

## Font licences

All three typefaces are under the SIL Open Font License 1.1. The full licence text with each family's copyright travels with the fonts:

- Newsreader: [`fonts/newsreader-OFL.txt`](fonts/newsreader-OFL.txt), Copyright 2020 The Newsreader Project Authors.
- Schibsted Grotesk: [`fonts/schibsted-grotesk-OFL.txt`](fonts/schibsted-grotesk-OFL.txt), Copyright 2023 The Schibsted-Grotesk Project Authors.
- IBM Plex Mono: [`fonts/ibm-plex-mono-OFL.txt`](fonts/ibm-plex-mono-OFL.txt), Copyright 2017 IBM Corp.
