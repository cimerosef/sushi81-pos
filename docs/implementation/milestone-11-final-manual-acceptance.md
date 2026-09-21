# M11 — Gestion intermediate export — final manual acceptance

**Status:** OWNER ACCEPTANCE PENDING / NOT YET EXECUTED
**Owner execution:** required only on the later exact accepted M11 candidate  
**Environment:** Windows WPF + real Microsoft Excel where workbook behavior/type inspection matters

## WP4 candidate-preparation evidence

The automated integration-hardening and candidate-preparation package was executed under `M11-WP4-INTEGRATION-OWNER-CANDIDATE-07` on PR #24. The accepted application source head is `dc8091fccb31923bacc16cbff7b49ada771f55fb`. The candidate is now durably downloadable from the GitHub Actions artifact recorded below; this document keeps the owner checklist separate from automated evidence and does not claim owner acceptance.

Automated WP4 evidence includes real SQLite migrations and export-ledger persistence, real ClosedXML workbook generation and validation, Hiboutik exclusion after cancellation/date filtering, immutable exact regeneration with a later pending update, prepared-file retry/idempotency, and non-authoritative preview/write blocking. Full Release regression, Release build, forbidden-data scan and the final candidate are required before owner execution.

Owner scenarios A–E below remain **NOT YET EXECUTED**. No M11 Passed claim is made here; controller review, owner execution and separate merge approval remain outstanding.

The goal is a short, high-value owner check. Automated evidence owns exhaustive schema/state/failure matrices.

## Durable owner candidate delivery

The delivery-only workflow extension is in `.github/workflows/ci.yml`, job `m11-owner-candidate-artifact`. It checks out and verifies the accepted source head above, then publishes the normal self-contained `win-x64` directory (`PublishSingleFile=false`) and uploads its ZIP. The successful artifact-producing run is [GitHub Actions run #814](https://github.com/cimerosef/sushi81-pos/actions/runs/35584565162).

- **Artifact name:** `M11-WP4-owner-candidate-win-x64-dc8091f`
- **Artifact ID:** `10631573948`
- **GitHub-reported artifact size:** `66.6 MB`
- **Uploaded ZIP bytes:** `70,111,153`
- **Retention:** 90 days from the run
- **Artifact digest:** `sha256:a67e44c31aab629dc9b542d1b29a61c7a606f2a5721e0985a6a65bd64333e677`
- **Published executable:** `Sushi81.Pos.Desktop.exe`; SHA-256 `94854C2F292E658466A10DFA1EE538FE28B1D5511DD6FC94D2D618ECA7E45A8C`
- **Uploaded ZIP SHA-256:** `C611EE17835F3617B80FD7E8DD7B209C22AA530AD4C19014B376AA2EF7BB4E67`

The executable and ZIP hashes above are the GitHub Actions reproduction from the exact accepted application source. They differ from the earlier local WP4 hashes because runner publish/ZIP output is not byte-identical; no byte-identity claim is made. The package safety scan passed with no `live.db`, recovery snapshot, real settings, credentials/tokens/secrets, logs, generated Gestion/Catalogue workbooks, CSV/business data or owner production files.

Owner download procedure:

1. Open the run linked above while signed in to GitHub.
2. At the bottom of the run summary, under **Artifacts**, download `M11-WP4-owner-candidate-win-x64-dc8091f`.
3. Extract the ZIP to a new review directory and verify the executable hash before launching it for scenarios A–E.

The candidate is for owner review only. Scenarios A–E remain **NOT YET EXECUTED** and M11 remains not Passed.

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
