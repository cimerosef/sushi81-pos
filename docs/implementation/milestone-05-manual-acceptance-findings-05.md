# M05 manual Windows/WPF acceptance findings — batch 05

**Milestone:** M05 — Lifecycle, payments, search and operational dashboard  
**Date:** 2026-09-06  
**Performed by:** project owner  
**PR:** #10 — `M05: lifecycle payments search and operational dashboard`  
**Production-code baseline under test:** `9d116227ccb9cf9ef1c6e14e20dfd70404d7580a`  
**Overall result:** Passed for acceptance items 8–10. A separate operator-usability follow-up remains for adding quick filtering to the current-Catalogue product picker; this is not a failure of items 8–10.

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

The project owner requested one further usability improvement for this picker: add a quick partial filter similar to the Caisse product search so an operator can reduce the list by typing a product-code fragment and, preferably, name text as well. This is a follow-up enhancement rather than an item-9 acceptance failure.

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
- item 10 required-group validation: Passed as expected behavior;
- item 10 successful explicit current-Catalogue reconfiguration: Passed, including expected total `26,01 €`.

M05 as a whole remains under manual acceptance because later payment/lifecycle/dashboard items are still pending. PR #10 remains open/unmerged. M06 remains not authorized.
