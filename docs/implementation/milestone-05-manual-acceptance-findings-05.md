# M05 manual Windows/WPF acceptance findings — batch 05

**Milestone:** M05 — Lifecycle, payments, search and operational dashboard  
**Date:** 2026-09-06  
**Performed by:** project owner  
**PR:** #10 — `M05: lifecycle payments search and operational dashboard`  
**Production-code baseline under test:** `6694d1660074cb9443295c1f5e537fc8ca192b4a`  
**Overall result:** Partial — snapshot quantity semantics and current-Catalogue add semantics passed; the add-current-product picker exposed an operator-UI defect. Explicit option reconfiguration still needs its successful-path completion after the owner intentionally created a required-group validation failure.

## Acceptance item 8 — quantity-only historical snapshot

Passed manually.

After changing the current Catalogue price, modifying only the quantity of an existing historical order line preserved the sale-time product price/snapshot rather than adopting the current Catalogue value.

## Acceptance item 9 — add a new current-Catalogue line

Business behavior passed manually: the newly added line used current Catalogue data as required.

However the `CatalogueProductPickerDialog` exposed an operator-UI defect:

- the list shows only product `Name`, omitting the operator-facing product `Code`;
- the current DockPanel child order / LastChildFill behavior stretches the action area into two oversized vertical `Annuler` / `Ajouter` controls;
- `Annuler` has no distinct business behavior from closing the picker window with the title-bar close button;
- the picker should be compact, readable and consistent with the rest of the application.

For operator identification the picker must show at minimum `Product.Code + Product.Name`. If the existing category short-code information is made available to the picker, it may also display the category short code (e.g. `P`) as a compact prefix; there is no second independent product-code field in the approved V1 model.

Preferred simple interaction:

- compact product list occupying the dialog body;
- one normal-sized `Ajouter` button at bottom-right;
- double-click remains a valid accept action;
- title-bar close cancels; a redundant `Annuler` button is not required;
- FR/zh-CN and supported small-window rendering must remain usable.

## Acceptance item 10 — explicit current-Catalogue option reconfiguration

Not yet fully completed, but the previously reported “lost historical preselection” defect was a false finding and is withdrawn.

Observed historical order line `TST002` previously contained:

- `Sans accompagnement (-1,00 €)`;
- `Sauce premium (+1,00 €)`.

The current Catalogue was edited so that `Sauce premium` became `+1,50 €`.

In the explicit reconfiguration dialog, the project owner **intentionally unchecked** `Sans accompagnement (-1,00 €)`. Therefore the subsequent localized validation:

`La sélection du groupe « Suppléments » est invalide.`

is expected behavior, because the current `Suppléments` group is required multi-select `1-2` and the owner deliberately left that required group with zero selected choices.

There is no evidence from this observation of a preselection defect, silent option loss, or option-ID regression. Do not change option-preselection semantics on the basis of this test.

The remaining manual step for item 10 is simply to complete a **valid explicit reconfiguration** using the current Catalogue configuration (including the changed `Sauce premium +1,50 €`), save the order, and verify that the resulting saved line/total uses current Catalogue option values while unrelated historical snapshot behavior remains protected.

## Acceptance state

- item 8: Passed;
- item 9 business semantics: Passed;
- item 9 picker UI: Failed / remediation required;
- item 10 required-group validation: Passed as expected behavior;
- item 10 successful explicit-reconfiguration path: Pending manual completion after picker UI remediation.

Do not mark M05 Passed. PR #10 remains open/unmerged. M06 remains not authorized.
