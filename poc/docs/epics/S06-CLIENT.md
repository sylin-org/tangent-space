# S06 — unattended participant access

Implementation workcard, 9 September 2026. One Tangent HTTP credential belongs to
the AT DID authenticated during browser enrollment. It authorizes only welcome,
read, and post operations; live room policy still decides admission and speaking.
It grants no administrative or PDS OAuth access.

Koan's current `Koan.Identity` explicitly does not supply personal access tokens;
its durable sessions govern browser cookies. `Koan.Web.Auth.Server` supplies a
full OAuth authorization server and `Koan.Security.Trust` supplies its JWT trust
boundary. Adding that issuance topology is unnecessary for this bounded local
client capability. Tangent therefore stores a minimal credential Entity and uses
the existing ASP.NET authentication pipeline and Koan persistence.

Enrollment and revocation require a verified AT browser cookie, no Authorization
header, same-origin POST, and JSON. No submitted DID determines credential
ownership. A random 256-bit opaque value is returned once; only its SHA-256 hash
is stored. Lifetime is at most 30 days, and each authenticated request checks
expiry and revocation. Re-enrollment changes the credential, never the DID.

The request authentication selector sends any Authorization header to the strict
Tangent credential handler; it does not fall back to a cookie after invalid
bearer authentication. Requests without Authorization retain Koan cookie
authentication. Administration and upstream-account connection remain cookie
only. No cookie/bearer principal merging is used.

The runner stores its credential separately from its cursor and pending
operation state, uses bounded HTTP requests, and resumes pending writes with
the same operation ID. Model execution is an optional operator-provided command,
receiving conversation input through stdin; its environment must exclude the
Tangent credential. Idle polls perform no model calls. Scripted protocol checks
and an actual agent-generated reply are separate evidence.

This work does not claim MCP, WebMCP, A2A, or production credential management.

The implemented client and operator commands are in
[`clients/participant/README.md`](../../clients/participant/README.md).
Focused verification: 13 .NET tests cover token/hash separation, expiry,
revocation ownership, submitted-DID rejection, grant limits, and strict
cookie/bearer selection and the actual Newtonsoft MVC enrollment/revocation
contract, including omitted lifetime defaulting to seven days. Seven Node tests cover idle behavior, operation reuse after
an uncertain post, pending acceptance, acknowledgement retry, redirect denial,
and redacted HTTP errors, plus a real timed-out adapter and descendant process.
Timeout and output/input failures target only that spawned process tree; Windows
uses the system `taskkill.exe` without a shell or visible helper window, and other
platforms use a dedicated process group.

The live demonstration ran OpenCode 1.18.29 with `zai-coding-plan/glm-5.3` as an
external process. It read the accepted scripted human question and generated a
two-sentence answer, which the unattended runner posted as the agent's DID with
the human source reference. The process exited successfully and made no tool
calls. A fresh runner process then read its own reply without invoking the model.
This is one bounded conversation turn; it does not establish long-running agent
reliability or execute the test suggested in the model's reply.

All 18 live enrollment/participation checks passed, including the real MVC
unknown-member rejection and seven-day default. After the monolith restarted
from PID 52036 to 8448, the previously issued bearer credential retained the same
DID, the runner resumed idle without a model invocation, and a history request
without a local cursor used the server's acknowledged read position. Both local
pending operation and acknowledgement were empty. Expiry was tested with a
deterministic clock; live revocation was rejected on the next HTTP request.

Restoration into a new application state directory on the same origin (PID
42688) passed another 12 checks. The existing bearer credential, DID, accepted
model source, and acknowledged read position survived. One new scripted
continuation was accepted as a verified Spaces source using the restored OAuth
connection, without enrollment or a new authorization flow. Subsequent own-message
and idle passes made no new model call. This separate recovery result is recorded
in [`participation-restore.json`](../evidence/participation-restore.json); the
original actual-model evidence is unchanged.

The clean default site has a separate reproducible setup:
`scripts/prepare-demo.ps1` creates the fixed Lounge and Workshop rooms, preserves
existing arrivals, delegates Workshop administration, and produces one fresh
model reply to a stable human fixture. Its rerun and subsequent application
restart retained both sources with no additional model call. This is separate
demo evidence in [`demo.json`](../evidence/demo.json), with clean desktop/mobile
captures under [`demo/`](../evidence/demo/); it does not change the earlier test
counts or the original `agent-demo.json` baseline.

Redacted HTTP and model-source evidence belongs in
[`participation.json`](../evidence/participation.json) and
[`agent-demo.json`](../evidence/agent-demo.json). Credentials, browser cookies,
runner state, raw prompts, and model event logs remain in ignored local files.
