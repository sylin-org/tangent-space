# Journeys

What a person or an agent meets, end to end, step by step, as the tree stands. Each step says what happens and whether it **works**, has **friction**, or is **missing**. A friction step carries its delight-filter score, the experience it costs against what it gives; a missing step names the package or the [FEATURES](FEATURES.md) row that brings it. A step the walk could not reach over HTTP says so. Walked on a running server after every slice; last walked 30 October 2026, on `dev` at `8d9350d`.

## 1 · Running a Tangent Space

The operator, on their own computer or a hosted machine.

| Step | What happens | State |
| --- | --- | --- |
| Get the tree | The README says what Tangent Space is, points at the documents, and says how to run one: what it needs, `publish` then `launch` or `docker compose up -d`, and Sign in with Bluesky. | works (5.6) |
| Build from a fresh clone | `scripts/prepare-framework.ps1` clones Koan from GitHub at the pinned revision, which Koan's `dev` there carries. | works |
| Start it | `pwsh scripts/tangent.ps1 publish`, then `launch`: one executable, one data folder, port 5220 or the one `-Port` names. Or `docker compose up -d`. Neither asks for an address. | works when walked on 29 October; the walk starts no server, so it was not walked again |
| First visit | Until the place is set up, every address answers the set-up page: one button, **Sign in with Bluesky**, which leads to bsky.social's own account chooser. Accounts on another server sign in by handle, behind "Use another server". | works (1.14) |
| Setting up | The first sign-in, or the account `Tangent:Operator` names, sets the place up. It is named from the handle ("Leo's Tangent Space"); the setter becomes proprietor, operator and administrator, as `tangent.json` says; the set-up page's sign-in lands them on the ready page. | works (1.12, 1.14) |
| The ready page | On a public address it prints the three pointers a site places to point at the Tangent Space. On a loopback address it prints none and says when they appear: where the address is configured, once `Koan:Web:ApplicationUrl` names a public one; otherwise once the place is reached at a public address, which it then offers. | works (5.6); walked on a configured loopback address |
| The address | It is learned from the address setup happens on. When the place is reached at a public `https` address while its own is local, the operator's door and their notifications offer it: Use it, or Not this one. A proxy on the same machine is believed as it is. A configured address wins, and nothing is offered. | works (1.13, 6.1) when walked on 29 October; not walkable on this server, whose address is configured: a request through a forwarded `https` origin offered nothing |

## 2 · Shaping the place

The proprietor and the people they trust.

| Step | What happens | State |
| --- | --- | --- |
| The first door after setup | Two Tangents: *General*, public and of discussions, made at setup, and the private moderators' Tangent, with **New Tangent** beside them. On a place that says join, anyone else who signs in joins the member role and starts Topics in General at once; a participant who arrived before that joins it at their next sign-in. | works (5.2) |
| Create a Tangent | **New Tangent** on the door's rail and in the Tangent switcher, for whoever holds `ManageSpace`: one field, the name; description, default kind and audience defaulted behind one quiet disclosure. | works (5.5); on a phone the door hides its rail, so New Tangent is reached from a Tangent's switcher, held for the polish stage |
| A Tangent's name, description, kind, order and audience | **Settings** on a Tangent's page, for whoever holds `ManageTangent` or `ManageSpace`: the name (its address stays), description, default kind and audience; moving it earlier or later needs `ManageSpace`. The moderators' Tangent keeps its access. A private Tangent is read by administrators alone: its audience choice says roles may be given access, and nothing on any page gives it, so a member reading it finds it absent. | works (5.5); a members-only Tangent is missing, which the overwrites editor brings (5.1) |
| The place's audience | The place itself is always public. Nothing in its settings makes it private, though the code reads a place's audience in its access, its descriptor and its `llms.txt`. | missing: its FEATURES row in slice 5 |
| The place's name and description | Named from the handle at setup; **Settings** in the account menu, for whoever holds `ManageSpace`, renames it and writes the line under its name on the door. | works (5.6) |
| Appearance: accent, light or dark | Each reader picks System, Light or Dark for themselves; the place has no accent of its own, and `/settings/appearance` answers not found. | missing (slice 9) |
| Roles and permissions | Seeded roles: `everyone`, member, moderator, administrator. Setup makes the proprietor an administrator. Whoever holds `ManageRoles` makes a participant a moderator or an administrator, and takes it back, from **People** in the place's settings or on their profile, one button each; nobody changes the roles of someone whose role is not below theirs, their own, or the operator's, and a refused change says why. The participant's door says what they were made, or which role was taken. | works (5.3); the role editor and custom roles are missing (5.1) |
| The people here | **People** in the place's settings, for whoever holds `ManageStanding` or `ManageRoles`: everyone admitted, people and agents alike, in handle order a page at a time, each with their kind, standing and roles, and found by typing the start of a handle. Inline, a moderator times a member out for an hour, a day or a week, bans them after a confirmation in the page, and lifts either; an administrator does the same to moderators and members, and the operator to everyone else; a row offers only what its reader may do. The same buttons are on a participant's profile. A timed-out participant reads and reports, and posts, replies and starts nothing until it ends, and their door says until when; a timeout that has ended reads as admitted everywhere, and the next sign-in puts back a member role that is missing. A banned one finds the place gone: every page answers a bare Not found, the word "absent" printed under it. Once lifted, their door names who lifted it and says they are back. A moderator's settings rail lists General and Admission beside Applications and People; both open read-only under "no role grants ManageSpace". | works (5.3); friction: the participant is not told why, and no note travels with a timeout or a ban (a note scored 4); the rail showing a moderator only the sections they may use scores 5, a rail of two dead ends against nothing they can do there; the stray "absent" scores 4, held for the polish stage |
| Admission | **Admission** in the place's settings, for whoever holds `ManageSpace`: join at once, the default, or apply and wait, each said in a sentence. On a place that says apply, a newcomer who signs in reads what a visitor reads, posts nothing, and their door says their application is waiting. Whoever holds `ManageStanding` learns that someone waits wherever they are: the masthead counts the application among their notifications, **Notifications** lists who applied and when, linked to **Applications**, and their door says how many wait, beside the way to them. There they find each applicant, newest first, and admit or decline each in the page, the decline confirmed inline; once anyone decides it, it waits for no one. The applicant's door then says who admitted them, or that it was declined, and a declined one can still be admitted. | works (5.2, 5.8); holding new accounts' Posts for approval is missing (its FEATURES row) |
| Reports | **Report** on any Post, for whoever is signed in, a timed-out participant among them: a report is taken on a tangent from the Post into the private moderators' Tangent, which the moderators and administrators read, and the reporter is told the moderators have it. Whoever may view the moderators' Tangent learns of it wherever they are, someone given that later among them: the masthead counts it among their notifications, **Notifications** lists who reported a post by whom, with the reason's first words, one click from the report, and their door counts it among the new posts in Moderators. Opening the notifications sees it. The reporter and the reported author read nothing of it. | works (2.8, 5.3, 6.2) |
| Moderating a Post | A moderator reaches a member's Post through its **Edit**, where they remove it or rewrite it. A removal leaves a tombstone that names who removed it, and the author is not told. A rewrite reads as the author's own words, with no mark that it changed or by whom. | friction: a mark on a Post someone other than its author changed scores 6, an author's words kept their own against one line under the Post; telling the author when a moderator removes their Post scores 5 |

## 3 · Taking part

A person, signed in or not.

| Step | What happens | State |
| --- | --- | --- |
| Arriving as a visitor | The door: whose place, its description, what's alive, who is around; the Tangents the visitor may read and their Topics. | works |
| Signing in | One button. | works |
| What's new | The door, since the reader's checkpoint: replies to them, mentions, Topics taken on a tangent from their Posts, changes of their standing and roles, what they watch, new per Tangent, new articles, and Mark all seen, after which it shows nothing new until something happens. A newcomer's first door reads "Since you were here" and counts everything written so far as new. | works; friction, 5: a newcomer is greeted as if returning from a visit they never made, against a first door that welcomes them and shows what is alive |
| Talking | Start a Topic, reply, edit, remove, take on a tangent, report and watch, each a form the page offers. A tombstone says who removed a Post; a Post taken on a tangent and its origin link to each other; a watched Tangent has its own group on the door. Live updates arrive on the page's stream while it is open. Drafts survive a closed tab. | works; drafts live in the browser and were not walked over HTTP |
| Mentioning someone | Typing `@ines.bsky.social` in a Post, the way a person writes a mention, by the composer or an agent's JSON, mentions Ines when the Post is written, if she is a participant here: the body keeps her identity, `[@ines.bsky.social](at://did:plc:…)`, she hears it on her door and among her notifications, and the Post the writer lands on shows it linked to her profile under the handle she holds then. A link to her identity mentions her the same way. A handle no participant here holds stays the words typed, as do an address, code, a link's words and a URL, and the Post shows it unlinked. | works (2.10), not walked on a server; suggestions as you type, a short handle standing for a full one and a note for a handle that names no one are not built, each scored below five |
| Someone's profile | A name on a Post or among who is around opens `/p/{handle}`: who they are as their PDS says, their roles here, when they arrived, and their recent posts in the Tangents the reader may view. Your own also lists the agents you approved, each revoked in the page. | works (5.4) |
| Notifications | Every page's masthead shows, beside the account, how many notifications the reader has not seen, nothing when there are none and 99+ past that, and the count rises while a door, a Tangent or a Topic is open as new ones arrive. It opens **Notifications**, also in the account menu: replies to them, mentions of them, Topics taken on a tangent from their Posts, changes of their standing and roles, the operator's Offers, and, for whoever may view the moderators' Tangent, reports, newest first, each one line that links to the Post, the Topic, the report, the door or their profile, those since they last looked marked new, older ones a page away. Opening it is seeing them: the count drops there and on the next page; an agent acting for them reads them without marking them seen. An open Offer carries Use it and Not this one there, and says how it was decided once it is. The door keeps its own groups and names the notifications, with how many, while there are some. | works (6.1, 6.2); 99+, a second page and an Offer were not reached on this server; filters by kind, day groups, preferences, a read mark per notification and mark unread are not built, each scored below five |
| Search | None: `/search` answers not found. | missing (6.3) |
| On a phone | It works; the layouts are tuned at the polish stage. | friction, held for the polish stage; not walkable over HTTP |

## 4 · An agent

| Step | What happens | State |
| --- | --- | --- |
| Finding a place | `tangent.json`, `llms.txt`, and the site's pointers. Every page answers JSON. `llms.txt` says what signing in makes of a newcomer from the place's own admission: a participant at once, or, on a place that says apply, an applicant who reads what a visitor reads and writes nothing until someone admits them. | works (8.1), not walked on a server |
| Acting as a person's delegate | The Device flow with the public client `delegate`: a code, the consent page where the person approves it, and a bearer. The agent reads as the person, and its reading of their notifications leaves them unseen. The person revokes it on their profile; it cannot renew, and the access it holds ends within 15 minutes, as the page says. | works |
| Writing as a delegate | The agent writes as the person, with its bearer and no antiforgery token. Every write a page offers is a form in its JSON, the one its HTML holds: a Topic's `reply`, each Post's `report`, the new-topic page's `start`, the edit page's `edit` and `remove`, and every other form a page carries, each saying where it posts, how, each field with what it holds, and the TID minted for it. A reader who may not write finds no form. `llms.txt` says how to follow one. | works (8.1), not walked on a server; a form's choices are named in words rather than listed, scored 4 |
| Coming as itself | An atproto identity proved and exchanged for a bearer. The token endpoint refuses the grant, and `/did.json` answers not found. | missing (slice 8) |
| Bring your agent | One button that hands the place to a listening Companion Lobby. | missing (slice 8) |

## 5 · Publishing and keeping

| Step | What happens | State |
| --- | --- | --- |
| Articles | A Tangent whose default kind is articles lists articles and offers **New article**, and the door counts new articles. An article is a Markdown Post on the topic screen, without an article editor or a cover. | missing (slice 3): the article editor and the cover |
| Publishing to atproto | `site.standard.document` under the proprietor's Connection. | missing (slice 3) |
| Feeds | None yet. | missing (slice 4) |
| Backup and restore | `scripts/tangent.ps1 backup` and `restore`, in the container too. | works when walked on 29 October; the walk runs no lifecycle command, so it was not walked again |
| Export and the static archive | None yet: `/settings/export` answers not found. | missing (slice 7) |

## The order these close in

What stops a person first comes first, then what keeps someone waiting unseen:
1. **Moderating a Post:** a mark on a Post someone else changed, and the author told when a moderator removes it.
2. **The small doors:** the settings rail shows a moderator only what they may use, and a newcomer's first door welcomes them.
3. **The role editor and overwrites (5.1),** which also bring members-only Tangents.
4. **The place's own audience:** public or private, set in the place's settings.
5. **Slice 3, articles and publication.**
