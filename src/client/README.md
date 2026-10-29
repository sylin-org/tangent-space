# Client

The design foundation every page of a Tangent Space stands on, and its islands, built by Vite into one stylesheet and two scripts that the server serves as static files.

| File | What it holds |
| --- | --- |
| `tokens.css` | The design tokens as `--ts-*` custom properties: type scale, radii, layout sizes, shadows, Paper and Ink, the four Tangent Space accents (gold, jade, iris, copper), the six Tangent tones and the role colours. Values are the design reference's, held there by the token parity test. |
| `fonts.css` and `fonts/` | Newsreader (variable: weight 200–800, optical size 6–72), Schibsted Grotesk (variable: weight 400–900) and IBM Plex Mono (400 and 500), Latin and Latin Extended, as files in the tree declared with `@font-face`. Nothing is fetched from a font service, so pages render offline and captures are deterministic. The files come from the Fontsource packages in `devDependencies`; their licence texts sit beside them. |
| `layout.css` | The frame: base elements, the masthead, the path bar with its elbow, the layout grid with its rail, the footer and the breakpoints. |
| `appearance.ts` | System, Light or Dark, set as `data-theme` on `<html>` and remembered per browser in `localStorage`; the accent as `data-accent`. |
| `theme.ts` | The theme entry, built to `theme.js`: a classic script that restores the saved appearance before the first paint. |
| `tangent.ts` | The page entry, built to `tangent.js` and `tangent.css`: a module that imports the three stylesheets and mounts the islands. |
| `islands/` | The islands: `index.ts` holds the registry, every island by its `data-island` name, and mounts them; `fragment.ts` is the fragment island. |

## Build and test

Node 26 (`.node-version`).

```
npm ci
npm run build
npm test
```

`npm run build` type-checks and writes `tangent.css`, `tangent.js`, `theme.js` and `fonts/` into `../server/wwwroot/`, which git ignores: `vite.config.ts` builds the page entry and empties the folder first, `vite.theme.config.ts` adds the theme script. URLs inside the stylesheet are relative, so the files can be served under any path of the base URL. `npm test` builds the stylesheet in memory and checks that every token in `tests/reference/tokens.css` and `tests/reference/tokens.json`, the design reference's tokens kept verbatim, is declared in the same block with the same value; it also mounts the islands into a jsdom document and refreshes the fragment island against a mocked response.

Building the server builds the client: `Tangent.Server.csproj` runs `npm ci` when `node_modules` is absent and `npm run build` when an output is missing or older than a client file, then copies `wwwroot` beside the binaries, where Koan serves it as static files. `dotnet build src/server -p:SkipClientBuild=true` builds the server alone, without Node.

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
