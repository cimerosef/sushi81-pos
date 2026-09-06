# M05 manual Windows/WPF acceptance findings — batch 02

**Milestone:** M05 — Lifecycle, payments, search and operational dashboard  
**Date:** 2026-09-02  
**Performed by:** project owner  
**PR:** #10 — `M05: lifecycle payments search and operational dashboard`  
**Production-code baseline under test:** `13f298be3ae146686b0ce83d1f21bd9aabc936c0`  
**Overall result:** Partial — FIX-05 functional remediation passed its primary checks; three follow-up findings require a focused correction before the remaining M05 checklist continues.

## FIX-05 directed regression results

The project owner completed the six directed checks requested after `M05-MANUAL-ACCEPTANCE-SEARCH-LAYOUT-FIX-05`.

Passed:

1. partial human-reference search;
2. partial telephone search, including formatted fragments;
3. Commandes upper-list / lower-detail composition, approved visible column set and scrolling behavior;
4. Caisse removal of duplicated saved-order/reload/date-browser UI plus materially taller Panier;
5. new-order success feedback uses the human `YYYYMMDD-NNN` reference.

Partially failed:

6. FR/zh-CN switching preserves the interface/layout, but an already-visible red Caisse validation message does not re-render into the newly selected language. Example observed: after switching the surrounding UI to Simplified Chinese while fulfilment remains unselected, the visible validation message remains French (`Le mode de commande est obligatoire.`).

The application must update visible validation feedback when the active UI language changes while preserving unrelated editor state. This is frozen in `docs/decisions/m05-manual-acceptance-followup-clarifications.md` D1.

## Additional operator findings

### Commandes filler width

At a wide window size, the nine approved business columns occupy only part of the DataGrid viewport and WPF leaves a large empty filler region after `Adresse`.

This is not a request for another business column. The list must remain exactly the existing columns plus `Mode`, `Commentaire`, `Adresse`. The useful remaining width should instead improve the readability of the long-text Commentaire/Adresse columns, while narrow supported sizes retain horizontal scrolling. See D2 of the follow-up clarification.

### Planned fulfilment time incorrectly blocks confirmation

The project owner observed that Caisse still requires a planned time before confirmation.

Authority review found that this is an existing specification/implementation defect:

- `docs/product-requirements.md` FR-017 defines planned fulfilment date and **optional time**;
- `docs/data-model.md` marks `planned_fulfilment_date` required and `planned_fulfilment_time` not required;
- `docs/implementation/milestone-04-order-entry.md` section 4.5 later contradicted those frozen semantics by requiring time for new POS confirmation;
- current M04/M05 presentation code inherited the stricter behavior.

The approved correction is recorded in `docs/decisions/m05-manual-acceptance-followup-clarifications.md` D3: planned date remains mandatory, planned time is optional; if selected, it must still be one of the approved structured five-minute slots.

## Acceptance state

Do not mark M05 Passed.

The already-passed batch-01 checks and FIX-05 directed checks 1–5 do not need wholesale repetition. After the focused follow-up correction is independently reviewed and republished, the project owner should recheck only the affected language/filler/optional-time paths before continuing the remaining M05 lifecycle/payment/dashboard checklist.

PR #10 must remain open/unmerged. M06 remains not authorized.
