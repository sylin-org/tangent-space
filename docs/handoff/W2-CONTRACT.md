# W2 wire contract — connector identity enrollment (frozen by the integrator)

This is the frozen contract between W2-A (connector) and W2-B2 (server). Either side may
propose changes through the integrator; neither may drift unilaterally. Additive fields are
tolerated everywhere (the connector DTO parser ignores unknown fields).

## Enrollment (new experience endpoint)

`POST {origin}/api/v1/experience/identities/enroll`

Request (no Authorization header — enrollment is pre-credential; consent is the server
setting or, later, an invitation capability):

```json
{
  "client": { "localId": "<connector guid v7>", "handle": "jeff", "displayName": "Jeff" },
  "serverRef": { "label": "The Lobby host" }
}
```

- `client.localId` is the connector's stable client-side identity GUID (32 hex chars,
  `tangent:`-namespace only as a value; the connector never formats it as a DID).
- `handle` is required, 2..253 chars, the label this identity wants; the SERVER decides the
  stored form and may deconflict display, never silently rebinding an exact existing
  identity value.
- `serverRef` is advisory context only.

Response (house style — every outcome is HTTP 200 with a status):

```json
{ "status": "ok",
  "participant": { "participantRef": "<server guid>", "identities": [
      {"kind": "internal", "value": "tangent:local:<guid>"},
      {"kind": "connector-client", "value": "<client guid>"}],
    "bestLabel": "jeff", "did": null },
  "credential": { "token": "ts_...", "name": "connector", "expiresAt": "...", "grants": ["welcome","read","post"] } }
```

Blocked outcomes: `status: "blocked"` with `problem: {code, message}`; codes:
`unbound_enrollment_disabled` (server setting off), `invalid_handle`,
`suspended_participant` (re-enrollment of a suspended identity), `request_conflict`
(same localId already enrolled maps to a different participant — the server returns the
existing mapping's participant WITHOUT a credential in that case, code
`already_enrolled`; the connector then uses its existing credential or re-enrolls after
forgetting).

The enrollment creates: one Participant (GUIDv7 key), an `internal` identity
(`tangent:local:{participantId}`), a `connector-client` identity (value = `localId`,
scoped to this server relationship — same localId at another server is a different
participant), identity-change chain rows for `created` + both additions, and a scoped
`ParticipantCredential` returned once. Re-enrollment with the same `localId` on the same
server is idempotent: it returns `already_enrolled` with the existing participant and no
new credential.

## Arrival identity segment (additive change to the existing envelope)

`identity` gains `identities` (array of `{kind, value}`) and a nullable `did`;
`participantRef` becomes the canonical participant reference (the server's GUIDv7 id)
and `did` is present only when the participant holds an atproto identity:

```json
"identity": { "participantRef": "<guid>", "did": "did:plc:... | null",
  "displayName": "Jeff", "handle": "jeff",
  "identities": [{"kind":"atproto","value":"did:plc:..."},
                 {"kind":"internal","value":"tangent:local:<guid>"}] }
```

The connector MUST key on `participantRef`, treat `did` as optional, and keep rendering
**you** from `displayName`/`handle`. Existing consumers that require `did.startsWith
("did:")` relax to: `participantRef` non-empty. The fake-server envelope in the
connector's `tests/common/mod.rs` mirrors this shape.

## Identity kinds (registry, ordered best-first for display)

`atproto` (verified DID proof, portable) > `internal` (server-minted
`tangent:local:{guid}`) > `connector-client` (server-scoped re-association key, never a
display identity) > future kinds. Admission strength (posture gates, wave 3) is the
strongest tier held, never the display order.
