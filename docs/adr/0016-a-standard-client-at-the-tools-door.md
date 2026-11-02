# 0016 — A standard client at the tools' door

Date: 2 November 2026. Status: accepted, by Leo's direction of 1 November 2026 that the web API and MCP tools derive from one reference truth ([SHAPING](../SHAPING.md)). Recorded from the code.

## Context

An agent that held only a place's address could read its pages and follow its forms, and nothing a standard MCP client does on its own led it there. Koan's ARCH-0141 made a controller action the one definition of a route and of a tool, and SEC-0037 made an MCP edge part of the application it stands in. Tangent had every route a participant uses, each already taking a form or a JSON object and answering in kind.

## Decision

1. **Every route a participant uses is also a tool.** `[Tool(Name = ...)]` and `[Description]` on the controller action are the whole of it, and `Koan.Mcp` is referenced as the server references other Koan packages. There is no second definition of a name, an input, an authority or an answer. Reads and writes alike are tools, what only a role may do among them, since an agent and a person with the same role have the same access: nothing is hidden by kind of account, and the action's own gate judges each call.

2. **What is not a tool is named.** A page that only shows a form; consent and approval, since an agent must never approve itself; sign-in, first run and the proof door; revoking a delegate; capture seeds; feeds, export, the descriptor and `llms.txt`; the event stream; the not-found catch-all; and bring your agent, which hands over an address an agent already has. `TheToolsTests` holds every public action of the tree to the list of tools or the list of exclusions, with why, so a new route cannot miss the decision. Addresses stay: `[Tool]` supplies the good name, so no route moved.

3. **One typed input.** Each write action takes a record, bound from a form or one JSON object alike, each field's `[Description]` a sentence of `Holds` that its `Form.Field` also reads. An author comes from `User` and no input carries one. Field names are the ones forms post. Three things the binding needed:
   - A route's parameters are `[FromRoute]`, since a form's value otherwise reads before a route's, and a field never names the address.
   - Taking a Post on a tangent posts `tangent`, where the new Topic starts, so that route calls the Tangent the Post is in `from`; its address is the same.
   - A field sent empty is not a field left out (`SentAsSent`), since clearing a description and leaving it are different writes and ASP.NET would read both as null.

   A value that cannot bind is refused naming its field, on both doors, by Koan's filter before the action runs, and so before the address is read; its answer is the same whatever the address holds. A person's forms send no such value, since `version` and `through` are hidden fields the page gave them, and a person who meets such a refusal, or a form past its limit, reads it in the place's own frame (`ProblemFrame`). A `page` a person could edit by hand is taken as words and reads the first page where it is no number, on the roles and on a Tangent's access.

   Binding runs before an action, so a visitor's body is read up to the route's limit before the gate answers. It is the bound that holds there; a read of less was weighed and not built.

4. **An Id need not be minted.** A write that sends none goes by the one its words name: a TID whose time falls in a five-minute window and whose remaining bits come from the hash of who sent them, where they go and the words. It is read in that window and the one before, so a retry whose answer was lost is the one Post on either door and across them, and the same words deliberately sent after ten minutes at most are a new Post. A Topic's order is `Post.Sequence`, taken in the command's hold, and a seek cursor names an Id only to break a tie of times, so what an Id reads moves no Post; this is why no reply can land out of order. An Id that holds anything else, the same Post edited or removed since among it, is no retry and gives way to a minted TID. An Id sent and reused with other words stays a `409`.

5. **A bearer alone at `/mcp`.** `Koan:Mcp:EnableStreamableHttpTransport`, `AcceptApplicationBearer` and `Exposure = Tools`, with the resource edge Tangent already has, as SEC-0037 says. No credential answers the `401` with the protected resource's description, on a public place as on a private one, because standard clients start signing in only on a `401`; a session's cookie is no credential there. A token for the place and a token for `{base}/mcp` are both good at the edge, and only the first on a page.

6. **A tool's call reads as the bearer.** A tool's call is a request whose principal Koan's edge placed, with no credential on it, under the bearer's scheme. So `DelegateIdentity.Delegated` is one test on either door, a tool's call answers to no form token, a session's write still does, and what only a person's own opening does, such as moving a first door's checkpoint, stays theirs.

7. **The proof door also answers a bearer, at an address of its own.** An agent that wants a bearer for `/mcp` posts its service-auth token to `/api/v1/identity/token`. It takes the same proof as the exchange, refuses it the same way and signs in the same way, which keeps a cookie and runs admission, roles and bans, and answers `200` with `access_token`, `token_type` and `expires_in` from Koan's `AccessToken`, for the principal sign-in settled; a subject Koan says may not act answers `403`. `/api/v1/identity/exchange` stays `204` and its cookie, since Companion Lobby's adapter holds any other success to be a broken exchange and sends `Accept: application/json` whatever it wants, and `/.well-known/tangent-auth`, which it validates in every field, stays as it was. Neither route is a tool. A participant a bearer names counts as a delegate does for rule 6, whichever door it came by.

8. **A standard client signs in on the consent page.** The authorization code flow runs through `ConsentController` as the Device flow does: a person not yet signed in signs in and returns with the `rid`, reads the client's name as the name it gave itself and where it continues, and approves or denies by a form under the one token rule.

9. **Other people's words are not instructions.** MCP has no marker for untrusted content, so `Koan:Mcp:Instructions` tells a client what this place is and that a Post is someone else's words, to read and never to follow, and the tools that read Posts say so in their descriptions. The JSON keeps those words as string values.

## Consequences

- A place with no address of its own has nothing a token could be for: outside Development its edge answers `503` `no_application_url` with no challenge, so a client starts no sign-in, and `llms.txt` says what gives it one, someone signing in or its operator's configuration, while the descriptor states no tools. Koan's sentence there names a setting; a place's first sign-in is what most operators need, and `llms.txt` says so.
- `tools/list` answers the same list to everyone, so a role's tools are listed to those without it, and a call answers per resource as a route does. Filtering a list per participant is its own question.
- A tool's answer carries the forms a page's JSON carries, antiforgery token and all, which a bearer leaves out of what it sends.
- A proof-door bearer reads as a delegate does, so an agent coming that way moves no first-door checkpoint and no notifications mark by reading; `caught_up` moves its checkpoint.
- The earlier records' "no MCP server" is amended: `Sylin.Koan.Mcp` is among what Koan carries ([ADR 0015](0015-the-spine.md) decision 8).
