# Export eligibility

**Status:** Approved — Phase 4 decision  
**Date:** 2026-08-27  
**Applies to:** Export from Sushi81 POS to `Gestion SUSHI 81.xlsm`

## Decision

Only orders satisfying **all** of the following conditions are eligible for the normal management export:

1. the order is POS-originated rather than Hiboutik paste-created;
2. the order is not Cancelled;
3. the order is fully settled under the approved payment rules;
4. the lifecycle status is `Closed`.

Accordingly:

- `Open` unpaid orders are not exported;
- `Open` partially paid orders are not exported;
- `Closed` fully settled ordinary POS orders are eligible;
- `Cancelled` orders are not exported as positive sales;
- Hiboutik paste-created orders are always excluded.

## Rationale

`Gestion SUSHI 81.xlsm` should receive only finalized ordinary POS sales rather than provisional financial states.

This prevents unpaid or partially paid orders from entering management history before their final payment outcome is known and reduces the number of correction cases created solely by exporting unfinished orders.

This eligibility rule does not delete or otherwise alter the POS order. Export remains an integration/reporting operation rather than an order lifecycle transition.
