# ADR 0013 — Composed Topic access and scoped authority

Date: 15 September 2026. Status: accepted, revised 27 September 2026 for the EPIC-008 Forum integration.

## Context

A role rule alone does not describe who can act in a Tangent or Topic. The product also has Tangent admission, scoped Topic membership, participation classification, restrictions, and a Topic reading audience. If a matching server role replaces those facts, a participant marked Removed can regain access and a Reader can post. An anonymous Public Topic has a separate read path that must not be confused with an authenticated membership decision.

## Decision

The current Topic decision is composed in two stages. `Topic.CurrentPolicy` derives the actor's scoped admission and participation ceiling from the Space/Tangent owner, Tangent admission and membership, Topic membership, classification, suspension, restrictions, and lock. `AccessEvaluator.Decide` intersects that ceiling with the resolved access map and the actor's role bag. A role match cannot restore `CanRead` or `CanWrite` after the scoped policy denies them, or management after scoped admission or restrictions deny it. An admitted participant may gain management from an effective Manage rule even when their scoped membership alone does not grant it. Topic creator management comes from `CurrentPolicy.CanManage` while admitted; creation alone does not make a delegated creator a Space or Tangent owner.

The access map has See, Post, Manage, CreateTangents, and CreateTopics decisions at their applicable levels. A null Topic decision inherits its Tangent parent; a listed Topic decision intersects with the parent and can only narrow it. See falls to `everyone` when no level decides. Other empty decisions retain the category's built-in global permission. Resolved categories append their actual `global:*` permission token, such as `global:topic_read` or `global:post_create`. Stored decisions accept `everyone`, `authenticated`, `role:<key>`, and `global:<permission>`; normalization and resolution occur in `AccessMap`.

Koan's role bag contains server roles and their permissions and derives the authenticated participant fact; Tangent's role adapter adds the Space owner's authority. It does not turn a scoped Tangent or Topic Member assignment into the server `role:member` token. The built-in server roles currently include Owner, Administrator, Member, and Participant. A Tangent or Topic membership role remains a separate scoped fact. Tangent removal bounds Topic access and Tangent-scoped content/activity; a Topic Reader may read but cannot post, and Topic Removed denies signed-in Topic access. Space/Tangent ownership remains explicit authority.

`TopicReadAudience` remains an explicit Restricted/Public setting. The anonymous public history route requires both Public audience and an effective See decision containing `everyone`; it does not borrow an authenticated participant's policy. Public Tangent cards may be discoverable without granting Topic content or management. Neither a public audience nor a visible card overrides scoped restrictions on signed-in writes.

## Consequences

Access decisions must be recomputed from current domain facts inside read and write gates. A client, connector, cached role bag, or transport grant cannot widen them. Topic children cannot widen their Tangent access rule. The UI must distinguish Topic membership roles from server roles such as `@Member`, and explain the separate anonymous reading audience. Owner and scoped steward actions must remain subject to their actual domain authority and transport grant; an ordinary role match alone is insufficient.
