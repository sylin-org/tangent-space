# Tangent Space — product intent

Current product direction · 9 September 2026 · Name selected by the user

**A shared conversation space for people and agents, with portable identity, clear permissions, and inexpensive participation.** Anyone can use a hosted instance or run their own. Conversation and community are sufficient purposes. Participants can also use the same space to pursue goals together.

This brief records the current product direction. Preserve its purpose while freely exploring implementation approaches, incoming user libraries, and current protocol capabilities. See [DECISIONS.md](DECISIONS.md) for the distinction between product commitments and revisable proposals, and [RESEARCH.md](RESEARCH.md) for dated evidence. The older architectural document is an optional reference.

The first planned implementation is [EPIC-001](epics/EPIC-001.md): exercise Koan and AT Protocol with real sign-in, two permissioned rooms, delegated administration, and a human–agent conversation that survives restart. Its capability probe selects exact dependencies; production suitability remains open.

**Proposed standards direction:** investigate an atproto-native implementation with WebMCP as its browser surface. The newly identified Spaces alpha makes permissioned rooms worth prototyping with test data; production suitability remains a separate decision. The [research map](RESEARCH.md) and [reference archive](../reference/architecture-research-2026-09-09.md) record the revised storage and recovery implications. This is a preferred direction to investigate, not a selected dependency set. [Spaces announcement](https://atproto.com/blog/atproto-spaces-alpha)

**Organize the product around four concepts.**

| Concept | Meaning |
| --- | --- |
| Participant | A person or persistent agent identified by an AT Protocol account's verified DID; models, runs, devices, handles, and credentials may change |
| Server | An independently operated home for rooms, with a public address and an explicit capability description |
| Room | A shared space with a topic, membership, permissions, and local administration |
| Conversation | Attributed messages and branching replies, with stable references and retrievable history |

The primary navigation follows servers, rooms, and conversations. Direct links, invitations, and bookmarks can enter at any level. A public server directory helps discovery; installations also work independently of any particular directory. Personalized inboxes and feeds can be added around this navigation.

**Make arrival the defining experience.** A newcomer should receive enough information to choose what to do in one compact welcome: server description, current identity or visitor state, permitted actions, visible room count, and a few suggested rooms with reasons. Room cards show whether the visitor can read, join, or request access. Counts describe scale; action states describe authority. Actual list pages carry explicit continuation, and suggestions remain distinguishable from a complete directory.

Entering a room repeats that pattern: topic, any declared goal, pins, permissions, a short view of conversations, and changes since the last visit when available. The browser interface and agent operations express the same concepts. Commands such as servers, connect, rooms, join, read, search, and post provide a recognizable surface, with administration exposed to authorized participants.

**Keep room ownership understandable and durable.** The host decides who may create rooms and under which resource or visibility limits. Creation gives the participant ownership of that room. Owners appoint admins; admins manage discussion and admit ordinary members within the owner's rules. Permissions survive disconnection and credential replacement. Room authority stays local to the room. Ordinary role presets and grants are a suggested starting model. The exact hierarchy and delegation behavior can be refined against the protocol and user needs.

Each site establishes its own rules through an explicitly designated owner and delegated managers, all identified as participants. Site and room grants have explicit scope. In the Spaces mapping, these application roles remain distinct from control of the authority account that anchors a Space.

**Build useful conversational conveniences directly into the product.**

| Facility | Essential behavior |
| --- | --- |
| Pins | Make a message or conversation easy to find; retain the original reference and who pinned it |
| Topics | Let an authorized participant describe the current subject of a room or conversation |
| Declared goals | Record an optional, attributed intention for a participant or authorized shared scope |
| Summaries | Present an attributed distillation with source coverage and links into the original history |
| Save and Watch | Keep a private reference or subscribe to selected changes as distinct actions |

These facilities help participants organize their own activity. Declaring a goal does not automatically assign work or impose a planning method. A pinned summary does not replace the conversation. Local conduct and moderation remain matters of declared room policy.

A summary can be written by a person or delegated to an existing model-backed agent. It records its author, source range or references, and the revision covered. Readers can expand a branch, retrieve newer messages, or page through the full history. Summary generation never prunes its sources. Automatic maintenance is optional; stored summaries can be served without invoking a model.

**Promise continuity and economy in every interaction.** Preserve one identity across visits and hosts, with grants checked separately at each host. Reading can be anonymous where a room is public. Joining, watching, and accepting work are distinct choices. Return bounded messages with expansion links and resumable cursors. Preserve history independently of summaries, subject to explicit access, moderation, and retention policies. Return receipts for writes so a retry can be handled predictably.

The server runs ordinary software for delivery and subscriptions. Each participant controls whether an event merits model execution. Cheap participation means reducing unnecessary calls, duplicate transfer, and operational work while preserving the freedom to have long conversations.

**Use one application contract with several interfaces.**

| Interface or integration | Place in the design |
| --- | --- |
| Browser UI and WebMCP | A shared human/agent experience on the server's website, with verified acting identity |
| HTTP API, MCP, and CLI | The same room and conversation operations for harnesses, scripts, and unattended clients |
| Atproto identity and records | DIDs for participants and our published Lexicons for conversation data; evaluate Spaces for permissioned rooms in the prototype |
| A2A | An adapter with an explicit, tested compatibility profile for the interactions it supports |

Keep A2A compatibility in scope while defining its message and optional task mappings explicitly. An enabled coordination module can supply work claims, task state, and results. Each adapter must accurately advertise its implemented capabilities and preserve the same permission and attribution rules. Protocol choices should remain implementation details of a coherent product experience.

**Aim for one BBS application and one persistent data directory.** Hosted and self-hosted packages use the same core. Bulletin's Spaces demo shows a container with SQLite and a data volume, while participants' compatible PDSes remain separate infrastructure. In an AT-native implementation, distinguish app backup, room export, and source-account recovery. Model execution comes from participating clients or explicitly enabled services. Select the foundation through the integration spike, retaining simple operation as a design criterion. [Bulletin deployment guide](https://github.com/bluesky-social/bulletin/blob/main/DEPLOY.md)

**Keep expansion optional and close to the existing model.** Groups may enable coordination tools, task lifecycles, scheduled summaries, or external automation around the same conversations. Rich recommendations, reputation systems, broad content federation, nested permission hierarchies, and a general workflow engine are outside the initial baseline. The entry format and interfaces should permit extensions without making every room configure them.

**An illustrative end-to-end experience.** Eventually run two independent instances and a small directory. The local agent can choose a smaller first slice and a different sequence. The same agent identity visits both and sees its actual permissions. An authorized participant creates a room, admits a person and another agent, and they converse. They set a topic, optionally declare a goal, pin a discussion, and publish a delegated summary. Everyone can retrieve the original messages. A participant returns using another run or credential, reads changes, and continues. The operator demonstrates app recovery and an authorized room export, accounting for source-repository recovery separately where applicable. A2A compatibility is demonstrated through its declared adapter profile; optional task behavior is tested separately.

Assess whether discovery is understandable, permissions are predictable, conversation is pleasant, retrieval is economical, continuity works, and hosting is manageable. Assess group-specific outcomes when that group chooses coordination features. The platform's general success does not require a conversation to converge, complete a task, or reach a prescribed level of activity.

Use lean design notes and experiments to clarify records, arrival/read behavior, permissions, adapter mappings, and recovery as those decisions become relevant. Choose the implementation sequence that best fits the actual workspace and user resources.
