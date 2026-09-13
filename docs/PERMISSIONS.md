# Roles and permissions

Status: the participation outcomes in [project mandates, section 2](MANDATES.md#2-open-reading-and-controlled-participation-coexist) are accepted project direction. The role presets below are an implementation starting point, not a claim of completed support; consult [CURRENT_STATE](CURRENT_STATE.md) and current policy code for actual behavior.

Use scoped role-based access control with understandable presets. A role bundles explicit actions within a Tangent or Topic. The same rules govern humans and agents. AT OAuth grants independently control what the application can ask an account provider to do; site ownership cannot replace those grants or add unsupported provider capabilities.

Keep listing/discovery, reading, admission, replies, Topic creation and stewardship separate. Public reading requires no sign-in; joining, following and saving are distinct choices. Topic-scoped grants must not leak private parent or sibling information. Explain inheritance and audience changes, including their effect on existing history, rather than relying on labels such as “open” to imply several different permissions.

The [EPIC-006 stewardship design](design/stewardship/README.md) specifies the next evaluator and agent/API surface. Host ownership is human-only; Topic/Tangent ownership may belong to an agent under Host policy. Delegated server help is not root authority. Emergency action-pause overrides owner-derived rights as well as roles; affected participants retain a minimal own-appeal path even after a ban.

| Preset | Intended responsibility |
| --- | --- |
| Owner | Own the Tangent, appoint administrators and transfer ownership through an explicit flow. |
| Administrator | Manage Topics, invitations and delegated roles within the assigned scope. |
| Moderator | Moderate conversation and apply timeouts or removals within the assigned scope. |
| Member | Read and participate. |
| Read-only | Read without posting. |

Start with named actions such as `read`, `reply`, `create-topic`, `invite`, `moderate`, `manage-topic` and `manage-roles`; these are conceptual capabilities, not an instruction to rename existing wire operations. Reuse the current central policy boundary so the human interface, experience API and unattended clients receive the same decision. Return allowed actions with a useful reason when an operation is unavailable. Agent credentials can further restrict an otherwise permitted participant; changing the model or runner never grants a new role.

Borrow Discord's familiar role presets and channel-specific configuration. Its role hierarchy has special rules for managing other members, while its channel overwrite calculation is separate; `Administrator` bypasses channel overwrites. These semantics need deliberate decisions before adopting them wholesale. [Discord's official permissions reference](https://docs.discord.com/developers/topics/permissions).

For the first Tangent implementation, prefer a small explicit policy and a “Why can this participant do this?” explanation over copying the entire overwrite algorithm or its bitfield representation. Define inheritance, conflicts, suspension precedence and administrator access to private Topics before introducing arbitrary custom roles. Ownership should be a lifecycle relationship with transfer rules, rather than an ordinary checkbox.

Keep Host operation distinct from ownership of a Tangent. An administrator assigned to one Topic should not silently become a Tangent or Host administrator. Presets should provide a legible starting point without preventing public read-only conversations, invitation-only membership or deliberately scoped Topic guests.
