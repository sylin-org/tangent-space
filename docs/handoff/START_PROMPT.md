# Copy-ready prompt for the next model

You are taking over Tangent Space in `E:\repo\github\sylin-org\tangent-space`, a Windows/PowerShell workspace with a running Docker prototype. Read `AGENTS.md`, then `docs/handoff/README.md` and `docs/handoff/EPICS_AND_NEXT_STEPS.md`. Use the other handoff files as needed. Do not reconstruct the conversation from historical documents or start a new implementation epic without my direction.

Tangent is a welcoming BBS-like home for humans and agents: Server → Tangent → Topic → Post. Each Tangent has a collectible card. Arrival says what happened while you were away; participation is live and event-driven. Keep a small meaningful agent vocabulary and cheap bounded context. AT accounts establish identity, not humanity or membership.

The server is a .NET/Koan DDD monolith with a singleton TangentServer hub, shared domain operations and registered authentication consumers. Browser, WebMCP and inbound MCP reuse the same permissions and source/activity pipeline. The personal cross-server MCP connector and protected companion credential UI are still future work. SelectCompanion returns companionId; Arrive returns a server-bound contextId. Neither handle is a credential.

The latest implemented slice includes explicit human-owner confirmation, first-Tangent creation/skip, canonical page routes, editorial heroes, eight responsive animated ASCII backgrounds, and Mouse Spotlight. Docker is running at http://127.0.0.1:5220/. The server was intentionally wiped and was still unclaimed at handoff. Opening `/` should route an unclaimed server to `/onboarding/`. Signing in is followed by a real profile card and Confirm as Owner / Switch account. Let me complete that flow unless I ask you to do it.

Keep current native experimental Spaces and compatible test accounts. Public Bluesky identity sign-in worked; public Spaces posting did not. Don't replace native Spaces with local-only posting to conceal a provider limitation. Don't restart the `tangent-spaces-network` container: its startup regenerates the test identities. The app can be rebuilt/restarted independently.

Most work after commit b2abeff is modified/untracked in this directory. Preserve it. The handoff kit contains no credentials or runtime backup. Use Build.bat/Launch.bat for the app; Backup.bat/Restore.bat copy its host-mounted state. Wipe.bat is for a deliberately requested clean start, not normal setup.

This is a POC. Use one relevant build/check and an affected happy-path check. Avoid production-style test expansion, extra instances, broad reruns and unnecessary token use. Use cheap bounded coding delegation when useful and authorized; follow the GLM skill when I name GLM/Z.ai/ZCode. Follow my next request, state assumptions briefly, and record any material change or remaining limitation for the following session.
