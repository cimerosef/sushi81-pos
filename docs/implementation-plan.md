# V1 implementation plan

**Status:** Approved — Phase 6 baseline  
**Approval date:** 2026-08-27  
**Product:** Sushi81 POS  
**Purpose:** Define the controlled implementation sequence for the frozen V1 Specification.

## 1. Authority and scope

This plan is subordinate to the frozen V1 Specification in:

- `v1-specification-freeze.md`;
- the Approved Phase 1–4 baseline documents;
- the Approved records under `decisions/`;
- `acceptance-criteria.md`;
- repository instructions in `../AGENTS.md`.

This plan does not amend product behavior, business rules, architecture, logical data semantics, storage safety, printing or export contracts.

Implementation must proceed one explicitly authorized milestone at a time. Completion of a milestone is not permission to start the next one.

If implementation exposes a genuine specification conflict or a missing material decision, the affected path stops for an approved specification amendment. Pure technical details that preserve the frozen behavior may be decided under the standing priority order:

**reliability > simplicity > maintainability > operational clarity > novelty.**

## 2. Delivery method

Sushi81 POS uses a hybrid approach:

1. establish a thin but complete technical foundation that cannot safely be retrofitted later;
2. prove the highest-risk OneDrive single-writer assumptions early;
3. deliver business functionality as end-to-end vertical slices;
4. add integrations only after their upstream business state is stable;
5. complete packaging and whole-V1 acceptance last.

The project must not build every technical layer in isolation before exercising real workflows. It must also not build disposable UI/business shortcuts that bypass money, migrations, transactions, snapshots, authority checks or recovery.

## 3. Milestone sequence

### M01 — Executable foundation and safe persistence spine

Create the .NET 10/WPF solution, dependency boundaries, build/test infrastructure, integer-cent money and rounding primitives, time/ID abstractions, application-data paths, configuration, redacted logging, SQLite connection policy, versioned migrations, transaction coordination, SQLite-safe local snapshot/validation/retention primitives, write-authority guard seam and localization foundation.

No Catalogue or Order production workflow is included.

Primary acceptance ownership: `AC-ARCH-001` through `AC-ARCH-004`, `AC-STO-001`; foundations for `AC-STO-006`, `AC-PROD-004`, `AC-NFR-001`, `AC-NFR-002` and `AC-NFR-004`.

Detailed authorized task definition: `implementation/milestone-01-foundation.md`.

### M02 — OneDrive single-writer feasibility gate

Before broad business implementation, prove that the target Windows/OneDrive environment can support the frozen fail-closed handoff/acquisition requirements, including synchronization-state observation, immutable snapshot/marker publication, lineage/generation/version/checksum validation and deterministic handling of competing acquisition attempts.

This is a technical feasibility gate, not permission to weaken the storage contract. If the frozen guarantees cannot be implemented without a material architecture/workflow change, stop for specification amendment.

Primary acceptance preparation: `AC-STO-002` through `AC-STO-005`, `AC-STO-007` through `AC-STO-010`.

### M03 — In-application catalogue and business settings

Implement Category, Product, OptionGroup, Option and BusinessSettings persistence, domain validation and WPF maintenance workflows. Excel batch import/export remains out of scope.

Primary acceptance ownership: `AC-CAT-001` through `AC-CAT-005`, `AC-ORD-011`. Historical-order portions close in M04.

### M04 — First complete order-entry vertical slice

Implement Catalogue browsing/search, ordinary option selection, cart editing, pricing/VAT, Retrait/Livraison validation, authoritative-total override, transactional confirmation, historical item/adjustment/tax snapshots, reload and the post-commit print-dispatch boundary using a test sink rather than the final Windows printer adapter.

Primary acceptance ownership: `AC-CAT-006`, `AC-CAT-007`, `AC-CAT-012`, `AC-ORD-001` through `AC-ORD-010`, `AC-LIFE-001`, `AC-LIFE-002`; partial foundation for `AC-LIFE-008` and `AC-NFR-003`.

### M05 — Lifecycle, payments, search and operational dashboard

Implement cumulative CB/Espèce editing backed by signed dated adjustments, effective-versus-recorded timestamps, Close/reopen/cancel, same-ID modification, abandon-edit, new order from reusable customer text, live telephone/comment search, operational turnover, received-payment summaries and future/due-today/overdue views.

Primary acceptance ownership: `AC-LIFE-003` through `AC-LIFE-014` and the live-search portion of `AC-LIFE-015`.

### M06 — Local recovery and authoritative/read-only enforcement

Connect the M01 recovery and authority primitives to every implemented durable business mutation. Complete recovery scheduling/debounce/flush, five-version retention, persistent non-authoritative/stale presentation and centralized blocking of every authoritative write.

Primary acceptance ownership: `AC-STO-006`, `AC-STO-010`; partial completion of `AC-PROD-002`.

### M07 — Pairing, formal handoff and disaster recovery

Implement device/lineage initialization, N-device pairing, formal exit handoff, safe acquisition, immutable versions, checksum/integrity validation, synchronization confirmation, five-version handoff retention, change-triggered recovery-only cloud checkpoints, explicit disaster recovery and generation invalidation.

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
- `implementation/` contains the detailed task contract for each milestone when that milestone is prepared and authorized.
- Tests should be linked by stable repository path and test name rather than copying test output into specification documents.
- Manual evidence should record the environment, action and result concisely without storing sensitive production data.

## 6. Current implementation state

Phase 6 planning is Approved.

M01 has a detailed task definition but production implementation has not yet started. No later milestone is authorized merely because its scope appears in this plan.

