# Isolated Mongo provider probe

This is an EPIC-005 experiment of actual Tangent `Message` / `ActivityHead` entities through the pinned Koan Mongo adapter, not an application acceptance test or a provider recommendation. It never starts the application host or its workers.

## Run

The coordinator must first provision and verify `../ProviderLab/compose.yaml`, reserve the serialized build/run slot, and confirm the loopback Mongo replica set. The probe is deliberately fixed to `127.0.0.1:27119`, `rs0`, `directConnection=true`; it cannot be pointed at a live/default database. No user credentials are read.

```powershell
./probes/MongoProbe/run.ps1 -Posts 10000
./probes/MongoProbe/run.ps1 -Posts 100000 -SkipBuild
```

The script checks the lab before/after each run, uses `DOTNET_PROCESSOR_COUNT=2`, and builds with `-m:2`. The executable fixes Windows affinity to two logical CPUs, checks its working set against 1.5 GiB and its database storage/index size against 5 GiB between batches/cases, and uses a 10-minute cancellation token. Final diagnostics have an independent eight-second deadline. These client checks are monitored/cooperative limits, not hard operating-system quotas. Lab containers have separate hard limits; the lab check refuses aggregate database volume state above 10 GiB or less than 8 GiB free host RAM.

Each execution creates a new validated `.local/experiments/epic005/mongo-baseline-<GUID>` directory and an explicit fresh `epic005_mongo_<GUID>` database. Inherited host configuration sources are cleared, so environment/default application database selection cannot replace this connection. The probe refuses existing directories/databases and reparse-point ancestors. It does not delete databases or prior evidence. The CRUD test deletes only its own synthetic record. The deferred failure test adds a validator only to its fresh synthetic `ActivityHead` collection.

`result.json` contains source hashes, version/resource metadata, failure diagnostics, all timings, representative native commands and full `executionStats` explains. The unique start marker must remain in the capped profiler collection, or capture fails as inconclusive. Every materialized query must yield exactly one count aggregate and one find; the first stream page must yield exactly one find. Profiler markers/collection administration are outside those application command counts.

## What it exercises

- First facade Save (including initialization), Unicode/date CRUD, edit/tombstone persistence and delete.
- `RequireAtomic` rejection with add + mutation callback + delete: exact capability exception, callback never invoked, zero traced Message-collection commands and independently read unchanged documents. Administrative commands and other namespaces are not covered by this trace assertion.
- Hot room of 10k or 100k rows plus two distractor rooms each at 10% of hot count. Deterministic repeated sequence ranges, text lengths, Unicode, edits and tombstones; every returned identity/sequence/room/content is checked.
- Actual `Message.Query` predicates, 21-row limit and ascending/descending order, with baseline `_id` index then a lab-only `(roomKey ASC, sequence ASC, _id ASC)` index.
- A separately captured first 21-row `QueryStream` page, then disposal. This is not a multi-page/resumable-stream proof.
- Actual deferred `Message → ActivityHead → Message` scope; forced Mongo validation error 121 on the second entity, independently inspected durable partial state. A replica set does not make this Koan deferred coordinator native-atomic.

Raw bulk BSON clones deliberately bypass domain/lifecycle authoring. They inherit first-row accepted/content timestamps and source URI/CID; facets are empty and source labels are synthetic. They are a read-query fixture, not valid protocol history. First-call numbers are not cold-disk numbers; six serial profiled warm samples are directional only, with nearest-rank p50 (third sorted sample) and p95 (maximum). No API/authorization/source-acceptance/restart/concurrency/tail-latency or saturation claim follows.

The initial rejection-message assertion was too narrow and failed safely before seeding; that separate failure artifact is retained in `docs/evidence/epic005/mongo-harness-initial-failure-20260912.json`. Successful current-code evidence and interpretation are in `docs/evidence/epic005/mongo-baseline-20260912.md`.
