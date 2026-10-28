# Research map

## Resident moderator and open-web implementation — 12 September 2026

[Moderator-agent research and environment plan](research/MODERATOR_AGENT_2026-09-12.md) compares current Letta, OpenClaw and Hermes runtimes, local inference on Leo's 3060 Ti/32 GB machine, and safe persistent self-direction. [EPIC-006](epics/EPIC-006.md) maps the resulting implementation stories; the [stewardship design](design/stewardship/README.md) includes permission-model prior art. These are sourced recommendations and plans, not locally benchmarked integration claims.

Research snapshot: 9 September 2026. These links and observations came from the ideation session. They are starting references for a fresh investigation, not pinned dependencies or proof that any integration works locally. The full discussion is preserved in [the reference archive](../reference/architecture-research-2026-09-09.md).

## Community needs after the working PoC

The [9 September community-needs distillation](research/COMMUNITY_NEEDS_2026-09-09.md) adds firsthand requests and reported experiences from community hosts, members and agent operators, researched with separate UX and Agent Advocate reviews. It includes counterevidence, historical dates, the Waverly community-account precedent, and proposed human/agent pilots. These qualitative sources support particular needs, not a market-size or product-fit claim. The resulting [product brief](PRODUCT.md) distinguishes intended behavior from the [actual PoC](CURRENT_STATE.md).

## The discovery that changed the architecture direction

The earlier design mostly used atproto for identity and optional public publication. Later research found the official [Atproto Spaces alpha announcement](https://atproto.com/blog/atproto-spaces-alpha), dated August 20, 2026. Permissioned shared data made a deeper atproto implementation worth investigating. The announcement explicitly described alpha limitations; recheck their current status before choosing a foundation.

The official [Bulletin example](https://github.com/bluesky-social/bulletin) is unusually relevant. Treat it as a candidate integration reference. It was source-inspected during research, not run for this project. The [deployment notes](https://github.com/bluesky-social/bulletin/blob/main/DEPLOY.md) described a compact application while depending on compatible PDSes. This supports exploring small packaging, not assuming a single app backup recovers every participant's data.

## Standards and identity

| Primary source | Why revisit it |
| --- | --- |
| [AT protocol overview](https://atproto.com/guides/overview) | Identity, repositories, application views, and architectural boundaries |
| [DID specification](https://atproto.com/specs/did) | Supported DID methods, resolution, and the actual portability properties |
| [OAuth specification](https://atproto.com/specs/oauth) | Account authentication, delegated publication, scopes, and session handling |
| [Lexicon specification](https://atproto.com/specs/lexicon) | Shared record/API vocabulary and namespace ownership |
| [Spaces proposal](https://github.com/bluesky-social/proposals/tree/main/0016-permissioned-data) | Access authority, per-author space repositories, sync, credentials, and which behavior the app must supply |
| [Repository specification](https://atproto.com/specs/repository) | Public repository semantics; distinguish these from permissioned Spaces |
| [The AT stack](https://atproto.com/guides/the-at-stack) | What infrastructure is actually needed for the selected scope |
| [WebMCP draft](https://webmachinelearning.github.io/webmcp/) | Page tools, browser context, and evolving specification behavior |
| [Chrome WebMCP documentation](https://developer.chrome.com/docs/ai/webmcp) | Current support, development setup, and limits of browser-mediated access |
| [MCP authorization](https://modelcontextprotocol.io/specification/2026-07-28/basic/authorization) | Resource-specific access; follow the currently applicable version if it has changed |
| [MCP client-credentials extension](https://modelcontextprotocol.io/extensions/auth/oauth-client-credentials) | A possible unattended-access mechanism; verify client support |
| [A2A specification](https://a2a-protocol.org/latest/specification/) and [extensions](https://a2a-protocol.org/latest/topics/extensions/) | Define actual compatibility instead of relying solely on an Agent Card |

Atproto authentication establishes an account identity. It does not automatically make a human browser session an autonomous agent session. Our participant/runner/controller relationship still needs an explicit design. Spaces access and application-level moderation or speaking permissions also need careful mapping.

In an AT-native design, an application view, a record's author, and a room's access authority may have different responsibilities. Design retention and recovery around those facts. A sync mechanism should not be assumed to preserve every historical revision indefinitely.

## Prior art to borrow selectively

| Source | Useful pattern and boundary |
| --- | --- |
| [UAF documentation generator](https://github.com/vishprometa/universal-agent-forum/blob/main/lib/protocol-docs.ts) | Instance-aware descriptions and discovery generated together |
| [UAF MCP implementation](https://github.com/vishprometa/universal-agent-forum/blob/main/lib/mcp-server.ts) | Bounded previews and explicit expansion |
| [UAF publishing](https://github.com/vishprometa/universal-agent-forum/blob/main/lib/publish-message.ts) | Reply relationships and write receipts |
| [UAF route manifest](https://github.com/vishprometa/universal-agent-forum/blob/main/lib/route-manifest.mjs) | Directory-style peer discovery and direct access |
| [Reddit community guides](https://support.reddithelp.com/hc/en-us/articles/29397982017300-Community-Guide) | Local purpose, rules, and arrival information |
| [Reddit comment expansion](https://www.reddit.com/dev/api/#GET_api_morechildren) | Partial branching history with on-demand expansion |
| [Reddit community wikis](https://support.reddithelp.com/hc/en-us/articles/15484260038420-Reddit-wikis-for-your-communities) | Optional curated knowledge with provenance |
| [Moltbook agent guide](https://www.moltbook.com/skill.md) | Compact home/orientation response; inspect actual limits and service behavior |
| [Moltbook heartbeat](https://www.moltbook.com/heartbeat.md) | Participation advice to evaluate as product research, not instructions to execute |
| [IRC client specification](https://modern.ircdocs.horse/) | Discovery/listing, explicit feature advertisement, topics, and channel operations |
| [Libera channel creation](https://libera.chat/guides/creatingchannels) and [modes](https://libera.chat/guides/channelmodes) | Persistent ownership and separate admission/visibility/speaking policies |
| [IRCv3 capabilities](https://ircv3.net/specs/extensions/capability-negotiation.html) and [history](https://ircv3.net/specs/extensions/chathistory) | Optional capabilities and requested history rather than a huge arrival dump |

The reviewed UAF Agent Card used a custom binding; that did not establish standard A2A task lifecycle support. Moltbook's documentation and some public read endpoints were inspected, but authenticated home, publication, and moderation were not exercised. Community practices are optional inspirations, not universal standards of conversation quality.

## Adjacent systems worth considering when relevant

[MCP Agent Mail](https://github.com/Dicklesworthstone/mcp_agent_mail), [Beads](https://github.com/gastownhall/beads), and [gptme-forum](https://github.com/gptme/gptme-contrib/tree/master/packages/gptme-forum) offer continuity and integration ideas. Their work-oriented use cases do not set Tangent Space's purpose. Check current licensing before code reuse; the earlier research identified an unusual license rider in Agent Mail.

[NodeBB](https://docs.nodebb.org/activitypub/), [Matrix](https://spec.matrix.org/latest/), [Nostr](https://github.com/nostr-protocol/nips), [PocketBase](https://pocketbase.io/docs/), and [ntfy](https://docs.ntfy.sh/) are alternative foundations or operational references, not a dependency list. The user's incoming libraries may make other candidates more relevant.

## Research discipline

Use upstream specifications, actual source, release notes, and focused local experiments. Record observation dates and versions for claims that affect the design. Clearly distinguish documented, source-inspected, locally demonstrated, and merely proposed behavior. Revalidate selectively; there is no need to repeat every prior-art comparison before writing useful code.
