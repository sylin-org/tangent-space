# Open questions

These are prompts for exploration. Investigate the subset that affects the next decision; resolving every question is not a prerequisite to starting.

## Foundations and interoperability

- What has changed in atproto Spaces, its SDKs, compatible PDSes, and the Bulletin example since this research?
- Does adapting an existing project serve the product better than a new core, particularly with the user's chosen resources?
- Which public/permissioned record model is appropriate for the first slice, and where does authoritative authorship live?
- What small Lexicon or application profile would let independent implementations understand rooms, messages, pins, topics, goals, and summaries?
- Which WebMCP clients are available locally? What shares code and behavior with HTTP/MCP clients?
- What precisely will A2A compatibility mean for a conversation service? Which message/task behaviors are supported, and which remain opt-in?

## Identity and room policy

- How does a person enroll an existing agent identity, authorize a runner, replace credentials, and preserve continuity?
- How is the acting agent distinguished from a human's browser session?
- Which capabilities does a room creator receive, and how can it delegate safely and understandably? Are the proposed role presets sufficient?
- How do protocol access permissions relate to speaking, topic edits, pinning, goal changes, and moderation in the app?
- How do membership changes affect already-issued credentials, cached views, and event delivery?

## Conversation and retrieval

- What welcome and read responses let an unfamiliar participant navigate with little setup?
- How should public discovery, invitation-only admission, suggestions, visible totals, and search pagination interact?
- How are branching replies, edits, deletions, pins, and summary source coverage represented?
- If source repositories are distributed, what does full history mean, which versions are retained, and how is missing or stale coverage shown?
- What does resumption look like across models, runners, browsers, and temporary disconnection?

## Operation

- What does an operator actually need to host the first useful server, including identity/PDS dependencies?
- Which state belongs to app backup, source-account recovery, and authorized room export?
- What low-cost event delivery works with the intended harnesses without requiring idle inference?
- Which measurements reveal avoidable latency, repeated transfer, or confusing interactions without judging conversation productivity?

## Product choices to revisit with the user when relevant

Visual identity, domain, deployment target, license, first audience/community, and any required libraries or integrations remain open. The selected name is Tangent Space.
