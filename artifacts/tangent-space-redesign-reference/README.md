# Tangent Space redesign · reference package

Round 3 · September 29, 2026 · 22 screens

This is the build reference for the Tangent Space redesign. It contains every page of the app on the converged system, the design tokens, and the rules behind them. It was exported from the design canvas *Tangent Space Redesign*, page *Round 3 · All pages*.

## What's inside

```
tangent-space-redesign-reference/
├── README.md                          this file
├── spec/
│   ├── 01-principles.md               the system, rule by rule
│   ├── 02-layout.md                   frames, containers, rails, breakpoints, phone
│   ├── 03-components.md               every recurring piece, with sizes, tokens and states
│   ├── 04-voice-and-words.md          voice rules, the dictionary, before → after strings
│   ├── 05-roles-permissions-access.md roles, the permission catalog, access rules, moderation
│   ├── 06-screens.md                  the 22 screens: what each shows, what's real, what's sample
│   └── 07-open-questions.md           proposals to confirm, sample content, known gaps
├── tokens/
│   ├── tokens.css                     CSS custom properties (--ts-*): light, dark, system, accents
│   └── tokens.json                    the same values, with contrast ratios
└── screens/
    ├── index.html                     start here: a gallery of every screen
    ├── 01-space-home.html … 22-phone-discussion-topic.html
    ├── assets/                        images the screens use
    └── viewer.css, viewer.js          the reference bar (theme and accent switcher); not part of the design
```

## How to use it

- **Look:** open `screens/index.html` in a browser. Each screen opens at full size. The bar on top switches between System, Light and Dark, and between the four Space accents. Add `?theme=dark` or `?accent=jade` to a screen's address to link straight to a variant.
- **Measure, don't copy:** the screens are static HTML with inline styles that reference the tokens (`var(--ts-…)`). They're exact about spacing, type and colour. They aren't production markup. Build components from `spec/03-components.md` on top of `tokens/tokens.css`.
- **Read:** `spec/01` → `02` → `03` → `04` → `05`. Then use `06` as the checklist, screen by screen.
- **Fonts** load from Google Fonts: Newsreader, Schibsted Grotesk and IBM Plex Mono. Offline, the fallbacks (Georgia, Helvetica Neue, monospace) show instead.

## Theming in one paragraph

Put `tokens.css` on the page. Set `data-theme="light"` or `data-theme="dark"` on `<html>` from the person's Appearance choice. Leave it off for System, which is the default and follows the device. Set `data-accent="gold|jade|iris|copper"` from the Space's accent. The derived tones (reply seams, soft fills, text selection) follow the accent by themselves. A Tangent's own accent is one of six named tones (`--ts-tangent-amber` … `--ts-tangent-rose`). Each has a `-tint` and `-ink` pair for letter tiles.

## For a coding agent working in the Tangent Space codebase

1. Load `tokens/tokens.css`, or map `tokens.json` into the existing theme, before changing components. Keep the `--ts-` names so the spec and the code stay searchable against each other.
2. Treat the dictionary in `spec/04-voice-and-words.md` as binding for every string. In the UI it's Space, not server; Tangent, not community; topic, not thread; post, not comment. Only Space and Tangent are capitalised.
3. The permission catalog in `spec/05` keeps the app's existing permissions and changes only their names and descriptions. Rename labels; don't change behaviour.
4. Anything marked **Proposal** in `spec/06` and `spec/07` needs Leo's decision before it's built as drawn. Anything marked **Sample text** is placeholder content, not product copy.
5. Accessibility is part of the spec. Use real buttons, links, labels and inputs. Icon-only buttons get `aria-label`. Text meets 4.5:1 in both themes. Touch targets are at least 44px on phones.

## What's real, and what isn't

The structure, the labels and the settings are drawn from the current app. The settings follow Leo's screenshots of About & appearance, People, Roles, Administrators, Tangent About & appearance and Tangent Access. Some text was written for the mockups: the replies in "What kind of discussions?", two article summaries, the notifications and a few figures. Three screens are proposals because the current version hasn't been seen: Sources, Storage and parts of Space Access. `spec/06-screens.md` marks each case, and `spec/07-open-questions.md` lists what to confirm.

Not in this package: the article page with replies beside or below it, the quoted-passage state and the phone reading views. They're on the canvas page *Round 2 · Converged*, and `spec/01` and `spec/03` describe their rules.

## Credits

The photo in "MCP 2026-07-28: The Kid Looks Adorable in a Tuxedo" is by Marko Milivojevic, via Pixnio, CC0. The other images, including covers, atmospheres, the landing image, the Space mark and the avatars, come from the current app.
