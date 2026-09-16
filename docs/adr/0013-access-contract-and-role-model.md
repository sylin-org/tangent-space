# ADR 0013 — The access contract and the role model

Date: 15 September 2026. Status: accepted. Leo settled this while [EPIC-007](../epics/EPIC-007.md) was subtracting; it answers the arrival failure the R1 walkthrough found (ledger N-035) and completes the removal of the stacked permission models [ADR 0011](0011-realigned-server-architecture.md) set out to end.

## Context

Three mechanisms answered "who may do this", and they disagreed. `AccessMap` held a group token per capability; `Room.ReadAudience` held a separate public flag; membership rows held a role that Koan's role bags then overruled.

The walkthrough made the disagreement visible. A signed-in participant who belonged nowhere saw **less** than a signed-out visitor: the signed-in path consulted the access map, the signed-out path consulted the audience flag, and only the second knew the Topic was public. Eight tests have failed since before the epic for the same family of reason. `TangentDefaults()` compounds it — the home Tangent is created open to signed-in participants while its own access map demands `role:member`, so a new arrival belongs nowhere and sees nothing.

## Decision

One mechanism answers the question: an access map of capability → group tokens, resolved against the actor's roles.

1. **Four built-in roles, and no others:** `everyone` (held by every visitor, signed out included), `participant` (held by everyone signed in), `administrator` (granted) and `owner` (derived from the Space, transferable only). An access list names **roles and nothing else**; a permission is what a role grants, and `global:<permission>` appears only as a category's `AlwaysOn` below, never as something a person lists. `everyone`, `participant` and `owner` are **derived and never stored**, so none can drift from the truth the way a membership row did; suspension stays an explicit restriction rather than the removal of a role. **Member is not built-in** — a Space that wants one creates it like any other role, which is exactly why nothing in the product may require it.
2. **Each capability category carries its own rules,** in one table that drives resolution, validation and what the settings page says:

   | Category | `WhenEmpty` | `everyone` listable | `AlwaysOn` |
   |---|---|---|---|
   | See | `everyone` | yes | `global:administration` |
   | Post | `global:administration` | no | `global:administration` |
   | CreateTopics | `global:administration` | no | `global:administration` |
   | Manage | `global:administration` | no | `global:administration` |

   Clearing a field therefore opens reading and closes writing — each field's natural gesture lands on its safe outcome. On every write category `WhenEmpty` and `AlwaysOn` agree, so a cleared field can never exclude the people who would have to restore it.
3. **There is no "nobody".** A locked Topic is `Post: [global:administration]`, which is exactly what clearing the field produces. An administrator can always restore any setting, so a state that excluded them was never enforceable — only a fiction the type allowed.
4. **`everyone` is exclusive, mechanically.** Adding a role drops `everyone`; adding `everyone` drops the roles. The server normalizes on write, so every client — browser, connector, tests — sees one canonical form and none of them carries the rule.
5. **A child narrows, never widens.** `null` inherits the parent's decision and a listed decision intersects with it. Marking a Post `everyone` inside a members-only Tangent does not publish it, so the surfaces state the effective decision and not only the local one.
6. **Roles are renameable data with stable identity.** The display name is editable; the `role:<key>` in a stored map never changes with it. Built-in roles keep well-known keys (`role:owner`, `role:administrator`) so defaults and code can name them without a lookup and stored maps stay legible; custom roles are minted with a time-ordered uuid.
7. **Roles carry mandatory permissions.** A role definition pins permissions that customizing cannot remove. Ownership is not one of them anywhere: the owner's authority derives from `site.OwnerParticipantId`, so ownership can only be transferred, never granted.
8. **No one grants what they do not hold.** A participant may neither grant a permission they lack nor edit a role that holds one. This replaces a positional role hierarchy: nothing needs to be ordered, and no two roles need an argument about which outranks which.
9. **Always-on bypasses are disclosed where they apply.** Each access field names the roles holding its `AlwaysOn` permission, computed from the live role definitions — so a renamed role renames itself in the sentence — and shown to readers, not only to the people editing settings. A bypass nobody can see is not a bypass anyone consented to.

## Consequences

- `Room.ReadAudience` is deleted; public reading becomes `See: [everyone]`, and the two paths that disagreed become one.
- `TangentDefaults()` stops demanding `role:member`. That single line is the whole of ledger N-030.
- Membership stops being the price of reading. A participant reads a public Tangent without joining; joining grants authority, it does not grant admission.
- The eight known baseline failures become rewritable against one evaluator, as R3.4 already planned. `Management_or_ownership_without_read_access_cannot_remove_a_post` keeps its meaning only while reading and moderating stay separate permissions, so `global:administration` and `global:removeanypost` are never merged into one "can moderate".
- The model diverges from Discord's prior art deliberately, and the divergences are not oversights: no positional hierarchy, because permission containment decides; and no allow/deny/neutral overwrite, because a child may only narrow and so never needs an explicit deny.
- `AccessCriteria`'s grammar **narrows**: it admits `everyone`, `authenticated`, `role:<key>` and `global:<permission>` today, and an access list will hold `role:<key>` alone — `everyone` and `authenticated` become the built-in roles `role:everyone` and `role:participant`, and `global:` leaves the listable set for the category table. `Normalize` is already the single funnel every write passes through, so the narrowing, the exclusivity rule and the category table all land in one place.
- The four built-in roles are the whole built-in set, so `TangentRole`'s `Member`, `Reader` and `Admin` values stop being product concepts. What survives of them is a Space's own choice of roles, and `TangentRole.Removed` becomes what it always meant: a restriction.
