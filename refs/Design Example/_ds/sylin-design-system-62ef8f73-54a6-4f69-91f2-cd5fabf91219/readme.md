# Sylin Design System

The brand system for **sylin.org** — the web home of Sylin, an independent
software toolworks. Local-first tooling that answers to you: account-free,
telemetry-free, built to keep working when everything else goes away.

> **Stance:** Tools that answer to you.
> One keeper today; grounds built to outlive him.

This system was built **from a written design brief** — there was no existing
codebase, Figma file, or logo. The visual language, information architecture,
and component vocabulary here are original inventions that interpret the brief's
chosen creative direction: **"Night Garden — the grounds at dusk."**

## Sources

- **Design brief** — the locked identity, portfolio, creative direction, and
  hard constraints (provided as pasted text; treated as canon).
- `uploads/ghostlight-mascot.png` → `assets/ghostlight-mascot.png` — 100×100
  kawaii ghost with a teal-flamed lantern on a night-indigo ground. Sets the
  creature register and the palette (ground `#202050`, flame `#90e0f0`).
- `uploads/leo_pixel-art.png` → `assets/leo-portrait.png` — 200×200 pixel
  portrait of the keeper, Leo Botinelly. Human register; keeper page only.

No brand logo was provided. **We do not invent one** — the wordmark is the name
"Sylin" set in the display serif, optionally lit by a single flame dot. See
`guidelines/brand-wordmark.html`.

---

## The portfolio (maturity labels are honest and load-bearing)

**Flagships** (production-track): **Koan** (the stack, .NET 10, v0.17 pre-1.0) ·
**Koi** (the network, single-binary LAN toolbox, v0.9 pre-1.0) · **Ghostlight**
(the guardian, governed browser access for agents, open-core).
**Growing:** Zen Garden · Agyo · os-tools.
**Research:** Hokora · Nagi.

The keeper: **Leo Botinelly** — named steward, credited but not the brand.
Person and org are deliberately separated.

---

## CONTENT FUNDAMENTALS — how Sylin writes

**Voice: a person, not a corporation.** Plain-spoken, calm, and exact. The
governing doctrine is **delight = respect × honesty × personality** — each layer
only lands if the one before it is present.

- **Person, "you", occasionally "we".** Addresses the reader as *you* ("Tools
  that answer to you"). Speaks as *we* for the org's choices ("comparisons that
  name where we lose"). Never the passive corporate "users."
- **Honesty is the brand.** Maturity labels mean what they say. "Pre-1.0" is
  stated wherever it's true. Candor reads as a **feature, not a confession** —
  the signature move is a *"When not to use this"* note on every product.
- **No superlatives without numbers.** Never "best-in-class" or "world's most
  powerful." Claims are backed by counts: "300+ ADRs", "0 accounts", "67 KB total."
- **Casing:** Sentence case for headings and buttons ("Get started", "Explore
  the grounds"). UPPERCASE + wide tracking reserved for mono eyebrow/labels only.
- **The garden lore, lightly.** Projects are "lights in the landscape"; Sylin is
  "the grounds." Koi = the network (pond), Zen Garden, Ghostlight = the guardian
  flame. Whimsy is earned, never loud. The mascot accompanies the what-it-is
  sentence — it never replaces it.
- **The name's origin is "a story kept, not told."** Never invent an etymology.
- **No emoji.** The register is spare and storybook; personality comes from
  voice, pixel art, and the garden lore — not emoji.

Examples that are on-brand: *"Tools that answer to you."* · *"You pay for
governance, never for autonomy."* · *"v0.9. Pre-1.0, and honest about it."* ·
*"one cross-platform binary, no accounts, no cloud, works when the internet
doesn't."*

---

## VISUAL FOUNDATIONS

**Direction: "Night Garden — the grounds at dusk."** A calm Japanese-garden
register — serene, spare, storybook. The projects are lights in a nighttime
landscape. Explicitly **NOT** retro-gamer nostalgia: no scanlines, no coin-op
jokes, no arcade palette.

- **Color — “Moonlit Ink.”** Dusk is the primary experience. A cinematic
  near-black ground with a faint teal-green undertone (`--ink-900` `#070c13`),
  cool moon-pale text (`--moon-*`), and **one bright accent**: the luminous
  jewel-teal lantern **flame** (`--flame-500` `#2fd4c6`). Secondary lights are
  used rarely: warm amber lantern-glow (`--glow-400`), koi-pond vermilion
  (`--koi-400`, also = danger), and a quiet moss green (`--moss-400`, = the
  “Growing” tier). A **Dawn** theme (`[data-theme="dawn"]`) is the *same grounds
  at first light* — cool blue-grey mist, ink-teal text — not an inverted sheet.
- **Type — self-hosted, three voices + mono.** Real OFL webfonts ship with the
  system (in `assets/fonts/`), so the “no third-party CDN” value still holds
  (production should subset to woff2). **Cormorant Garamond** — a dramatic
  high-contrast literary serif — is the *brand* display voice (wordmark, hero,
  section headings). **Space Grotesk** is the dedicated *tools* voice: project
  names (Koan, Koi, Ghostlight) are set in it — a deliberate person-vs-tools
  distinction (the org speaks in serif, the tools in grotesk). **IBM Plex Sans**
  is the working body voice; **IBM Plex Mono** is the technical register for
  versions, stats, code, and eyebrow labels.
- **Backgrounds.** No photographs, no busy patterns. Depth comes from soft
  **radial glows** on the dark ground (a warm flame-glow top-right, a cool teal
  wash top-left), a faint **starfield** (tiny moon-pale dots, a few gently
  twinkling), and a few ambient **fireflies** (teal dots that drift —
  off under `prefers-reduced-motion`). Illustration medium is **pixel art**,
  rendered crisp (`image-rendering: pixelated`, integer scale).
- **Motion.** Calm and natural — nothing bounces. Standard `cubic-bezier(.4,0,.2,1)`;
  enters use a gentle ease-out. Durations 120/220/420ms. Ambient drift ~6s.
  All motion collapses under reduced-motion.
- **Hover / press.** Hover = a 1px lift + a color warm-up (primary → `--accent-hover`;
  secondary/ghost gain the tinted `--accent-quiet` fill). Press = settle back to
  `translateY(0)` + `--accent-press`. Links shift toward `--link-hover`.
- **Borders & hairlines.** Thin. Default card border is a 1px translucent white
  hairline (`--border-line`); separators are fainter (`--border-hairline`). The
  one exception is the **flame-lit** border (`--accent-line` + `--glow-flame-sm`)
  reserved for the single featured thing.
- **Shadow / glow.** Shadows are soft, cool, and low (`--shadow-sm/md/lg`) —
  depth on the night ground comes more from an inset **well** (`--shadow-inset`,
  for sunken code panels) than from drop shadows. The **glow** system
  (`--glow-flame-*`, `--glow-warm`) is the only thing that truly emits light,
  used sparingly on the accent.
- **Transparency & blur.** Used rarely: the sticky nav is a `blur(14px)` scrim
  over an 82%-opaque ground; dialog scrims use `--surface-overlay`.
- **Radii.** Storybook-round, not app-round: 3px chips, 6px buttons/inputs,
  10px cards, 16px large/project panels, 24px hero, pill for maturity labels.
- **Cards.** Flat translucent-bordered panels on `--surface-card`; `sunken` adds
  an inset well; `raised` a soft shadow; `glow` the flame border. Rounded 16px,
  generous padding, lots of negative space — the garden breathes.
- **Layout.** Centered, generous. Content maxes at ~72rem; prose at ~42rem.
  Fixed sticky nav; everything else flows. 4px spacing rhythm, large section gaps.
- **Imagery vibe.** Cool, dark, luminous — night-indigo with teal highlights.
  Pixel art is the only illustration; it is warm-kawaii against the cool ground.

---

## ICONOGRAPHY

Sylin uses **no icon font and no CDN icon set** — that would violate the
"self-contained, no third-party" value. Iconography is deliberately **sparse**;
the aesthetic is uncluttered and personality comes from pixel art, not glyphs.

- **UI glyphs** are a tiny hand-drawn **inline-SVG** set in the `Icon` component
  (`components/core/Icon.jsx`): `arrow`, `arrow-up-right`, `chevron`, `book`,
  `code`, `spark`, `dot`, `moon`, `sun`, `check`, `lantern`. Simple geometric
  strokes, 1.6px, `currentColor`. **Intentional addition** (see below) — the
  brief defines no icon set, but the site needs a few affordances (external-link
  arrow, theme toggle, back-chevron).
- **No brand marks are drawn.** GitHub/LinkedIn links are text + the generic
  up-right arrow (via `ExternalLink`) — we never reconstruct a third party's logo.
- **Pixel art PNGs** are the true illustration medium (`assets/*.png`), rendered
  crisp via `PixelSprite`.
- **No emoji, no unicode-as-icon.** A single flame **dot** (a glowing circle)
  stands in for a logo mark beside the wordmark.

---

## Components

Reusable React primitives. Namespace: `window.SylinDesignSystem_62ef8f`.

**Core** (`components/core/`):
- **Button** — primary (flame) / secondary / ghost; sizes; icon slots; `<a>` when `href` set.
- **Icon** — the spare inline-SVG glyph set (+ `ICON_NAMES`).
- **ExternalLink** — outbound text link with up-right arrow.
- **Card** — base surface: flat / sunken / raised / glow.
- **Tag** — quiet keyword chip (neutral / accent; mono option).
- **PixelSprite** — crisp pixel-art renderer, optional lantern glow.

**Patterns** (`components/patterns/`):
- **MaturityLabel** — honest, load-bearing tier (flagship / growing / research) + version chip.
- **ProjectCard** — the signature piece: mascot + what-it-is sentence + maturity + get-started links.
- **CommitmentCard** — one of The Way's four commitments.
- **Stat / StatRow** — values-as-proof (0 accounts, 300+ ADRs…).
- **Callout** — candor aside ("When not to use this"), note, or warm.

### Intentional additions
- **Icon** — the brief names no icon set, but the site needs a handful of UI
  affordances. Added as a tiny self-contained inline-SVG set (no font, no CDN)
  to honor the "self-contained" constraint.

---

## Index / manifest

Root:
- `styles.css` — global entry point (@import list only). **Consumers link this.**
- `readme.md` — this file.
- `SKILL.md` — Agent-Skill-compatible entry for downloadable use.

`tokens/` — `colors.css`, `typography.css`, `fonts.css`, `spacing.css`, `effects.css`, `base.css`.
`assets/` — `ghostlight-mascot.png`, `leo-portrait.png`, and `fonts/` (self-hosted OFL webfonts).
`components/core/` and `components/patterns/` — the primitives above (each with `.jsx`, `.d.ts`, `.prompt.md`, and a `@dsCard` HTML).
`guidelines/` — foundation specimen cards (Colors, Type, Spacing, Brand).
`ui_kits/sylin-org/` — the interactive site recreation (hub, The Way, keeper, flagship detail). Entry: `index.html`.

## Constraints honored (the site practices its values)
Static files only · no analytics/trackers/cookie banner · **fonts are self-hosted**
(OFL binaries in `assets/fonts/`, no font CDN) — the design-system *cards* load
React/Babel from unpkg for preview only; the shipped site would not · production
should subset the fonts to woff2 to hold the ~67 KB page-weight brag ·
`prefers-reduced-motion` respected · visible keyboard focus · legible contrast in
dusk and dawn.

## Never publish
Personal phone/email/street address (contact is **hello@sylin.org** + LinkedIn/
GitHub) · employer names as endorsements · the name's origin.
