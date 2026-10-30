# Journeys

What a person or an agent meets, end to end, step by step, as the tree stands. Each step says what happens and whether it **works**, has **friction**, or is **missing**. A friction step carries its delight-filter score, the experience it costs against what it gives; a missing step names the package or the [FEATURES](FEATURES.md) row that brings it. Walked on a running server after every slice; last walked 29 October 2026, on `dev` at `ad181c9`.

## 1 · Running a Tangent Space

The operator, on their own computer or a hosted machine.

| Step | What happens | State |
| --- | --- | --- |
| Get the tree | The README says what Tangent Space is and points at the documents. It doesn't say how to run one. | missing: a short "Run your own" section (package 5.6) |
| Build from a fresh clone | `scripts/prepare-framework.ps1` clones Koan from GitHub at the pinned revision. That revision has not been pushed, so a fresh clone cannot build. | missing: Koan's `dev` pushed (Leo's word) |
| Start it | `pwsh scripts/tangent.ps1 publish`, then `launch`: one executable, one data folder, port 5220. Or `docker compose up -d`. Neither asks for an address. | works |
| First visit | One button, **Sign in with Bluesky**; the account chooser is Bluesky's own. Accounts on another server sign in by handle, behind "Use another server". | works (1.14) |
| Setting up | The first sign-in, or the account `Tangent:Operator` names, sets the place up. It is named from the handle ("Leo's Tangent Space"); the setter becomes proprietor, operator and administrator; they land on the ready page. | works (1.12, 1.14) |
| The ready page | It prints the three pointers a site places to point at the Tangent Space. On a loopback address no site can use them: the DNS record reads `_tangent.127.0.0.1`. | friction: show them once the place has a public address, and say so until then (package 5.6) |
| The address | It is learned from the address setup happens on. When the place is reached at a public `https` address while its own is local, the operator's door offers it: Use it, or Not this one. A proxy on the same machine is believed as it is. | works (1.13) |

## 2 · Shaping the place

The proprietor and the people they trust.

| Step | What happens | State |
| --- | --- | --- |
| The first door after setup | One Tangent, the private moderators' Tangent, and "Nothing written here yet". There is nowhere public to talk, and anyone else who signs in sees no Tangent at all. | missing: a first Tangent made at setup (package 5.6) |
| Create a Tangent | **New Tangent** on the door's rail and in the Tangent switcher, for whoever holds `ManageSpace`: one field, the name; the rest defaulted behind one quiet disclosure. | works (5.5); on a phone the door hides its rail, so New Tangent is reached from a Tangent's switcher, held for the polish stage |
| A Tangent's name, description, kind, order and audience | **Settings** on a Tangent's page, for whoever holds `ManageTangent` or `ManageSpace`: the name (its address stays), description, default kind and audience; moving it among the others needs `ManageSpace`. The moderators' Tangent keeps its access. | works (5.5) |
| The place's name and description | Set from the handle at setup; nothing changes them. | missing (package 5.6) |
| Appearance: accent, light or dark | Each reader picks System, Light or Dark for themselves; the place has no accent of its own. | missing (slice 9) |
| Roles and permissions | Seeded roles: `everyone`, member, moderator, administrator. Setup makes the proprietor an administrator, and nothing makes anyone a moderator. | missing: making a participant a moderator or an administrator, then the role editor (packages 5.3, 5.1) |
| The people here | No directory, and no time-out or ban. | missing (package 5.3) |
| Admission | Anyone who signs in joins. The place can't be made apply-to-join, and new accounts can't be held for approval. | missing (package 5.2) |
| Reports | A report reaches the private moderators' Tangent, taken on a tangent from the Post. | works (2.8); its readers today are the administrators alone, until moderators can be named |

## 3 · Taking part

A person, signed in or not.

| Step | What happens | State |
| --- | --- | --- |
| Arriving as a visitor | The door: whose place, what's featured, what's alive, who is around; the Tangents and their Topics. | works |
| Signing in | One button. | works |
| What's new | The door, since the reader's checkpoint: replies to them, mentions, Topics taken on a tangent from their Posts, new per Tangent, new articles, and Mark all seen. | works |
| Talking | Start a Topic, reply, mention, edit, remove, take on a tangent, report, watch; live updates while a page is open. Drafts survive a closed tab. | works |
| Someone's profile | A name is a name; there is no profile to open. | missing (package 5.4) |
| Notifications | The door's groups cover replies and mentions; there is no page of them. | missing (6.1) |
| Search | None. | missing (6.3) |
| On a phone | It works; the layouts are tuned at the polish stage. | friction, held for the polish stage |

## 4 · An agent

| Step | What happens | State |
| --- | --- | --- |
| Finding a place | `tangent.json`, `llms.txt`, and the site's pointers. Every page answers JSON. | works |
| Acting as a person's delegate | The Device flow, a code, and the consent page. | works |
| Coming as itself | An atproto identity proved and exchanged for a bearer. | missing (slice 8) |
| Bring your agent | One button that hands the place to a listening Companion Lobby. | missing (slice 8) |

## 5 · Publishing and keeping

| Step | What happens | State |
| --- | --- | --- |
| Articles | An article is a Post kind; there is no article editor or cover yet. | missing (slice 3) |
| Publishing to atproto | `site.standard.document` under the proprietor's Connection. | missing (slice 3) |
| Feeds | None yet. | missing (slice 4) |
| Backup and restore | `scripts/tangent.ps1 backup` and `restore`, in the container too. | works |
| Export and the static archive | None yet. | missing (slice 7) |

## The order these close in

A place nobody can shape can't be used, so the proprietor's tools come before articles:
1. **The address offer (1.13).**
2. **Creating and configuring Tangents (5.5).**
3. **The proprietor's place (5.6):** a first Tangent at setup, the place's own name and description, the ready page's pointers once there is a public address, and "Run your own".
4. **The people here (5.3):** naming moderators and administrators, time-out and ban.
5. **Admission (5.2).**
6. **Profiles (5.4).**
7. **Notifications (6.1),** which also hold the operator's offers.
8. **Slice 3, articles and publication.**
