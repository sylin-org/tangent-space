# ASCII atmospheres

Eight procedural scenes give Tangent its BBS atmosphere: Spiral Galaxy (rotating dust arms), Synapses (travelling signals), Aurora (folded light curtains), Tidal Lines (interference contours), Orrery (orbital rings), Mycelium (branching filaments), Silver Rain (falling light and ripples), and Nebula (flowing noise clouds).

The Atmosphere button opens a live scene preview and eight static thumbnails. Choices apply locally to the browser; Use server settings removes the override. Owners can set the current choice for the server. A scene palette or custom colour, intensity, motion, Mouse Spotlight, and background-off are available. Ownership is checked by the existing server governance operation, not by the visibility of the button.

`PATCH /api/server` accepts:

| Field | Values | Default |
| --- | --- | --- |
| `backgroundScene` | `galaxy`, `synapses`, `aurora`, `tides`, `orrery`, `mycelium`, `rain`, `nebula`, `none` | `galaxy` |
| `backgroundColor` | `#RRGGBB` or empty for scene colours | empty |
| `backgroundIntensity` | integer 0–100 | 35 |
| `backgroundMotion` | boolean | true |
| `backgroundMouseSpotlight` | boolean | true |

These settings persist with the server entity in the existing host-mounted database. Server changes emit the existing activity event; connected BBS clients refresh their settings, while personal overrides remain personal.

One decorative canvas uses an ASCII glyph atlas and procedural light fields. Characters keep a roughly constant size as the viewport grows, adding cells instead of enlarging a small image. Stars, neural nodes and orbital rings gain detail on larger grids. Only very large viewports hit the approximately 42,000-cell budget. Device pixel ratio affects sharpness separately. Resize recomputes the composition; gallery thumbnails are static, and only the selected preview animates.

Animation is limited to 15 frames per second (10 on slower frames). Hidden tabs stop scheduling animation frames. Reduced-motion and Data Saver preferences force a still scene; participants can also pause locally. The canvas is hidden from assistive technology and never captures pointer events. Conversation panes remain mostly opaque, while the hero and onboarding surface let a little light through. No external media, shader library, or background network traffic is required.

Mouse Spotlight enriches glyph colour and brightness near the mouse, with a smooth radial fade into the usual subdued scene. Its radius adapts to the viewport. Pointer-only updates reuse the current light field and preserve paused scene time; they stop when the pointer stops moving. Leaving the page or switching away clears the spotlight. Touch does not activate it, and forced-colour mode disables it. Pointer coordinates remain local to the page.
