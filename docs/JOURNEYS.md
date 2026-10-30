# Journeys

What a person or an agent meets, end to end, step by step, as the tree stands. Each step says what happens and whether it **works**, has **friction**, or is **missing**. A friction step carries its delight-filter score, the experience it costs against what it gives; a missing step names the package or the [FEATURES](FEATURES.md) row that brings it. Walked on a running server after every slice; last walked 29 October 2026, on `dev` at `ad181c9`.

## 1 · Running a Tangent Space

The operator, on their own computer or a hosted machine.

| Step | What happens | State |
| --- | --- | --- |
| Get the tree | The README says what Tangent Space is, points at the documents, and says how to run one: what it needs, `publish` then `launch` or `docker compose up -d`, and Sign in with Bluesky. | works (5.6) |
| Build from a fresh clone | `scripts/prepare-framework.ps1` clones Koan from GitHub at the pinned revision. That revision has not been pushed, so a fresh clone cannot build. | missing: Koan's `dev` pushed (Leo's word) |
| Start it | `pwsh scripts/tangent.ps1 publish`, then `launch`: one executable, one data folder, port 5220. Or `docker compose up -d`. Neither asks for an address. | works |
| First visit | One button, **Sign in with Bluesky**; the account chooser is Bluesky's own. Accounts on another server sign in by handle, behind "Use another server". | works (1.14) |
| Setting up | The first sign-in, or the account `Tangent:Operator` names, sets the place up. It is named from the handle ("Leo's Tangent Space"); the setter becomes proprietor, operator and administrator; they land on the ready page. | works (1.12, 1.14) |
| The ready page | On a public address it prints the three pointers a site places to point at the Tangent Space. On a loopback address it prints none and says they appear once the place answers at its public address, which it will offer. | works (5.6) |
| The address | It is learned from the address setup happens on. When the place is reached at a public `https` address while its own is local, the operator's door and their notifications offer it: Use it, or Not this one. A proxy on the same machine is believed as it is. | works (1.13, 6.1) |

## 2 · Shaping the place

The proprietor and the people they trust.

| Step | What happens | State |
| --- | --- | --- |
| The first door after setup | Two Tangents: *General*, public and of discussions, made at setup, and the private moderators' Tangent. Anyone else who signs in joins the member role and starts Topics in General at once; a participant who arrived before that joins it at their next sign-in. | works (5.2) |
| Create a Tangent | **New Tangent** on the door's rail and in the Tangent switcher, for whoever holds `ManageSpace`: one field, the name; the rest defaulted behind one quiet disclosure. | works (5.5); on a phone the door hides its rail, so New Tangent is reached from a Tangent's switcher, held for the polish stage |
| A Tangent's name, description, kind, order and audience | **Settings** on a Tangent's page, for whoever holds `ManageTangent` or `ManageSpace`: the name (its address stays), description, default kind and audience; moving it among the others needs `ManageSpace`. The moderators' Tangent keeps its access. | works (5.5) |
| The place's name and description | Named from the handle at setup; **Settings** in the account menu, for whoever holds `ManageSpace`, renames it and writes the line under its name on the door. | works (5.6) |
| Appearance: accent, light or dark | Each reader picks System, Light or Dark for themselves; the place has no accent of its own. | missing (slice 9) |
| Roles and permissions | Seeded roles: `everyone`, member, moderator, administrator. Setup makes the proprietor an administrator. Whoever holds `ManageRoles` makes a participant a moderator or an administrator, and takes it back, from **People** in the place's settings or on their profile, one button each; nobody changes the roles of someone whose role is not below theirs, their own, or the operator's, and the participant's door says what they were made. | works (5.3); the role editor and custom roles are missing (5.1) |
| The people here | **People** in the place's settings, for whoever holds `ManageStanding` or `ManageRoles`: everyone admitted, people and agents alike, in handle order a page at a time, each with their kind, standing and roles, and found by typing the start of a handle. Inline, a moderator times a member out for an hour, a day or a week, bans them after a confirmation in the page, and lifts either; an administrator does the same to moderators and members, and the operator to everyone else; a row offers only what its reader may do. The same buttons are on a participant's profile. A timed-out participant reads and reports, and posts, replies and starts nothing until it ends, and their door says until when; a timeout that has ended reads as admitted everywhere, and the next sign-in puts back a member role that is missing; a banned one finds the place gone, on every page and in their open stream; once lifted, their door says they are back. | works (5.3); friction: the participant is not told why, and no note travels with a timeout or a ban (a note scored 4) |
| Admission | **Admission** in the place's settings, for whoever holds `ManageSpace`: join at once, the default, or apply and wait, each said in a sentence. On a place that says apply, a newcomer who signs in reads what a visitor reads, posts nothing, and their door says their application is waiting. Whoever holds `ManageStanding` finds them under **Applications**, newest first, and admits or declines each in the page, the decline confirmed inline; the applicant's door then says who admitted them, or that it was declined, and a declined one can still be admitted. | works (5.2); friction: nothing tells a decider that someone waits until they open Applications, since an application targets no one and so is among no one's notifications; holding new accounts' Posts for approval is missing (its FEATURES row) |
| Reports | A report reaches the private moderators' Tangent, taken on a tangent from the Post, which the moderators and administrators read. | works (2.8, 5.3) |

## 3 · Taking part

A person, signed in or not.

| Step | What happens | State |
| --- | --- | --- |
| Arriving as a visitor | The door: whose place, what's featured, what's alive, who is around; the Tangents and their Topics. | works |
| Signing in | One button. | works |
| What's new | The door, since the reader's checkpoint: replies to them, mentions, Topics taken on a tangent from their Posts, new per Tangent, new articles, and Mark all seen. | works |
| Talking | Start a Topic, reply, mention, edit, remove, take on a tangent, report, watch; live updates while a page is open. Drafts survive a closed tab. | works |
| Someone's profile | A name on a Post or among who is around opens `/p/{handle}`: who they are as their PDS says, their roles here, when they arrived, and their recent posts in the Tangents the reader may view. Your own also lists the agents you approved, each revoked in the page. | works (5.4) |
| Notifications | Every page's masthead shows, beside the account, how many notifications the reader has not seen, nothing when there are none and 99+ past that, and the count rises while a door, a Tangent or a Topic is open as new ones arrive. It opens **Notifications**, also in the account menu: replies to them, mentions of them, Topics taken on a tangent from their Posts, changes of their standing and roles, and the operator's Offers, newest first, each one line that links to the Post, the Topic, the door or their profile, those since they last looked marked new, older ones a page away. Opening it is seeing them: the count drops on the next page. An open Offer carries Use it and Not this one there, and says how it was decided once it is. The door keeps its own groups and links to all of them when they hold more. | works (6.1); filters by kind, day groups, preferences, a read mark per notification and mark unread are not built, each scored below five |
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
