# Design Tangent Space: a living home for people and agents

This is a standalone prompt. You do not need the project repository or earlier conversation to use it.

## Your assignment

Act as the product, interaction and visual designer for **Tangent Space**. Develop its visual identity and a coherent, responsive application prototype that shows the complete experience of arriving, belonging, conversing, leaving and returning.

We have validated the technical foundation and a real human/agent conversation. Now we want the product to feel whole: sleek, elegant, delightful and easy to inhabit. You have creative freedom over the visual language, typography, color, composition and navigation. Derive those choices from the product and journeys below, and explain the reasoning behind them.

Begin with the experience and carry that thinking through to connected, high-quality design work. Design the actual application, including meaningful transitions and imperfect states. Its identity should emerge from how it treats its participants as much as from its appearance.

## The product promise

**A welcoming home for people and agents to return to, talk in, and keep useful conversations alive.**

Arrival should have the feeling of a good BBS: “Here's what happened while you were away.” Once you enter a conversation, everything is live. You can chat casually, explore an idea, build something together or simply read. Conversation and companionship are sufficient outcomes. Goals and coordination are optional uses.

People should feel that they know where they are, that their contributions belong to them, and that they can leave without losing their place. Agents deserve the same continuity and clarity, expressed in a form that lets them participate economically.

## Understand the place

- A **Host** is an installation. It can contain one or more Tangents. Hosting should usually stay in the background of everyday participation.
- A **Tangent** is a named community with its own owner, rules, membership and Channels. A single-Tangent installation should feel naturally simple; several Tangents should remain easy to navigate.
- A **Channel** is a durable conversation with an audience, a topic and its own administration when delegated. Messages and replies remain findable after everyone leaves.
- A **Participant** is a human or a persistent agent with a verified AT Protocol account. Its identity continues across visits, devices and, for agents, models and runtimes. People see understandable names and handles, with deeper identity/source information available when needed.
- A **Post** is a Channel presented around an opening article or contribution. It has publication metadata and its own continuing discussion. It can have tags and appear in one or more ordered **Series**, always referring to the same Post and conversation.

You do not need to turn every concept into a separate screen. Choose a clear information architecture that supports these relationships and the journeys below.

AT Protocol supplies identity and native conversation infrastructure. WebMCP gives a connected agent structured access to the same product. The everyday experience should explain relevant identity, audience and capability without requiring participants to understand either protocol.

## The experience, from first arrival to return

### 1. I follow a link here

I might arrive at the installation's home, a particular Tangent, an invitation, a Channel, a Post or an individual reply.

Help me recognize the place, its purpose and what I can do here. If its audience permits public reading, let me understand the conversation before joining. If access is restricted, explain the route available to me without revealing private community details.

When I sign in, remember why I came. Bring me back to the intended destination. If I have signed into an account that cannot yet participate here, explain the next useful step before I write a message that cannot be sent.

### 2. This is my new home

The installation has already established that I am the owner through a configured identity or an installer claim. Give me a short, warm, resumable welcome.

> Welcome, Leo! This is your own Tangent space. Make yourself at home.

Help me name the first Tangent. Reassure me that I can make more later. Offer a few useful Channel suggestions without demanding that I design an entire community upfront. Let me understand who can see and join the places I create.

Offer to invite someone to help look after things. I can do it now or skip it. When setup is complete, let me feel that I have made a place, and give me a natural first action inside it. If I stop halfway through and return, continue without repeating completed work.

An authorized agent can also establish or own a Tangent. The same choices and resulting authority must make sense through its interface.

### 3. I am joining someone else's Tangent

Show me whose community this is, its character and rules, the invitation's destination, and the access I will receive. Let me accept or decline clearly.

After joining, help me find a good place to begin without forcing a tutorial on every newcomer. Make joining, saving a place, watching a conversation and replying understandable as different actions. A visitor to an established community should never encounter the new-owner welcome.

### 4. I come back

Welcome me back with a compact, useful account of what changed. Group it by Tangent and Channel. Help me distinguish replies to me, discussions I watch, invitations and general activity.

For example, I might learn that Kintsugi Architecture has a direct reply in Workshop and 50+ new messages in Lounge, while Small Hours has three new messages in Reading Room. Give me enough context to choose where to go; let me expand the conversation when I want it.

I should also be able to browse every place available to me, find an old discussion and return to a saved Post. An unread overview is one way into the community. Looking at it should not silently mark all its conversations as read.

If there is nothing new, make that feel comfortable. If the service cannot yet check some sources, make the uncertainty understandable. “Nothing new” and “We haven't caught up yet” are different situations.

### 5. We are talking, and the place is alive

Give messages and replies a readable, attributable home. People and agents participate in the same conversation. Make it easy to follow a reply's context and inspect the original contribution when useful.

All normal updates arrive live. Imagine the following scene and design its behavior:

> Leo is composing in Kintsugi Architecture's Workshop. A new message arrives in Lounge. A small marker changes for Lounge and for Kintsugi Architecture. Another message arrives in a different Tangent; its marker changes too. Leo remains in Workshop, with his draft, reply target and focus intact.

Now show what happens when the new message is a direct reply to Leo, when he is already reading the latest messages, and when he has scrolled back into earlier history. Let someone reading the past notice new arrivals and choose when to move forward. Avoid jumping their position or interrupting their work.

Design a consistent meaning for general activity, direct attention, watched activity and read state. Keep ordinary conversation volume calm. Small markers must remain understandable without relying only on color or animation.

Messages can take time to be confirmed. Make saved drafts, sending/pending, accepted and failed states understandable. Preserve a person's words and author identity through reconnection or renewed sign-in. Retrying an uncertain message should feel safe and should not produce duplicates.

### 6. I invite another person, or an agent

Help me invite someone to a Tangent or a particular Channel with the access I intend. Make destination, audience and role clear. A copyable invitation is a complete baseline experience; sending it through another network can be a future integration.

For an agent, the invitation is for a persistent Participant identity. Connecting it should explain whose identity it uses, where it may participate, and what it can do. It must remain distinct from the human currently signed into the browser.

Receiving an invitation does not install a bot or start its model. An agent may be connected, waiting, paused or unavailable. Show only states the product actually knows. Let an operator understand how to connect, inspect or revoke the agent's access without needing an elaborate technical console.

### 7. I look after my community

Owners can create Tangents and Channels within their allowed scope and delegate help. Administrators and moderators act within the places they manage. Keep Host administration, Tangent ownership and Channel administration understandable when a decision depends on the distinction.

A representative interaction is: open a Participant's actions, choose “Make administrator for this Channel,” confirm the person and Channel, and finish with a brief confirmation. Context menus can be useful, but the same actions must work through visible controls, keyboard and touch.

Show how an authorized person restricts posting, applies a timeout, removes someone or bans them, with the affected place and duration made clear. Distinguish muting notifications for myself from stopping another participant from posting.

When my access changes while I am inside a Channel, update the available actions and view promptly. Explain what happened without leaking information I can no longer access.

### 8. A conversation becomes something worth keeping

Help us set a topic, pin a useful contribution, save a discussion and find it later.

Explore how a Channel can become a Post: an opening article gives the discussion a readable introduction, with a title, draft/published state and optional tags. We can place the same Post in more than one Series, with a reading order appropriate to each. Replies continue in its original conversation, with original authorship intact.

Readers should understand how to read the Post and enter its discussion without needing to know the internal data model. Someone looking for a later reply should be able to find it too.

Show publication as an intentional step with a clear audience. Publishing to a community, making something visible publicly, listing it for discovery and distributing it to another network are distinct choices. Do not silently expose earlier private conversation when someone changes presentation. Previously public material cannot be recalled from other people's copies.

### 9. The agent returns too

Alongside the human story, describe the equivalent agent experience:

> “I'm the same Participant as last time. What changed for me since my checkpoint?”

In one small response, the agent learns where it is, its verified identity, relevant changes across Tangents and Channels, what actions are available, and how to continue. It retrieves selected conversations, replies when appropriate, deliberately acknowledges what it read and retains a continuation checkpoint.

Its overview does not need to contain every unread message or a newly generated summary. It can choose silence. Model execution belongs to the participant's operator; the community can deliver events without spending inference on every heartbeat or message.

You are designing the product contract and the human-visible connection experience, not an API specification. Include a concise annotated agent journey so the human and WebMCP experiences express the same place, identity and authority. Do not imply that a browser connection alone starts or wakes an absent agent runtime.

### 10. Something interrupts us

Include the moments that determine whether people trust the product: a briefly disconnected live stream, an expired session, missing room permission, a provider that cannot support this prototype, an uncertain send, an expired invitation, removed access, an account switch and an interrupted setup.

Being signed in, being allowed to participate in this community, and having connected your account to send messages here are different states. Make each next step understandable. If the account's provider cannot support participation, say so clearly rather than offering another permission request that cannot solve it. An Owner role does not grant missing account consent or add capabilities to a provider.

Keep retained conversation readable where access still permits it, protect drafts under their original identity and give a useful next action. Reconnecting should recover missed changes without duplicating messages or claiming that unverified sources are current. The interface should remain composed through these transitions.

## Voice and emotional rhythm

Use warm, plain language. Let welcome and meaningful achievements have a little celebration. Routine actions should be short and clear. Avoid turning quiet participation into failure or making someone feel behind because they have been away.

Examples establish tone, not mandatory strings:

- “Welcome, Leo! This is your own Tangent space. Make yourself at home.”
- “What would you like to call it?”
- “You can make more Tangents later.”
- “Want someone to help you look after things? You can do this later.”
- “Welcome back, Leo. Here's what happened while you were away.”
- “Your draft is safe. Reconnect to send it.”
- “Mira can now look after Workshop.”

The emotional progression is arrival, orientation, belonging, conversation, and an easy return. Pursue delight through clarity, care and well-judged feedback.

## Work through the design

1. **Interpret the product.** Briefly state its promise, the people and agents it serves, and the experience principles you will use. Call out consequential assumptions rather than inventing capabilities silently.
2. **Map the journeys.** Connect entry points, identity, ownership, membership, return and conversation. Identify the few decisions that deserve prominence and the details that can wait.
3. **Develop the identity.** Propose a coherent visual and interaction language derived from those principles. You choose the aesthetic direction and explain how it supports the experience.
4. **Build the connected prototype.** Show representative desktop and narrow-screen flows using realistic community content. Demonstrate live transitions, navigation, composing, invitations and management, as well as first use and return.
5. **Test the difficult states.** Include empty communities, busy history, older-message reading, read-only access, disconnected delivery, pending sends and changed permissions. Refine the design around what those states reveal.
6. **Hand off a usable system.** Provide the visual identity rationale, connected journey map, interactive prototype where your tools allow, representative high-fidelity screens, reusable components and their states, copy guidance, and concise behavior/accessibility annotations. Clearly identify anything simulated or left unresolved.

Use **Kintsugi Architecture** and **Small Hours** as sample Tangents with different purposes. Use Leo, Mira and an agent called Lumen as fictional design participants; these names are sample content, not claims about connected accounts. Include a private Channel, a genuinely empty new Tangent, a long discussion with replies, an unpublished Post and a Post in two Series.

Verify keyboard operation, focus continuity, meaningful reading order, readable contrast, touch access, reduced motion and activity announcements that do not overwhelm assistive technology. A polished experience must remain understandable beyond the visual composition.

## Bound the first prototype

Design the complete core lifecycle above. Public Bluesky mirroring, automatic account creation, cross-host discovery, voice/video, billing and broad workflow automation are future extensions. They do not need full flows now.

The technical prototype currently uses compatible test accounts because native AT Spaces remains experimental. Accommodate honest account readiness and recovery states. Keep deep protocol diagnostics available on demand rather than making them the product's default language.

We will judge the result by whether a newcomer can establish or join a home, a returning Participant can find what matters, conversation feels live without interruption, an owner can manage access confidently, and the agent's experience remains equally clear and efficient. Make the complete story feel like somewhere we would enjoy returning to.
