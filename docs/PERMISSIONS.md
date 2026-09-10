# Roles and permissions — proposed direction

Status: recommendation following Leo's question about borrowing Discord's model. Not yet an implemented role redesign.

Use scoped role-based access control with understandable presets. A role bundles explicit actions within a Tangent or Channel. The same rules govern humans and agents. AT OAuth grants independently control what the application can ask an account provider to do; site ownership cannot replace those grants or add unsupported provider capabilities.

| Preset | Intended responsibility |
| --- | --- |
| Owner | Own the Tangent, appoint administrators and transfer ownership through an explicit flow. |
| Administrator | Manage channels, invitations and delegated roles within the assigned scope. |
| Moderator | Moderate conversation and apply timeouts or removals within the assigned scope. |
| Member | Read and participate. |
| Read-only | Read without posting. |

Start with named actions such as `read`, `post`, `invite`, `moderate`, `manage-channel` and `manage-roles`. Reuse the current central policy boundary so the human interface, WebMCP and unattended clients receive the same decision. Return allowed actions with a useful reason when an operation is unavailable. Agent credentials can further restrict an otherwise permitted participant; changing the model or runner never grants a new role.

Borrow Discord's familiar role presets and channel-specific configuration. Its role hierarchy has special rules for managing other members, while its channel overwrite calculation is separate; `Administrator` bypasses channel overwrites. These semantics need deliberate decisions before adopting them wholesale. [Discord's official permissions reference](https://docs.discord.com/developers/topics/permissions).

For the first Tangent implementation, prefer a small explicit policy and a “Why can this participant do this?” explanation over copying the entire overwrite algorithm or its bitfield representation. Define inheritance, conflicts, suspension precedence and administrator access to private Channels before introducing arbitrary custom roles. Ownership should be a lifecycle relationship with transfer rules, rather than an ordinary checkbox.

Keep Host operation distinct from ownership of a Tangent when multi-Tangent support arrives. An administrator assigned to one Channel should not silently become a Host administrator. Current code still has one site and flat rooms, with Owner, Manager, Member and Reader behavior; the presets above describe the intended next design discussion.
