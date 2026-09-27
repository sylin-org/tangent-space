# Content adapters and service interaction

Design guidance for the accepted application boundary. Implementation evidence belongs
in the EPIC-007 work ledger. This note describes the intended experience and does not
claim that the capabilities are implemented.

## Ownership

Tangent owns content, its interpretation, permissions, presentation and synchronization.
Its two content adapters are Local and Bluesky. Local uses the installation's selected
Koan record provider. Bluesky translates remote content and operations into Tangent's
content model and records source identity and synchronization progress.

CompanionLobby owns connections, credentials, caller isolation, operation discovery,
invocation and result delivery. A service supplies operation names, descriptions,
argument schemas and human/model-readable results. CompanionLobby transports the
service's structured result without parsing Posts, Topics, actors, feeds, reactions,
mentions, content cursors or attention relationships. Service schemas may describe
those concepts; the connector treats the schemas as data.

The service must supply useful text alongside structured content. Otherwise moving
content types out of CompanionLobby would leave agents with an unexplained JSON dump.
Tangent authors concise orientation, reading results and next actions. The connector
may add its own connection status and acting-account label.

Discovered operations remain scoped to the selected, authenticated service. A catalog
does not authorize arbitrary remote URLs or confer permissions. Invocation checks the
current caller, connection and credential state; Tangent checks content authority.
The service defines whether an operation is safe to retry.

## First useful journey

1. Start Tangent with Local ready and SQLite selected. The operator can name their
   Space, sign in, create a Tangent and write its first Topic without storage setup.
2. Open Settings to connect Bluesky. Show the acting account, supported actions and
   any missing authorization. Keep authentication, content selection and publication
   as separate explicit steps in the page.
3. Select a supported Bluesky source and synchronize a bounded page. Show imported
   content with its source, original link and last successful refresh. A failed
   refresh preserves the readable stored page and exposes the failure beside Retry.
4. Read, follow or reply according to the item's actual capability. The composer
   always states the destination and audience. Publishing local content to Bluesky
   is an explicit action with its public destination visible before submission.
5. Connect an agent through CompanionLobby. The service's catalog and first result
   explain its place, identity and available actions. All content interpretation is
   performed by Tangent and the participating agent.

Every form and confirmation appears inline or as a page panel. No modal dialog is
needed. Drafts survive source changes and failed submissions. Unsupported operations
receive a concrete explanation; successful local storage does not imply remote
publication succeeded.

## Settings

| Section | Controls and feedback |
| --- | --- |
| Database | SQLite default; choices supplied by bundled Koan record providers; provider-specific connection fields; Test connection; tested configuration; activation result. |
| Content sources | Local ready; Bluesky account, connection state, permissions, selected sources, last success, bounded refresh progress and Retry. |

Database activation uses the exact configuration that passed the test. Changing a
field invalidates that result. Test failure leaves the active database in place.
The UI describes restart and data-transfer behavior exactly as the implementation
supports it; a connection test alone does not establish that existing data moved.
Only the Space's authorized operator sees database credentials or activation controls.

Bluesky is a content source, so it never appears in the database provider selector.
The Local adapter remains available when Bluesky is disconnected. Reauthorization
names the account and missing permission without implying a second local identity.

## Reading and attention

History uses ordinary links containing `after=<postId>` or `before=<postId>` with a
bounded limit. Tangent authorizes the Topic, resolves the anchor within that Topic,
then queries its ordered Sequence range through Koan. The first page, next page and
empty end state are understandable without decoding a token. A Post link opens its
bounded surrounding context. Imported Posts retain stable local IDs independent of
remote handles, content edits and synchronization order.

Continuation does not acknowledge reading. Read acknowledgement remains a separate
action and cannot be inferred from a requested anchor. New content offers a visible
way to continue without moving the reader away from the passage on screen.

Tangent presents one concise attention view: replies or mentions needing attention
first, followed by activity in followed conversations. Counts disclose incomplete
coverage. CompanionLobby displays Tangent's authored attention result and may retain
an opaque service checkpoint; it does not reconstruct or classify content attention.

Local moderation, synchronization exclusion and remote deletion have distinct
effects. Their labels and inline confirmations explain the actual affected copy.
Private Tangent content cannot be published or included in a public projection as
an incidental synchronization side effect.

## Implementation seams and order

1. Define the service catalog/invocation/result envelope and Tangent's authored
   presentation together. Relevant seams: `Identity/TangentMcpDiscoveryController`,
   `Api/ExperienceController`, `Api/ExperienceContracts` and `Api/ExperienceService`.
   CompanionLobby's service traits, operation decoding, catalog and presentation
   consume that envelope without domain-specific enums or result renderers.
2. Establish Local and Bluesky adapter contracts under Tangent, including source
   identity, capability reporting and bounded synchronization state. Port the useful
   Bluesky transport and normalization behavior under this ownership before exposing
   connection controls. Keep account-specific projections and permissions isolated.
3. Supply database/provider and source state to Settings. Browser seams:
   `components/SettingsPanel.vue` and `composables/settings.ts`. Pair every enabled
   action with its actual backend path; avoid placeholder controls.
4. Complete ordinary Post continuation through conversation services, public pages,
   authenticated routes and `composables/conversation.ts`. Remove room vocabulary
   consistently from routes, selectors, accessible labels, filenames and docs.
5. Show source and destination in `components/ConversationPanel.vue`; use the existing
   `App.vue` catch-up area for concise attention. Preserve narrow-screen layout,
   keyboard flow, reading position and drafts.

Verification follows these journeys: a bounded read across a page boundary; an anchor
from a different Topic; a failed database test; exact tested configuration activation;
a failed or repeated source refresh; visible publication destination; remote outcome
uncertainty; and current-account isolation. Use focused existing checks and an actual
happy path. Record live authenticated coverage separately from controlled fixtures.
