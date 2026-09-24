# V1 Specification freeze

**Status:** Approved — Phase 5 baseline, amended 2026-09-17
**Freeze date:** 2026-08-27
**Latest approved amendment:** 2026-09-21
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
- local storage, normal handoff, recovery and annual archive behavior;
- Hiboutik paste-order fallback;
- kitchen/customer printing and reprinting;
- export to the downstream `Gestion SUSHI 81` workflow;
- V1 acceptance criteria;
- repository/agent phase-state instructions used by the implementation phase.

The freeze is a controlled baseline, not a ban on later necessary amendments. Any approved amendment becomes part of the implementation-authoritative V1 baseline when affected documents are aligned consistently.

## 2. Frozen baseline documents

The implementation-authoritative V1 baseline is:

### Phase 1

- `current-system.md` — Approved — Phase 1 baseline; historical/current-system reference only, not a target-behavior override.
- `product-requirements.md` — Approved — Phase 1 baseline.

### Phase 2

- `order-lifecycle.md` — Approved — Phase 2 baseline.
- `business-rules.md` — Approved — Phase 2 baseline.
- `catalogue-management.md` — Approved — Phase 2 baseline, including later approved Catalogue amendments.

### Phase 3

- `architecture.md` — Approved — Phase 3 baseline, including later approved amendments recorded there.
- `data-model.md` — Approved — Phase 3 baseline.
- `storage-strategy.md` — Approved — Phase 3 baseline, including later approved amendments.

### Phase 4

- `paste-order-import.md` — Approved — Phase 4 baseline and later M09 amendment consolidation.
- `printing.md` — Approved — Phase 4 baseline.
- `export.md` — Approved — Phase 4 baseline.

### Phase 5

- `acceptance-criteria.md` — Approved — Phase 5 baseline (V1 Specification);
- approved acceptance amendments under `docs/acceptance-criteria-amendment-*.md`;
- this `v1-specification-freeze.md` record, with amendment log below.

## 3. Approved decision records incorporated into the V1 baseline

Approved records under `docs/decisions/` materially constrain V1 and supplement/amend the baseline where stated. Important examples include:

- `advance-order-marker.md`;
- `cancelled-order-reprinting.md`;
- `category-name-uniqueness.md`;
- `customer-ticket-no-b2b-invoice.md`;
- `delivery-fee-vat.md`;
- `export-date-range.md`;
- `export-eligibility.md`;
- `export-intermediate-file.md`;
- `export-post-export-correction.md`;
- `filtered-catalogue-bulk-activation.md`;
- `hiboutik-paste-option-confirmation.md`;
- `hiboutik-paste-simplification.md`;
- `hiboutik-paste-total-calculation.md`;
- `non-authoritative-device-printing.md`;
- `order-modification-printing.md`;
- `payment-effective-date.md`;
- `reprint-marking.md`;
- `target-directed-authority-handoff.md`;
- `github-handoff-transport.md`;
- M07 self-join/DR/recovery-ordering decisions;
- `m08-print-layout-and-receipt-identity.md`;
- `m09-hiboutik-paste-operator-workflow-and-source-reference.md`;
- `post-m09-hiboutik-daily-payment-dashboard.md`;
- `m10-category-short-code-workbook-semantics.md`;
- `m11-export-lifecycle-and-settlement-clarifications.md`;
- `m12-local-archive-and-user-selected-export.md`.

Where a later Approved decision explicitly supersedes a narrow earlier clause, the later decision controls.

## 4. Phase 5 Hiboutik cleanup — historical freeze state

The earlier complex Hiboutik emergency-order model was superseded and removed from the V1 target model during Phase 5.

The initial frozen model retained only ordinary POS-originated versus Hiboutik paste-created source distinction for anti-double-counting and excluded the old emergency-order UI/status/discrepancy subsystem.

Later M09 and post-M09 approved amendments deliberately added only the narrowly recorded product-block/operator-resolution/source-reference and two-value dashboard behavior. Those later sections control where they supersede the initial Phase 5 wording.

## 5. Final payment-date decision

Phase 5 froze the business-attribution rule:

- payment entry defaults effective date to current business date;
- later/back-entered payment may use the date money was actually received;
- daily CB/Espèce/received summaries use effective business date;
- separate `recorded_at` preserves persistence time.

This is recorded in `docs/decisions/payment-effective-date.md` and incorporated into lifecycle/data/acceptance specifications.

## 6. Phase 5 consistency-review result

The Phase 5 review corrected stale/conflicting/incomplete specification material, including:

- Draft/superseded product requirements;
- former complex Hiboutik emergency lifecycle/data model;
- obsolete storage references;
- category/workbook ambiguity;
- payment effective-date ambiguity;
- incomplete acceptance coverage;
- repository-level pre-freeze status wording.

Historical Phase 5 details remain available in repository history; later amendments below are additive controlled changes rather than a reopening of the freeze.

## 7. Post-freeze amendment — target-directed authority handoff (2026-08-28)

M02 feasibility proved generic OneDrive claim/election could not safely provide ordinary N-device single-writer acquisition. The owner approved target-directed source-to-one-target handoff with strict GitHub Release Asset receipt/grant semantics. OneDrive remained approved for recovery/archive at that point; the later 2026-09-21 M12 amendment moved annual archives to local application-managed storage while preserving OneDrive Disaster Recovery.

The controlling decisions are `docs/decisions/target-directed-authority-handoff.md` and `docs/decisions/github-handoff-transport.md`. Architecture/storage/acceptance were aligned before later milestones proceeded.

## 8. Post-freeze amendment — filtered catalogue bulk activation/deactivation (2026-08-30)

The owner approved bulk Activate/Deactivate over the complete current composed Catalogue filter result, with immutable capture, impact confirmation, no-op handling, atomic state-only mutation, conflict rollback, filter preservation and FR/zh-CN presentation.

The controlling decision is `docs/decisions/filtered-catalogue-bulk-activation.md`; `catalogue-management.md` and the matching acceptance amendment are authoritative.

## 9. Post-freeze amendment — Hiboutik paste-order fallback (2026-09-14)

The approved M09 amendment is recorded in:

- `docs/decisions/m09-hiboutik-paste-operator-workflow-and-source-reference.md`;
- `docs/acceptance-criteria-amendment-m09-hiboutik-paste-fallback.md`;
- consolidated `docs/paste-order-import.md`.

It controls product-detail-block paste scope, fail-safe unresolved-line operator handling, passive Hiboutik source identification and nullable read-only `source_total_ttc`, while ordinary POS pricing remains authoritative and the old emergency subsystem remains excluded.

M09 is Passed and merged through PR #17 at `d840066d8d2ffa1856c4fcd88dbfdd3c8f2a1be5`.

## 10. Post-freeze amendment — Post-M09 Hiboutik daily payment dashboard (2026-09-16)

The approved amendment is recorded in:

- `docs/decisions/post-m09-hiboutik-daily-payment-dashboard.md`;
- `docs/acceptance-criteria-amendment-post-m09-hiboutik-daily-payment-dashboard.md`.

It authorizes exactly two passive read-only top-Caisse values, `Hiboutik CB aujourd'hui` and `Hiboutik Espèce aujourd'hui`, derived from signed effective-date payment adjustments on non-Cancelled `HIBOUTIK_PASTE` orders. It does not add a schema field, durable entity, write path, Hiboutik turnover/count/discrepancy metric or dedicated workflow.

The enhancement is Passed and merged through PR #19 at `861cfba1dfacbb3289395c0370f6d42765b6c223`.

## 11. Post-freeze amendment — M10 Category short-code workbook semantics (2026-09-17)

During M10 preparation, the audit found one genuine operator-visible gap: the original three-sheet Catalogue workbook baseline predated the later Approved M04 Category `short_code` business field, even though M04 explicitly required future `.xlsx` support to preserve it.

The project owner approved the proposed contract on 2026-09-17. The controlling records are:

- `docs/decisions/m10-category-short-code-workbook-semantics.md`;
- `docs/acceptance-criteria-amendment-m10-category-short-code-workbook.md`;
- aligned `docs/catalogue-management.md`.

The amended V1 Catalogue workbook semantics are:

- `Products` visibly carries Category name and Category short code;
- no operator-facing `Categories` worksheet is added;
- `category_id` remains technical/non-operator identity;
- new Categories created through import may receive one optional consistent short code under existing validation;
- repeated Product rows referring to the same new Category may not contain conflicting non-blank short codes;
- for an existing Category, blank short code preserves the current value and the same normalized value is valid consistency data;
- a different non-blank value for an existing Category is a blocking Error;
- an existing Category with no short code cannot be assigned one through workbook import;
- workbook import never clears/replaces/globally changes an existing Category short code;
- existing Category short-code changes remain in the normal in-application Category manager;
- Add-only mode follows the same Category rules;
- any Category short-code conflict blocks the entire atomic import.

This amendment closes the last material M10 workbook-specification gap. It does **not** authorize M10 implementation by itself.

## 12. Post-freeze amendment — M11 Gestion export clarifications (2026-09-20)

During M11 readiness, the audit found material ambiguity around the exported SettlementDate and post-export correction precedence. The project owner approved the controlling semantics on 2026-09-20.

Records:

- `docs/decisions/m11-export-lifecycle-and-settlement-clarifications.md`;
- `docs/acceptance-criteria-amendment-m11-gestion-export.md`;
- aligned `docs/export.md`.

The amendment freezes:

- Closed as the positive-sale lifecycle export gate; Open never emits CREATE/UPDATE;
- no new independent operator-configurable payment gate in export; the existing exact-payment Close invariant remains;
- SettlementDate from effective payment business date, not merely Close/recording date;
- UPDATE waits until the current committed order is Closed;
- CANCEL supersedes a pending UPDATE that has never been successfully emitted;
- CANCEL for a previously exported order does not require current Closed/settled state;
- the versioned workbook fields remain fixed rather than selected ad hoc per run.

This amendment does not authorize M11 implementation by itself.

## 13. Post-freeze amendment — M12 local annual archive and user-selected export (2026-09-21)

During M12 readiness, the owner chose to keep canonical annual archive databases in application-managed local storage rather than OneDrive.

The controlling records are:

- `docs/decisions/m12-local-archive-and-user-selected-export.md`;
- `docs/acceptance-criteria-amendment-m12-local-archive.md`;
- aligned `docs/storage-strategy.md`, `docs/architecture.md` and M12 implementation controls.

The amendment freezes:

- canonical annual archives under local application business-data storage;
- no OneDrive annual archive publication/synchronization/discovery;
- staged local build + validation + durable local canonical promotion/reopen validation before live removal;
- permanent local retention in normal POS workflow;
- automatic February/late-start archive remains non-interactive;
- explicit archive export is a separate action whose destination is chosen by the operator;
- export copies rather than moves the canonical archive and failure cannot mutate archive/live data;
- normal handoff does not automatically transfer archive files.

Archive-year rules, authority boundaries, historical snapshots/reprinting and M11 pending-export preservation remain unchanged.

## 14. Post-freeze amendment — M13 Gestion export ledger retention/compaction (2026-09-23)

The project owner approved a final V1 data-retention requirement for the M11 Gestion export ledger as part of M13.

The controlling records are:

- `docs/decisions/m13-gestion-export-ledger-retention-compaction.md`;
- `docs/acceptance-criteria-amendment-m13-gestion-export-retention.md`;
- aligned M13 implementation controls.

The amendment freezes:

- unresolved PREPARED/pending/unemitted CREATE/UPDATE/CANCEL work is never pruned;
- while an order remains live, required last-successful export state remains available for duplicate protection and later UPDATE/CANCEL semantics;
- archived-order export state becomes cleanup-eligible only after the order leaves the live set through completed M12 archival, no unresolved work remains and no still-live decision can be affected;
- complete immutable successful-batch payload/exact-regeneration history is retained for at least 30 days after successful completion;
- legitimately pruned successful batches are no longer presented as regenerable history;
- compaction is transactional, idempotent, failure-safe, foreign-key safe and preserves authority/recovery/handoff invariants.

The implementation may retain state longer when a dependency remains; it must not prune earlier than the Approved safety boundary.

## 15. Authority and conflict rule for implementation

Codex and other implementation agents must use the frozen-and-amended GitHub specification rather than prior chat memory or legacy VBA behavior.

`current-system.md` is historical/current-system reference and does not override later approved V1 target behavior.

If implementation discovers a genuine contradiction or missing decision that would change business behavior, data semantics, architecture, storage safety, printing/export contract or another acceptance criterion:

1. do not silently choose a behavior;
2. stop the affected path;
3. surface it for product/specification resolution;
4. record any approved amendment in GitHub before implementation continues.

Pure implementation details preserving approved semantics may be selected autonomously according to:

**reliability > simplicity > maintainability > operational clarity > novelty.**

## 16. Change-control rule after freeze

The V1 Specification is a baseline, not an immutable historical artifact.

A future necessary change is allowed, but any change that alters frozen product/business/architecture/data behavior must be treated as an explicit specification amendment and update affected baseline/acceptance documents consistently before implementation is considered conformant.

Approved post-freeze amendments above demonstrate this process.

## 17. Current exit condition

**Phase 5 remains complete.**

Phase 6 implementation is active.

Current state as of 2026-09-24:

- M01 through M11: Passed / merged;
- current authoritative `main`: M12 merge commit `f59663c6b47ab21114c24360544e4e25094f4722`;
- M12 PR #25: CLOSED / MERGED;
- M12 final pre-merge head `49a0fe69e23e68c5591ef36ba5c56ef09a9d88b3`; CI #862 / run `36021889765`: SUCCESS, 881/881 passed, 0 failed, 0 skipped, Release build 0 warnings / 0 errors;
- M12 post-merge CI #863 / run `36023054757`: build-and-test SUCCESS;
- M12 is controller-accepted/merged under the explicit owner waiver; real populated-archive operational verification remains deferred and is not claimed Passed;
- M13 is owner-authorized in preparation on `codex/m13-installer-final-acceptance-authorized`;
- the Approved M13 export-ledger retention/compaction amendment is part of the frozen-and-amended V1 baseline;
- GitHub currently reports the source repository as public, conflicting with the project's private-repository expectation; this repository-safety discrepancy blocks executable M13 work.

Issue #4 remains the sole live execution switch and is currently CLOSED. No executable M13 handoff exists until the repository-safety discrepancy is resolved.
