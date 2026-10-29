# Client

The design foundation every page of a Tangent Space stands on, built by Vite into one stylesheet and one script.

| File | What it holds |
| --- | --- |
| `tokens.css` | The design tokens as `--ts-*` custom properties: type scale, radii, layout sizes, Paper and Ink, the four Tangent Space accents (gold, jade, iris, copper), the six Tangent tones and the role colours. Values are the design reference's, held there by the token parity test. |
| `fonts.css` and `fonts/` | Newsreader, Schibsted Grotesk and IBM Plex Mono as files in the tree, declared with `@font-face`. Nothing is fetched from a font service, so pages render offline and captures are deterministic. |
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

`npm run build` type-checks and writes `dist/tangent.css`, `dist/tangent.js` and `dist/fonts/`. URLs inside the stylesheet are relative, so the files can be served under any path of the base URL. `npm test` builds the stylesheet in memory and checks that every token in `tests/reference/tokens.css`, the design reference's tokens kept verbatim, is declared in the same block with the same value.

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

The script is a classic script in `<head>`, without `defer`, so the saved appearance is on `<html>` before the first paint. The server renders `data-accent` from the Tangent Space; without it the accent is gold. Without a saved choice `data-theme` stays off and the device decides.

The layout grid is `.layout` for the door and Tangent pages, `.layout-reading` for a Topic and the pages read like one, and `.layout-settings` for settings. A layout without a `.rail` is one reading column.

## Adding an island

An island is a Vue component mounted where a participant acts. It lives in this folder beside the foundation, uses the `--ts-*` tokens and the layout classes and never a value of its own, and is reached from `main.ts`, the one entry. Anything that touches the page runs after the document is parsed, since the entry runs in `<head>`. Its styles are imported by the island and land in the one stylesheet.

## Font licences

All three typefaces are under the SIL Open Font License 1.1 (https://scripts.sil.org/OFL), as their files declare:

- Newsreader: Copyright 2020 The Newsreader Project Authors (https://github.com/productiontype/Newsreader).
- Schibsted Grotesk: Copyright 2023 The Schibsted-Grotesk Project Authors.
- IBM Plex Mono: Copyright 2017 IBM Corp.
