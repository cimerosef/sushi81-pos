# M05 manual Windows/WPF acceptance findings — batch 01

**Milestone:** M05 — Lifecycle, payments, search and operational dashboard  
**Date:** 2026-09-02  
**Performed by:** project owner  
**PR:** #10 — `M05: lifecycle payments search and operational dashboard`  
**Production-code baseline:** behavior under test is code-equivalent to automated-review head `6b578a750a523c44fb03b38f2aa63d591377615f`; later PR heads before this record contained documentation/process-only changes.  
**Overall result:** Partial — batch 01 completed; one search defect/usability gap plus approved presentation changes require remediation before the remaining manual checklist continues.

## Batch 01 checks performed

The project owner completed the first eight M05 manual checks:

1. ordinary Caisse Retrait order creation;
2. visible human reference in Commandes using the new `YYYYMMDD-NNN` format rather than requiring GUID handling;
3. same-business-date reference increment for a second order;
4. search for the first order by human reference;
5. search by telephone and comment;
6. Modify telephone/comment then Abandon and verify persisted values are restored;
7. Modify and Save and verify the same human reference is retained;
8. restart and verify the modified order/reference persist.

## Results

Passed as expected:

- ordinary Caisse order creation;
- new human reference visibility;
- same-day sequence increment;
- full human-reference search;
- full telephone search;
- comment substring search;
- Modify + Abandon restores the persisted state;
- Modify + Save retains the same human reference;
- restart persistence of the modified order/reference.

Finding requiring remediation:

- human-reference and telephone searches currently require the complete value in the operator scenario; partial fragments do not return the order;
- comment search already supports partial text.

The project owner approved partial matching for human reference, telephone and comment as recorded in `docs/decisions/m05-manual-acceptance-search-and-layout-clarifications.md`.

## Approved UI changes discovered during the same acceptance pass

### Commandes

Current side-by-side composition (orders list left, selected-order detail right) should be replaced by:

- search/date/refresh controls at top;
- orders list across the full width in the upper area;
- selected-order detail in the lower area;
- horizontal and vertical scrolling on the orders list;
- additional useful list columns including fulfilment mode, comment and delivery address, alongside the existing reference/date/time/status/financial/telephone facts.

The intent is to make the master list materially more informative without squeezing every column into the viewport.

### Caisse

Remove the duplicated M04-era committed-order UI now that Commandes is the dedicated retrieval/management workspace:

- saved/reloaded-order summary;
- old technical/GUID order-ID entry/display;
- reload-ID button row;
- `Commandes enregistrées` / date-browser group.

Use the released vertical space to make the Panier substantially taller while retaining cart scrolling. Keep the compact M05 operational dashboard strip.

## Acceptance state

Do not mark M05 Passed from this batch.

The remaining M05 manual checklist is paused until the search/layout remediation is implemented, independently reviewed and republished for operator continuation.

PR #10 must remain open/unmerged. M06 remains not authorized.
