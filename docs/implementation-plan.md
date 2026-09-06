# V1 implementation plan

**Status:** Approved — Phase 6 baseline, amended 2026-08-31  
**Approval date:** 2026-08-27  
**Latest amendment:** 2026-08-31  
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
2. prove the highest-risk remote handoff/single-writer assumptions early (historical OneDrive evidence plus the approved GitHub transport revalidation);
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

Detailed authorized task definition: `implementation/milestone-01-foundation.md`.

### M02 — Remote handoff feasibility gate and GitHub transport revalidation

M02 runs before broad business implementation.

#### Original feasibility result

The original generic/competitive acquisition model was tested under `implementation/milestone-02-onedrive-feasibility.md`.

PR #2 / merge commit `5bacafa0e4ca906d8ff058e34586dee43503bc42` established:

- corrected documented Windows Cloud Files state interpretation;
- strict synthetic handoff publication/validation primitives;
- deterministic N-device delayed/reordered simulation;
- an executable double-writer counterexample for claim-only competitive acquisition;
- a blocker conclusion because the approved OneDrive/local-filesystem model exposes no documented cross-client atomic exclusive-grant primitive;
- the approved GitHub private-repository Release Asset transport amendment and its strict server-receipt gate.

The resulting historical evidence is `implementation/milestone-02-feasibility-report.md`.

#### Approved amendment

On 2026-08-28 the user approved `decisions/target-directed-authority-handoff.md`.

Normal authority transfer is now source-directed to one target device; normal close distinguishes retaining authority from explicitly transferring it; the source must durably relinquish business-write authority before a target-releasing marker can exist; non-target devices no longer compete through claims/election.

The amended behavior is folded into `architecture.md`, `storage-strategy.md` and `acceptance-criteria.md`.

#### Required revalidation

Before M03, M02 re-verified the amended target-directed protocol, including:

- close-and-retain semantics;
- target binding;
- durable source relinquishment before target release;
- crash/restart boundaries;
- pre/post-relinquishment failures;
- wrong-target rejection;
- N-device safety and valid-path liveness;
- automated GitHub REST/Release Asset receipt and failure-matrix evidence;
- real private GitHub two-device upload/download/round-trip evidence.

Primary acceptance preparation remained: amended `AC-STO-002` through `AC-STO-005`, `AC-STO-007` through `AC-STO-010`.

Detailed authorized revalidation task definition: `implementation/milestone-02-directed-handoff-revalidation.md`.

M02 is Passed and merged; its gate is closed by evidence.

### M03 — In-application catalogue and business settings

Implement Category, Product, OptionGroup, Option and BusinessSettings persistence, domain validation and WPF maintenance workflows. Excel batch import/export remains out of scope.

The approved 2026-08-30 catalogue amendment adds an M03 in-application workflow for atomic bulk Activate/Deactivate of the complete current code/name-search + category + status filtered Product result, with immutable target capture, explicit impact counts/confirmation, zero-change no-op behavior, preserved filters, localized UI and no bulk permanent deletion.

Primary acceptance ownership: `AC-CAT-001` through `AC-CAT-005`, `AC-CAT-013`, `AC-ORD-011`. Historical-order portions close in M04.

Detailed original M03 task definition: `implementation/milestone-03-catalogue-settings.md`.

Detailed authorized extension: `implementation/milestone-03-filtered-bulk-activation-extension.md`.

M03 is Passed and merged through PR #5 at merge commit `57f89cac0672d6d98dda7dc3c9a8ba7b3434e292`. AC-CAT-003 historical-order independence and the AC-ORD-011 pricing-consumer cross-check were deliberately deferred to M04.

### M04 — First complete order-entry vertical slice

Implement Catalogue browsing/search, ordinary option selection, cart editing, pricing/VAT, Retrait/Livraison validation, authoritative-total override, transactional confirmation, historical item/adjustment/tax snapshots, reload and the post-commit print-dispatch boundary using a test sink rather than the final Windows printer adapter.

Primary acceptance ownership: `AC-CAT-006`, `AC-CAT-007`, `AC-CAT-012`, `AC-ORD-001` through `AC-ORD-010`, `AC-LIFE-001`, `AC-LIFE-002`; partial foundation for `AC-LIFE-008` and `AC-NFR-003`.

Approved detailed contract: `implementation/milestone-04-order-entry.md`.

Durable authorization: `implementation/milestone-04-authorization.md`.

Approved M04 pricing/interaction clarification amendment: `decisions/m04-order-entry-pricing-clarifications.md`.

### M05 — Lifecycle, payments, search and operational dashboard

Implement cumulative CB/Espèce editing backed by signed dated adjustments, effective-versus-recorded timestamps, Close/reopen/cancel, same-ID modification, abandon-edit, new order from reusable customer text, live telephone/comment search, operational turnover, received-payment summaries and future/due-today/overdue views.

Primary acceptance ownership: `AC-LIFE-003` through `AC-LIFE-014` and the live-search portion of `AC-LIFE-015`.

M05 is Passed and merged through PR #10 at merge commit `79499d7c6ed65a74f524097c1507ca648dc151c3`. Accepted production-code head: `84c1c534c1df105ccb1839cbc6dfc9e0e055bb70`; final documentation/status head: `217d187dd3ef5498c11f21bc516eccc6737fa952`.

### M06 — Local recovery and authoritative/read-only enforcement

Connect the M01 recovery and authority primitives to every implemented durable business mutation. Complete recovery scheduling/debounce/flush, five-version retention, persistent non-authoritative/stale/pending-transfer presentation and centralized blocking of every authoritative write.

Primary acceptance ownership: `AC-STO-006`, `AC-STO-010`; partial completion of `AC-PROD-002`.

Approved detailed contract: `implementation/milestone-06-local-recovery-read-only-enforcement.md`.

Durable authorization: `implementation/milestone-06-authorization.md`.

Prepared evidence records: `implementation/milestone-06-worklog.md` and `implementation/milestone-06-final-manual-acceptance.md`.

M06 deliberately stops before real pairing, target-directed remote handoff, target acquisition, recovery-only cloud checkpoints and Disaster Recovery, which remain M07.

### M07 — Pairing, target-directed formal handoff and disaster recovery

Implement device/lineage initialization, N-device pairing, close-and-retain behavior, explicit target selection, target-directed transfer-and-close, durable source relinquishment, safe target acquisition, immutable versions, checksum/integrity validation, GitHub server acknowledgement, three-version normal handoff retention, change-triggered recovery-only cloud checkpoints, explicit Disaster Recovery and generation invalidation.

Normal M07 acquisition must not reintroduce generic competitive claim/election semantics superseded by the 2026-08-28 amendment.

Primary acceptance ownership: `AC-STO-002` through `AC-STO-005`, `AC-STO-007` through `AC-STO-009`, and `AC-PROD-002`.

### M08 — Printing and reprinting

Implement deterministic kitchen/customer print models, fixed-document layout, Windows print-queue adapters, independent local printer configuration, responsive post-commit submission, failure/retry, selective reprint, required reprint/cancellation markings and non-authoritative-device warning behavior.

Primary acceptance ownership: `AC-PRINT-001` through `AC-PRINT-008`, `AC-PRINT-010`, `AC-PRINT-011`, `AC-ARCH-006`. Archived printing closes in M12.

### M09 — Hiboutik paste-order fallback

Implement the deterministic untrusted-text parser, exact product-code resolution, unresolved-line handling, mandatory ordinary option confirmation, ordinary order draft prepopulation and hidden anti-double-counting source marker. Do not introduce any former emergency-order model.

Primary acceptance ownership: `AC-HIB-001` through `AC-HIB-009`; export exclusion receives final cross-check in M11.

### M10 — Catalogue `.xlsx` import/export

Implement the ClosedXML three-sheet catalogue workbook, protected technical relationships, update mode, explicit add-only mode, complete validation/preview and atomic commit.

Primary acceptance ownership: `AC-CAT-008` through `AC-CAT-011`; catalogue portion of `AC-ARCH-005`.

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

A passing build without the required tests and acceptance evidence is not milestone completion.

## 5. Implementation records

- `implementation-status.md` is the living AC/milestone traceability record.
- `implementation/` contains the detailed task contract for each milestone/revalidation when prepared and authorized.
- Tests should be linked by stable repository path and test name rather than copying test output into specification documents.
- Manual evidence should record the environment, action and result concisely without storing sensitive production data.

## 6. Current implementation state

Phase 6 planning remains Approved.

M01 is `Passed` and was merged to `main` through PR #1 at merge commit `b8590d1d0a2aee4ec6554ddee43587a257cedc47` after automated verification and successful Windows/WPF manual re-verification.

M02's original generic acquisition design correctly ended `BLOCKED — specification/architecture amendment required`; its evidence was merged through PR #2 at `5bacafa0e4ca906d8ff058e34586dee43503bc42`. The approved target-directed/GitHub transport amendment was incorporated and the amended M02 revalidation subsequently Passed and merged.

M03 is `Passed`. PR #5 was explicitly approved and merged to `main` at `57f89cac0672d6d98dda7dc3c9a8ba7b3434e292`. Its complete operator Windows/WPF acceptance is recorded in `implementation-status.md` and `implementation/milestone-03-worklog.md`.

M04 — First complete order-entry vertical slice — is Passed and merged through PR #6 at merge commit `ab218263bd4eee9c1be203d36acc552988cef43a`, with its final Windows/WPF acceptance recorded in the M04 evidence.

M05 — Lifecycle, payments, search and operational dashboard — is Passed and merged through PR #10 at merge commit `79499d7c6ed65a74f524097c1507ca648dc151c3`. Accepted production-code head `84c1c534c1df105ccb1839cbc6dfc9e0e055bb70`, full Release tests 340/340, Release build 0 warnings/0 errors, self-contained `win-x64` publish, exact-head CI success and project-owner Windows/WPF acceptance are recorded in the M05 evidence. M05 is no longer open work.

M06 — Local recovery and authoritative/read-only enforcement — is the current explicitly authorized controlled implementation milestone on branch `codex/m06-local-recovery-read-only`. Its authoritative task definition is `implementation/milestone-06-local-recovery-read-only-enforcement.md`; durable owner authorization is `implementation/milestone-06-authorization.md`; implementation evidence belongs in `implementation/milestone-06-worklog.md`; and the final owner Windows/WPF checklist is `implementation/milestone-06-final-manual-acceptance.md`.

M06 implementation is activated only through the dedicated M06 PR mailbox, the single-use `CODEX_HANDOFF_READY: M06-IMPLEMENT-01` record and GitHub issue #4 being OPEN after the mailbox pointer is valid. M07 and later milestones are not authorized by M06 implementation or completion.
