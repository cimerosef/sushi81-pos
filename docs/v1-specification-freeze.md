# V1 Specification freeze

**Status:** Approved — Phase 5 baseline, amended 2026-09-14
**Freeze date:** 2026-08-27  
**Latest approved amendment:** 2026-09-14
**Product:** Sushi81 POS  
**Purpose:** Record completion of the V1 design/specification phase and establish the authoritative implementation baseline for the Codex implementation phase.

## 1. Freeze declaration

The Sushi81 POS V1 product and technical specification was **frozen** on 2026-08-27.

The Phase 1–5 document set completed a repo-wide consistency review covering:

- product scope;
- order lifecycle and payment semantics;
- commercial/business rules;
- catalogue management and `.xlsx` batch semantics;
- architecture;
- logical data model;
- local storage, OneDrive handoff, recovery and annual archive behavior;
- Hiboutik paste-order fallback;
- kitchen/customer printing and reprinting;
- export to the downstream `Gestion SUSHI 81` workflow;
- V1 acceptance criteria;
- repository/agent phase-state instructions used by the implementation phase.

The freeze is a controlled baseline, not a ban on later necessary amendments. Any approved amendment becomes part of the implementation-authoritative V1 baseline when affected documents are aligned consistently.

## 2. Frozen baseline documents

The implementation-authoritative V1 baseline is:

### Phase 1

- `current-system.md` — **Approved — Phase 1 baseline**; historical/current-system reference only, not a target-behavior override.
- `product-requirements.md` — **Approved — Phase 1 baseline**.

### Phase 2

- `order-lifecycle.md` — **Approved — Phase 2 baseline**.
- `business-rules.md` — **Approved — Phase 2 baseline**.
- `catalogue-management.md` — **Approved — Phase 2 baseline**, including the 2026-08-30 filtered bulk activation/deactivation amendment.

### Phase 3

- `architecture.md` — **Approved — Phase 3 baseline**, including later approved amendments recorded in that document.
- `data-model.md` — **Approved — Phase 3 baseline**.
- `storage-strategy.md` — **Approved — Phase 3 baseline**, including later approved amendments recorded in that document.

### Phase 4

- `paste-order-import.md` — **Approved — Phase 4 baseline**.
- `printing.md` — **Approved — Phase 4 baseline**.
- `export.md` — **Approved — Phase 4 baseline**.

### Phase 5

- `acceptance-criteria.md` — **Approved — Phase 5 baseline (V1 Specification)**, including later approved amendments recorded in that document.
- `acceptance-criteria-amendment-filtered-catalogue-bulk-activation.md` — **Approved 2026-08-30 V1 acceptance amendment**, defining AC-CAT-013 until the next consolidated acceptance-criteria rewrite.
- this `v1-specification-freeze.md` record — **Approved — Phase 5 baseline**, with amendment log below.

## 3. Approved decision records incorporated into the V1 baseline

The following approved records under `docs/decisions/` materially constrain V1 and have been checked against the baseline documents:

- `advance-order-marker.md`;
- `cancelled-order-reprinting.md`;
- `category-name-uniqueness.md`;
- `customer-ticket-no-b2b-invoice.md`;
- `delivery-fee-vat.md`;
- `export-date-range.md`;
- `export-eligibility.md`;
- `export-intermediate-file.md`;
- `export-post-export-correction.md`;
- `filtered-catalogue-bulk-activation.md` — **Approved 2026-08-30 V1 specification amendment**;
- `hiboutik-paste-option-confirmation.md`;
- `hiboutik-paste-simplification.md`;
- `hiboutik-paste-total-calculation.md`;
- `non-authoritative-device-printing.md`;
- `order-modification-printing.md`;
- `payment-effective-date.md`;
- `reprint-marking.md`;
- `target-directed-authority-handoff.md` — **Approved 2026-08-28 V1 specification amendment**.

The baseline documents are aligned so implementation should not need to resolve normal V1 behavior merely by comparing decision chronology.

## 4. Hiboutik model cleanup — final V1 state

The earlier complex Hiboutik emergency-order model is **superseded and removed from the target V1 model**.

V1 retains only the following source distinction:

- ordinary POS-originated order;
- Hiboutik paste-created order identified through a hidden non-user-facing source discriminator.

That marker exists only to prevent double counting and automatically excludes the underlying Hiboutik web order from ordinary POS-originated:

- operational turnover;
- received-payment summaries;
- CB amount that must newly be represented/entered in Hiboutik;
- export to `Gestion SUSHI 81`.

V1 does not contain a dedicated emergency-order UI, dashboard count, original Hiboutik amount, discrepancy/reconciliation workflow, dedicated Hiboutik reference field or `EmergencyImportDetail` entity.

## 5. Final payment-date decision

Phase 5 froze the last identified business-attribution ambiguity:

- normal payment entry defaults the effective payment date to the current business date;
- when a payment is entered/corrected later, the operator may select the date the money was actually received;
- daily CB/Espèce/total-received summaries use that effective business date;
- the separate application-generated `recorded_at` timestamp preserves the actual persistence time.

This is recorded in `docs/decisions/payment-effective-date.md` and incorporated into `order-lifecycle.md`, `data-model.md` and `acceptance-criteria.md`.

## 6. Phase 5 consistency-review result

The Phase 5 review found and corrected the following classes of stale, conflicting or incomplete specification material:

- `product-requirements.md` was still marked Draft and contained superseded Hiboutik emergency-order requirements;
- `order-lifecycle.md` still described the former special emergency-import lifecycle;
- `data-model.md` still contained `EmergencyImportDetail`, original-Hiboutik-total/discrepancy concepts and an obsolete `sync-and-backup.md` reference;
- `business-rules.md` still contained early drafting language implying that already-approved target rules were awaiting later confirmation;
- `catalogue-management.md` still contained early “deferred to later UI design” wording that could be read as an unresolved V1 decision and did not explicitly freeze how category names are represented/resolved in the three-sheet catalogue workbook;
- `storage-strategy.md` needed alignment with the later approved non-authoritative-device printing and simplified Hiboutik source semantics;
- `docs/decisions/README.md` described a numeric filename convention not used by the repository;
- several decision records still described their already-completed baseline incorporation as a future documentation action;
- `export-intermediate-file.md` still described workbook-contract details as generally implementation-defined even though the later Approved `export.md` freezes the concrete V1 four-sheet contract;
- the final payment effective-date/back-entry behavior had not yet been frozen;
- the initial acceptance draft needed stronger direct coverage of business-setting edits, main-screen summaries/reminders, telephone/comment search, explicit archive access, local-recovery triggers and change-triggered disaster-recovery checkpoints;
- repository-level `README.md`, `AGENTS.md`, `src/README.md` and `tests/README.md` still described the project as pre-freeze design work.

All of those items were corrected during Phase 5.

## 7. Post-freeze amendment — target-directed authority handoff (2026-08-28)

During Phase 6 M02, deterministic feasibility testing proved a material architecture blocker in the original generic OneDrive acquisition model:

- an eventually synchronized file-claim/election protocol can expose different claim sets to different devices;
- a claim-only design has an executable double-writer counterexample;
- a fully fail-closed design can avoid double writers only by refusing ordinary N-device acquisition without an external atomic grant;
- OneDrive per-file synchronization state is useful transport evidence but is not a documented distributed mutex or cross-client compare-and-swap.

The blocker evidence was merged through PR #2.

The user approved the amendment recorded in `docs/decisions/target-directed-authority-handoff.md` on 2026-08-28.

The V1 normal-handoff model is therefore amended as follows:

- N-device support remains;
- `live.db` remains local to each device; normal handoff uses a dedicated private GitHub Release Asset repository, while OneDrive remains recovery/archive storage and historical diagnostic transport only;
- normal close distinguishes **Close and retain authority** from **Transfer authority and close**;
- normal transfer is directed by the current authoritative source to exactly one eligible target device;
- the source must durably relinquish business-write authority only after strict GitHub snapshot server receipt, and the target-bound grant can exist only after that durable transition;
- after relinquishment the source is read-only/pending-transfer across restart and may only retry the same immutable transfer;
- only the exact designated target may acquire the normal handoff;
- non-target devices do not compete through claims/election;
- inability to recover/complete the designated target path uses explicit Disaster Recovery rather than ordinary target substitution;
- no Graph/OAuth/backend/server is introduced merely to arbitrate normal V1 authority transfer; GitHub REST is used only as the approved asset transport/acknowledgement boundary.

`architecture.md`, `storage-strategy.md` and `acceptance-criteria.md` are amended to contain these semantics directly. The approved GitHub transport amendment is recorded in `docs/decisions/github-handoff-transport.md`.

M02 revalidated the amended protocol before M03 began.

## 8. Post-freeze amendment — filtered catalogue bulk activation/deactivation (2026-08-30)

During Phase 6 M03 interactive catalogue acceptance, the operator identified a practical current-catalogue maintenance gap: search/category/status filters could narrow the catalogue, but enabling or disabling the resulting products still required one-by-one edits.

The user approved the amendment recorded in `docs/decisions/filtered-catalogue-bulk-activation.md` on 2026-08-30.

V1 Catalogue maintenance is therefore amended as follows:

- existing code/name search, category filter and Active/Inactive/All status filter compose as the bulk-selection boundary;
- bulk Activate and bulk Deactivate target the complete current filtered result, not just visible viewport rows;
- the action captures an immutable Product-ID/target-state snapshot before confirmation;
- confirmation exposes matched and effective-change counts plus the target action;
- already-target-state products are skipped;
- zero effective changes perform no business write;
- the required active-state changes are committed atomically as one business mutation;
- stale/missing/conflicting captured products fail the complete operation rather than allowing partial success;
- only Product active/inactive state may change;
- historical snapshots and all unrelated current catalogue fields/options remain unchanged;
- no bulk permanent-delete workflow is introduced;
- successful completion refreshes the catalogue while preserving current filters;
- all operator-facing additions are localized in French and Simplified Chinese.

`catalogue-management.md` now contains these semantics directly. `acceptance-criteria-amendment-filtered-catalogue-bulk-activation.md` adds `AC-CAT-013` as an Approved acceptance amendment until the next consolidated rewrite of `acceptance-criteria.md`.

M03 owns this extension; M04 remains gated until M03 including AC-CAT-013 is accepted and merged.

## 9. Post-freeze amendment — Hiboutik paste-order fallback (2026-09-14)

The approved M09 amendment is recorded in `docs/decisions/m09-hiboutik-paste-operator-workflow-and-source-reference.md`, registered in `acceptance-criteria-amendment-m09-hiboutik-paste-fallback.md`, and consolidated in `paste-order-import.md`. Those records are part of the current implementation-authoritative V1 baseline for M09. The amendment's implementation and acceptance status are tracked separately in the M09 implementation records; this freeze record does not change those business semantics.

## 10. Authority and conflict rule for implementation

Codex and other implementation agents must use the frozen-and-amended GitHub specification rather than prior chat memory or legacy VBA behavior.

`current-system.md` documents the former/current operational system and may explain why a requirement exists. It must **not** override later approved V1 target behavior.

If implementation discovers a genuine contradiction or a missing decision that would change business behavior, data semantics, architecture, storage safety, printing/export contract or another acceptance criterion:

1. do not silently choose a behavior;
2. stop the affected decision path;
3. surface the issue for product/specification resolution;
4. record any approved amendment in GitHub before implementing the changed behavior.

Pure implementation details that preserve all approved semantics may be selected autonomously according to the project priority order:

**reliability > simplicity > maintainability > operational clarity > novelty.**

## 11. Change-control rule after freeze

The V1 Specification is a baseline, not an immutable historical artifact.

A future necessary change is allowed, but any change that alters frozen product/business/architecture/data behavior must be treated as an explicit specification amendment and must update all affected baseline/acceptance documents consistently before the implementation is considered conformant.

The 2026-08-28 target-directed authority-handoff amendment and the 2026-08-30 filtered catalogue bulk activation/deactivation amendment demonstrate this process.

## 12. Current exit condition

**Phase 5 remains complete.**

Phase 6 implementation is active. M01 through M06 are Passed and merged. M06 — Local recovery and authoritative/read-only enforcement — was merged through PR #11 to `main` at merge commit `2c5eb52740d0c12e3e837579ecceac6d0600b59e`; accepted production repair head is `4a0c1ca9e44a6c48899e6ef8dc211172371e4d20`, final documentation/PR head is `86326d81551aa4cb5cdcbc6826b8c740317b34c4`, Release tests are 364/364 Passed, and project-owner Windows/WPF manual acceptance is Passed. M07 is the next planned milestone but is not yet implementation-authorized; no M07 implementation branch, PR or active handoff exists and Codex execution gate issue #4 is CLOSED. M08 and later milestones are not started.
