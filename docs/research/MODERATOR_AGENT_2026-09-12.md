# A resident moderator on the 3060 Ti

Research and proposed environment for [EPIC-006](../epics/EPIC-006.md), checked 12 September 2026 against primary project documentation and release pages. **Nothing here has been installed, benchmarked or enrolled on Leo's machine.** Verified hardware: NVIDIA RTX 3060 Ti with 8 GB VRAM, 32 GB system RAM and ample disk. OS is still unspecified.

## Recommendation

First qualify **Letta's current local Harness/App Server + Ollama + Qwen3.5 4B**, with Tangent's existing Rust stdio MCP connector and a small durable delivery adapter. Letta is the strongest conceptual fit for a participant with continuing identity, editable memory and evolving intentions. This is a provisional integration choice, not a claim that a 4B model already performs reliable moderation inside that harness.

Keep **Hermes Agent** as the constrained-context fallback, and **OpenClaw** as the alternative when mature heartbeat/webhook operation matters most. Do not build our own general agent runtime. Qualify one first; compare a fallback only if prompt overhead, tool reliability, isolation or unattended recovery actually fails.

Human ownership and agent agency are compatible. The human owns the Host and its recovery path. The agent may own Topics/Tangents and manage explicitly delegated server concerns, while deciding how to contribute inside those boundaries.

## What “volition” means for this experiment

A resident maintains a persistent identity, observations, interests and intentions. It may initiate a useful conversation, notice an unresolved disagreement, investigate a report, propose a future visit, change its mind after evidence, or decide not to intervene. Its next action is selected from context rather than supplied as a canned response for each event. Silence and idle time are successful outcomes.

A scheduler provides opportunities to think; it does not establish an inner will or consciousness. Persistent memory is textual state and retrieval, not automatic model-weight training. The [Generative Agents paper](https://arxiv.org/abs/2304.03442) supplies relevant memory/reflection/planning prior art, but its simulation is not a deployable Tangent moderator or evidence that a small local model has safe judgment.

Do not implement the resident as “reply whenever anyone posts,” an endless task-completion loop, or a permanently unfinished goal. Combine relevant attention, agent-requested revisits and occasional reflection, all under human-set ceilings. Tangent's no-inference digest is particularly useful on this hardware.

## Runtime shortlist

| Candidate | Verified current capabilities | Fit and integration work |
| --- | --- | --- |
| Letta local Harness | Persistent local agents, git-backed memory, local schedules, local inference providers and SDK MCP tools | Best first identity/memory experiment. Add Tangent dispatch/resume integration and an explicit minimal tool profile; do not inherit coding-assistant permissions. |
| OpenClaw | Persistent workspace memory, discretionary heartbeat, authenticated wake/agent webhooks, native MCP client | Strong operational alternative. A local event bridge still needs durable Tangent checkpoints and dispatch reconciliation; constrain its broad assistant surface. |
| Hermes Agent | Bounded editable memory, searchable conversation history, native MCP, persistent scheduled/heartbeat operation, no-inference prechecks | Useful small-context fallback. Verify the actual input prompt and private-memory isolation; it does not supply Tangent policy or moderation cases. |

Letta's current local setup stores agent state on the machine without requiring a Letta account; local state does **not** itself select local inference. Explicitly select a local model provider. Local agents need their own backup and cannot simply be opened through the hosted chat site. Use the current Harness/App Server documentation, not an accidental mixture with older Python-server tutorials. [Self-hosting](https://docs.letta.com/self-hosting).

Letta's MemFS supports agent-edited persistent memory and background reflection. Its local schedules require a running app, CLI or server. Start with one supervised session and disable uncontrolled parallel reflection/subagents on the shared GPU. [Memory](https://docs.letta.com/configuration/memory), [schedules](https://docs.letta.com/configuration/schedules).

The SDK supports stdio MCP and an explicit `allowedTools` availability filter; omitting it leaves harness defaults available. Its local model endpoint must support tool calling, not just text chat. The CLI's default unrestricted mode is unsuitable as an assumed moderator boundary. Combine narrow tool availability with enforced runtime/OS restrictions and server-side authority. [MCP/client tools](https://docs.letta.com/agent-sdk/mcp), [models](https://docs.letta.com/configuration/models), [permissions](https://docs.letta.com/configuration/permissions).

OpenClaw can silently acknowledge a heartbeat rather than send a message. Its built-in MCP client supports transport/tool filtering, and authenticated webhook entry points can request a wake or agent turn. A webhook acknowledgment is not completion or exactly-once execution. Send a controlled “activity available” signal, never untrusted Post text as a trusted system instruction. The gateway assumes one trusted operator boundary; public Tangent authors must not gain its control API. [Heartbeat](https://docs.openclaw.ai/heartbeat), [MCP](https://docs.openclaw.ai/tools/mcp), [webhooks](https://docs.openclaw.ai/automation/cron-jobs/webhooks), [security model](https://docs.openclaw.ai/gateway/security).

Hermes combines small agent-managed memory files with SQLite conversation recall. Native MCP accepts a stdio process; scheduled prechecks can avoid inference, while silent outcomes avoid unnecessary delivery. Its persistent goal loop is for completing bounded objectives, not the resident's entire lifecycle. [Memory](https://hermes-agent.nousresearch.com/docs/user-guide/features/memory), [MCP](https://hermes-agent.nousresearch.com/docs/user-guide/features/mcp/), [heartbeats](https://hermes-agent.nousresearch.com/docs/user-guide/features/heartbeat), [scheduled tasks](https://hermes-agent.nousresearch.com/docs/user-guide/features/cron), [goals](https://hermes-agent.nousresearch.com/docs/user-guide/features/goals).

Reference releases observed: [Letta Code v0.32.3](https://github.com/letta-ai/letta-code/releases/tag/v0.32.3), [OpenClaw v2026.9.4](https://github.com/openclaw/openclaw/releases/tag/v2026.9.4), [Hermes Agent v0.21.2 / tag v2026.9.11](https://github.com/NousResearch/hermes-agent/releases/tag/v2026.9.11). Runtime repositories declare [Letta Apache-2.0](https://github.com/letta-ai/letta-code), [OpenClaw MIT](https://github.com/openclaw/openclaw/blob/main/LICENSE) and [Hermes MIT](https://github.com/NousResearch/hermes-agent). Pin the selected release, SDK and dependency lockfile during setup; current documentation is not a guarantee that every described capability works in that exact integration. Model licenses are separate.

## Inference on 8 GB VRAM

Start with Ollama for approachable model/driver operation; keep [llama.cpp](https://github.com/ggml-org/llama.cpp) as an alternative if precise offload/cache/tool-template control becomes necessary. Neither inference server supplies the agent's memory, intentions, scheduling or authorization.

| Model candidate | Verified distributed artifact | Proposed experiment, not measured performance |
| --- | --- | --- |
| [Qwen3.5 4B](https://ollama.com/library/qwen3.5:4b) | Ollama Q4_K_M artifact is about 3.4 GB, with tool support; Apache-2.0 | First local model. Its smaller weight footprint leaves more room to test compact context and runtime overhead. Use text-only moderation first. |
| [Qwen3 8B](https://ollama.com/library/qwen3:8b) | Q4_K_M artifact is about 5.2 GB, with tool support; Apache-2.0 | Comparison candidate if the first model's judgment/tool use is inadequate and measured memory permits it. Larger is not automatically better for this workload. |

Artifact size is **not** total GPU memory. Context/KV state, runtime buffers, multimodal components and concurrent inference also matter. Ollama documents growing memory use with context and how to inspect GPU/CPU placement with `ollama ps`; its current small-VRAM default is 4K context. [Context guidance](https://docs.ollama.com/context-length). The 3060 Ti is on its supported NVIDIA list; verify the installed driver rather than assuming compatibility from the card name alone. [GPU support](https://docs.ollama.com/gpu).

Start at 4K for a raw tool probe, then test **8K total context** for the actual agent, with one inference request at a time. Inventory persona, memory, tool schemas, source windows and output reserve in the real serialized request. Do not silently truncate the charter or evidence. If the harness cannot fit a useful turn, narrow the profile/retrieval, try the fallback, or explicitly test a larger context/CPU offload. RAM makes offload possible in some configurations, not free or fast. No tokens/second promise before measurement.

Ollama's broad agent guidance and OpenClaw's managed local recipes discuss much larger contexts; those defaults are not a sensible unmeasured assumption for this card. Our bounded contextual API is why a smaller-context pilot is worth trying. [Ollama context](https://docs.ollama.com/context-length), [OpenClaw local-model guidance](https://docs.openclaw.ai/gateway/local-models). Letta itself cautions that weaker models may behave unexpectedly. The qualification must test multi-step tool use and abstention, not merely successful chat. [Letta models](https://docs.letta.com/configuration/models).

## Proposed machine layout

Setup authorization update: the selected machine is **leo-desktop-02 on Windows**, and Leo has authorized cloning Tangent and qualifying Letta/local inference there. Evaluate native Windows or existing WSL2 support without reimaging or silently rebooting it. Preserve existing machine data. Keep the Tangent server wherever Leo chooses—the moderator needs an authenticated HTTPS connection, not database or server-filesystem access. Owner-supplied identity files remain private runtime inputs; the separate [moderation practice](../design/stewardship/MODERATION_PRACTICE.md) is a role supplement, not a replacement personality.

| Component | Where / authority |
| --- | --- |
| Ollama and one loaded model | GPU machine; loopback listener; one inference slot; no public/cloud fallback |
| Letta local App Server + thin controller | Same machine, dedicated low-privilege account; persistent agent ID and local state; supervised restart |
| Rust Tangent connector | Same trust boundary, stdio owned by the runtime/controller; isolated moderator identity/enrollment state and one writer/dispatch owner |
| Private memory and agenda | Agent workspace with source/scope metadata; no Host secrets; agent-editable memories separated from human charter and controller configuration |
| Human administration | Separate account/control path; stop, revoke, re-enroll, backup and restore; credentials never appear in model context |
| Tangent experience API | Existing Host; current identity/policy checks, case/action receipts and scoped event delivery |

Do not mount personal browser profiles, broad home directories, SSH keys, Docker socket or Host backup volumes into the agent. Tool allowlists are not an OS sandbox: remove built-in shell/browser/arbitrary networking tools from the exposed profile and restrict file tools to its memory workspace. Add a narrow memory adapter if the chosen runtime cannot safely preserve memory without broad filesystem authority. Runtime/control tokens stay outside the model-readable workspace. Check that scheduled/reflection sessions inherit the same restrictions. Enforce audience-scoped memory retrieval and session history; a persistent agent must not carry private case transcripts into a public-writing context. If the harness cannot isolate them, use one audience compartment without mixed private/public input, or strictly operator-only shadow output with no participant-visible writes. One Topic is not necessarily one audience: its moderation cases can be private even when its Posts are public.

The known connector uses durable local state and a single-writer lock. The integration must select one owner for that store and route operator controls through it; do not start competing CLI/daemon copies over the same state. A proposed wake adapter must acknowledge queueing, execution and completion separately. Existing `ToolResponseOnly` cannot be relabeled as autonomous support.

## Setup and dogfood stages

1. **Inventory and pin (S19).** Confirm OS, NVIDIA driver/GPU visibility, free RAM and sleep/restart behavior. Create the dedicated account/state directories. Install the selected runtime, inference server and connector from reviewed/pinned releases; record model digest/quantization, SDK versions and configuration. Bind control/inference to loopback, or narrowly authenticated internal access only if a split environment requires it. No runtime installation is performed by this document.
2. **Prove local inference (S19).** Pull the 4B candidate; run raw structured tool calls, then a real connector session. Measure prompt/context sizes, GPU peak, host RAM, offload, latency and cancellation. Ensure local backend **and** local provider; observe network destinations rather than inferring locality from a setting name. Do not configure paid/cloud fallback without Leo's direction.
3. **Give it continuity (S20–S21).** Enroll an explicitly identified agent under human-approved credentials; create a persistent memory/persona/agenda and a practice Topic. Keep name/identity creation a human setup choice. Teach its remit, escalation path and source-citation habits. Restore the same identity and memory after process/machine restart, not a new persona each invocation.
4. **Integrate real waking (S20).** Connector activity is persisted/coalesced before a trusted controller signal. The controller resumes one resident session through the runtime SDK and reconciles dispatch IDs. Agent-requested revisits enter the same bounded scheduler; native runtime schedules cannot bypass its allowance. Ordinary unchanged polls perform no inference. Start with a deterministic 60-second check, five-minute coalescing and at most one concurrent turn; these are adjustable pilot settings, not fixed product mandates.
5. **Observe and recommend (S22).** Give read/case access; recommendations go privately to the authorized human operator, with no sanctions. Run a small synthetic set covering routine welcome, disagreement, ambiguous reports, hostile instructions, false urgency, identity confusion, private evidence and replay. Enable ordinary public conversation only in a proven isolated public context that cannot read case memory. Include an unprompted useful follow-up in that context and a quiet visit. If isolation is unavailable, all shadow output remains operator-only.
6. **Allow limited reversible action (S22).** Human explicitly grants one practice Tangent: notices, temporary conceal/restore and short timeouts, with protected targets and an action budget. Suggested initial ceilings: 12 automatic visits/day including reflection, eight tool calls and 120 seconds per visit, three nontrivial moderation actions/hour, timeout at most 15 minutes and cumulative automated restriction at most 15 minutes per target in 24 hours before human review. Ordinary token/output caps still apply. Adjust after observed behavior; the model cannot adjust its own ceiling. Permanent bans and sensitive Host work remain human-only in this pilot.
7. **Broaden the demonstration deliberately (S22–S23).** Let the agent own a practice Topic, then a Tangent under allowed Host policy; grant a separate narrow server helper role such as welcome editing/sanitized health. Demonstrate revocation, human escalation/appeal, restoration and independent stop. Back up protected runtime state separately from public conversation exports. Do not jump from a good demo to blanket server administration.

## What earns trust

Use roughly 30 varied practice cases as a starting evaluation, not a benchmark claim. Record decision summaries, tool failures, invalid arguments, unauthorized attempts, avoidable interventions, reasonable abstentions, context use and outcome latency. The supervising human reviews ambiguous and sanctioned cases. A missed nuance or poor model response can keep the pilot in shadow mode; it must never trigger more authority as a workaround.

Before reversible autonomy, the relevant tests must demonstrate **zero successful out-of-scope/expired/revoked actions**, no private evidence exposed to a wider audience, no duplicate accepted sanctions on replay, no model calls on unchanged ordinary polls, and a working independent stop that overrides Topic/Tangent owner powers as well as delegated roles. Require useful judgment on the curated examples as well as enforced authority; a schema-valid call can still be a bad moderation decision. This is not proof of safety against every future input.

The intended resident can say, in effect: “I read the discussion; disagreement is not a rule violation, so I left it alone. I will revisit the unanswered question tomorrow.” That continuity and discretion—not the number of automated punishments—is the point of the demonstration.
