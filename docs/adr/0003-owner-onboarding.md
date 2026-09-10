# ADR 0003 — Confirm the owner, create the first Tangent

Status: Accepted, 10 September 2026.

A fresh server opens on a welcome and Atmosphere sign-in. Sign-in establishes account identity only. The next screen displays the verified account handle/DID and its fetched public profile (display name, avatar, bio), with Confirm as Owner and Switch account. Confirmation makes the human-accountability declaration and atomically claims ownership; a configured owner DID remains a reservation. The browser supplies the DID shown on the card so a changed session cannot confirm an unseen account. Optional profile details never decide identity or permissions and may fall back to the verified account information.

After confirmation, the owner names the first Tangent, optionally describes it, and sees a live card preview. Create completes the first Tangent; Skip completes a default named My Tangent. Both land directly on the main page. The durable owner and home-Tangent SetupComplete state make reload/resumption and completion retries straightforward. No Topic, source connection, invitation or policy wizard intervenes.

The singleton hub exposes the existing governance operations and a singleton profile reader using Koan's guarded AT transport. Profiles are read from the resolved account PDS and briefly cached; they do not receive extra authentication permissions. No new workflow engine is introduced.

Prototype data is disposable. No legacy room migration or data-preservation work is required. The Docker state was explicitly wiped at the user's request, the new image launched, and the unclaimed sign-in page opened for the user's own walkthrough. Build, JavaScript syntax and fresh anonymous state were checked; the authenticated walkthrough is deliberately left to the user.
