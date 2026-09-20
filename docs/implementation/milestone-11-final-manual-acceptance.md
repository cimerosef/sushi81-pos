# M11 — Gestion intermediate export — final manual acceptance

**Status:** PREPARED / NOT YET EXECUTED  
**Owner execution:** required only on the later exact accepted M11 candidate  
**Environment:** Windows WPF + real Microsoft Excel where workbook behavior/type inspection matters

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

Record:

- exact tested source head;
- EXE/ZIP hashes;
- exact CI run;
- A–E PASS/FAIL;
- any defect evidence;
- final controller disposition.

M11 is not Passed and its PR must not merge until owner acceptance, controller closure and separate explicit project-owner merge approval are complete.
