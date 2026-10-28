---
name: visual-loop
description: Score a Tangent screen against the design reference and the rubric, iterate until it passes the gate, and record the result. Run it whenever a change touches what a participant sees.
---

# Visual loop

A screen is done when an independent scorer rates fixed captures of it at or above the gate. The builder never scores its own work.

## Targets

- The design reference: `artifacts/tangent-space-redesign-reference` (its `spec/` and `tokens/`, with the screens' vocabulary read as Tangent for Space and Board for Tangent).
- For a screen with no reference (the door, take this on a tangent, bring your agent), the builder produces a mock first; Leo approves it; the mock is the target from then on.

## Captures

`npm run capture -- <screen>` in `src/client` renders the screen with seeded fixture data and bundled fonts at 1440, 1024 and 390 wide, in light and dark, in every state the screen has (loading, empty, populated, partial, error). Output: `.dream-loop/<screen>/<n>/` with the reference rendered beside it at the same width and theme. `.dream-loop/` is ignored.

## Rubric

Each dimension 0–10, with a one-line reason and the one change that would raise it most.

1. **Fidelity**: layout, spacing, type scale and tokens match the reference system. Hard gate: no off-system value.
2. **Hierarchy**: in two seconds, where am I and what is new?
3. **Typography and rhythm**: Newsreader, Schibsted Grotesk and IBM Plex Mono used as the spec says; measure, leading, baseline.
4. **Color and contrast**: tokens only; 4.5:1 in both themes; accents restrained; cards legible over backgrounds.
5. **Responsiveness**: 390 with no horizontal scroll; 44px targets; narrow layouts keep the place, reading position and draft.
6. **States**: loading, empty, partial and error are designed and honest.
7. **Affordance and accessibility**: real controls, visible focus, keyboard paths, no dialogs.
8. **Character**: it reads as someone's place; warm; nothing generic.

## Gate

Mean ≥ 8.5, no dimension below 7, no hard-gate failure, at every width, theme and state.

## Scorer

Spawn a subagent with a fresh context. Give it only: the captures, the reference captures, the relevant `spec/` sections, this rubric. Never the builder's reasoning or diff. It returns a scorecard per width and theme. If any mean falls within 0.3 of the gate, spawn a second scorer; the lower result stands.

## Loop

1. Capture. 2. Score. 3. If it fails, fix the largest deficits and return to 1. At most four rounds; then stop and bring Leo the best attempt, the scorecard and the sticking points.

## Record

When a screen passes, add or update its row in `docs/SCREENS.md`: screen, mean, lowest dimension, widths, themes, commit, date. The file holds current scores only.
