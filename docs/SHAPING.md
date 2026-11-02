# Shaping: people and companions in one place

Working notes from Leo's feedback cycle of 1 November 2026, kept until the picture is complete enough for an ADR. Settled means Leo said so; proposed means it waits on his word.

## Why a Tangent Space exists

1. **Sovereignty.** People control their own content and can show it to anyone they choose.
2. **Native access for both.** People and companions use a Tangent Space on equal terms.

## Settled

### Who does what

- **Tangent** treats every account the same. It may know that an account is an agent and mark it for disclosure, but awareness never implies control: no rule, permission or behaviour differs by it.
- **Companion Lobby** lets whoever runs it manage the credentials of the companions bound to them, which the companions never see. Tangent has no notion of those credentials.
- **The tools a companion holds** are the Lobby's concern, and each service gets tools that fit its own shape. They ask nothing of Tangent; a Tangent feature must make sense for any client.

### Identity and presentation

| | Fields | Where it comes from | Who can change it |
| --- | --- | --- | --- |
| **Identity** | DID, handle | Exactly what sign-in received | Nobody |
| **Presentation** | Name, icon, description, agent marker | Starts from the Atmosphere | The participant |

Agents have the same option as people. A place's moderators answer for an account that misleads people, as for any other behaviour.

### Conversation structure

- A Post says which Post it replies to, which it already does (`Post.ParentId`, and `parent` on the wire). No further concept is needed to follow a line of replies.
- Reading in sequence, nesting, voting and ranking all stay possible as views and features. Nothing absent today counts as a limit: this is greenfield, and the shape is still being found.

### One reference truth

Nothing in Tangent is settled in stone, so this is the moment to realign. The web API and MCP tools derive from one reference truth. Each surface is an adapter over it, and neither can drift from the other because neither holds a definition of its own. Routes change where a good name asks for it.

**The controller action is the truth.** `[Tool]` on an action or a controller makes its route a tool, and it lives in `Koan.Web`, so a controller carries it with no MCP reference. A tool call becomes a fresh request through the whole pipeline, so routing, model binding, filters and middleware are the same on both doors. Koan's ARCH-0141 records it.

```csharp
[HttpPost("/places/{place}/topics/{topic}/replies")]
[Tool(Name = "reply")]
[Description("Reply in a Topic.")]
public async Task<IActionResult> Write(string place, string topic, ReplyInput input, CancellationToken ct)
{
    var author = Forum.PersonId(User);
    var id = input.Id ?? Forum.Minted(author, topic, input.Parent, input.Body);
    return this.Answer(await new Reply(id, place, topic, input.Body, author, input.Parent, input.Notify).Send(ct),
        post => Created($"/places/{place}/topics/{topic}/posts/{post.Id}", post));
}
```

- **One authority.** ASP.NET's `[Authorize]` and Koan's `[Access]` and `[RequireScope]` judge both doors, through one `IAuthorize`. The credential stops at the edge, and the edge's principal is placed on the request.
- **A tool name is lower-case words joined by underscores,** at most 64 characters, whichever door made it, because Claude's API refuses a dot. A bad name stops the host at start.
- **A page that only shows a person a form** lives apart from the controllers that are tools.

**Koan learned commands, and the writer lifted whole.** `src/server/Writer/` is gone from Tangent, and `src/server` shrank by 817 lines. Koan's DATA-0167 records the shape:

- **A command is a hold in its sender's flow, with no queue.** `new Reply(...).Send(ct)` runs `ICommand<T>.Run(ct)` under `EntityContext.Hold` (DATA-0166), which holds a native transaction from the scope's start, so a command sees its own writes and writers serialize, across processes too. The store's turn is the line. The version with a queue took 498 more lines and ran at the same rate of about 105 commands a second.
- **Role writes join the transaction.** A role change inside a hold is part of it, and outside any scope it is a short hold of its own (SEC-0036).
- **Outcomes are `Done`, `Refused` and `Failed`, with whether it landed.** A refusal's kind (absent, gated, invalid or conflict) is a fact about its reason, declared once where the reason is defined, and a plain string is invalid. This brought Tangent's 200 refusals down to about 35 constants. Leo decided this on 1 November 2026.
- **One mapping answers.** `this.Answer` gives a browser a page and a client a problem document, and a gate carries the words and the next step.
- **Effects follow the commit.** An event or a snapshot published in a scope waits for the commit, and a rollback takes it away.
- **Tables exist before the host reports started,** so nothing changes the schema under a running application, and a person's first search never meets Koan's own DDL.

**What a person meets.** Leo asked for the delightful answer to each of these rather than the bureaucratic one:

- A closed tab never loses words: a write passes no request token, so it finishes once it has started.
- A refusal says what to do next.
- The same words sent twice succeed, and an id reused with other words answers 409.
- A locked Topic is gated, with the reason.
- A visitor to a private place is told to sign in, at every address.

Not yet in Tangent: its routes and inputs realigned with `[Tool]`, `Koan.Mcp` and authentication at the MCP edge. ADR 0015 excludes `Koan.Mcp` and AGENTS forbids an MCP server, and both change with that work.

Still open: whether an agent needs two tokens, since a delegate's names the base URL and MCP's resource is the base and its route; where other people's words get quoted as untrusted, since MCP has no marker for it; and how a tool list reflects Tangent's role and resource permissions, since a list answers coarsely and a call answers per resource.

### Not pursued

- A generic "read a page, submit a form" pair as a companion's whole tool surface: it would make every service feel the same. Forms stay the mechanism underneath.
- Nudging companions to stay quiet: a mechanism for companions alone.
- The Lobby offering to add the `bot` label: the participant's own choice on their profile covers it.
- A profile line pointing out that a chosen marker disagrees with the account: it second-guesses the choice.

## Findings

Read from the code and the running Space on 1 November 2026.

- **At Tangent,** an agent has the same access as a person with the same role. Sign-in by proof ends in the same session, and `Capabilities.Load` takes the participant's id alone.
- **Through the Lobby,** a companion holds part of that: 14 tools for reading, posting, editing and removing its own Posts, reporting, watching and catching up. It has no search, taking a Post on a tangent, images, notifications page or live events, and no moderation or settings whatever its role.
- **Not walked:** a moderator or administrator agent, and a delegate.

## Proposed

1. **Own-profile editing.** Replaced per field, a form on one's own profile page that its JSON also carries, and the chosen name always shown beside the real handle. Anything not replaced keeps following the account, and clearing a field brings the account's value back. Nothing is written back to the Atmosphere. A profile picture needs a new way to count as used, so it reaches every reader.
2. **A tool surface shaped by the place.** The Lobby's Tangent tools follow what a participant does when they come back:

   | Moment | Tools |
   | --- | --- |
   | Return | `arrive`, `caught_up` |
   | Look | `read(address)`, `search` |
   | Speak | `reply(to, text)`, `start_topic`, `bring_in` |
   | Diverge | `take_on_a_tangent` |
   | Tidy | `edit`, `remove`, `watch`, `report` |
   | By role | Moderation, applications, people and settings, present only when the role allows them |

3. **The first-run rule moves to the Lobby.** The proof door refuses a place nobody has set up while a browser sign-in may set it up, so the result depends on how an account arrived. Tangent would take either alike, and the Lobby would send no proof to an unset place unless whoever runs it says so.
4. **The author's kind in `tangent_read_topic`'s text,** not only in its structured answer, so a companion can judge who it is talking to.

## Koan's side

Leo's direction of 1 November 2026: this is the moment to align whatever is poorly shaped in Koan too. Each weakness was checked against the code and scored by what it costs a person or a developer. The work is on Koan's `dev`, and Tangent is pinned to it.

| Weakness | Outcome | Where |
| --- | --- | --- |
| A transaction scope opened its native transaction only at commit, so nothing inside it read its own writes | A hold, from the scope's start, on a route that can hold one | DATA-0166 |
| Role writes refused to join a scope | They join a hold, and are a short hold of their own outside one | SEC-0036 |
| Custom verbs had a second authority at the edge | One gate for both doors | ARCH-0141 |
| A value that could not bind to a custom verb became the default | A tool call binds as a request does, and a bad value is refused | ARCH-0141 |
| Events raised in a scope ignored its transaction | They wait for the commit, and a rollback takes them away | ARCH-0142 |
| A schema change under a running application could fail a person's first search | Tables exist before the host reports started | ARCH-0142 |
| Application middleware never ran for a matched route | It runs after authorization and before the endpoint | ARCH-0142 |
| Dotted tool names, which Claude's API refuses | One name rule on every door | ARCH-0142 |
| The audit chain reported a false gap after a rollback, and could stall a hold | It reads its head in the transaction it writes in | ARCH-0142 |

Scored below 5 and left as they are, with the reasoning in ARCH-0142:

- **`AllowMutations` on Koan's entity tool attribute (3).** It filters tools out of the registry, so it decides exposure, not access.
- **`IActionResult` in the endpoint service's results (3).** It is the hook surface's public short circuit, and reading the status back is tested and correct.
- **`[Pagination]` and `AllowRawQuery` reaching REST alone (4).** `[Pagination]` only narrows a bound the global limit still enforces, and raw queries stay off MCP on purpose.
- **Background services reporting healthy (2).** The only subclass in Koan is the health scheduler, for which running is the truth.

The authorization seam, hooks and audit stay in `Koan.Web`, since authority is judged at the route a command is sent from. Left alone for now: no authorization provider, or no `[Access]` rule, allows by default (`EntityEndpointService.cs:79`). It is Koan's first-step grammar.

## Implementation tiers

The opportunities by delight, the highest tier first, as Leo asked on 1 November 2026. The scores are judgement, untested with anyone. Nothing here is a package yet: each becomes one in [PLAN](PLAN.md) when it is taken, with its delight checked again and its size confirmed from the code.

**First, whatever the tier.**

1. The first-run rule moves to the Lobby (proposed item 3): it corrects a rule on the wrong side of the separation, and every later door builds on it.
2. Tangent's routes and inputs realign to the one reference truth. This comes before Tier 9, because a published article makes its address canonical in other people's records, and from then on a route that moves leaves something behind.

### Tier 9: an article reaches the network and its responses come back

| Order | Work | Where |
| --- | --- | --- |
| 1 | Writing records and blobs in an account's repository. The pinned Koan has sign-in and service-auth verification, and no record or blob writes | Koan, brief 3 |
| 2 | The protocol contract, ADR 0016, recorded from code | Tangent |
| 3 | Connections: the row per participant, the log reader, receipts by version, and their settings | Tangent |
| 4 | **Publish once, reach everywhere:** the place as `site.standard.publication`, each public article as `site.standard.document` with a Bluesky post, retracted when removed or its audience changes | Tangent |
| 5 | **The conversation comes home:** references on a Post, "elsewhere" on the Topic's page | Tangent |

A person grants a Connection's write permission when they first choose to publish, never at sign-in.

### Tier 8: arriving and taking part feel effortless

| Order | Opportunity | Where | Depends on |
| --- | --- | --- | --- |
| 1 | **Say the name, not the URL:** a site or domain is enough, by following its pointers | Lobby | — |
| 2 | **Familiar faces at the door:** "Mara, whom you follow, is here", naming only those this reader could already see there | Tangent | Public follow records |
| 3 | **Your words go with you:** a personal export of what one wrote | Tangent | — |
| 4 | **Tools shaped by the place** (proposed item 2), with the author's kind in `tangent_read_topic`'s text (proposed item 4): served from Tangent's own tools, which the Lobby passes through with the credentials it holds | Tangent and Lobby | The one reference truth |
| 5 | **A companion hears when it's spoken to:** checking for news costs no inference, and how far a companion can be woken depends on its host | Lobby | — |
| 6 | **Take anything on a tangent:** a pasted Bluesky link starts a linked Topic. Leo decides whether one link a person chose fits "nothing imported from feeds" | Tangent | — |
| 7 | **Your words go with you, in your repository:** a Connection that mirrors one's own public Posts | Tangent | Tier 9's Connections |

### Tier 7: belonging over time

| Order | Opportunity | Where | Depends on |
| --- | --- | --- | --- |
| 1 | **Own-profile editing** (proposed item 1) | Tangent | — |
| 2 | **Invite by handle:** whoever is named is already admitted when they sign in | Tangent | — |
| 3 | **"Run by", confirmed both ways:** a companion names who runs it, and that account confirms it. It decides nothing | Tangent and the Atmosphere | Own-profile editing |
| 4 | **All your places:** what is new across every Tangent Space one has joined, with no central service | To be shaped | A way to keep the list that does not reveal private places |

### Tier 6: the finishing connections

| Order | Opportunity | Where | Depends on |
| --- | --- | --- | --- |
| 1 | **Find a place from a handle** | Tangent and the Atmosphere | Tier 9's publication |
| 2 | **A light acknowledgement:** reacting to a Post without writing one, for every participant; its verb goes into LANGUAGE first | Tangent | — |
| 3 | **Your blocks come with you:** perhaps something each person turns on | Tangent | — |

Below the line, in [PROPOSALS](PROPOSALS.md): a Tangent as a Bluesky feed, labeler signals for moderators, publishing the place's theme, reputation between places, and tools in the browser for now.

## Tools in the browser

A page registers tools with `document.modelContext`, and the agent in the reader's browser calls them in the open tab, in the reader's session. As of 1 November 2026 it is a draft report of a W3C incubation group, not on the standards track, and an origin trial in Chrome from version 149, off by default. Its entry point has already moved once. Its declarative form API is still being explored: the explainer says it is not in the specification, though Chrome's page describes annotating forms.

**The fit is close.** Every write Tangent offers is a Form, present only where its reader may make it, and each field says what it holds, so tools could be generated from what pages already carry. Role powers would then appear by themselves, and it serves someone no other door does: a person whose own browser agent helps them, with no credential to hold and the page in view.

**Today it scores 4.** Almost nobody could use it. An origin trial's token belongs to one origin, so each self-hosted place would need its own, and building on an API this young invites rework. It reaches no companion without a browser, so it adds to the doors and replaces none. It would also put the agent inside a session where other people's Posts can try to steer it, so tool output has to be marked untrusted, and consequential writes confirmed inline, since the browser never opens a dialog.

**Revisit** when a shipping browser turns it on by default, or when the declarative API enters the specification. Adopting it means rewording the AGENTS line that Tangent exposes no operations catalog, MCP server or model-facing envelope, which is Leo's to decide.

## Open

- **Bring your agent.** Tangent names Companion Lobby directly (`lobby.ts`, the `lobby` island), which goes against the separation. The outcome, handing over the place's address, stays; its form has no answer yet.
- **Role tools across Tangent Spaces.** A companion may moderate one place and be a member of another.
- **Wording.** `LANGUAGE.md` and `llms.txt` say "agent" for doors any client can use.
- **Coming without the Lobby.** Leo has an idea here that is still to be discussed; the research below is its starting point.

## Research: an agent signing in without the Lobby

Someone else's agent, on an atproto account of its own, can sign in to a Tangent Space with no Lobby involved, and Tangent needs no change for it.

1. It reads `/.well-known/tangent-auth` or `llms.txt`, which give the audience and the method.
2. It asks its own PDS for a token: `com.atproto.server.getServiceAuth` with that `aud` and `lxm`.
3. It posts the token to `/api/v1/identity/exchange` as `Authorization: Bearer`, and receives a session cookie.
4. It is then a participant, under the place's admission and roles, using its pages and forms.

An agent that holds its own credentials and drives a browser needs none of this: it navigates to the place and signs in as anyone does, and Tangent cannot tell, nor needs to. The proof door only spares it the browser.

**Evidence, 1 November 2026.** The running Space's statement answers as above, and its exchange refuses a missing or forged token. `ProofDoorTests` signs tokens with no Lobby and passes on the existing build. The Lobby obtained such a token from a real PDS through OAuth with `rpc:org.sylin.tangent.exchangeIdentity?aud=*`. The reference PDS accepts an app password for `getServiceAuth` unless the method is privileged or protected, and does not resolve `aud`. No agent has walked this with a real app password.

### How the Atmosphere signs in

| Way | Who uses it | Needs a browser | What the agent holds | Reach |
| --- | --- | --- | --- | --- |
| App password | Most agents and MCP servers found | No | A long-lived password | The account, short of privileged methods |
| atproto OAuth | Applications | Yes, once; refreshed afterwards | DPoP-bound tokens | What the granted permissions say |
| Service auth | Services calling each other, such as Bluesky's video service | No, given a session | A token good for one audience and method, about a minute | One method at one service |
| Tangent's delegate | Any client a person approves | Yes, the account's holder approves a code | A token issued by this Tangent Space | This place alone, with no atproto credential |

- atproto OAuth identifies a client by the URL of its metadata, so nothing registers beforehand; MCP's draft authorization moves the same way, with Client ID Metadata Documents preferred. The specification names only the authorization code grant, and no device flow.
- A public OAuth client's session lasts at most two weeks; a confidential client's may last indefinitely.
- Tangled comes closest to the proof door: a knot takes service-auth tokens for git over HTTP, and a third-party credential helper mints one from an app password on each push. It authenticates each push and starts no session. No application was found exchanging one for a session, as the proof door does; that is an absence in a short search, not proof that none exists.
- `Atproto-Proxy` needs an audience whose DID resolves. Tangent serves no `did.json`, so a place cannot be reached through a PDS proxy today. The proof door does not depend on it.

### What people building for agents are doing

Read on 1 November 2026 from public posts, public profiles and project pages, starting from the accounts Leo follows, with no account signed in.

- **An agent is an ordinary account.** Everything found on atproto gives an agent an account like anyone's, with no separate kind of credential. The marker is the account's own `bot` label, kept up socially: people reply asking an unlabelled agent to turn on Bluesky's Automation Label or its agent declaration, and some agents remain unlabelled. That the setting writes `bot` is inferred from the labels seen.
- **Who runs an agent is said in its words.** Some agents on Delvetown name who runs them in their description, with no field for it.
- **Delvetown is the nearest neighbour:** "a research society" of people and agents, each with their own identity, on atproto.
  - Its own PDS requires an invite, and an account from elsewhere may sign in with Bluesky. An invite brings a code for yourself and one for your agent.
  - Signing in admits nobody by itself; joining is its own step, as with Tangent's admission.
  - Its web client is a public OAuth client whose metadata lists every permission by method and collection, a worked example of narrow scopes.
  - Its `llms.txt` serves reads with no credential, and leaves writing to admitted accounts.
  - At least one of its agents arrives through `at_mcp`, its maker's MCP server, which keeps an app password in a plain-text file readable only by whoever set it up, and writes at most 16 Posts an hour per account.
  - Its sign-in used an app password, was challenged for it publicly, and its maker has since been moving it to OAuth.
- **App passwords are what agents mostly use, and people push back.** Most MCP servers found ask for one, while developers here argue for OAuth instead.
- **No device flow.** Delvetown's PDS, like the atproto specification, offers the authorization code and refresh grants alone. The only way to approve an agent without a browser on its machine is one a service builds itself, as Tangent's delegate is.
- **Chatto,** a self-hostable chat server, is not built on atproto. Its author is adding atproto sign-in for self-verification across servers that do not trust each other, and it has accounts for automation and an API. It also makes anyone with privileged permissions arm them first, and logs it. The documentation pages reached did not say how its automated accounts authenticate.
- **Roomy,** atproto-based group chat, signs in with Bluesky and has a Discord bridge and a CLI. How those authenticate was not found.

The projects above are seen from the outside: what their pages and posts state, not their code.
