# M09 — Hiboutik paste-order fallback — preparation/readiness

**Status:** Preparation complete — ready for project-owner implementation authorization  
**Date:** 2026-09-14  
**Milestone:** M09 — Hiboutik paste-order fallback  
**Implementation authorization:** **NOT GRANTED by this record**  
**Codex gate:** Issue #4 must remain CLOSED until separate owner authorization and executable handoff publication.

## 1. Verified baseline

Preparation re-established the baseline from GitHub rather than prior chat memory.

Verified merge baseline before M09 preparation:

- `main` was at M08 merge commit `8f246ce7fb32baa33e1dfe1d334175bf2df60c1f`;
- PR #14 — `M08: printing and reprinting` — was CLOSED / MERGED;
- M08 accepted production candidate: `86d19cbc3aa127c836b1b13f91292ddcb54d08bd`;
- M08 final pass-record head: `5f8c92c29116e17a3365ecd8801f7e13f107269c`;
- exact-head CI #675 succeeded;
- Issue #4 was CLOSED with no active Codex handoff;
- M09 implementation had not started;
- M10+ remained unauthorized.

The living status/issue/root README still contained pre-merge M08 wording. That was identified as state-accounting drift, not a specification conflict. M09 preparation includes cleanup of current-state pointers while preserving historical M08 evidence.

## 2. Controlling M09 specification

M09 owns AC-HIB-001 through AC-HIB-009, as amended on 2026-09-14.

Primary sources:

- `docs/paste-order-import.md`;
- `docs/acceptance-criteria.md` plus `docs/acceptance-criteria-amendment-m09-hiboutik-paste-fallback.md`;
- `docs/decisions/hiboutik-paste-simplification.md`;
- `docs/decisions/hiboutik-paste-option-confirmation.md`;
- `docs/decisions/hiboutik-paste-total-calculation.md`;
- `docs/decisions/m09-hiboutik-paste-operator-workflow-and-source-reference.md`;
- ordinary pricing/business rules/lifecycle/data/storage/printing specifications already Passed through M04-M08.

The 2026-09-14 decision is a narrow post-freeze amendment. Where older Phase 4 wording says the Hiboutik source must be completely invisible or no source amount may be retained, the new decision controls.

## 3. Frozen M09 operator workflow

The approved fallback is:

1. operator copies the Hiboutik **product-detail block** directly from the automatic-order email;
2. copied text may include per-item `Total : ...` lines, final `TOTAL ...`, and the known `Livraison (0)` technical/service line;
3. paste/parse itself performs no business write;
4. exact current active product codes resolve automatically;
5. known source-total/service lines are specifically classified and ignored as order items;
6. unknown/material lines become unresolved and cannot disappear silently;
7. operator resolves each unknown by selecting a current active catalogue product or explicitly marking it not-a-product/ignore;
8. imported products with enabled option groups use the existing ordinary option UI, including explicit no-selection confirmation for optional groups;
9. operator manually fills ordinary order-level fields: fulfilment mode, planned date/time, telephone, address, comment/reference as desired;
10. ordinary POS pricing/validation remains authoritative;
11. an optional read-only `source_total_ttc` may retain a reliably determined Hiboutik source total for evening matching;
12. normal confirmation durably commits the ordinary order with `source_type = HIBOUTIK_PASTE` before ordinary M08 printing;
13. later lifecycle/edit/payment/printing remain ordinary;
14. anti-double-counting reporting/export exclusions remain source-driven.

There is no emergency-order screen, status, counter, discrepancy state, dedicated Hiboutik reference field, raw email retention, duplicate subsystem or Hiboutik-specific payment workflow.

## 4. Real-source readiness

During preparation the owner supplied two screenshots of real Hiboutik automatic-order emails.

The screenshots established the source structure needed for M09 without committing production-sensitive material to Git:

- product lines use `quantity x product-code source-description (source price)`;
- each product is followed by `Total : amount`;
- a final `TOTAL amount` may follow the block;
- delivery examples may include `1 x Livraison (0)` followed by `Total : 0`.

The owner confirmed that the normal store workflow copies the whole product-detail block rather than cleaning total lines individually.

A synthetic structural fixture is committed at:

`samples/pasted-orders/hiboutik-product-block-synthetic.txt`

No real customer name, address, telephone or Hiboutik order reference from the screenshots is stored in the repository.

## 5. Existing implementation seams discovered

### 5.1 Ordinary order-entry draft/pricing

The existing M04 order-entry spine already provides:

- `NewOrderDraft`;
- ordinary product/cart lines;
- ordinary product option resolution;
- ordinary pricing through `OrderPricingService`;
- ordinary validation;
- manual authoritative-total override;
- commit-before-print confirmation.

M09 should populate/reuse this spine, not create a second Hiboutik order-confirmation service.

### 5.2 Catalogue lookup

Current catalogue queries already support active product listing/search and loading the current product aggregate for order entry.

M09 needs a deterministic exact-code resolution seam. Implementation may add a dedicated exact-code query/service method rather than abusing fuzzy/general search semantics.

### 5.3 Option workflow

The WPF order-entry presentation already supports loading a current product and adding a configured ordinary cart line after option selection.

M09 should route both automatically resolved products-with-options and manually resolved unknown lines through the existing ordinary option UI.

A transient imported-line confirmation state is acceptable to distinguish “optional group explicitly confirmed empty” from “not yet reviewed”; it must not become a durable Hiboutik entity.

### 5.4 Source discriminator/data model

The domain and SQLite persistence already support:

- `OrderSourceType.Pos`;
- `OrderSourceType.HiboutikPaste`;
- persisted `source_type` values `POS` / `HIBOUTIK_PASTE`.

Existing lifecycle modification code preserves the current order source rather than allowing it to change.

M09 therefore needs only the approved nullable `source_total_ttc` persistence extension plus source-aware new-order confirmation/presentation.

### 5.5 Operational reporting exclusion

Existing SQLite operational turnover and received-payment queries already filter to `source_type='POS'`.

M09 must add regression evidence proving the newly reachable production `HIBOUTIK_PASTE` path remains excluded from those POS-originated summaries while still appearing in ordinary order search/future/due/overdue operational views.

The final `Gestion SUSHI 81` export-exclusion production cross-check remains M11.

### 5.6 Authority/recovery

The M06/M07 centralized write-authority/recovery spine already protects durable order mutations.

Paste/parse/transient unresolved review is not a business write. Final confirmation of a Hiboutik paste order must use the same authoritative-device write guard and durable-change notification as any ordinary new order.

A non-authoritative device may inspect/compose transient state as allowed by the ordinary UI, but must not be able to commit through the paste path.

### 5.7 M08 printing

The existing confirmation boundary persists the order before invoking printing. M09 should reuse that exact path.

No Hiboutik-specific print adapter/model is required. The customer/kitchen selling amount remains the ordinary authoritative POS total, not `source_total_ttc`.

### 5.8 Localization/testing

Desktop localization and the existing Application/Domain/Infrastructure test projects provide the seams for FR/ZH labels plus parser/application/integration regression tests.

## 6. Material specification decisions

The preparation audit originally found one parser-format blocker: no reliable real Hiboutik source grammar was committed or documented.

That blocker is now resolved by owner-supplied real examples plus the approved product-block contract and synthetic repository fixture.

The following material decisions were also explicitly made and frozen during preparation:

- product-block-only paste scope;
- total lines tolerated for copy speed;
- fail-safe unresolved-line operator handoff;
- explicit operator ignore option;
- unresolved state blocks confirmation;
- passive read-only Hiboutik source indication;
- nullable read-only/preserved `source_total_ttc` reference amount;
- no parser ownership of fulfilment/date/time/customer fields.

No unresolved material business/specification decision remains for M09 implementation.

## 7. Proposed implementation branch / PR / mailbox / gate

After separate owner implementation authorization:

- branch: `codex/m09-hiboutik-paste-fallback`;
- PR title: `M09: Hiboutik paste-order fallback`;
- PR Conversation comments are the durable ChatGPT↔Codex mailbox;
- Issue #4 body must point to the exact active M09 branch/PR;
- Issue #4 remains CLOSED while ChatGPT prepares/publishes the first executable handoff;
- only after the complete `CODEX_HANDOFF_READY` exists should the owner be asked to reopen Issue #4;
- Codex processes only that authorized handoff and may not start M10;
- merge always requires separate explicit project-owner approval.

No M09 implementation branch/PR is created by this preparation record.

## 8. Readiness conclusion

**Specification readiness:** PASS  
**Real-source/parser-format readiness:** PASS  
**Existing implementation seams:** PASS  
**Material business questions:** none open  
**M08 dependency:** satisfied / merged  
**Issue #4:** CLOSED  
**Executable Codex handoff:** none  
**M10+:** unauthorized

### Final disposition

**M09 is READY FOR PROJECT-OWNER IMPLEMENTATION AUTHORIZATION.**

This readiness conclusion is not itself implementation authorization. Codex must not modify production code until a separate explicit owner authorization is recorded and the repository gate/mailbox prerequisites are satisfied.
