# M11 — Gestion intermediate export — final manual acceptance

**Status:** OWNER ACCEPTANCE PENDING / NOT YET EXECUTED
**Owner execution:** required only on the later exact accepted M11 candidate  
**Environment:** Windows WPF + real Microsoft Excel where workbook behavior/type inspection matters

## WP4 candidate-preparation evidence

The automated integration-hardening and candidate-preparation package was executed under `M11-WP4-INTEGRATION-OWNER-CANDIDATE-07` on PR #24. The exact final source head, self-contained `win-x64` candidate paths, sizes, SHA-256 values, package scan and exact-head CI result are recorded in the matching durable `CODEX_DONE` comment. This document intentionally keeps the owner checklist separate from automated evidence and does not self-reference a commit hash.

Automated WP4 evidence includes real SQLite migrations and export-ledger persistence, real ClosedXML workbook generation and validation, Hiboutik exclusion after cancellation/date filtering, immutable exact regeneration with a later pending update, prepared-file retry/idempotency, and non-authoritative preview/write blocking. Full Release regression, Release build, forbidden-data scan and the final candidate are required before owner execution.

Owner scenarios A–E below remain **NOT YET EXECUTED**. No M11 Passed claim is made here; controller review, owner execution and separate merge approval remain outstanding.

The goal is a short, high-value owner check. Automated evidence owns exhaustive schema/state/failure matrices.

## A — Mixed selection and CREATE workbook

Prepare/identify a small mixed set including:

- ordinary POS Closed orders;
- at least one Open order;
- at least one Cancelled order;
- at least one `HIBOUTIK_PASTE` order.

Run default Gestion export.

PASS if:

- only applicable ordinary POS Closed CREATE actions are present;
- Open/Cancelled/Hiboutik orders do not appear as positive sales;
- workbook opens normally in Excel;
- sheets are exactly `Meta`, `Orders`, `OrderLines`, `TaxBreakdown`;
- obvious amount/date/text cells behave as native Excel numeric/date/text values;
- no per-column selection step is required/offered.

## B — Inclusive dates and duplicate protection

Use an optional start/end range with known orders on both boundaries.

PASS if both boundary dates are included and outside orders are excluded.

Run an ordinary later export again.

PASS if the already-successful unchanged CREATE is not emitted again.

## C — Exact regeneration

After a successful batch, change one exported order and save the later business state.

Regenerate the original successful batch.

PASS if:

- BatchId is unchanged;
- original exported amounts/lines/tax remain those from the old batch;
- later order changes do not leak into the regenerated file;
- regeneration does not consume/create a new business correction event.

## D — UPDATE then CANCEL semantics

Modify a previously exported order.

If modification leaves/reopens it Open, confirm no UPDATE export occurs yet. Re-Close it and export.

PASS if the next action is one full-replacement UPDATE with the same OrderId.

Then create another later modification if useful but do not export it, and cancel the order.

PASS if the next applicable action is directly CANCEL for the same OrderId; an un-emitted intermediate UPDATE is not forced first. CANCEL does not carry positive line/tax rows.

## E — Failure and retry

Cause one controlled destination/finalization failure, for example by choosing a path/file condition that prevents successful finalization without damaging business data.

PASS if:

- the application reports failure clearly;
- the order remains normally viewable/unchanged;
- the failed action is not falsely treated as successfully emitted;
- after correcting the destination problem, retry succeeds without duplicate CREATE;
- any pre-existing good export file is not destroyed by the failed attempt.

## Final acceptance record

Record after the controller has approved the exact candidate and the project owner has executed the checklist:

- exact tested source head;
- EXE/ZIP hashes;
- exact CI run;
- A–E PASS/FAIL;
- any defect evidence;
- final controller disposition.

M11 is not Passed and its PR must not merge until owner acceptance, controller closure and separate explicit project-owner merge approval are complete.
