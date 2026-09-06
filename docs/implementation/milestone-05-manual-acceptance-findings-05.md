# M05 manual Windows/WPF acceptance findings — batch 05

**Milestone:** M05 — Lifecycle, payments, search and operational dashboard  
**Date:** 2026-09-06  
**Performed by:** project owner  
**PR:** #10 — `M05: lifecycle payments search and operational dashboard`  
**Production-code baseline under test:** `6694d1660074cb9443295c1f5e537fc8ca192b4a`  
**Overall result:** Partial — snapshot quantity semantics passed; current-Catalogue add/reconfiguration workflow exposed one picker UI defect and one explicit-reconfiguration preselection defect.

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

For operator identification the picker must show at minimum `Product.Code + Product.Name`. If the existing category short-code information is made available to the picker, it should also display the category short code (e.g. `P`) as a compact prefix; there is no second independent product-code field in the approved V1 model.

Preferred simple interaction:

- compact product list occupying the dialog body;
- one normal-sized `Ajouter` button at bottom-right;
- double-click remains a valid accept action;
- title-bar close cancels; a redundant `Annuler` button is not required;
- FR/zh-CN and supported small-window rendering must remain usable.

## Acceptance item 10 — explicit current-Catalogue option reconfiguration

Not yet passed.

Observed historical order line `TST002` previously contained the option selections:

- `Sans accompagnement (-1,00 €)`;
- `Sauce premium (+1,00 €)`.

The current Catalogue was then edited so that `Sauce premium` became `+1,50 €`. In the explicit reconfiguration dialog:

- the current `Sauce premium (+1,50 €)` value appeared and remained selected;
- `Sans accompagnement (-1,00 €)` appeared but was no longer selected;
- the `Suppléments` group is current-Catalogue required multi-select `1-2`, so pressing Add produced the localized validation `La sélection du groupe « Suppléments » est invalide.`

The validation itself is correct for a required group with zero selected choices. The defect is the lost compatible historical preselection: editing another current option's price should not silently unselect an unchanged historical option whose identity remains available in the current Catalogue.

Current production code already intends to seed `OptionSelectionDialog` from historical `SourceOptionId` values and `ProductEditorDialog` preserves option IDs when ordinary option fields such as price are edited. Therefore this scenario requires diagnosis and deterministic regression coverage.

Required behavior:

1. Explicit reconfiguration deliberately loads current Catalogue option prices/availability.
2. Historical option selections whose IDs still exist in current Catalogue remain preselected when the dialog opens.
3. A current price edit for one option must not silently unselect another unchanged historical option.
4. If a historical selected option truly no longer exists/is unavailable, the operator receives a clear localized explanation before facts are silently dropped; the historical persisted line remains unchanged unless a valid explicit reconfiguration is confirmed and saved.
5. Required-group validation remains enforced after the operator's actual current selections are known.
6. No real business database access or mutation is required for diagnosis; use synthetic deterministic snapshots/current Catalogue data.

## Acceptance state

- item 8: Passed;
- item 9 business semantics: Passed;
- item 9 picker UI: Failed / remediation required;
- item 10: Partial / remediation required before final acceptance.

Do not mark M05 Passed. PR #10 remains open/unmerged. M06 remains not authorized.
