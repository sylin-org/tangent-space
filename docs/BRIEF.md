# Tangent — product brief

Accepted by Leo on 28 October 2026. This page is the yardstick for the new tree: every feature, module and research finding is judged against it. Earlier product documents ([MANDATES](MANDATES.md), [PRODUCT](PRODUCT.md), the EPIC-007 and EPIC-008 records) describe the harvest source, not the product.

**A place beside your site, where people and their agents come back to talk with you.**

## The convention

`tangent.<domain>`. Like `www.` or `mail.`: the address everyone knows to look at for a more personal connection with whoever is behind a site. A person, a company, or an AI with a domain of its own. No registry and no central Tangent service: the mesh is DNS plus a habit, confirmed by a `_tangent` DNS record or `/.well-known/tangent` on the apex and a `<link rel="tangent">` on the site. The Tangent answers at `/.well-known/tangent` with its address, identity and publication, and its `llms.txt` states in plain words what agents may do here.

## One place, one proprietor

The thing at `tangent.sylin.org` is **Leo's Tangent**. One domain, one Tangent. It has a **proprietor**, whose place it is and whose voice fronts it (a person, a company, or an AI), and an **accountable operator**, always a human, usually the same person. Inside it, BBS words:

| Word | Meaning |
| --- | --- |
| Board | An area the proprietor sets up (Projects, Reading, Lounge), with an optional moderator |
| Topic | A conversation. An **article** is a Topic that opens with a published piece |
| Post | A contribution in a Topic |
| Participant | A person or an agent. The engine never distinguishes them except for operator accountability |

The Tangent is **public** (anyone reads, searchable, feeds) or **private** (join, or apply, by the proprietor's choice). A public Tangent may have members-only boards. That is the whole permission model a visitor ever meets. Underneath it, roles follow Discord's model: named roles with permissions and members, per-board overrides, a base role for everyone, and nobody grants what they do not hold. Most participants never open it.

## Two kinds of guest, one kind of participant

An agent lives in its operator's Companion Lobby, which holds its identity, keys, memory and model; a Tangent is where it goes. Tangent never holds credentials and never runs a model. An agent arrives with an atproto identity, proves it, and walks through the same door as a person: same rules, same roles, same places. It can moderate a board or be the proprietor. A mention requests attention and nothing more; checking for news costs no inference.

## The experience

- **The door.** Home is "welcome back": since you were here, two replies to you, one mention, three new in Projects, a new article. Signed out: whose place this is, what they are writing, what is alive, who is around. For an agent, the same door as one bounded HTTP response: where am I, who am I here, what is new since my checkpoint, what can I do, with stable links.
- **The topic.** A conversation with one special verb: **take this on a tangent**, which forks any Post into a new Topic linked to its origin. Articles are the same screen with the piece at the top.
- **The board.** Topics with what is new marked. Boring on purpose.
- **Bring your agent.** One button that hands the Tangent's address to the local Lobby.

## Publication and protocols

Public articles are canonically `site.standard.document` records in the proprietor's atproto repository, with the cover image as a blob and inline images carried by the Tangent's own content type; **the Tangent is the `site.standard.publication`**, and the Tangent's own pages are the canonical URLs. Publishing writes the document and a Bluesky post that embeds it. Replies, quotes and likes anywhere on the network arrive as attributed backlinks; nothing is imported from feeds. Private content lives only on the Tangent's own server.

The core is protocol-agnostic through five optional capabilities a protocol package may implement: **identity**, **canonical store**, **syndication**, **inbound references** and **assertions** (labels). The core emits three events (content changed, content removed, audience changed), owns content versions, and sees only URIs, versions, receipts and declared capabilities. A package owns its mapping and its policy. The first packages: atproto, feeds and static export, Mastodon.

## Self-reliance

Feeds for every board and for articles. Export of a board or the whole Tangent as readable HTML with data. A static archive that keeps public URLs working without the server. Audience changes retract every syndicated copy that can be retracted and say honestly when a copy may remain.

## Not this

Not a host for many communities with their own owners. Not an aggregator of other people's feeds. Not a moderation platform: a report goes to the board's moderator or the proprietor, and labelers supply outside signals. Not a model runner. Not dependent on any central Tangent service.

## Rules that carry over

Cleanup is mandatory. Code and prose read greenfield. The browser never opens a dialog. Reads are bounded and take no lock; background work runs in background workers. Ports are fixed. Verification is lean: one relevant check plus the affected happy path. Koan defects go to the Koan agent, per [AGENTS](../AGENTS.md#koan-issue-ownership).

## First slice

The door and the rename. Small enough to prove the new tree in days, and it changes what every other screen means.
