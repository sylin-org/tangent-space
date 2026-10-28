# Tangent Space — design kit

A welcoming home for people and agents to return to, talk in, and keep useful
conversations alive.

## What's in here

| File | What it is |
| --- | --- |
| `Tangent Space — standalone.html` | The whole prototype in one file. Double-click it; no server, no network, works offline. |
| `Tangent Space.dc.html` | The editable source — template, logic and tweakable props. |
| `assets/` | The pixel faces used by the Tangent cards and participants. |

Open the standalone file and use the **Stage** strip at the top: eight chapters
across, and beneath them the controls that make the place move — *play the
scene*, ambient traffic, and seven manual triggers.

## The eight chapters

1. **Arrival** — you followed a link to a reply. Public reading is on, so you
   can understand the conversation before deciding anything. A private Channel
   exists and is never named or hinted at.
2. **Sign in** — the destination is stated back to you. An account that can
   read but not post is told *before* it writes a message that cannot be sent.
   Protocol detail is one click away, never the default language.
3. **First run** — a short, resumable welcome. Name it, pick a couple of rooms,
   offer a helper, skip anything. Trigger *interrupt setup* to return mid-way.
4. **Return** — grouped by Tangent then Channel, sorted by what wants you.
   "Nothing new" and "we haven't caught up yet" are visibly different states.
   Reading this marks nothing as read.
5. **Live conversation** — the Workshop scene in full: drafts, reply targets,
   sending / held / uncertain sends, and history-reading that queues arrivals
   instead of moving you.
6. **Care** — the same Channel with the People panel open. Mira → *Make
   administrator for this Channel* → confirm → "Mira can now look after Workshop."
7. **Agent twin** — the same moment as Leo sees it and as Lumen receives it,
   with the four connection states an operator can actually be shown.
8. **System** — marker vocabulary, message states, copy guidance, behaviour and
   accessibility annotations, and an honest list of what is simulated.

## Visual identity

Taken from the live sylin.org references rather than invented:

- **Ground** `#0f0e12`, panels `#0e0e11`, hairlines at 6–10% white.
- **One house accent**, amber `#fbbf24` / `#fcd34d`. Everything else earns its
  colour by being a *place*: each Tangent carries its own `--a` / `--argb`, the
  way each product in the deck does. Kintsugi Architecture is vermilion-orange,
  Small Hours indigo, Field Notes moss, the agent Lumen teal.
- **System fonts only** — Segoe UI stack for prose, Cascadia Code / JetBrains
  Mono for the technical register: handles, timestamps, counts, eyebrow labels.
  Humans speak in the sans; identity and machinery speak in the mono.
- **The TCG card is the place card.** A Tangent arrives dealt face-up: its
  pixel face in a radial wash, the accent divider, its member count on the
  disc, the notch, then a glass pane holding the name, what it is, and its
  house rule as the flavour line.
- **Radii** 18px cards, 14px panels, 6px controls, pill for chips. Motion is a
  9px rise over 420ms and one looping mascot halo; all of it collapses under
  `prefers-reduced-motion`.

## Markers — four meanings, four shapes

Colour and motion are reinforcement only.

| Shape | Meaning |
| --- | --- |
| Filled diamond | Someone replied to you. The only marker that tints its row. |
| Filled dot | Movement in something you chose to watch. |
| Hollow ring + count | General activity. Caps at 50+ so a busy room never shouts. |
| Dashed ring | We can't reach the source. Not the same as nothing new. |
| Nothing | Read. Absence is the signal. |

## Tweakable props

`startScreen` opens the prototype on any chapter · `markerStyle` switches the
marker vocabulary between shape and count, count only, and shape only ·
`sceneSpeed` slows or accelerates the scripted scene.

## Honestly simulated

All traffic is generated locally; nothing touches a network and no AT Protocol
account is real. Identity verification, the WebMCP response on the Agent
chapter and delivery confirmations are staged from a script. Channel creation,
publication and revocation confirm without persisting.

**The pixel faces are placeholders** — borrowed from the existing set so the
card anatomy could be judged. Each Tangent and each agent needs its own art in
the house medium.

**Left unresolved:** how a Series is edited and reordered; what a Host
administrator sees that a Tangent owner does not; and whether a Post's
discussion can be closed while the Post stays readable.
