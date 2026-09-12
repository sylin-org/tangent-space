# MongoDB / actual Koan provider experiment — 2026-09-12

## Result

The actual Tangent entities work through the current pinned Koan Mongo adapter for the tested CRUD and 21-row read shapes. With a matching lab-only compound index, the forward count-free first stream page examined **21 documents and 21 keys** at both fixture sizes, versus **120,002 documents** without that index at 100k hot-room rows. This confirms bounded native work for that specific indexed query, not for the current whole application API.

Two remaining limitations are directly observable: materialized `Message.Query` still performs an exact count, and the reverse ordering has a mismatched tie-break direction for the single ascending index. The deferred coordinator also permits a durable partial write across entities. These are query/index/coordination boundaries; this experiment does not select a provider winner or prove native cross-entity Koan transactions.

Artifacts:

- [10k raw result](mongo-baseline-20260912-10k.json)
- [100k raw result](mongo-baseline-20260912-100k.json)
- [Initial harness assertion failure](mongo-harness-initial-failure-20260912.json)
- [Runnable harness](../../../probes/MongoProbe/README.md)

## Environment and scope

Actual `TangentSpace.Conversation.Message` / `TangentSpace.Activity.ActivityHead` assemblies are referenced, together with the explicit Koan Mongo provider. Koan checkout HEAD `e07a84cc3f71a0867f1122b03b723cc80727e772`; Tangent HEAD `5c98bd4cf5a1bdd50d10ed8aef0a980cd6f48994`. Both worktrees contain unrelated work preserved as-is; the JSON records exact hashes of the measured entity, history service, facade, data path, Mongo repository and deferred coordinator. No app/framework source was changed by this experiment.

Mongo image `mongo:8.3.4@sha256:309d760ba3f7962e14d54ac6123c7fe72ed2d465196b4d0eaf8abcea7dd39450`; measured server 8.3.4, MongoDB .NET driver 3.10.0, .NET runtime 10.0.12 / SDK 10.0.401, Windows build 26200, Intel i7-12700KF (20 logical processors). Client affinity is two logical CPUs with `DOTNET_PROCESSOR_COUNT=2`; Mongo container is hard-limited to one CPU and 2 GiB. The dedicated lab bridge has outbound networking (not an outbound-blocking security boundary); the published endpoint is loopback-only `127.0.0.1:27119`, replica set `rs0`, direct connection, synthetic unauthenticated lab state. No app host or application network/identity workers were started, no credentials loaded, and no live application state or ports were used.

The 10k run used fresh database `epic005_mongo_352b8e55fdf6415fae473521e522d4bf`; 100k used `epic005_mongo_d342e81c22a942798f0b819666fd0765`. Each has two distractor rooms of 10% hot-room size, plus two atomic-admission sentinels present during reads: 12,002 / 120,002 documents in the measured collection. Bulk seed clones an actual provider-authored BSON template and varies ID/room/sequence/author/text/edit/tombstone. Accepted/content timestamps and source URI/CID remain inherited, facets are empty, and source labels are synthetic. This bypasses domain/lifecycle authoring; it is not a valid source-protocol/history fixture.

Build passed with zero warnings/errors. Built-in deterministic nearest-rank and wrong-room/window self-tests passed; both completed runs returned exit 0 with empty failure and diagnostic-failure arrays. Every one of the 21 returned rows was checked for exact order, ID uniqueness, room, sequence, text, tombstone and edit presence, including distractor-room queries.

## Native query evidence

Every materialized window was verified to emit one exact count aggregate (`$match` plus `$group`/`$sum`) followed by one `find`, `limit: 21`, without a `skip`. Query predicates explicitly bound room and sequence edges. Forward sort is `{sequence: 1, _id: 1}`; reverse is `{sequence: -1, _id: 1}`. The count-free stream capture consumed exactly one 21-row page and disposed; this does not prove later stream pages use resumable keysets.

At 100k hot rows:

| Query shape | Baseline native work | Added ascending compound index |
| --- | --- | --- |
| Forward find (all tested positions) | 120,002 documents + top-k sort | 21 documents / 21 keys; no blocking sort |
| Forward middle exact count | 120,002 documents | 0 documents / 50,000 keys |
| Reverse middle find | 120,002 documents + top-k sort | 21 documents / 49,999 keys + blocking sort |
| First forward stream page | One find, 120,002 documents | One find, 21 documents / 21 keys |

Baseline indexes are only `_id_`. The experiment then adds non-unique `{roomKey: 1, sequence: 1, _id: 1}`, named `epic005_room_sequence_id`, only in each isolated database. The reverse query still sorts because reversing this index reverses both sequence and ID, while the emitted sort reverses only sequence. This is a Tangent/Koan query-shape versus index-direction gap, **not a MongoDB provider bug**. A mixed-direction index or a coherent reversible tie-break design remains a separate follow-up; neither was added or measured here.

An index also does not eliminate exact-count work: 100k beginning counts examine 100,000 keys and middle counts 50,000. The current first-page stream demonstrates an existing count-free seam, while explicit count intent for materialized windows remains the K02 contract task.

## Instrumented timing (milliseconds)

These are first shape calls plus six serial warm samples with database profiling enabled. The table gives nearest-rank p50; JSON retains all samples and p95, which is simply the maximum of six. Profiling, JIT/cache history, a local single-user lab and small sample count make these directional measurements, **not application p95 targets, cold-disk results or saturation benchmarks**.

| Window | 10k baseline | 10k indexed | 100k baseline | 100k indexed |
| --- | ---: | ---: | ---: | ---: |
| Beginning | 14.92 | 5.10 | 91.69 | 13.46 |
| Middle-after | 11.63 | 5.39 | 88.75 | 9.26 |
| Tail-after | 11.29 | 5.36 | 72.85 | 3.84 |
| Middle-before | 13.21 | 8.58 | 84.97 | 45.85 |
| Distractor room | 10.10 | 4.94 | 48.87 | 5.88 |

The first facade Save including initialization took 242.52 ms / 254.90 ms. Raw seed took 0.433 s / 4.035 s. Complete measured runs took 3.88 s / 10.34 s, with peak client working sets 159.55 MiB / 170.69 MiB. End-of-run per-database `totalSize` was 0.45 MiB / 13.80 MiB (a snapshot, not final disk settlement or only logical payload). The final infrastructure guard observed combined Mongo+Postgres volume state 264.50 MiB and 32,814 MiB free host RAM. No resource ceiling was reached; no state was deleted.

## Capability and failure evidence

`RequireAtomic: true` with a new add, an existing mutation callback and a delete is rejected by the facade with the expected `NotSupportedException`: the adapter does not expose a proved native atomic batch boundary. The batch reports capabilities `None`; the callback did not run, captured Message-collection commands were empty, and an independent Mongo driver read confirmed all three target documents unchanged. The unique profiled start marker remained retained, so the empty trace is not inferred from a missing or wrapped capture. Administrative commands and other namespaces are outside this trace assertion.

The separate actual deferred scope queued `Message → ActivityHead → Message`; a lab-only Mongo validator rejected the second write with provider error 121. The exception chain reported one completed operation. Independent driver reads confirmed **first message present, rejected ActivityHead absent, third message absent**. The harness requires this exact injected-provider failure and durable state. This proves the measured deferred coordination is not an atomic native transaction, even on a writable replica set. It does not test all Tangent acceptance rows, activity/outbox invariants, restart recovery, unknown outcomes or concurrent writers.

## Initial failure and evidence quality

The first attempt (`d0589937dccf4e7bb9e79a2c66b3992b`) passed CRUD but stopped before bulk seeding because the harness expected only the Mongo repository's capability-rejection message. The facade correctly rejected earlier with a different precise message. That was a harness false-failure, not a provider defect; its artifact remains separate. The accepted-message set was narrowed to those two known capability exceptions, with all unrelated errors still failing the experiment.

Independent pre-run review also tightened capped-profile retention (a unique retained start marker, not a row-count offset), exact command-count expectations, full-window validation, exception-chain inspection and bounded failure diagnostics. Final cleanup has an independent eight-second token and records unavailable metadata rather than losing the local failure report. No outage/restart test was run in this bounded baseline. Independent runtime rerun is a separate red-team result, not claimed by this report.
