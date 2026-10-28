# Tangent Space: different ways to inhabit a forum

Round 2 · 27 September 2026 · Interface ideation, not implementation

This round explores four different interaction models: a persistent live workspace, a branching forum, a spatial atlas and a finite reading session. They change how someone arrives, chooses a conversation, follows a reply and returns. Each keeps one identifying image per Tangent, but uses it differently.

The image boards show an active desktop conversation plus first-visit, home and mobile studies. First visit here means a visitor discovering a public Tangent. Installation-owner onboarding remains Account → Data → First Tangent, with persistent SQLite selected by default and a deliberate alternative-database path; its final preview should show the selected format's actual destination.

[Open the image gallery](../../../artifacts/interface-ideation/index.html). All images were made with the built-in imagegen tool from the [saved prompts](round-2-prompts.json). They are generated proposals with illustrative people and content, not screenshots of implemented features.

## What could make a forum delightful?

The useful question is not whether a forum looks modern. It is what makes someone want to enter a conversation, contribute something, and return. The following evidence suggests opportunities; it does not validate these designs.

| Human payoff | Evidence | Design implication and limit |
| --- | --- | --- |
| **Someone heard me.** | Joyce and Kraut studied 2,777 newcomers in six Usenet groups; subsequent posting was associated with receiving a reply, 56% versus 44% without one. [Predicting Continued Participation in Newsgroups](https://academic.oup.com/jcmc/article/11/3/723/4617705), 2006. | Make replies unmistakably addressed to people. Offer willing regulars a way to find unanswered invitations. Historical observational evidence; no proof of a particular UI or a reason to manufacture agent responses. |
| **I can follow this thought.** | A study of Meneame's change from linear to hierarchical discussion found increased reciprocity using interrupted time series and regression discontinuity analysis. [To Thread or Not to Thread](https://ojs.aaai.org/index.php/ICWSM/article/view/14880), 2017. | Explicit reply relationships deserve a serious design experiment. This is one community's transition, not proof that nesting is universally easier or more delightful. |
| **I can arrive late and still belong.** | Zulip organizes messages into named conversations that can continue asynchronously. Discord's thread design keeps a side conversation visible alongside its parent context. [Zulip topics](https://zulip.com/help/introduction-to-topics); [Discord Threads](https://discord.com/blog/connect-the-conversation-with-threads-on-discord), 2021. | Combine durable Topics with immediate conversation. These are product models and vendor rationale, not independent evidence that copying either interface produces the same outcomes. |
| **I can understand the shape without reading everything.** | Wikum's lab studies explored navigable summary trees. In its 13-person reader study, summarized conditions improved identification over unsummarized discussion; the difference between Wikum and the document-summary condition was not statistically significant. [Wikum research](https://homes.cs.washington.edu/~axz/wikum.html), 2017. | Let people choose depth and return to original voices. This informs progressive context, not a claim that an unsummarized map works or that automatic summaries are trustworthy. |
| **I can leave without falling behind.** | An analysis of 2.65 million posts across 600 Usenet groups associated overload with simpler replies and withdrawal. [Information Overload and Message Dynamics](https://pubsonline.informs.org/doi/10.1287/isre.1040.0023), 2004. | Explore bounded visits and relevant changes instead of global unread debt. Older observational evidence does not prove that a finite reading session is the solution. |

The recurring frustrations suggested by this work are lost reply context, conversations buried by unrelated activity, too much material to absorb, and contributions that seem to disappear unanswered. The corresponding design ambitions are acknowledgement, navigability, optional depth and control over attention. Those ambitions are more useful than adding decorative rewards.

## 01 — Live workspace

[View the board](../../../artifacts/interface-ideation/r2-01-live-workspace.png).

**“I am here with these people.”**

A narrow rail of photographic Tangent bookmarks sits beside the selected community's Topic navigator. The broad center is a compact chronological conversation; an optional sidepane follows replies to one Post. This is visibly closer to Discord than to a conventional website.

The landing view is the workspace itself. An anonymous visitor can read immediately; participation prompts appear where they become relevant. On mobile, the same hierarchy becomes separate full-width views, including a named return from reply context.

The community photograph is always within reach but never repeated as a giant hero. Topic names and reply targets keep short exchanges from becoming one undifferentiated stream.

**The pleasure to explore:** immediacy, familiarity and the ability to move between conversations effortlessly.

**The cost:** more persistent navigation and the risk of feeling always on. The reply pane must never create a hidden second conversation or distract someone who just wants to read. New activity is not proof someone is currently present.

**Distinctive interaction to prototype:** select Lumen's Post, read its direct replies alongside the timeline, then close that context without losing position.

## 02 — Branching forum

[View the board](../../../artifacts/interface-ideation/r2-02-branching-forum.png).

**“I can follow this particular line of thought.”**

The opening Post is a root. Responses form visible branches; a reply appears beside the thought it addresses. Selecting a branch exposes its ancestor path and an inline composer. A chronological alternative remains available.

Community images become compact photographic tabs. Home can preview a Topic's shape rather than treating recency or reply count as its only useful property. On mobile, show one path and its replies with Other replies navigation, rather than endlessly narrowing indentation.

This is not a forced argument map: ordinary remarks do not require claim/support/rebuttal categories. Side-by-side branches are a desktop exploration; at larger scale they need focus and collapse, not an ever-wider canvas.

**The pleasure to explore:** understanding a conversation's relationships and seeing that a small contribution has a specific place.

**The cost:** collapsed branches can hide fresh replies, and competing columns can feel analytical. A clear route to new replies and chronological reading is essential.

**Distinctive interaction to prototype:** follow the bench discussion, add a response to Lumen, then inspect the bakery branch without rereading the entire Topic.

## 03 — Conversation atlas

[View the board](../../../artifacts/interface-ideation/r2-03-conversation-atlas.png).

**“There is something unexpected over here.”**

A Tangent becomes a navigable map of Topics. Labeled nodes connect through explicit continuations; selecting one opens a docked reader while its neighborhood remains visible. Posts remain ordinary attributed text in that reader.

The image anchors the Tangent's map identity. It is not an unreadable background. Links mean something concrete, such as Continued from, rather than guessed semantic similarity. Position should remain stable as activity changes. A list is an equally available route.

On mobile, use a full-width reader and a connected-conversations list; the map is optional. A single-Topic community must work without pretending to be a rich network.

**The pleasure to explore:** curiosity, spatial memory and discovery of how conversations lead to other conversations.

**The cost:** high risk of making an attractive visualization that becomes inconvenient to use. Keyboard navigation, stable arrangement, clear connection meaning and useful empty states are unresolved design work. Research on summary trees does not establish this atlas's effectiveness.

**Distinctive interaction to prototype:** follow an explicit continuation from the city Topic to Who gets to linger?, then return with place and draft intact.

This is the deliberate wild card, not the presumed default.

## 04 — Correspondence

[View the board](../../../artifacts/interface-ideation/r2-04-correspondence.png).

**“I have a little time for a good conversation.”**

Home is a finite contents page. A reading session opens one Topic at a time, with a small sequence indicator, complete-discussion access, optional previous/next navigation and an explicit Finish for now action.

A narrow image bookmark identifies the source Tangent. The reader sees original people and their Posts, not an unattributed summary. Selecting topics is voluntary; Browse all remains available. The session ends without a score, streak, obligation or claim that all activity has been consumed.

On mobile, the one-column reading session works naturally. Explicit navigation accompanies any optional gesture.

**The pleasure to explore:** a manageable visit and a satisfying place to pause.

**The cost:** selecting a few conversations can hide relevant activity or make a community feel too curated. Selection rules must be understandable. The board's short thread does not solve the presentation of hundreds of replies.

**Distinctive interaction to prototype:** choose three conversations, answer one, skip another, and end the visit while retaining an honest route back.

## The differences worth comparing

| Format | Organizes around | Image-per-Tangent role | Most promising for | Primary risk |
| --- | --- | --- | --- | --- |
| Live workspace | Current conversation and fast switching | Persistent photographic navigation rail | Frequent social participation and short exchanges | Attention pressure and visual density |
| Branching forum | Reply relationships | Community tabs above the discussion structure | Following a specific exchange, multi-perspective discussion | Hidden updates and tree complexity |
| Conversation atlas | Explicit links between Topics | Map identity and community bookmarks | Exploration and connected continuations | Navigation becoming harder than a list |
| Correspondence | A voluntary finite visit | Source bookmark within a reading session | Occasional return and thoughtful reading | Selection obscuring the wider community |

I would put **Live workspace and Branching forum** through the next interaction comparison; they challenge the current format in different useful ways. Atlas is worth exploring as a more speculative discovery experience. Correspondence tests whether the pleasure of a finite visit matters enough to shape the main experience. Do not combine all four into one interface before learning which mental model people actually prefer.

## How to evaluate these concepts

Use the same people, Posts and Tangent imagery in every format. Compare behavior before palette preference:

- Follow a shared link to Lumen's Post. Explain what it responds to and who can read it.
- Reply to that Post, then explore another exchange without losing the draft.
- Return after new Posts arrive. Find the relevant response without treating every unread Post as a task.
- Move to another Topic in Night Garden and explain how it relates to the first.
- End the visit. Ask whether it felt sociable, comprehensible, overwhelming or unfinished.

A generated screenshot can establish a visual direction; it cannot establish scroll behavior, keyboard access, accessibility, performance or felt social presence. Reply panes, tree navigation, the atlas and finite sessions are proposed capabilities. The first next step is a small interaction prototype of the chosen format, not implementation of every visible control.

## Design critique of the generated boards

An independent Astra High visual review finds these formats materially distinct. The most useful preference question is whether a person wants to keep Tangent open while others talk, follow one developing line of thought, or visit for a few conversations and finish.

- **Live workspace:** the generated sidepane duplicates content already expanded in the timeline and offers a second composer. A prototype should make one writing destination active at a time and reduce duplicate context. The reaction counters shown by the generator are not part of the essential concept or a decision to add gamification.
- **Branching forum:** the tree makes perspectives tangible but can feel like a diagram to inspect. Each Post needs a visible Reply action; Hide replies is not an adequate participation control. Its focused path must identify the composer's parent precisely.
- **Conversation atlas:** the generator added decorative node photographs, ambiguous extra connections and unlabeled overview dots. Those do not establish a useful relationship model. Keep authored continuation links distinct from other associations, with readable labels and a plain-list alternative.
- **Correspondence:** the finite visit is the distinctive idea. The generated peripheral slogans and “thoughtful reply” placeholder prescribe a tone unnecessarily. On mobile, reduce the image strip to a compact identity header to give reading the full width.

These are concrete refinement questions, not implementation approval. The concepts remain available for discussion without committing to every generated detail.
