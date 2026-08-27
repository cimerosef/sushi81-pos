# Hiboutik paste-import simplification

**Status:** Approved — Phase 4 decision  
**Date:** 2026-08-27  
**Applies to:** Hiboutik emergency paste import, order UI, data model and downstream export/statistics

## Context

Earlier Phase 1–3 drafts treated a Hiboutik emergency paste import as a special operator-facing emergency-order subtype with dedicated reconciliation information, original-Hiboutik amount storage, discrepancy alerts and a visible emergency-order count.

That design is now intentionally simplified.

The actual operational need is only to recover quickly from Hiboutik server-side printing failure by converting the pasted Hiboutik order text into a normal Sushi81 POS product order that can be reviewed, saved and printed.

## Decision

A Hiboutik paste import must behave like a normal order from the operator's point of view.

Approved rules:

1. The paste-import function exists only as a fast order-creation aid.
2. It parses the Hiboutik order text and pre-populates the ordinary Sushi81 POS order-entry model.
3. After parsing, the operator works in the same normal order screen and uses the same normal edit/confirm/print workflow as for any manually created order.
4. There is no dedicated emergency-order detail screen, special visual style, dashboard counter, discrepancy panel or Hiboutik-specific payment/reconciliation UI.
5. There is no dedicated Hiboutik order-number field in V1. If the operator wants to retain the Hiboutik order number/reference, it is written manually in the normal order comment field.
6. V1 does not require a separately preserved immutable `hiboutik_original_total_ttc` field or a dedicated `EmergencyImportDetail` business entity.
7. The parser may extract useful ordinary order facts such as products, quantities, fulfilment mode/date/time, address, telephone and comments when the source text provides them, but the resulting values become ordinary editable order fields before confirmation.
8. The importer must not create a committed order until the operator confirms the normal order screen.
9. Persistence still occurs before printing, so print failure cannot destroy an already confirmed order.

## Minimal hidden source marker

The application may retain one non-user-facing technical source discriminator indicating that an order was created through the Hiboutik paste-import entry point.

This marker exists only where required to preserve the already-established accounting/export boundary and must not change the normal order UI or operator workflow.

In particular, because the underlying web order already exists in Hiboutik, a pasted Hiboutik order must continue to be excluded from:

- ordinary POS-originated turnover totals;
- ordinary POS-originated received-payment totals;
- the amount that must newly be represented/entered in Hiboutik;
- export to `Gestion SUSHI 81.xlsm`.

This exclusion is technical and automatic. The operator is not asked to classify, reconcile or manage the source marker.

## Superseded earlier requirements

This decision supersedes earlier requirements that demanded any of the following for Hiboutik paste-imported orders:

- a visible emergency-order marker/style;
- a current-day emergency-order dashboard count;
- dedicated Hiboutik discrepancy warnings;
- separate preservation/display of an original Hiboutik total versus a POS operational total;
- a Hiboutik-specific reconciliation workflow;
- a dedicated Hiboutik source-reference field;
- a one-to-one `EmergencyImportDetail` business entity.

Where older approved documents still contain those requirements, this later decision takes precedence until the final V1 consistency pass folds the simplification directly into every baseline document.

## Rationale

The simplified design matches the real operational purpose and the project's standing priority order:

**reliability > simplicity > maintainability > operational clarity > technical novelty.**

It avoids building a second order subtype and reconciliation workflow for a rare fallback scenario while still preventing double counting in downstream POS-only statistics and exports.
