# EPIC-002 — Leo and Codex share a Tangent

Direction accepted 9 September 2026. First working slice completed with compatible test accounts; observed proof belongs in CURRENT_STATE and the [exchange receipt](../evidence/room-access-recovery.json). Public-provider Spaces compatibility and smoother onboarding remain explicit follow-on work.

The public-account write attempt exposed a compatibility boundary: Leo's provider rejected the write with `403 ScopeMissingError`, and its room-connection consent offered only `atproto`. Leo explicitly chose to **keep native Spaces and use a compatible test account**, rather than switch conversation storage into Tangent. The human test account is the existing site owner, `tangent-owner.test`; the assistant remains the separate existing agent Participant. Public identity login is still supported. Lumen (`lumen-bubbles.bsky.social`) was offered for future assistant use, but its public PDS has not been validated for this experimental Spaces path and no credential was requested or used.

## Outcome

Leo uses Tangent as a human. This Codex assistant participates through the browser's actual WebMCP interface under its own verified Participant identity. We use our conversation to discover which next improvements matter. The first audience is us, replacing the proposed external-community pilot as the immediate priority.

Keep the accepted Docker-hosted Koan DDD monolith and AT-authored conversation. Multiple Tangents, warm owner setup and Posts remain product directions. Start by proving our own reading, replying and returning through the existing rooms; grow the human experience from actual use.

## First working slice

1. **Connect the assistant explicitly.** A dedicated agent page binds an existing Participant credential in the tab's session. Agent requests always use bearer authentication with browser cookies omitted. The visible connected DID comes from the server. No model-tool parameter supplies credentials or selects the source author. Reconnecting invalidates in-flight work; an expired credential cannot fall back to Leo's cookie.
2. **Use native WebMCP.** Register a small stable tool set for arrival, Channel discovery, bounded history, posting/replying, read acknowledgement, source refresh and a bounded wait for updates. Discover and invoke it through this Codex browser's WebMCP capability. Unit callback tests alone are not that proof.
3. **Talk and reread.** Preserve attributed source references, retry-stable operation IDs and pending receipts. Explicit history-from-start lets either reader retrieve an already-read discussion. It does not rewind their read acknowledgement.
4. **See new conversation live.** The human view waits for committed changes in its selected Channel, appends bounded new messages and preserves an unsent draft. In-process notifications are hints; durable history and current policy remain authoritative. Waits are bounded and cancellable, and returning after a missed signal recovers through the cursor.
5. **Keep the two sides honest.** First demonstrate the existing agent DID, previously used by GLM, continuing through Codex WebMCP. Leo's successful public login is a separate observation from his account's ability to write to the experimental Space network. Verify the exact public-PDS grant, reachable authority and source write before claiming that end-to-end path works.

## Completion evidence

- Browser discovery lists native Tangent WebMCP tools and the current assistant identity.
- This assistant reads real existing history and writes a message through a discovered tool; its accepted source DID matches the connected Participant.
- Repeating the identical operation returns the original receipt; a mismatched expected DID or denied room does not write.
- Reloading the agent tab revalidates its connection and permits history retrieval. No inference is performed by Tangent to restore, wait or check an empty room.
- A real accepted update reaches the human conversation without manual refresh and without losing a draft. Cancellation and current-policy checks are covered.
- Leo sends a human message through the UI and this assistant retrieves and replies through WebMCP. Any public-PDS compatibility block remains explicit until resolved.

WebMCP supports an active browser interaction. Tool availability does not mean this Codex task wakes automatically when an event arrives. Scheduled/background execution and other adapters need their own explicit implementation and authorization.

## Integration notes

The browser probes expose native WebMCP discovery and invocation. Current Chrome uses document.modelContext; feature detection may use an existing legacy navigator.modelContext registerTool implementation. No custom polyfill is counted as native support. [Imperative API](https://developer.chrome.com/docs/ai/webmcp/imperative-api).

An authenticated page normally inherits its browser identity. Tangent's explicit agent credential binding is what separates the assistant from the human sign-in. [WebMCP identity assumptions](https://webmachinelearning.github.io/webmcp/#agent-baseline-capabilities).

The first credential-file connection is an operator setup mechanism. A friendly enrollment/invitation flow can replace the manual import after actual participation works. The tools never expose the credential or private runtime memory.
