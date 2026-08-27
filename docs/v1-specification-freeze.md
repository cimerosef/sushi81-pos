# V1 Specification freeze

**Status:** Approved — Phase 5 baseline  
**Freeze date:** 2026-08-27  
**Product:** Sushi81 POS  
**Purpose:** Record completion of the V1 design/specification phase and establish the authoritative implementation baseline for the next Codex phase.

## 1. Freeze declaration

The Sushi81 POS V1 product and technical specification is **frozen** as of 2026-08-27.

The Phase 1–5 document set has completed a repo-wide consistency review covering:

- product scope;
- order lifecycle and payment semantics;
- commercial/business rules;
- catalogue management;
- architecture;
- logical data model;
- local storage, OneDrive handoff, recovery and annual archive behavior;
- Hiboutik paste-order fallback;
- kitchen/customer printing and reprinting;
- export to the downstream `Gestion SUSHI 81` workflow;
- V1 acceptance criteria.

No unresolved V1 business decision or known cross-document technical contradiction remains at this freeze point.

Production implementation has **not** been started by this freeze action. The freeze authorizes the repository to enter the implementation phase only when an explicit Codex implementation task is issued.

## 2. Frozen baseline documents

The implementation-authoritative V1 baseline is:

### Phase 1

- `current-system.md` — **Approved — Phase 1 baseline**; historical/current-system reference only, not a target-behavior override.
- `product-requirements.md` — **Approved — Phase 1 baseline**.

### Phase 2

- `order-lifecycle.md` — **Approved — Phase 2 baseline**.
- `business-rules.md` — **Approved — Phase 2 baseline**.
- `catalogue-management.md` — **Approved — Phase 2 baseline**.

### Phase 3

- `architecture.md` — **Approved — Phase 3 baseline**.
- `data-model.md` — **Approved — Phase 3 baseline**.
- `storage-strategy.md` — **Approved — Phase 3 baseline**.

### Phase 4

- `paste-order-import.md` — **Approved — Phase 4 baseline**.
- `printing.md` — **Approved — Phase 4 baseline**.
- `export.md` — **Approved — Phase 4 baseline**.

### Phase 5

- `acceptance-criteria.md` — **Approved — Phase 5 baseline (V1 Specification)**.
- this `v1-specification-freeze.md` record — **Approved — Phase 5 baseline**.

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
- `hiboutik-paste-option-confirmation.md`;
- `hiboutik-paste-simplification.md`;
- `hiboutik-paste-total-calculation.md`;
- `non-authoritative-device-printing.md`;
- `order-modification-printing.md`;
- `payment-effective-date.md`;
- `reprint-marking.md`.

The baseline documents have been aligned so implementation should not need to resolve normal V1 behavior merely by comparing decision chronology.

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

Phase 5 also froze the last identified business-attribution ambiguity:

- normal payment entry defaults the effective payment date to the current business date;
- when a payment is entered/corrected later, the operator may select the date the money was actually received;
- daily CB/Espèce/total-received summaries use that effective business date;
- the separate application-generated `recorded_at` timestamp preserves the actual persistence time.

This is recorded in `docs/decisions/payment-effective-date.md` and incorporated into `order-lifecycle.md`, `data-model.md` and `acceptance-criteria.md`.

## 6. Consistency-review result

The Phase 5 review found and corrected the following classes of stale/inconsistent material:

- `product-requirements.md` was still marked Draft and contained superseded Hiboutik emergency-order requirements;
- `order-lifecycle.md` still described the former special emergency-import lifecycle;
- `data-model.md` still contained `EmergencyImportDetail`, original-Hiboutik-total/discrepancy concepts and an obsolete `sync-and-backup.md` reference;
- `storage-strategy.md` needed alignment with the later approved non-authoritative-device printing and simplified Hiboutik source semantics;
- `docs/decisions/README.md` described a numeric filename convention not used by the repository;
- the final payment effective-date/back-entry behavior had not yet been frozen;
- repository-level phase/status files still described the project as pre-freeze design work.

After correction, no target V1 baseline document remains intentionally in Draft status.

Intentional implementation-level choices may remain where they do not alter frozen behavior, for example exact UI layout, typography, physical SQL table/index naming, minor coordination serialization details and similar low-level representation choices expressly delegated by the approved specifications.

## 7. Authority and conflict rule for implementation

Codex and other implementation agents must use the frozen GitHub specification rather than prior chat memory or legacy VBA behavior.

`current-system.md` documents the former/current operational system and may explain why a requirement exists. It must **not** override later approved V1 target behavior.

If implementation discovers a genuine contradiction or a missing decision that would change business behavior, data semantics, architecture, storage safety, printing/export contract or another acceptance criterion:

1. do not silently choose a behavior;
2. stop the affected decision path;
3. surface the issue for product/specification resolution;
4. record any approved amendment in GitHub before implementing the changed behavior.

Pure implementation details that preserve all frozen semantics may be selected autonomously according to the project priority order:

**reliability > simplicity > maintainability > operational clarity > novelty.**

## 8. Change-control rule after freeze

The V1 Specification is now a baseline, not an immutable historical artifact.

A future necessary change is allowed, but any change that alters frozen product/business/architecture/data behavior must be treated as an explicit specification amendment and must update all affected baseline/acceptance documents consistently before the implementation is considered conformant.

## 9. Exit condition

**Phase 5 is complete.**

The repository is ready for the next phase: explicit Codex implementation planning and execution against the frozen V1 Specification and `acceptance-criteria.md`.
