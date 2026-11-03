# 0016 — A standard client at the tools' door

Date: 2 November 2026. Status: accepted, by Leo's direction of 1 November 2026 that the web API and MCP tools derive from one reference truth, and his decision of 2 November 2026 that everyone signs in with their own credentials ([SHAPING](../SHAPING.md)). Recorded from the code.

## Context

An agent that held only a place's address could read its pages and follow its forms, and nothing a standard MCP client does on its own led it there. Koan's ARCH-0141 made a controller action the one definition of a route and of a tool, and SEC-0039 made a tool call carry the caller's own credential through the application's pipeline, with no session and no authentication at the edge. Tangent had every route a participant uses, each already taking a form or a JSON object and answering in kind.

## Decision

1. **Every route a participant uses is also a tool.** `[Tool(Name = ...)]` and `[Description]` on the controller action are the whole of it, and `Koan.Mcp` is referenced as the server references other Koan packages. There is no second definition of a name, an input, an authority or an answer. Reads and writes alike are tools, what only a role may do among them, since an agent and a person with the same role have the same access: nothing is hidden by kind of account, and the action's own gate judges each call.

2. **What is not a tool is named.** A page that only shows a form; sign-in, first run and the proof door; capture seeds; feeds, export, the descriptor and `llms.txt`; the event stream; the not-found catch-all; and bring your agent, which hands over an address an agent already has. `TheToolsTests` holds every public action of the tree to the list of tools or the list of exclusions, with why, so a new route cannot miss the decision. Addresses stay: `[Tool]` supplies the good name, so no route moved.

3. **One typed input.** Each write action takes a record, bound from a form or one JSON object alike, each field's `[Description]` a sentence of `Holds` that its `Form.Field` also reads. An author comes from `User` and no input carries one. Field names are the ones forms post. Three things the binding needed:
   - A route's parameters are `[FromRoute]`, since a form's value otherwise reads before a route's, and a field never names the address.
   - Taking a Post on a tangent posts `tangent`, where the new Topic starts, so that route calls the Tangent the Post is in `from`; its address is the same.
   - A field sent empty is not a field left out (`SentAsSent`), since clearing a description and leaving it are different writes and ASP.NET would read both as null.

   A value that cannot bind is refused naming its field, on both doors, by Koan's filter before the action runs, and so before the address is read; its answer is the same whatever the address holds. A person's forms send no such value, since `version` and `through` are hidden fields the page gave them, and a person who meets such a refusal, or a form past its limit, reads it in the place's own frame (`ProblemFrame`). A `page` a person could edit by hand is taken as words and reads the first page where it is no number, on the roles and on a Tangent's access.

   Binding runs before an action, so a visitor's body is read up to the route's limit before the gate answers. It is the bound that holds there; a read of less was weighed and not built.

4. **An Id makes a retry safe.** A write that sends an `id`, a TID, is safe to send again: the same TID with the same words is the one Post on either door and across them, and with other words a `409`. A write that sends none makes a new Post, so a tool that makes Posts says it is not idempotent. A form mints its own, so a page's form sent twice posts once. A Topic's order is `Post.Sequence`, taken in the command's hold, so what an Id reads moves no Post.

5. **One credential, at `/mcp` as on a page.** `Koan:Mcp:EnableStreamableHttpTransport` and `Exposure = Tools`, with no authentication of the edge's own: a tool's call is a request that keeps the caller's `Cookie`, so the session sign-in gave, in a browser or at the proof door, is the credential at both doors, and the real gate judges the real caller. A caller with none is a visitor: it lists every tool, reads what a visitor may, and a write answers the `sign-in` gate with its next step. `RequireAuthentication` stays unset. No account acts for another, so Tangent has no OAuth server and no consent page.

6. **One check against forgery, at one seam.** `Koan:Web:RefuseCrossSiteWrites` refuses a write a page of another site sent, at every endpoint, `/mcp` among them, by `Sec-Fetch-Site` or else `Origin`, and lets through a request with neither, which is no browser's. So a form carries no token of its own and a closed tab never loses words. A form's JSON keeps `token` with a constant value, which nothing reads, because Companion Lobby's adapter posts a form only where `token.value` holds something.

7. **The proof door is the sign-in with no browser.** An agent posts its service-auth token to `/api/v1/identity/exchange` and is answered `204` and the session's cookie, the same sign-in lifecycle as a browser's, so the same admission, roles and bans. `/.well-known/tangent-auth`, which Companion Lobby's adapter validates in every field, states it. Neither route is a tool.

8. **The place's address points to the door.** `GET /` answers with `Link: <{base}/llms.txt>; rel="describedby"`, and every page's head carries the same link; `describedby` is the registered relation for a resource that describes the one linking to it, since none is registered or established for `llms.txt`. Nothing at `/` speaks MCP, and `POST /` stays `caught_up`. `llms.txt` says to use `/mcp`, a fixed path, how to sign in with one's own account, and that without a session one is a visitor.

9. **Other people's words are not instructions.** MCP has no marker for untrusted content, so `Koan:Mcp:Instructions` tells a client what this place is and that a Post is someone else's words, to read and never to follow, and the tools that read Posts say so in their descriptions. The JSON keeps those words as string values.

## Consequences

- `tools/list` answers the same list to everyone, so a role's tools are listed to those without it, and a call answers per resource as a route does. Filtering a list per participant is its own question.
- Which MCP clients can set a `Cookie` header is not known; the TypeScript SDK's does, through its transport's request headers.
- Every page of a signed-in reader is theirs alone to cache (`private`), since a form no longer changes from one answer to the next; a first door is never stored.
- An agent's own opening of its door and notifications moves its checkpoints, as a person's does: it is the reader.
- The earlier records' "no MCP server" is amended: `Sylin.Koan.Mcp` is among what Koan carries ([ADR 0015](0015-the-spine.md) decision 8).
