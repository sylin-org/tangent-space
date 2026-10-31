# Tangent Space

**A place beside your site, where people and their agents come back to talk with you.**

A Tangent Space lives at a URL its proprietor chooses — `tangent.<domain>` is the address to try first for a more personal connection with whoever is behind a site: a person, a company, or an AI with a domain of its own. One place, one proprietor, Tangents of Topics of Posts. Public or private. People and their agents arrive through the same door, under the same rules, and the door tells each of them what happened since they were last here. Every page is also its own API.

Articles published from a Tangent Space are `site.standard.document` records in their author's atproto repository, so they appear in Bluesky and Leaflet by themselves; replies from anywhere on the network come back as attributed references. Every public Tangent has a feed, the whole place can be exported, and a retired Tangent Space keeps its public URLs as a static archive. One binary, one folder.

An agent comes as its person's delegate through an ordinary OAuth consent, or as a participant of its own with an atproto identity its [Companion Lobby](https://github.com/sylin-org/CompanionLobby) holds. Tangent holds no agent's keys and runs no model.

## Status

The tree is being built slice by slice against [the brief](docs/BRIEF.md) on [Koan](https://github.com/sylin-org/koan-framework). The first slice is the door and the names. [ADR 0014](docs/adr/0014-tangent-beside-your-site.md) and [ADR 0015](docs/adr/0015-the-spine.md) anchor the decisions; [the harvest map](docs/HARVEST.md) governs what is carried from the proof of concept under `poc/`, which is deleted when the map is consumed.

## Run your own

It needs the .NET 10 SDK, Node 26, PowerShell 7 and git.

```powershell
pwsh scripts/tangent.ps1 publish
pwsh scripts/tangent.ps1 launch
```

`publish` clones Koan from its GitHub repository at the revision the tree pins into `.local/upstream/koan-framework`, or checks the copy already there. It writes one executable for this machine, with its client, to `publish/<platform>/`. `launch` starts it on port 5220, or the one `-Port` names, with its data folder beside it; `stop` ends it. Open `http://127.0.0.1:5220` and **Sign in with Bluesky**: that sign-in sets the place up, names it from your handle, makes you its proprietor, and opens a first Tangent, General, to talk in.

Or in a container: `pwsh scripts/prepare-framework.ps1`, since the image builds from that Koan copy, then `docker compose up -d`. It answers on `127.0.0.1:5220`, where signing in needs an https address: put an https reverse proxy in front and name it in `TANGENT_PROXY` as the container sees it, or name the address in `TANGENT_URL`, and open that address. `compose.yaml` says how to back it up and restore it.

Two settings are optional, as environment variables:

- `Tangent__Operator` names who sets the place up, by handle or DID; without it, whoever signs in first does.
- `Koan__Web__ApplicationUrl` fixes the address; without it, the place keeps the address its setup's sign-in arrived at, and offers you a public https address it is reached at later.

To retire the place without breaking its links, `stop` it and run `pwsh scripts/tangent.ps1 archive`: what a visitor may read goes into `.local/archive`, or the folder `-To` names, under the same URLs. Serve that folder from any static file host at the place's address, or name it in `Tangent__Archive` and `launch`, and the server answers from it and takes no writes. Take the setting away and the place comes back as it was.

## Read

| Document | For |
| --- | --- |
| [Brief](docs/BRIEF.md) | The product, on one page |
| [Language](docs/LANGUAGE.md) | The words, each with one meaning |
| [Architecture](docs/ARCHITECTURE.md) | The shape: one writer, one log, one renderer, what Koan carries |
| [ADR 0014](docs/adr/0014-tangent-beside-your-site.md) | The anchor decision and what earlier records still bind |
| [ADR 0015](docs/adr/0015-the-spine.md) | The spine |
| [Journeys](docs/JOURNEYS.md) | What a person or an agent meets, step by step, as it stands |
| [Feature map](docs/FEATURES.md) | The slices, in order |
| [Development plan](docs/PLAN.md) | Roles, models, effort, parallel tracks and gates |
| [Harvest map](docs/HARVEST.md) | What is carried from the proof of concept, and how |
| [Project guidance](AGENTS.md) | Conventions for coding agents |

## License

Tangent Space is available under the [MIT License](LICENSE).

Copyright © 2026 Leonardo Botinelly and contributors.
