# M11 — Gestion intermediate export — final manual acceptance

**Status:** OWNER A–E ACCEPTANCE COMPLETE / CLOSURE-READY
**Owner execution:** complete on the exact accepted M11 candidate
**Environment:** Windows WPF + real Microsoft Excel where workbook behavior/type inspection matters

## Historical WP4 candidate-preparation evidence

The automated integration-hardening and candidate-preparation package was executed under `M11-WP4-INTEGRATION-OWNER-CANDIDATE-07` on PR #24. The historical owner candidate was built from `dc8091fccb31923bacc16cbff7b49ada771f55fb`; it is retained below for failure traceability and is not approved for another owner retest.

## Historical owner-A repair state — candidate not yet retested

The prior candidate failed owner scenario A and requires repair before any owner acceptance can continue. The observed failure was a localized `导出未能完成。` after a preview containing eight CREATE actions; the durable PREPARED batch remained available for retry. Repair 09 fixed the optional empty-string workbook validation, PREPARED retry visibility, and visible Chinese feature name, but controller review found that its DatePicker evidence only asserted `Language` and did not prove the actual watermark. The current narrow repair synchronizes the WPF UI culture, reapplies the selected DatePicker language and updates the real `DatePickerTextBox` watermark template part in-session for `fr-FR`/`zh-CN` without changing `CurrentCulture`, export semantics, or stored data.

At the time of this historical entry, the newest repaired candidate had **not yet been retested by the project owner**. The subsequent owner A–E PASS record is preserved in the final acceptance section below; the historical failure and repair sequence is not rewritten.

## Historical owner-A repair — real workbook round-trip

Owner A's second retest reproduced a remaining finalization failure on the preserved PREPARED BatchId after the DatePicker and optional-text repairs were accepted. The authorized Repair 11 started from exact head `1732d8b790ad3c45c034c6f6249de3c3140f6ab6` and is limited to the ClosedXML workbook validation boundary.

The production regression uses non-zero sub-millisecond CLR ticks in `Meta.GeneratedAt` and `Orders.CreatedAt`. Native Excel/OLE serial dates represent these values at millisecond precision, so exact CLR tick equality rejected a workbook whose native date values were contract-correct. The repair validates DateTime values through `DateTime.FromOADate(expected.ToOADate())`, the exact native representation, while preserving the fixed Schema 1.0 columns and stored SQLite/business payload meaning. A materially different one-second value still fails validation. The same regression audits native TimeSpan and fixed two-decimal money/VAT values; those paths remain exact.

Implementation commit: `64c08f8`. Local evidence: focused workbook gateway **3/3 passed**; full Release solution **831/831 passed**, 0 failed, 0 skipped; Release build 0 warnings / 0 errors; `git diff --check` clean. At the time of this historical entry, the new candidate was not yet retested by the owner. The later owner A–E PASS and Closure-ready state are recorded below.

Automated WP4 evidence includes real SQLite migrations and export-ledger persistence, real ClosedXML workbook generation and validation, Hiboutik exclusion after cancellation/date filtering, immutable exact regeneration with a later pending update, prepared-file retry/idempotency, and non-authoritative preview/write blocking. Full Release regression, Release build, forbidden-data scan and the final candidate are required before owner execution.

The checklist below records the acceptance contract. The completed owner A–E evidence is recorded in the final acceptance section; M11 remains Closure-ready rather than Passed until final controller closure is recorded.

The goal is a short, high-value owner check. Automated evidence owns exhaustive schema/state/failure matrices.

## Durable owner candidate delivery

The delivery-only workflow extension is in `.github/workflows/ci.yml`, job `m11-owner-candidate-artifact`. It checks out and verifies the exact PR head, then publishes the normal self-contained `win-x64` directory (`PublishSingleFile=false`) and uploads its ZIP. The artifact below is the historical Repair 09 candidate and is not approved for owner retest; the newest candidate details are recorded in the matching durable `CODEX_DONE` after the DatePicker repair push and exact-head CI success.

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

The historical candidate is for traceability only and must not be used for owner retest. The accepted candidate and completed A–E evidence are recorded below.

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

## Final acceptance record — owner A–E complete

Owner A–E acceptance is complete and PASS on exact accepted runtime candidate source head `77ccecf9d947462e96e74b8aa1d99ced30e3788e`:

- A PASS: PR #24 comment `5762194270`; preserved PREPARED BatchId retry succeeded, produced one successful batch, and the four-sheet workbook opened in Excel.
- B PASS: PR #24 comment `5762229722`; inclusive/date-scoped selection behaved correctly and unchanged successful CREATE was not duplicated.
- C PASS: PR #24 comment `5762338169`; regeneration preserved the historical batch while the later changed order became an UPDATE candidate.
- D PASS: PR #24 comment `5762376235`; cancellation superseded the un-emitted UPDATE and emitted a direct CANCEL.
- E PASS / A–E summary: PR #24 comment `5762475899`; CANCEL succeeded without duplication, and the earlier real PREPARED failure/retry evidence remained valid.
- Repair 11 controller acceptance: PR #24 comment `5762078952`.
- Exact-head CI: run `35610026160` / CI #819 succeeded; full Release evidence was **831/831 passed**, 0 failed, 0 skipped; Release build was **0 warnings / 0 errors**.
- Accepted owner artifact: `M11-WP4-owner-candidate-win-x64-77ccecf`, artifact ID `10643522785`, GitHub-reported size `69,864,230` bytes, expiry `2026-12-20T14:07:30Z`, outer digest `sha256:6a0c53028ae278407d3718e182cb95bceb4390e4f5169d6f587a60e760a6275c`.
- Inner candidate ZIP: `70,112,049` bytes, 421 entries, SHA-256 `B13C6DEC3410C7B9D4C579362940866DAFE2E5A9F6ECF7D19621A300B19C475F`.
- Candidate executable: `Sushi81.Pos.Desktop.exe`, `162,816` bytes, SHA-256 `4F91EEEFCAFCC1343D86FC03C2F2C4FB83B26A4500A85886990C07BA56017DAC`.
- Packaging safety: controller filename scan and the GitHub artifact job scan both reported zero forbidden names; the accepted artifact contains no production business data, credentials, tokens, secrets, logs or generated business workbooks.

M11 is **Closure-ready**, not yet Passed: final controller closure remains outstanding. PR #24 remains Draft/Open/unmerged; separate explicit project-owner merge approval is still required. M12/M13 remain unauthorized, and no further owner manual testing is required for M11.
