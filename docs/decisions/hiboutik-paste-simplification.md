# Hiboutik paste-import simplification

**Status:** Approved — Phase 4 decision  
**Date:** 2026-08-27  
**Applies to:** Hiboutik paste import, order UI, data model and downstream export/statistics

## Context

Earlier Phase 1–3 drafts treated a Hiboutik fallback paste import as a special operator-facing emergency-order subtype with dedicated reconciliation information, original-Hiboutik amount storage, discrepancy alerts and a visible emergency-order count.

That design was intentionally simplified before the V1 Specification freeze.

The actual operational need is only to recover quickly from Hiboutik server-side printing failure by converting pasted Hiboutik order text into a normal Sushi81 POS product order that can be reviewed, saved and printed.

## Decision

A Hiboutik paste import behaves like a normal order from the operator's point of view.

Approved rules:

1. The paste-import function exists only as a fast order-creation aid.
2. It parses Hiboutik order text and pre-populates the ordinary Sushi81 POS order-entry model.
3. After parsing, the operator uses the same normal order screen and the same normal edit/confirm/print workflow as for a manually created order.
4. There is no dedicated emergency-order detail screen, special visual style, dashboard counter, discrepancy panel or Hiboutik-specific payment/reconciliation UI.
5. There is no dedicated Hiboutik order-number field in V1. If the operator wants to retain the Hiboutik order number/reference, it is written manually in the normal order comment field.
6. V1 does not preserve an immutable `hiboutik_original_total_ttc` field or a dedicated `EmergencyImportDetail` business entity.
7. The parser may extract useful ordinary order facts such as products, quantities, fulfilment mode/date/time, address, telephone and comments when the source text provides them, but the resulting values are ordinary editable order fields before confirmation.
8. The importer does not create a committed order until the operator confirms the normal order screen.
9. Persistence occurs before printing, so print failure cannot destroy an already confirmed order.

## Minimal hidden source marker

The application retains one non-user-facing technical source discriminator indicating that an order was created through the Hiboutik paste-import entry point.

This marker exists only to preserve the accounting/export anti-double-counting boundary and must not change the normal order UI or operator workflow.

Because the underlying web order already exists in Hiboutik, a pasted Hiboutik order is excluded automatically from:

- ordinary POS-originated turnover totals;
- ordinary POS-originated received-payment totals;
- the amount that must newly be represented/entered in Hiboutik;
- export to `Gestion SUSHI 81.xlsm`.

The operator is not asked to classify, reconcile or manage the source marker.

## Superseded earlier requirements

The following former requirements are **not part of the V1 Specification**:

- visible emergency-order marker/style;
- current-day emergency-order dashboard count;
- dedicated Hiboutik discrepancy warnings;
- separate preservation/display of original Hiboutik total versus POS operational total;
- Hiboutik-specific reconciliation workflow;
- dedicated Hiboutik source-reference field;
- one-to-one `EmergencyImportDetail` business entity.

## Phase 5 incorporation

The final V1 consistency pass has folded this simplification directly into the relevant baselines, including:

- `docs/product-requirements.md`;
- `docs/order-lifecycle.md`;
- `docs/data-model.md`;
- `docs/storage-strategy.md` where source/archive semantics are relevant;
- `docs/paste-order-import.md`;
- `docs/printing.md`;
- `docs/export.md`;
- `docs/acceptance-criteria.md`.

Implementation therefore does not need to resolve this simplification by document chronology. The frozen target model is the ordinary Order model plus the hidden anti-double-counting source discriminator only.

## Rationale

The simplified design matches the real operational purpose and the project's standing priority order:

**reliability > simplicity > maintainability > operational clarity > technical novelty.**

It avoids building a second order subtype and reconciliation workflow for a rare fallback scenario while still preventing double counting in downstream POS-only statistics and exports.
