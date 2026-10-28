# 02 · Layout

All desktop screens are drawn at 1440px wide and phone screens at 390 × 844. The numbers below are in CSS pixels. Tokens are named where one exists (`--ts-…`, see `tokens/tokens.css`).

## The frame

| Part | Size | Notes |
|---|---|---|
| Header | 60px tall (`--ts-header-height`) | Padding 0 32px. Surface background, 1px bottom line. Stays at the top while scrolling. |
| Path bar | 48px tall (`--ts-pathbar-height`) | Padding 0 32px, mono 13px. The elbow hangs from the Space mark. Stays under the header. |
| Page gutter | 32px (`--ts-page-gutter`) | Left and right, on header, path bar and footer. |
| Footer | 72px | 1px top line. Shown on pages that end; hidden where a composer is pinned to the bottom. |

### The elbow

This is a single element in the path bar: `position: absolute; left: 45.5px; top: -15px; width: 20px; height: 39px; border-left + border-bottom: 1px solid var(--ts-connector); border-bottom-left-radius: 10px`. It starts under the centre of the 28px Space mark in the header and ends at the first crumb, which gets `margin-left: 34px`. On phones the elbow is `left: 28.5px; top: -13px; 16 × 35; radius 8`, and the first crumb gets `margin-left: 26px`.

## Containers

Each container is centred in the page, with the left rail on its left.

| Used by | Container | Rail | Gap | Main column |
|---|---|---|---|---|
| Space home, Tangent pages (Articles, Discussions) | 1072 (`--ts-home-container`) | 216 | 56 | 800 (`--ts-home-column`) |
| Discussion topic, notifications, new topic, article editor, New Tangent, full-page article | 952 (`--ts-reading-container`) | 216 | 56 | 680 (`--ts-reading-column`) |
| Space settings, Tangent settings | 1144 (`--ts-settings-container`) | 216 | 48 | 880 (`--ts-settings-column`). With a live preview: form 492, gap 44, preview fills the rest (about 344) |
| Article with replies beside (Round 2) | full width | 200, at 32 from the left | 48 | article 640. The replies column is 472, fixed to the right edge, with a 1px left line and surface background |

- Rail width is `--ts-rail-width` (216). Rails start 40–48px below the path bar and don't scroll separately at these heights. In production, make them sticky under the path bar.
- The reading measure is 640–680px for serif body text at 16–20px.

## Mastheads

| Masthead | Height or padding | Contents |
|---|---|---|
| Space home | 196px, surface, 1px bottom line | Mark 72 (radius 16), name in display type at 46/1.05, a line only stewards see (while the byline is empty), stats at 13px. The atmosphere is drawn behind it on the right, at the Space's intensity. |
| Tangent (Articles) | Padding 36 top, 32 bottom | Tile 72 (radius 14, 6px edge), name 44/1.05, motto in italic display type at 18px, meta row at 13px (articles, replies, audience) |
| Tangent (Discussions) | Padding 32 top, 28 bottom | Letter tile 64 (radius 13, 5px edge), name 42, meta row (topics, posts, audience) |
| Settings page | Content padding-top 40 | Page title in display type at 34, then a lede at 14px in text-2, max 640 wide |

## Vertical rhythm

- Content starts 32–48px below the path bar.
- Sections sit 26–36px apart, and form fields 20px apart.
- List rows are 58–76px tall. Each list sits in a card with a 1px line and radius 12.
- Composer: pinned to the bottom of the replies column, or of the discussion's column, with a 1px top line. It never covers the last reply: scroll padding equals its height.
- Save bar: sticky at the bottom of the viewport while changes exist, 24px from the edge, as wide as the content column.

## Breakpoints (recommended)

The canvas draws the widest and narrowest cases. For the widths in between:

| Viewport | Behaviour |
|---|---|
| 1400 and up | Replies beside articles. All rails shown. |
| 1024–1399 | Replies below articles. Rails stay. Containers keep their widths and shrink their gaps first. |
| 720–1023 | The rail folds into a button at the right of the path bar ("Contents", "Topics", "Settings"). One column, 680 at most. |
| Under 720 | Phone layout, described below. |

## Phone (390 × 844)

| Part | Size | Notes |
|---|---|---|
| Header | 56px | Mark 26 and name in display type at 18. Search, notifications and account are 44 × 44 each. On a topic, it becomes a back header: back button 44, two-line title (Tangent in mono 11px, topic title at 15/600), Following, More. |
| Path bar | 44px | Mini elbow. Crumbs 40px tall. One context action on the right (Contents, Following). |
| Gutter | 20px (`--ts-phone-gutter`) | |
| Touch targets | 44px minimum (`--ts-touch-target`) | Chips 36px tall with 44px hit areas. |
| Quick reply | Pinned to the bottom | Who and audience line at 11.5px, avatar 30, pill input 44 (radius 22), send button 44 round in the accent. |
| Shelves | | The Articles shelf scrolls sideways (cards 236 wide, image 148). Discussions stack as cards. |

The phone never shows New topic or New article. Replies, follows and reading are all available.
