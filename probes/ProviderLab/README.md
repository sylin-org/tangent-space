# EPIC-005 isolated provider lab

This is a **synthetic-data experiment**, not a Tangent deployment. Neither application
container, application volume, identity credential nor external publication worker is
connected. The probes build the actual Tangent Entity types and pinned Koan adapter but
do not start the application host. No database switch has been made for the live app.

The dedicated Compose project is `tangent-epic005-provider-lab`. The two image digests are
pinned in `compose.yaml`. MongoDB 8.3.4 was already present locally; PostgreSQL 17.11 was
pulled for this experiment. These are tested versions, not a recommendation about the
newest version or production topology.

| Service | Host endpoint | Test configuration |
| --- | --- | --- |
| MongoDB | `127.0.0.1:27119` | `rs0`, single member, `directConnection=true` |
| PostgreSQL | `127.0.0.1:25432` | database `postgres`; each probe creates a fresh GUID schema |

The PostgreSQL credential in Compose is deliberately public, synthetic, and valid only
for this lab. Mongo has no authentication. Both ports bind to host loopback, on a dedicated
bridge network, not shared with application containers. Outbound network access is not
firewalled. Do not expose these services, reuse these credentials, copy user
data into them, or regard a single-member replica set as an availability test.

## Resource and state boundaries

- Each database has a hard Docker limit of one CPU, 2 GiB memory with no additional swap,
  256 processes and bounded logs. PostgreSQL shared memory is 128 MiB; Mongo's configured
  WiredTiger cache is 0.5 GiB. These are lab constraints, not production tuning.
- Run one probe at a time, restricted to two logical CPUs, with a monitored 1.5 GiB
  working-set ceiling and ten-minute deadline. This keeps the aggregate database/client
  allocation within four CPUs and approximately 6 GiB. Builds are separate and use `-m:2`;
  build memory is not included in measured client figures.
- Require 8 GiB free host RAM before starting. Check database storage between phases;
  stop if aggregate generated database state exceeds 10 GiB. Named volumes **do not**
  enforce a disk quota. Check free host disk and Docker stats as well.
- Probe output is under a new `.local/experiments/epic005/*-<GUID>` directory. Database
  namespaces are fresh GUIDs. Existing namespaces/paths must never be overwritten.
- Retain raw synthetic state for reproduction. `docker compose ... stop` releases runtime
  resources without deleting it. There is intentionally no automatic cleanup/reset step.

Before first creation, inspect container/volume names and ports to ensure none belong to
other work. Before resuming, verify the Compose project labels, image digests, loopback
ports, limits and lab-only named volumes. Never run a broad Docker prune or delete volumes
as part of these probes.

On this Docker Desktop host, an internal-only network suppressed host port publication
despite healthy containers. The lab therefore uses a dedicated bridge and verifies actual
TCP reachability **and** `127.0.0.1` port bindings; configuration intent alone is insufficient.

## Start and inspect

From the repository root, after the checks above:

```powershell
docker compose -f probes/ProviderLab/compose.yaml config --quiet
docker compose -f probes/ProviderLab/compose.yaml up -d --wait --wait-timeout 60
```

On a **new** Mongo volume only, initialize its single member:

```powershell
docker exec tangent-epic005-provider-lab-mongo-1 mongosh --quiet --eval 'rs.initiate({_id:"rs0",members:[{_id:0,host:"localhost:27017"}]})'
docker exec tangent-epic005-provider-lab-mongo-1 mongosh --quiet --eval 'db.hello().isWritablePrimary'
```

The last result must be `true` before running a Mongo probe. On an existing initialized
volume, inspect `rs.conf()` and `db.hello()` instead of reinitializing it. The driver must
connect to `mongodb://127.0.0.1:27119/?replicaSet=rs0&directConnection=true` so Docker's
single-member advertised address is not used as a host discovery target.

```powershell
docker exec tangent-epic005-provider-lab-postgres-1 psql -U epic005 -d postgres -Atc 'SELECT version()'
docker stats --no-stream tangent-epic005-provider-lab-mongo-1 tangent-epic005-provider-lab-postgres-1
docker exec tangent-epic005-provider-lab-mongo-1 du -sk /data/db
docker exec tangent-epic005-provider-lab-postgres-1 du -sk /var/lib/postgresql/data
```

Use each sibling probe's README for its exact invocation and evidence limitations. Run
Mongo first and PostgreSQL afterward; no concurrent performance measurements.

Run `./probes/ProviderLab/check.ps1` before/after each load phase. It verifies actual
container identity, limits, published loopback ports and data-volume ownership, then checks
aggregate on-disk state and host-memory headroom. A failed check stops the workflow; it
never deletes anything. This is a point-in-time check, not continuous resource enforcement.

Stop when finished:

```powershell
docker compose -f probes/ProviderLab/compose.yaml stop
```

## Interpretation

Provider health is not production admission. RequireAtomic rejection may be the **correct**
adapter behavior. A successful native same-Entity batch does not establish atomicity across
Tangent's Message/source decision/sequence/activity/receipt writes. Koan's ambient transaction
scope currently provides deferred sequencing, not native atomicity. A deployment decision
still requires domain failure/restart recovery and full-API/browser workload evidence.

Provisioning references: [MongoDB development replica sets](https://www.mongodb.com/docs/manual/tutorial/deploy-replica-set-for-testing/)
and the [PostgreSQL official image configuration](https://hub.docker.com/_/postgres).
