# Acceptance criteria amendment — Post-M09 Hiboutik daily payment dashboard

**Status:** Approved — V1 acceptance amendment  
**Date:** 2026-09-16  
**Applies to:** AC-LIFE-006, AC-HIB-008 and new AC-HIB-010  
**Decision source:** `docs/decisions/post-m09-hiboutik-daily-payment-dashboard.md`

This amendment adds a narrow passive Hiboutik payment view while preserving the existing ordinary POS-originated daily reporting and anti-double-counting boundary.

## AC-LIFE-006 clarification — ordinary POS received-payment summary remains unchanged

The existing main-screen `Encaissé aujourd'hui`, ordinary `CB aujourd'hui` and ordinary `Espèce aujourd'hui` values continue to include only ordinary POS-originated non-Cancelled orders and continue to use signed `PaymentAdjustment` deltas attributed by effective business date.

Hiboutik paste-created payment adjustments must not enter those ordinary POS values.

## AC-HIB-008 clarification — passive source-specific daily payment values are allowed

`HIBOUTIK_PASTE` remains system-controlled and non-editable. It continues to enforce the existing anti-double-counting exclusions.

The source discriminator may additionally be used for exactly the passive daily payment values defined by AC-HIB-010. This does not create a Hiboutik emergency-order count, turnover metric, discrepancy view, dedicated lifecycle, reconciliation state or payment workflow.

## AC-HIB-010 — Hiboutik daily CB/Espèce dashboard

**Given** business date D and persisted payment adjustments,  
**when** the top Caisse daily dashboard is refreshed,  
**then** it displays exactly these two additional passive values:

- `Hiboutik CB aujourd'hui`;
- `Hiboutik Espèce aujourd'hui`.

For each value:

- include only adjustments whose parent order has `source_type = HIBOUTIK_PASTE`;
- exclude parent orders whose current status is `CANCELLED`;
- use the payment adjustment effective business date for D;
- sum signed deltas, not current cumulative order values and not order totals;
- CB includes only `CB` bucket deltas;
- Espèce includes only `ESPECE` bucket deltas;
- `recorded_at`, order creation date and planned fulfilment date do not change the business-date attribution;
- positive and negative corrections contribute with their sign;
- Open and Closed non-Cancelled Hiboutik orders are both eligible.

The two values are strictly separate from ordinary POS `CA opérationnel`, `Encaissé`, `CB` and `Espèce` values. No Hiboutik total-received value, turnover value, order count or discrepancy value is required.

The values are read-only and use the existing dashboard refresh/current-business-date behavior. A non-authoritative/read-only device may display them from its locally available committed data without gaining a write path. Existing authority/staleness warnings remain sufficient.

French and Simplified Chinese labels are required. UI language switching must not change business data or amounts.

No schema migration/new durable field is required or approved for this criterion.

**Evidence:** focused SQLite reporting tests for source/status/date/bucket/signed-delta behavior; application/presentation tests for dashboard projection/refresh; localization resource parity test; Windows/WPF manual acceptance on the exact candidate.