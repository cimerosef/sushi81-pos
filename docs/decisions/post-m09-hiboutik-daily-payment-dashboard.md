# Decision — Post-M09 Hiboutik daily payment dashboard

**Status:** Approved — post-freeze V1 amendment  
**Date:** 2026-09-16  
**Scope:** Independent post-M09 enhancement before M10  
**Owner decision source:** project-owner approval recorded for GitHub Issue #18 and explicit implementation authorization on 2026-09-16

## Decision

Add exactly two passive read-only values to the top Caisse daily dashboard:

- `Hiboutik CB aujourd'hui`;
- `Hiboutik Espèce aujourd'hui`.

This enhancement does not create a Hiboutik turnover metric, order count, discrepancy metric, special status, dedicated lifecycle, emergency-order UI or reconciliation subsystem.

## Business semantics

For business date D, each value is derived only from persisted `PaymentAdjustment` rows whose parent order has system-controlled `source_type = HIBOUTIK_PASTE` and whose current order status is not `CANCELLED`.

Date attribution uses the existing payment-adjustment effective business date semantics. `recorded_at`, order creation date and planned fulfilment date do not substitute for the effective payment date.

The values are signed dated sums:

- `CB` deltas on D -> `Hiboutik CB aujourd'hui`;
- `ESPECE` deltas on D -> `Hiboutik Espèce aujourd'hui`.

Positive, negative and correction deltas therefore affect the selected business date exactly as they already do for ordinary daily payment accounting.

Cancelled Hiboutik orders are excluded from both values even though their historical `PaymentAdjustment` facts remain durably retained.

## Anti-double-counting boundary

The existing ordinary POS-originated dashboard remains unchanged. Hiboutik daily values must not be included in:

- `CA opérationnel aujourd'hui`;
- `Encaissé aujourd'hui`;
- ordinary `CB aujourd'hui`;
- ordinary `Espèce aujourd'hui`;
- the ordinary POS CB amount to be represented in Hiboutik;
- `Gestion SUSHI 81` export.

The two new values are a separate passive source-specific view of already-retained payment facts, not new turnover and not a new accounting authority.

## UI and localization

The two values appear in the existing top Caisse operational dashboard and reuse its refresh/business-date behavior.

They are display-only. They must not add a write action, drill-down workflow or Hiboutik-specific payment editor.

French and Simplified Chinese labels are required. Business data is never translated by the UI language switch.

A non-authoritative/read-only paired device may display these values from the committed local data it currently holds. The existing non-authoritative/stale warning remains the authority signal; this enhancement adds no write path and no new authority-transfer behavior.

## Data/architecture consequence

No new durable field, table, schema migration, payment entity, source type or background integration is required or approved.

The existing `Order.source_type`, `Order.status` and `PaymentAdjustment` facts are sufficient. Prefer the smallest extension of the existing `OrderOperationalSummary` / `IOrderLifecycleStore.GetOperationalSummaryAsync` / Caisse dashboard presentation seam.

## Scope placement

This is an independent post-M09 enhancement. It is not M10 and not M11.

M10 remains Catalogue `.xlsx` import/export. M11 remains the Gestion intermediate export milestone. Neither is authorized by this decision.