# V1 implementation plan

**Status:** Approved — Phase 6 baseline, amended 2026-08-31  
**Approval date:** 2026-08-27  
**Latest plan amendment:** 2026-08-31  
**Current-state reconciliation:** 2026-09-21  
**Product:** Sushi81 POS  
**Purpose:** Define the controlled implementation sequence for the frozen-and-amended V1 Specification.

## 1. Authority and scope

This plan is subordinate to the V1 Specification in:

- `v1-specification-freeze.md`;
- the Approved Phase 1–4 baseline documents and later approved amendments recorded in them;
- Approved records under `decisions/`;
- `acceptance-criteria.md` and approved acceptance amendments;
- repository instructions in `../AGENTS.md`.

This plan does not independently amend product behavior, business rules, architecture, logical data semantics, storage safety, printing or export contracts.

Implementation must proceed one explicitly authorized milestone at a time. Completion of a milestone is not permission to start the next one.

If implementation exposes a genuine specification conflict or a missing material decision, the affected path stops for an approved specification amendment. Pure technical details that preserve the approved behavior may be decided under the standing priority order:

**reliability > simplicity > maintainability > operational clarity > novelty.**

## 2. Delivery method

Sushi81 POS uses a hybrid approach:

1. establish a thin but complete technical foundation that cannot safely be retrofitted later;
2. prove the highest-risk remote handoff/single-writer assumptions early;
3. if the feasibility gate reveals a material blocker, amend the specification before broad business implementation and re-verify the amended safety model;
4. deliver business functionality as end-to-end vertical slices;
5. add integrations only after their upstream business state is stable;
6. complete packaging and whole-V1 acceptance last.

The project must not build every technical layer in isolation before exercising real workflows. It must also not build disposable UI/business shortcuts that bypass money, migrations, transactions, snapshots, authority checks or recovery.

## 3. Milestone sequence

### M01 — Executable foundation and safe persistence spine

Create the .NET 10/WPF solution, dependency boundaries, build/test infrastructure, integer-cent money and rounding primitives, time/ID abstractions, application-data paths, configuration, redacted logging, SQLite connection policy, versioned migrations, transaction coordination, SQLite-safe local snapshot/validation/retention primitives, write-authority guard seam and localization foundation.

No Catalogue or Order production workflow is included.

Primary acceptance ownership: `AC-ARCH-001` through `AC-ARCH-004`, `AC-STO-001`; foundations for `AC-STO-006`, `AC-PROD-004`, `AC-NFR-001`, `AC-NFR-002` and `AC-NFR-004`.

Detailed task definition: `implementation/milestone-01-foundation.md`.

### M02 — Remote handoff feasibility gate and GitHub transport revalidation

M02 ran before broad business implementation.

The original generic/competitive OneDrive acquisition model was deterministically tested and found unable to provide the required ordinary N-device single-writer guarantee without an external exclusive grant. The approved amendment changed normal transfer to source-directed transfer to exactly one target and selected a dedicated private GitHub Release Asset transport with strict server receipt/grant semantics. OneDrive remains recovery/archive storage.

Primary acceptance preparation: amended `AC-STO-002` through `AC-STO-005`, `AC-STO-007` through `AC-STO-010`.

Detailed records include `implementation/milestone-02-feasibility-report.md` and `implementation/milestone-02-directed-handoff-revalidation.md`.

M02 is Passed and merged.

### M03 — In-application catalogue and business settings

Implement Category, Product, OptionGroup, Option and BusinessSettings persistence, domain validation and WPF maintenance workflows. Excel batch import/export remains out of scope.

The approved 2026-08-30 catalogue amendment adds an in-application workflow for atomic bulk Activate/Deactivate of the complete current composed Product filter result, with immutable capture, impact counts/confirmation, zero-change no-op behavior, preserved filters, localized UI and no bulk permanent deletion.

Primary acceptance ownership: `AC-CAT-001` through `AC-CAT-005`, `AC-CAT-013`, `AC-ORD-011`. Historical-order portions close in M04.

Detailed records: `implementation/milestone-03-catalogue-settings.md` and `implementation/milestone-03-filtered-bulk-activation-extension.md`.

M03 is Passed and merged through PR #5 at `57f89cac0672d6d98dda7dc3c9a8ba7b3434e292`.

### M04 — First complete order-entry vertical slice

Implement Catalogue browsing/search, ordinary option selection, cart editing, pricing/VAT, Retrait/Livraison validation, authoritative-total override, transactional confirmation, historical item/adjustment/tax snapshots, reload and the post-commit print-dispatch boundary using a test sink rather than the final Windows printer adapter.

Primary acceptance ownership: `AC-CAT-006`, `AC-CAT-007`, `AC-CAT-012`, `AC-ORD-001` through `AC-ORD-010`, `AC-LIFE-001`, `AC-LIFE-002`; partial foundation for `AC-LIFE-008` and `AC-NFR-003`.

Approved detailed contract: `implementation/milestone-04-order-entry.md`.

Durable authorization: `implementation/milestone-04-authorization.md`.

Approved pricing/interaction clarification: `decisions/m04-order-entry-pricing-clarifications.md` plus later M04 operator ergonomics amendments including Category short code.

M04 is Passed and merged through PR #6 at `ab218263bd4eee9c1be203d36acc552988cef43a`.

### M05 — Lifecycle, payments, search and operational dashboard

Implement cumulative CB/Espèce editing backed by signed dated adjustments, effective-versus-recorded timestamps, Close/reopen/cancel, same-ID modification, abandon-edit, new order from reusable customer text, live telephone/comment search, operational turnover, received-payment summaries and future/due-today/overdue views.

Primary acceptance ownership: `AC-LIFE-003` through `AC-LIFE-014` and live-search portion of `AC-LIFE-015`.

M05 is Passed and merged through PR #10 at `79499d7c6ed65a74f524097c1507ca648dc151c3`.

### M06 — Local recovery and authoritative/read-only enforcement

Connect recovery and authority primitives to every implemented durable business mutation. Complete recovery scheduling/debounce/flush, five-version retention, persistent non-authoritative/stale/pending-transfer presentation and centralized blocking of every authoritative write.

Primary acceptance ownership: `AC-STO-006`, `AC-STO-010`; partial completion of `AC-PROD-002`.

Detailed records: `implementation/milestone-06-local-recovery-read-only-enforcement.md`, `milestone-06-authorization.md`, `milestone-06-worklog.md`, `milestone-06-final-manual-acceptance.md`.

M06 is Passed and merged through PR #11 at `2c5eb52740d0c12e3e837579ecceac6d0600b59e`.

### M07 — Pairing, target-directed formal handoff and disaster recovery

Implement device/lineage initialization, N-device pairing, close-and-retain behavior, explicit target selection, target-directed transfer-and-close, durable source relinquishment, safe target acquisition, immutable versions, checksum/integrity validation, GitHub server acknowledgement, normal handoff retention, change-triggered recovery-only cloud checkpoints, explicit Disaster Recovery and generation invalidation.

Normal acquisition must not reintroduce generic competitive claim/election semantics superseded by the 2026-08-28 amendment.

Primary acceptance ownership: `AC-STO-002` through `AC-STO-005`, `AC-STO-007` through `AC-STO-009`, and `AC-PROD-002`.

M07 is Passed and merged through PR #13 at `9ea7d5e15bceba6932cb2caba50d0afb64ca1ff9`.

### M08 — Printing and reprinting

Implement deterministic kitchen/customer print models, fixed-document layout, Windows print-queue adapters, independent local printer configuration, responsive post-commit submission, failure/retry, selective reprint, required reprint/cancellation markings and non-authoritative-device warning behavior.

Primary acceptance ownership: `AC-PRINT-001` through `AC-PRINT-008`, `AC-PRINT-010`, `AC-PRINT-011`, `AC-ARCH-006`. Archived printing closes in M12.

M08 is Passed and merged through PR #14 at `8f246ce7fb32baa33e1dfe1d334175bf2df60c1f`.

### M09 — Hiboutik paste-order fallback

Implement the deterministic untrusted-text parser, exact product-code resolution, fail-safe unresolved-line handling, ordinary option confirmation, ordinary order-draft prepopulation and source-based anti-double-counting semantics without reintroducing the former emergency subsystem.

Primary acceptance ownership: `AC-HIB-001` through `AC-HIB-009` as amended; final Gestion export exclusion cross-check remains M11.

M09 is Passed and merged through PR #17 at `d840066d8d2ffa1856c4fcd88dbfdd3c8f2a1be5`.

The separately approved post-M09 daily Hiboutik CB/Espèce dashboard enhancement was completed and merged through PR #19 at `861cfba1dfacbb3289395c0370f6d42765b6c223`. It does not alter the milestone sequence below.

### M10 — Catalogue `.xlsx` import/export

Implement the ClosedXML three-sheet Catalogue workbook, protected technical relationships, normal ID-preserving update mode, explicit add-only mode, complete validation/preview and one atomic commit.

The approved 2026-09-17 Category short-code workbook amendment requires:

- visible Category name + Category short code on Product rows;
- no operator-facing Categories worksheet;
- new Category optional consistent short code;
- existing Category short code preserve/consistency-only semantics, with workbook changes blocked;
- no Category technical ID as an operator field.

Primary acceptance ownership: `AC-CAT-008` through `AC-CAT-011` as clarified by `acceptance-criteria-amendment-m10-category-short-code-workbook.md`; Catalogue portion of `AC-ARCH-005`.

Prepared control package: `implementation/milestone-10-preparation-readiness.md`, `milestone-10-catalogue-xlsx.md`, `milestone-10-final-manual-acceptance.md`, `milestone-10-worklog.md`, `milestone-10-authorization.md`.

### M11 — Gestion intermediate export

Implement selection eligibility, optional inclusive date filtering, the versioned four-sheet `.xlsx` contract, temporary generation/validation/finalization, immutable batch payloads, duplicate protection, exact regeneration and CREATE/UPDATE/CANCEL correction semantics.

Primary acceptance ownership: `AC-EXP-001` through `AC-EXP-011`, final export portion of `AC-HIB-008`, export portion of `AC-ARCH-005`.

### M12 — Annual archive and historical access

Implement February/late-start previous-calendar-year archiving, end-year eligibility, staged validation/publication, failure-safe live removal, explicit archive selection/search, read-only hydration and historical reprinting.

Before removing an order from `live.db`, preserve every not-yet-emitted or pending export action as a durable immutable technical payload, or use an equivalent implementation that demonstrably preserves the frozen export contract. Archiving must never make a valid export/correction silently disappear.

Primary acceptance ownership: `AC-STO-011` through `AC-STO-014`, `AC-PRINT-009`, archive portion of `AC-LIFE-015`.

### M13 — Installer, localization completion and final V1 acceptance

Complete the self-contained Windows x64 release, per-user Inno Setup installer, upgrade/reinstall preservation, full French/Chinese UI, diagnostics, performance work, repository safety review, complete acceptance regression and the practical user operating guide required by the root README.

Primary acceptance ownership: `AC-PROD-001` through `AC-PROD-004`, `AC-ARCH-007`, `AC-NFR-001` through `AC-NFR-004`, all explicit V1 exclusions and the complete production-target acceptance gate.

## 4. Mandatory milestone gate

Every milestone must finish with all of the following:

1. build the applicable solution in Release configuration;
2. run all existing tests plus the new milestone tests;
3. run failure-path tests relevant to durable data or external operations;
4. identify every acceptance criterion completed, partially covered or still pending;
5. perform the milestone's stated manual acceptance checks;
6. update `implementation-status.md` with concrete evidence paths/results;
7. report any specification conflict instead of silently choosing behavior;
8. leave the repository buildable, tested and free of real customer/business secrets.

A passing build without required tests and acceptance evidence is not milestone completion.

## 5. Implementation records

- `implementation-status.md` is the living AC/milestone traceability record.
- `implementation/` contains detailed milestone task contracts, readiness, authorization, worklogs and manual-acceptance records.
- Tests should be linked by stable repository path/test name rather than copying test output into specification documents.
- Manual evidence should record environment/action/result concisely without storing sensitive production data.

## 6. Current implementation state

Phase 6 planning remains Approved.

Current merged baseline as of 2026-09-21:

- M01 through M10: Passed / merged under their recorded PRs;
- M11 — Gestion intermediate export: Passed / merged through PR #24 at `1a94f3400e0aa9fe9f878bbe98a8285112206ba9`;
- M11 accepted runtime candidate: `77ccecf9d947462e96e74b8aa1d99ced30e3788e`;
- M11 final documentation head: `4eddf0a93c5a141326d76c6e67a9a9c5840e5068`;
- M11 controller closure: PR #24 comment `5762784303`;
- M11 merge completion: PR #24 comment `5762846107`;
- post-merge CI #821 / run `35617254203`: success, 831/831 passed, 0 failed, 0 skipped, Release build 0 warnings / 0 errors.

M12 — Annual archive and historical access — is owner-authorized on branch `codex/m12-annual-archive-authorized` / Draft PR #25. Its control package begins with `implementation/milestone-12-preparation-readiness.md`, `implementation/milestone-12-annual-archive-historical-access.md` and `implementation/milestone-12-authorization.md`. Execution remains one-package-at-a-time through Issue #4.

The first executable package is WP1 only: archive-year/eligibility contracts and independent local staged archive SQLite construction/validation. WP1 performs no OneDrive publication and no live-row deletion.

The M12 readiness review records a known technical blocker for the later WP2 delete gate: accepted M02 evidence proved no documented local-only per-artifact OneDrive remote-upload acknowledgement for newly-created regular files, while `AC-STO-013` requires confirmed archive publication/synchronization before live removal. The affected path must not treat local file existence as cloud success or introduce a new remote/authentication workflow without explicit approval.

M13 remains unauthorized.
