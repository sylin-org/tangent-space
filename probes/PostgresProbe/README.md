# EPIC-005 PostgreSQL adapter-health probe

This console probe references the actual Tangent `Message` entity and the current pinned
Koan PostgreSQL source. It never starts the application host, HTTP listeners, authentication,
source/profile workers or domain acceptance. Only the coordinator's synthetic laboratory
is accepted: `127.0.0.1:25432`, database `postgres`, user `epic005`, public fixture password
`epic005-synthetic-local-only`. Set these explicitly in `TANGENT_POSTGRES_LAB_CONNECTION`.
No user connection settings or credentials are loaded. The source routes to a new
`epic005_pg_<GUID>` schema; the probe refuses an existing output root or schema.

After the coordinator grants the serialized build/run slot, use `./probes/PostgresProbe/run.ps1`.
It checks the laboratory's exact images, container/network/volume identities, loopback
bindings, resource limits, combined database size and host headroom before mutation and
after completion. For an already built probe use `-SkipBuild`. Its underlying commands are:

```powershell
$env:DOTNET_PROCESSOR_COUNT = '2'
dotnet build ./probes/PostgresProbe/PostgresProbe.csproj -c Release -m:2 --nologo -v:q -clp:ErrorsOnly
dotnet ./probes/PostgresProbe/bin/Release/net10.0/PostgresProbe.dll --self-test
$env:TANGENT_POSTGRES_LAB_CONNECTION = 'Host=127.0.0.1;Port=25432;Database=postgres;Username=epic005;Password=epic005-synthetic-local-only'
$pgProbeRoot = Join-Path (Get-Location) ('.local/experiments/epic005/postgres-health-' + [Guid]::NewGuid().ToString('N'))
dotnet ./probes/PostgresProbe/bin/Release/net10.0/PostgresProbe.dll --repo (Get-Location).Path --root $pgProbeRoot
```

The root must be an absolute new direct child of `.local/experiments/epic005`; its ancestors
must not be reparse points. The probe deletes neither schemas nor directories. The lab
container has the coordinator's separate 1-CPU/2-GiB cap; this Windows client uses affinity
mask 3 (two logical processors), a 10-minute cancellation deadline, a monitored 1.5-GiB
working-set ceiling, a monitored 10-GiB laboratory-database ceiling, 10-second connection
timeout and 45-second command timeout. These are bounded diagnostic runs, not saturation.

One real `Message.Save` provisions Koan's actual schema. A small create/read/update/delete
check runs through the entity facade. Native PostgreSQL binary COPY then clones the real
stored JSON template to 10,000 and 100,000 hot-Topic rows, with two additional Topics each
having 10% as many rows and the same Sequence ranges. Text mixes short/long Unicode,
synthetic source references and tombstones; timestamps retain the fixed actual template
value. COPY elapsed time is fixture construction, never domain-write throughput.

The four keyset windows match the SQLite probe: beginning, after-middle, after-tail,
before-middle; all 21 IDs, sequences, order and room identities are checked. It compares
materialized `Message.Query` with only the first 21 rows of `Message.QueryStream`, disposing
the iterator there. QueryStream's numbered continuation is not claimed to be mutation-safe
keyset traversal. Each operation has one discarded initial sample and six warm serial
samples; nearest-rank p95 is the maximum, not meaningful load-test tail latency.

Npgsql command logging captures actual statement text, including count side effects.
Representative captured SELECT/count SQL is replayed under `EXPLAIN (ANALYZE, BUFFERS,
FORMAT JSON)` with the known fixture predicate parameters. Connection pooling is disabled;
instrumented query latency is not a provider winner or full-API result. Original schema
indexes are recorded, followed by a separately labeled expression-index phase in this
disposable schema only. Descending plans remain explicit because reversing an all-ASC
compound index also reverses its ID tie-breaker.

Finally, native same-entity `RequireAtomic` batch success and second-write failure/rollback
are checked using an independent connection. A separate three-save
`EntityContext.Transaction` failure reports its durable rows. Same-entity batch atomicity
does not establish a native cross-entity transaction or repair Tangent acceptance. Reports
record source hashes and reject a run if those source files changed during measurement.

Build and run only in the coordinator's exclusive slot; PostgreSQL and MongoDB measurements
must not run concurrently or race shared project build outputs. The complete result is
written under the ignored experiment root. Publish only its small sanitized evidence.
