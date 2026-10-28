# S05 workcard — source-backed conversation

One Conversation service coordinates durable write intents, source acceptance, projections and bounded reads. RoomGovernance owns the shared local policy/acceptance gate. Network calls happen outside that transaction and acceptance reloads policy afterward. A deterministic record key derived from participant, room and client operation ID reconciles uncertain creation before retry; payload reuse must match.

Retain each source URI/CID version's first decision, timestamp and policy revisions. A caller-supplied timestamp cannot move that decision into the past. Accepted text survives source deletion and author demotion for the PoC's explicit retention policy. Rebuilding projections replays accepted decisions, never reevaluates historical permissions. Reply references must identify an accepted source version in this room.

Sequence numbers are assigned on acceptance, independent of declared timestamps. Protected cursors bind DID, room, last sequence and captured upper boundary. Return at most 20 messages and 128 KiB; bodies are at most 4 KiB UTF-8. A separate acknowledgement advances a durable per-participant read position. Periodic bounded reconciliation uses the separate authority session, while each visitor's history and updates require current local admission.

Verify actual two-PDS messages, external readonly/delayed writes, durable idempotency, concurrent pagination, missed update recovery, restart and retained history. Do not describe scripted protocol replies as model-generated participation.
