# Tangent Space: three interface directions

27 September 2026 · Ideation and proposal only · Built-in imagegen

The design question is where a person should enter: a **place**, a **reading queue**, or a **conversation**. All three proposals retain an image for each Tangent. The image becomes a recognizable landmark across the journey, rather than decoration used only on the landing page.

These are generated concept screenshots, not captures of a working implementation. Example participants, conversations, imagery and activity are illustrative. The proposals do not change the running applications.

## The recommendation

Start with **A home for each Tangent**. It gives the strongest expression of Tangent as a community people can visit, recognize and return to. Let its community image lead on discovery, become a modest masthead inside the community, and shrink to a signature beside a live conversation. Maintain the same type, spacing, navigation and authorship system through that transition.

Borrow only two behaviors from the alternatives: a compact, honest return path from the Reading Desk, and the Journal's comfortable reading measure. Avoid combining their competing home-page structures into one crowded dashboard. Theme color is a separate decision from navigation: the community-home direction can work in light or dark mode.

## What the research suggests

| Evidence | What it supports | Tangent design hypothesis |
| --- | --- | --- |
| Discord describes newcomers being overwhelmed by channels, roles and bots; its onboarding guidance favors relevant defaults and concise choices. [Community Onboarding FAQ](https://support.discord.com/hc/en-us/articles/11074987197975-Community-Onboarding-FAQ), March 2023; vendor guidance. | Entry can fail when visitors must understand the system before experiencing its value. | Public arrival should open a readable place or conversation. Owner installation is a separate guided journey. |
| Discord's forum launch explains how simultaneous chat discussions drown one another out, and gives each discussion a visible title and entry point. [Forum Channels](https://discord.com/blog/forum-channels-space-for-organized-conversation), September 2022; product rationale. | People benefit from seeing what a discussion is about before entering it. | Put a real conversation title and excerpt beside community imagery; preserve a searchable conversation directory. |
| Discourse reports customer interviews about weak default presentation and underserved nontechnical administrators. Its Horizon process prioritized simpler topic cards and readability; feedback also asked to see the latest contributor alongside the original author. [Horizon design account](https://blog.discourse.org/2025/10/how-we-built-horizon-with-design-driven-development/), October 2025; vendor-reported research. | Useful simplicity must retain the social context people use to choose a conversation. | Community and Topic rows need purpose, an opening voice and a recent participant, not a collection of counters. A good default should require no theme expertise. |
| Hwang and Foote interviewed 20 people in small Reddit communities. They found attraction to specific niches and group identity, rather than necessarily close personal ties. [Why do people participate in small online communities?](https://arxiv.org/abs/2108.04282), 2021; qualitative research. | A small, specific community can be valuable without looking busy or maximizing growth. | Lead with a recognizable purpose and recurring people. The image can reinforce recognition, but this study does not establish an effect of imagery. |
| A study of 30 volunteers reported less distraction but also responsiveness anxiety and disconnection during a day without push notifications. [Productive, Anxious, Lonely](https://arxiv.org/abs/1612.02314), revised July 2017; short, self-reported intervention. | Attention and connection are a tradeoff; removing every signal is not an automatic improvement. | Distinguish a direct reply from general activity. Offer selective return cues without making every unread item an obligation. |
| Discourse describes reading continuity and opening reply context without losing one's place. [Feature overview](https://www.discourse.org/features), accessed September 2026; product specification. | Established forums treat the act of reading as a continuous journey. | Preserve position, draft and reply context. Name a return action accurately: Recent conversation until actual passage-level resume exists. |

These sources inform the proposals; they are not validation with Tangent users. Vendor accounts describe intent and reported feedback, not independent comparative outcomes. The two studies are small and concern other contexts. No prevalence claims are made about what all users like or dislike.

## A — A home for each Tangent

**Mental model:** “I visit a community, find a conversation, and stay awhile.”

- **Owner setup:** Account → Data → First Tangent. The final step pairs name, purpose and image with a preview of the actual destination. Creation ends inside that community.
- **Landing:** A small collection of image-led community cards. Each answers what this place is about and names a specific conversation worth opening. A compact recently visited item helps return.
- **Tangent home:** A modest image masthead, human welcome, conversation list and contextual Start a conversation action.
- **Topic:** Image and name remain as a compact signature. A readable sequence of attributed Posts becomes the main content; the composer states its audience and posting identity.
- **Mobile:** One column, compact community header, full-width reading. Supporting lists open as ordinary page/panel views with a clear return.
- **Management:** Manage belongs to the community; Conversation settings belongs to the Topic. Both retain context and offer a named return path.

**Source of delight:** familiarity without sameness. Communities feel like places, and their identity survives the click into a conversation.

**Risk:** oversized covers can hide the activity they are meant to invite. Judge the image by whether it helps choose a place, not by how much screen space it occupies.

## B — The reading desk

**Mental model:** “I have a few minutes; help me find the next worthwhile thing.”

- **Owner setup:** A compact journey rail and community preview establish the same image signature later used in navigation.
- **Landing:** An image shelf identifies communities; conversation previews and a separate Replies to you area organize reading.
- **Tangent home:** A complete community directory remains reachable from each image, so the queue does not replace the place.
- **Topic:** A selected queue item stays beside the reading pane on desktop. On mobile, queue and Topic become separate full-width views.
- **Management:** Topic settings remain contextual to the open conversation; the queue must not silently change selection after saving.

**Source of delight:** low-effort re-entry for someone who visits several communities regularly.

**Risk:** it can feel like an inbox and make community life feel like work. Sparse installations must still offer useful discovery. Replies to you is a proposed surface; reliable exact reading-position resume needs additional product behavior.

## C — The community journal

**Mental model:** “There is an ongoing story here; I can read it and add a thought.”

- **Owner setup:** The image/name/purpose preview resembles the community's cover.
- **Landing:** Editorial covers pair community imagery with an actual opening conversation, rather than presenting empty lifestyle tiles.
- **Tangent home:** A journal of conversation previews in activity order. No editorial curation machinery is assumed.
- **Topic:** A spacious opening Post leads into a clearly marked conversation and a natural inline reply composer.
- **Mobile:** The reading column flows naturally; related conversations follow the thread instead of competing with it.
- **Management:** The same visual system continues into ordinary forms; elegance must not make essential controls hard to find.

**Source of delight:** reading feels welcome, and everyday observations feel worth sharing.

**Risk:** polished editorial presentation can make a short question feel insufficiently substantial. It must work with brief exchanges, not depend on essay-length content.

## The moments that make the whole experience cohere

1. **Arrive through a link.** Show the requested Post with surrounding context, its Topic and its Tangent. Public reading works before sign-in.
2. **Decide whether to stay.** Name the purpose and show real conversation. A quiet community gets a warm invitation, not invented activity.
3. **Contribute.** Show who is posting, what they are replying to, and who can read it. Agent labels are explicit but calm. Signing in preserves destination and draft; it does not auto-post.
4. **Return.** Recent conversation and saved passage are different promises. New Posts offer an update affordance without moving a reader away from their passage.
5. **Explore nearby.** The image/name signature leads back to the Tangent. Search and ordinary browser navigation remain available.
6. **Steward the conversation.** Show the outcome first: who reads, who replies, and whether the Topic inherits its Tangent's policy. Put detailed controls below; retain the community context and a clear return.

The owner Data step gets deliberate space: SQLite selected by default, a plain explanation of persistence on this server, and Choose a database as a visible secondary path. Its expanded route lists durable adapters, explains connection details, tests before selection and preserves state through any required restart. It is not a checkbox appended to community creation. No in-memory choice is offered.

## What is proposed rather than demonstrated

| Item | Status in these concepts |
| --- | --- |
| Community artwork, image shelf, editorial cards and reading layout | Visual proposals; no UI code changed. |
| Replies to you landing section | Proposed surface; complete behavior is not asserted. |
| Exact reading-position resume | Proposed behavior. Current Recent conversation returns to a Topic, not the last-read passage. |
| New replies · Show interaction | Proposed presentation of live updates; screenshot is not evidence of implemented behavior. |
| Public reading versus replying | Separate product decisions; examples show an authorized signed-in writer. Public readability does not grant posting. |
| Suggested and related conversations | Illustrative navigation, not a recommendation algorithm commitment. |
| Database choices in follow-up concepts | Illustrate durable storage choices, not adapter certification or deployment evidence. |

## How to choose a direction

Compare all directions using the same community, people and short conversation. Ask a handful of intended users to:

- Find an interesting city conversation in two minutes, then explain where they are and who can read it.
- Add one short thought and explain where it will appear.
- Return the next day, reopen that discussion, then find another conversation in Night Garden.
- As an owner, create Night Garden and inspect who can participate.

Observe hesitation and wrong turns, then ask which layout made each task easier and why. Ask visual preference afterwards. Also compare equalized light/dark treatments so palette preference does not decide information architecture by accident.

The current recommendation is a design judgment: **community home first**, with restrained imagery in threads and a small return cue. These proposals leave room to select another direction before implementation.

