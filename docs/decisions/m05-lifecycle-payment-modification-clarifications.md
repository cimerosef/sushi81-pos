# M05 lifecycle/payment/modification clarifications

**Status:** Approved  
**Decision date:** 2026-09-02  
**Approved by:** project owner  
**Applies to:** M05 — Lifecycle, payments, search and operational dashboard

This record freezes the three material M05 decisions that remained open after M04 merged. It supplements the frozen V1 baseline and must be incorporated consistently into the M05 implementation contract, affected baseline/acceptance documents and tests.

## D1 — Human-readable order reference

Every durable business order retains its existing opaque `Order.Id` GUID as the technical primary identity. M05 additionally introduces one immutable operator-facing order reference.

### Format

The reference format is:

`YYYYMMDD-NNN`

Example:

`20260902-017`

Rules:

1. `YYYYMMDD` is the `IBusinessClock.BusinessDate` on which the order is first durably confirmed/created.
2. It is not the planned fulfilment date and is not recomputed after later modification.
3. `NNN` is a per-business-date sequence allocated atomically with order creation.
4. Display is zero-padded to at least three digits (`001`, `002`, ...).
5. The sequence must not fail merely because a business date exceeds 999 orders; values may naturally grow to `1000`, `1001`, etc.
6. The operator-facing reference is immutable for the lifetime of the order, including later planned-date changes, Closed/Open changes and cancellation.
7. Reusing telephone/address/comment from an older order creates a new order and therefore a new reference.
8. Cancelled orders retain their reference.
9. Internal relations continue to use the GUID; the human reference is a unique business-facing lookup/display key, not a replacement technical foreign-key model.
10. Normal operator UI should prefer the human reference. The GUID may remain available for diagnostics/technical support but must not be the identity staff need to read, remember or dictate in ordinary operation.

### Migration of pre-M05 M04 orders

Existing durable orders that predate this field must receive deterministic immutable references during the additive M05 migration.

For each order:

- derive the reference date from `created_at` converted into the configured business timezone/business date semantics;
- assign the per-date sequence in deterministic `created_at` order;
- use the stable opaque GUID as tie-breaker when timestamps are equal;
- never derive the date portion from planned fulfilment date;
- preserve every existing Order/OrderItem/Adjustment/Tax value.

After migration, backfilled references are permanent and participate in the same uniqueness/search rules as newly created references.

## D2 — Existing-order snapshot preservation versus current Catalogue

Opening or saving an existing order must never silently rebuild historical business values from the current Catalogue.

### Default preservation rule

When an existing non-cancelled order is opened for modification, the editable draft starts from its persisted order/item/adjustment/tax snapshots.

The following changes must preserve existing sale-time Product/Category/price/VAT/discount-eligibility/adjustment snapshot authority unless another explicitly price-affecting action below requires recalculation:

- telephone;
- delivery address;
- comment;
- planned fulfilment date/time;
- other non-price-affecting order metadata.

Changing only those fields must never cause current Catalogue changes to rewrite historical line values.

### Quantity and removal of an existing line

Changing quantity on an existing snapshotted line continues to use that line's already-persisted Product base price, Product VAT, discount eligibility and snapshotted selected/custom adjustments. The pricing engine recalculates the line/order from those snapshots for the new quantity.

Removing an existing line removes that line from the newly saved order state.

### Adding a line

A newly added line uses the current active Catalogue exactly as ordinary new-order entry does and creates a new current sale-time snapshot when the modification is saved.

### Explicit option reconfiguration

The historical snapshot contains the selected options/adjustments but is not a complete historical copy of every option that was available at the original sale time. Therefore M05 must not fabricate an old option menu from current Catalogue data.

If the operator explicitly chooses to reconfigure an existing line's structured options:

- use the current Catalogue Product/Option configuration as the authoritative source for that explicit reconfiguration;
- clearly treat this as a deliberate replacement of the Catalogue-dependent configuration for that line;
- validate current active option choices using the ordinary option rules;
- save the resulting current configuration as the refreshed snapshot for that line.

If the source Product no longer exists or is inactive, the historical line remains readable and may still have its quantity corrected or be removed, but current-Catalogue option reconfiguration is unavailable until the operator replaces/adds an appropriate current Product line explicitly.

### Pricing/settings interaction

When a saved modification is price-affecting, calculate the resulting order using the approved pricing engine and the current `BusinessSettings`, while retaining the snapshot-preservation rules above for unchanged historical line components.

Price-affecting modifications clear an active manual total override under the existing V1 rule. Non-price-affecting edits preserve the manual override and existing authoritative tax snapshot.

This decision does not create an operator-visible revision history. The latest successfully saved state becomes the current committed order snapshot for later viewing/printing/reporting/export.

## D3 — Cumulative payment amounts cannot be negative

The V1 payment-adjustment ledger may contain positive, zero-omitted and negative signed deltas because corrections can reduce a previously recorded cumulative amount.

However, the operator-facing/current cumulative values are constrained independently:

- cumulative CB must be `>= €0.00`;
- cumulative Espèce must be `>= €0.00`.

Examples:

- CB `€20 -> €10` => valid signed delta `-€10`;
- CB `€20 -> €0` => valid signed delta `-€20`;
- CB `€20 -> -€5` => rejected before persistence.

A negative cumulative received-payment amount would represent a refund/credit workflow that V1 does not define. This rule therefore preserves signed correction history without inventing a new refund business model.

## Consequences

M05 implementation must at minimum:

- add the immutable operator reference and deterministic migration/backfill;
- support exact reference lookup in the live/current database;
- preserve GUID identity and same-order modification semantics;
- implement explicit snapshot-preserving existing-order editing;
- use current Catalogue only for newly added lines or explicit current reconfiguration;
- enforce non-negative cumulative CB/Espèce while persisting signed dated deltas;
- add automated migration/domain/application/integration/WPF evidence for all three decisions.

No decision in this record authorizes M06+ recovery enforcement, M08 printing adapter implementation, M09 Hiboutik parsing, M10/M11 Excel workflows, M12 archives or M13 packaging.