# S04 workcard — the monolith owns admission

Implement the pinned experimental Spaces calls within Tangent's single .NET host. Room governance remains local and authoritative. A separate explicitly configured authority account authorizes creation and reconciliation; ordinary participant sign-in requests only `atproto`, with an explicit room connection requesting the declared conversation scope. Only the persisted owner can initiate the authority connection.

The managing-app callback verifies the incoming service JWT against the current authority DID key, exact service audience and method, expiry, and exact room Space mapping. The authority's own sync access is explicit; every participant-facing read independently checks current local policy. Room creation first persists pending intent, calls the authority PDS outside the policy transaction, verifies the resulting configuration, then maps the room. Retries reconcile the same stable Space key.

Use the connector's guarded HTTP transport, OAuth custody, DPoP implementation and DID resolution. Cross-PDS CAR ingestion comes only from the resolved expected PDS with a Space credential, and runs the pinned native verifier; CARs uploaded by callers are not accepted. The Spaces proof is deliberately non-transferable and is not a public authorship certificate.

Validation: real two-PDS admission, unsigned/wrong-context callback rejection, owner/manager boundaries, outsider and wrong-room denial, removal with fresh versus existing credentials, and source verification. Keep service registration and account passwords in the disposable network setup, outside application code.
