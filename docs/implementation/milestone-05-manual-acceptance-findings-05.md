# M05 manual Windows/WPF acceptance findings — batch 05

**Milestone:** M05 — Lifecycle, payments, search and operational dashboard  
**Date:** 2026-09-06  
**Performed by:** project owner  
**PR:** #10 — `M05: lifecycle payments search and operational dashboard`  
**Production-code baseline under test:** `b51a4bbb02605ec7aea5a5eacb1160738a46b080`  
**Overall result:** Passed for acceptance items 8–10 and the FIX-12 current-Catalogue picker quick-filter follow-up.

## Acceptance item 8 — quantity-only historical snapshot

Passed manually.

After changing the current Catalogue price, modifying only the quantity of an existing historical order line preserved the sale-time product price/snapshot rather than adopting the current Catalogue value.

## Acceptance item 9 — add a new current-Catalogue line

Passed manually.

The newly added line used current Catalogue data as required.

The picker remediation from FIX-11 was also manually accepted:

- each row shows `Product.Code + Product.Name`;
- the malformed oversized vertical `Annuler` / `Ajouter` action area is gone;
- the redundant `Annuler` button is removed;
- one normal-sized `Ajouter` action remains;
- title-bar close retains cancel semantics;
- the resulting compact picker is operationally acceptable.

## FIX-12 follow-up — current-Catalogue picker quick filter

Passed manually.

The project owner verified all four targeted quick-filter behaviors on the published FIX-12 build:

- product-code fragment filtering works (for example `002` reduces the list to `TST002`);
- product-name partial filtering works case-insensitively (for example `simple` / `SIMPLE`);
- clearing the search restores the full supplied product list;
- filtering does not silently auto-select or auto-add a product; explicit selection is still required before `Ajouter` can accept it.

This usability follow-up is therefore closed.

## Acceptance item 10 — explicit current-Catalogue option reconfiguration

Passed manually.

Historical order line `TST002` previously contained:

- `Sans accompagnement (-1,00 €)`;
- `Sauce premium (+1,00 €)`.

The current Catalogue was edited so that `Sauce premium` became `+1,50 €`.

The project owner first intentionally unchecked `Sans accompagnement (-1,00 €)`, producing the localized required-group validation:

`La sélection du groupe « Suppléments » est invalide.`

That validation was confirmed as expected behavior, not a preselection defect.

The project owner then completed a valid explicit reconfiguration on the existing order and saved it. The persisted/currently displayed line used the current Catalogue option value `Sauce premium (+1,50 €)`, and the authoritative order total recalculated to `26,01 €`, exactly matching the expected current-Catalogue reconfiguration result.

There is no evidence of silent option loss or option-ID regression from this acceptance batch.

## Acceptance state

- item 8: Passed;
- item 9 business semantics: Passed;
- item 9 FIX-11 picker layout/identification: Passed;
- FIX-12 picker quick filter: Passed;
- item 10 required-group validation: Passed as expected behavior;
- item 10 successful explicit current-Catalogue reconfiguration: Passed, including expected total `26,01 €`.

M05 as a whole remains under manual acceptance because later payment/lifecycle/dashboard items are still pending. PR #10 remains open/unmerged. M06 remains not authorized.
