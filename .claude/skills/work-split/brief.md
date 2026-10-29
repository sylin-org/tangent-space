# Brief — package <id> · <name>

<executor preamble from executor.md>

## Closes

The FEATURES rows this package marks `done`, quoted.

## Outcome

What exists when this is done, in two or three sentences, as the system — not the steps.

## Area

The folders and files this package owns. Anything outside them is out of bounds unless named here.

## Out of bounds

What this package does not do, even if it looks adjacent: other packages' rows, cleanup the brief did not ask for, abstractions for later needs.

## Koan first

The capabilities this package touches, each with what the lead already knows Koan has for it (package, file) or does not. The executor verifies the list in the checkout before writing anything and extends it in the report.

## Constraints

The standing rules that bite here, by name: the language file's words, no dialogs, bounded reads, the writer as the only code that opens a transaction, Koan before hand-rolled, never derive a Koan identifier. Any decision already made that the executor must not reopen, with its ADR.

## Done is checked by

The one build or test command and the affected happy path; the rule checks; for a screen, the capture command the visual loop needs. State the exact commands.

## Report

Outcome first. Each check with its command and result. What failed or is unverified, said plainly. Files changed. Decisions the brief did not cover. Questions for the lead. Nothing claimed without a tool result from this session.
