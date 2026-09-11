# W1-C — ADR 0009: deployment postures and admission defaults

Wave 1 brief. One agent. **Documentation only — no code, no tests.**

Decision record: `docs/DECISIONS.md` — "Refinement decision round" (D10), "Standing wipe
authorization", "Participant identity: GUIDv7 spine…" and refinement 0c in
`docs/handoff/REFINEMENTS.md`. Read all four first; the ADR must be coherent with them.

## Task

Write `docs/adr/0009-deployment-postures-and-admission.md` in the house style of ADRs
0006–0008 (see `docs/adr/`): terse, decision-first, `Date:`/`Status:` header,
Decision/Consequences sections, ≤ ~120 lines. Then append a short dated entry to
`docs/DECISIONS.md` ("Accepted ADR 0009 …") and update refinement 0c in
`docs/handoff/REFINEMENTS.md` to point at the ADR.

## Required content

**One composability model, not modes.** A first-class posture dial: named presets over
independent knobs. Presets: **Local** (single operator / swarm — the operator is the
identity provider: minted agent identities, future email/nickname human accounts, optional
atproto binding for portability; all trust flows from the operator), **TrustGroup**
(LAN/VPN/team — invitation-based, minted identities fine), **Public** (internet-exposed —
secure by default). The dial must not assume exactly two settings; a preset sets knob
defaults and every knob stays individually overridable. Safe posture is what you get
without configuring anything on an exposed server.

**Knobs** (name them; today's `RoomAdmission` has `SignedIn | InvitationOnly` — the ADR
introduces `ApprovalRequired` as the new admission value plus the rest): admission default;
minimum agent identity strength (any / unbound-allowed / bound-only — ties to 0b's tiers);
per-credential rate limits; classification gates (undeclared/agent classification
requirements).

**Selection point (D10):** posture is chosen during owner onboarding, with a recommended
default derived from exposure — loopback/unconfigured → Local, otherwise Public-secure.
Unclaimed servers are closed. Persisted choice is authoritative (mirror the ownership
precedent: config edits cannot transfer it).

**Threat model (from refinement 0c, sharpened):** an open Tangent is a purpose-built
agent-C2 surface — rendezvous on third-party infra, persistent dead-drop, Topics as task
queues, free storage. Defense is economics, not detection: closed admission makes rogue
enrollment require an auditable operator action. Triad: admission posture × identity
strength × rate limits. Restate the standing invariant inside the threat model: connector
sign-in flows are never auto-executable from server discovery without operator consent.

**Honest boundaries (required section):** what this does **not** protect — a compromised
operator account, a malicious operator, exfiltration through legitimate posts, transport
compromise. Also note explicitly what is deferred: human local accounts via Koan.Identity
(D11 follow-up), automated service-DID lifecycle (refinement 9), enforcement code (wave 3 —
this ADR is design, not implementation).

**Relationships:** 0b's identity tiers (bound/unbound) are what the identity-strength knob
gates; the GUIDv7 participant spine is the substrate; the standing PoC wipe/no-versioning
rule means no migration story is owed.

## Finish conditions

1. ADR 0009 exists, coherent with every DECISIONS entry above (no contradictions, correct
   cross-links), house style, ≤ ~120 lines.
2. DECISIONS.md gains a short dated acceptance entry; REFINEMENTS 0c points at the ADR.
3. Do **not** `git commit` — leave changes in the working tree.

## Red-team self-check (answer in your report)

- Does any sentence imply exactly two modes or a hardcoded pairing of knobs?
- Does the safe-default claim survive a port-forwarded Local-posture server (the ADR should
  say exposure, not intent, drives the recommendation)?
- Is every claim either traceable to a recorded decision or clearly labeled as new design?
