# Community needs and Tangent's next experiment

Researched 9 September 2026, with separate UX and Agent Advocate reviews.

**The strongest opportunity is continuity: help a community return to its conversations, participate live, and keep useful discussion accessible.** Persistent agents can join that experience through inexpensive, selective attention. Public publishing can help a community reach others, but the research does not establish that every community wants agents or automatic distribution.

This brief informs [the product intent](../PRODUCT.md). It proposes experiments; it does not authorize a new implementation epic or claim that the proposed experience already exists. Actual capabilities are recorded in [CURRENT_STATE.md](../CURRENT_STATE.md).

## Method and limits

We examined firsthand public requests, reported workarounds, host discussions and follow-up outcomes in Discourse, Bluesky/atproto and agent-project communities. Sources span 2015–2026. We deliberately included objections and simpler solutions, rather than counting feature requests as votes for Tangent.

This is a qualitative convenience sample, heavily weighted toward English-speaking technical communities. We did not interview these people, recruit them as users, reproduce their reported incidents, or measure market size or willingness to pay. Historical complaints do not establish current product deficiencies. An open issue is a proposal; a closed issue does not establish either a fix or the disappearance of demand. Protocol documentation informs feasibility, not customer demand.

## What people and community hosts asked for

**1. Let casual discussion become something lasting without losing the people in it.** In January–March 2023, Discourse participants described problems moving chat into topics: quotation could lose useful authorship, reply targeting and notifications. Others wanted to choose between chat and long-form presentation after a conversation had developed. A staff participant preferred a curated summary for their community. [Moving conversations from chat to topics](https://meta.discourse.org/t/moving-conversations-from-chat-to-topics/253209).

**Our interpretation:** Post-as-Channel is a promising internal simplification. Preserve authors, message references and replies across presentations. Offer an intentional introduction or editorial selection when a discussion becomes a Post. Changing presentation must not silently publish its history to a wider audience. This evidence supports the need; it does not validate our exact domain model.

**2. Offer everyday connection and deeper conversation together.** A literary-community founder in September–October 2023 wanted friendships, feedback and thoughtful discussion, while weighing members' existing WhatsApp habits against a forum. Participants worried about fragmented attention and navigation clutter; one reported that synchronized identity helped chat and forum feel like one community. [Literary-community discussion](https://meta.discourse.org/t/whatsapp-for-human-connection-or-are-discourse-chat-and-channels-used/280473).

**Our interpretation:** test the BBS-to-live experience with an existing group. Installing chat, agents or a bridge cannot itself establish belonging. Onboarding should give people an immediate conversation to enter, with few mandatory decisions.

**3. Help me catch up without hiding what I already read.** In September 2022, Discourse users objected when category and “Everything” links opened unread-filtered lists. A host reported similar moderator confusion; other participants preferred unread-oriented navigation. [Sidebar navigation feedback](https://meta.discourse.org/t/sidebar-topic-list-links-prefer-unread-and-new-over-latest/239575).

**Our interpretation:** “While you were away” belongs alongside a stable directory, ordinary search and saved places. Seen, read and resolved are different states. A person should be able to ignore a backlog and still find the conversation later. Unread counts must not imply an obligation to finish everything.

**4. Let me follow a discussion without posting to it.** A March 2024 Bluesky feature request asks for thread reply notifications without having to contribute or repeatedly refresh. The issue was closed when inspected; that is not a current capability assessment. [Thread subscription request](https://github.com/bluesky-social/social-app/issues/3084).

**Our interpretation:** Watch, Join, Save and Reply are separate decisions. A public reader may want occasional updates without becoming a member or making a social contribution just to retain the thread.

**5. Give related discussions an understandable reading order.** A March 2022 Discourse request explicitly asks for a series across multiple topics. Participants discussed tag-based workarounds and moderation effort. [Post series or collections](https://meta.discourse.org/t/plugin-feature-for-post-series-or-collections/222073). A separate academic-series organizer asked for structure in 2015 and reported in 2018 that ordinary categories ultimately worked well. [Organization request and outcome](https://meta.discourse.org/t/category-naming-site-organization-conventions/33168).

**Our interpretation:** titles and a simple ordered series are worth testing. Tags should be optional. The user's multiple-series direction remains a useful design hypothesis; these sources do not establish demand for many-to-many series membership or an elaborate taxonomy.

**6. Help me reach the source conversation, including later replies.** A March 2025 forum operator described useful answers appearing well after the opening post and wanted relevant topic results and a way to discard an unhelpful AI summary. He also valued conversational query refinement. [Direct search feedback](https://meta.discourse.org/t/conversational-ai-search-coming-to-discourse-ai/355939/4).

**Our interpretation:** a Post's opening article is orientation, not its entire knowledge. Search replies, preserve source links, and make summaries optional. Agent participation should add to a useful human experience rather than become the required route to information.

## What agent operators asked for

**7. Keep the conversation when the deployment or interface changes.** A March 2026 Letta request seeks conversation-history migration with timestamps and explicitly asks that importing messages not invoke reasoning. [Conversation import request](https://github.com/letta-ai/letta/issues/3237). A July 2026 Hermes proposal seeks one logical conversation across CLI, desktop and messaging interfaces, raising recovery and concurrent-delivery questions. [Canonical cross-platform session](https://github.com/NousResearch/hermes-agent/issues/62780).

**Our interpretation:** prove that a second independent runner can continue as the same Participant using authorized history. Portable identity does not promise that a model's hidden state or private notes migrate. Imported history must not be treated as a new instruction to act.

**8. Route events with ordinary software; spend inference selectively.** A February 2026 OpenClaw contributor described an external notification router with deduplication, quiet hours, digests and on-demand model analysis, motivated partly by idle model cost. The feature request was closed as not planned. [Event-driven notification request](https://github.com/openclaw/openclaw/issues/11018).

**Our interpretation:** cheap software polling, long polling or push are delivery choices. None should require a model merely to discover that nothing happened. A runner should batch changes, expand selected context and be allowed to skip. Public replies and mentions are candidate events, not unconditional spending authorization.

**9. Let agents collaborate without runaway replies.** An April 2026 OpenClaw reporter described repeated bot-to-bot mentions consuming resources and affecting other agents sharing the same provider account. This incident was not reproduced; the issue's closure and linked work do not establish a verified fix. [Bot-to-bot loop report](https://github.com/openclaw/openclaw/issues/58789).

**Our interpretation:** deduplicate delivery and bound autonomous exchanges with operator-controlled budgets, reply limits and pauses. A mention alone cannot be the loop-control mechanism. Silence and declining an event are successful supported behaviors.

**10. Understand shared discussion without merging everyone's private memory.** A December 2023 Letta/MemGPT Slack-bot builder reported that separate per-user agents could not answer questions about other speakers and previous public-channel messages. [Multi-user agent request](https://github.com/letta-ai/letta/issues/668). An April 2026 Hermes reporter raised unexpected memory carryover and profile-isolation concerns; these were not independently reproduced. [Profile isolation report](https://github.com/NousResearch/hermes-agent/issues/10376).

**Our interpretation:** speaker-aware, permissioned channel history is valuable shared context. It does not imply consent to pool private runtime memory or unrelated conversations. These developer needs support a participation foundation; they do not demonstrate broad demand for a social network of continuously active agents.

## Operation, public reach and prior art

**11. Make moving and operating a community comprehensible.** A June 2025 engineering-community representative sought mobile usability, migration help, familiar sign-in and predictable hosting costs, describing limited IT proficiency. Their stated scale is not a validated Tangent capacity requirement. [Forum migration request](https://meta.discourse.org/t/help-with-migrating-forum-to-discourse-self-hosting/369287). A 2022 Matrix user reported that membership and role transfer were insufficient when years of history could not conveniently follow a homeserver move. [History export request](https://github.com/8go/matrix-commander/issues/89).

**Our interpretation:** measure host effort, successful recovery and usable exported history. A small Docker package is helpful, but its PDS/account dependencies still matter. Possible managed hosting is a business hypothesis; these requests do not establish a price or willingness to pay for Tangent.

**12. Public participation still has audience expectations.** A June 2023 Bluesky proposal asks for control over feed inclusion and visibility into redistribution, particularly for conversations intended for narrower contexts. [Distribution-control request](https://github.com/bluesky-social/proposals/issues/28).

**Our interpretation:** make audience, directory listing and external distribution separate settings. A public Bluesky doorway is worth a contained trial; automatic mirroring of every public conversation is not validated. Changing a local setting cannot recall existing public copies. A private continuation is clearer than suggesting previously published history becomes private.

**13. Community accounts on atproto have direct prior art.** Waverly's founder described communities represented by shared actors, local access control, resharing members' posts and a custom AppView in March 2024. He also stated that external PDS ingestion and federation were not yet in place. In March 2025 he reported taking the website down for lack of funding. [Waverly founder discussion](https://github.com/bluesky-social/atproto/discussions/2306).

**Our interpretation:** a community account is not sufficient differentiation. Tangent's opportunity is the quality and economy of returning, reading and participating across surfaces. Waverly's funding outcome neither disproves the idea nor validates its sustainability; interoperable foundations do not by themselves establish a durable business.

## What this changes in our priorities

| Priority | Next useful outcome | What would weaken the case |
| --- | --- | --- |
| First | A welcoming, understandable community with a few discussions, live replies, catch-up and complete browsing | Members require repeated help finding their place, or the host must continually manufacture activity |
| First | One invited agent with bounded context and an explicit response policy; continuation through two different runners | Repeated manual rebriefing, unexplained model calls, private-context leakage or duplicate replies |
| Next | One discussion presented as a Post, with optional tags and a simple series | People cannot distinguish reading from editing/publishing, or organization costs more effort than it saves |
| Then | One deliberately public Post/Channel with a Bluesky link and clearly attributed external replies | Participants misunderstand the audience, or moderation/duplication costs outweigh useful participation |
| Evidence before expansion | Cross-host saved places, richer series, broader adapters and managed hosting | Additional infrastructure creates little voluntary use or operational benefit |

The hierarchy and Post-as-Channel approach remain user-selected directions. Their implementation can support multiple Tangents and multiple series without forcing a newcomer to configure or understand all of them. No broad user demand was established for automatic account provisioning, mandatory AI summaries, unrestricted bot wakeups or a general workflow engine.

## Proposed pilot and evidence to keep

Invite one existing small reading, creative or interest group to use real discussions for three weeks. Give it a few Posts with live replies, simple return navigation and ordinary retrieval. Make one agent available on request. Leave tags, publication and migration optional. A second small architecture/maintainer group can test the technical use case, but should not be the only audience studied. These are recruitment proposals; nobody has been contacted.

Observe natural absences and returns. Can members locate an already-read discussion, understand what changed and contribute without assistance? Record host setup/moderation effort, self-initiated conversations, voluntary continued use and unwanted agent interruptions. Raw message volume and an empty unread count are not success criteria. Agree on useful thresholds with each host before the pilot rather than inventing benchmark percentages here.

In parallel, run a technical acceptance exercise: runner A contributes and stops; a different runner B authenticates as the same DID, retains current grants and retrieves only relevant shared history. Backfill causes no inference by default. A selected live event may cause one bounded reply; interruption and redelivery do not duplicate its accepted write. Revoked authority stays revoked. Both runners keep private notes outside shared context, can choose silence and obey their own configured turn limits. Record bytes transferred and the cause and count of model calls.

Test bot-to-bot loop control separately with a second Participant/DID. Demonstrate an exchange stopping at an operator-selected limit despite redelivery, without depending on provider rate limits. Sequential runner replacement under one identity does not establish this behavior.

The PoC already demonstrates bounded history, write receipts, credential/restart continuity and one actual GLM contribution. It does not yet prove the complete two-runner event path, the onboarding experience, multi-Tangent navigation, Post presentation or public bridge. [Existing evidence](../evidence/README.md).

## Feasibility references, kept separate from demand

[Standard.site's document lexicon](https://standard.site/docs/lexicons/document/) is a candidate for public Posts: it describes document metadata including tags, contributors and a Bluesky post reference. It does not define Tangent's series model. A public publication mapping still needs an interoperability experiment.

[The Spaces alpha announcement](https://atproto.com/blog/atproto-spaces-alpha) and our local protocol proofs support continued experimentation with permissioned channels. They do not establish production suitability, portable local governance or automatic discovery of all a participant's memberships. Check the actual supported protocol when implementing each feature.
