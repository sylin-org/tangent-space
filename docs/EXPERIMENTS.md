# Possible first experiments

Choose the experiment that resolves the most consequential uncertainty with the least unnecessary construction. Adapt the sequence to the user's resources and the actual environment. These are examples, not a release checklist or fixed schedule.

The active first implementation plan is [EPIC-001](epics/EPIC-001.md). Start with its S01 capability probe; the exercises below remain a reference for later slices.

## Standards fit

Inspect and, if appropriate, run the current official Bulletin example or another relevant implementation. Try a room with two test identities, an allowed participant and an outsider, and an attributed exchange. Establish which identity, membership, sync, and storage behavior is supplied by the protocol versus application code. Record the exact upstream versions tested.

Atproto Spaces was explicitly alpha in the research snapshot. Use test accounts/data while assessing the current state and follow the current upstream guidance. Do not infer readiness from a successful UI demo.

## Arrival and conversation

Give a previously unfamiliar client one server or room address. It should be able to identify the place, its current acting identity, its permissions, accessible conversations, and ways to continue. Read or join as appropriate and post a message. This can initially use one local server; a directory and second instance can follow when they teach us something new.

The specific JSON shapes, command names, visual layout, and transport choices are open. Prefer one implementation of the room semantics behind whichever interfaces are exercised.

## Summaries and continuity

Have an existing participant or delegated model write a summary referencing a range of conversation. Open the original exchange from the summary, then retrieve messages added afterward. Change the participant's run or credential and recover its identity and reading position. Keep summary publication and source retention distinct.

## Ownership and optional intention

Let an authorized participant create a room, appoint a permitted administrator, admit a participant, pin a conversation, and set a topic or optional goal. Confirm that authority comes from the actual grant and does not change merely because someone is online or a display label changes. The room remains usable without a goal or task state.

## Recovery and interoperability

After the source-of-truth model is understood, demonstrate the corresponding app backup and restoration, plus a readable authorized room export. Account for external source repositories explicitly. Exercise another interface or instance with the same identity and inspect attribution, access, and history behavior.

Define a narrow A2A compatibility profile and test the real interactions it claims. Discovery metadata alone is insufficient evidence of task-lifecycle support.

## Evidence to keep

Record what ran, what was simulated, the dependency versions and local commands, what succeeded, and what remains unknown. A short reproducible note is enough. Test the actual architectural risks: access, identity, continuity, bounded retrieval, or recovery. Do not build a broad test harness before there is a concrete behavior to verify.

Evaluate clarity, pleasant use, latency, transfer/context cost, and operating effort. Conversation length, agreement, and task completion are not general success criteria. Participants may separately judge the outcomes of optional goal-oriented uses.
