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
