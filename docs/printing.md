# Printing

**Status:** Draft — Phase 4 working design  
**Last updated:** 2026-08-27  
**Product:** Sushi81 POS  
**Purpose:** Specify reliable kitchen/customer printing, reprinting and print-failure behavior without changing the approved order lifecycle or storage model.

## 1. Scope

This document defines V1 printing behavior for Sushi81 POS.

It covers:

- kitchen ticket generation;
- customer ticket/receipt generation;
- automatic printing after order confirmation;
- selective reprinting;
- printing after later order/payment changes;
- future-order print information;
- retained/archive-order reprinting;
- printer configuration and failure handling;
- interaction with authoritative versus non-authoritative device state.

It does not redefine:

- pricing, discount or VAT business rules (`business-rules.md`);
- order/payment lifecycle (`order-lifecycle.md`);
- data retention/archive mechanics (`storage-strategy.md`);
- Hiboutik paste parsing (`paste-order-import.md`);
- downstream Excel export (`export.md`).

## 2. Authoritative baseline

The following requirements are already approved and are not reopened here:

1. A confirmed order is durably persisted **before** printing is attempted.
2. Print failure must never roll back, delete or corrupt an already committed order.
3. On confirmation of an ordinary or future order, V1 automatically generates/prints:
   - one kitchen ticket;
   - one customer/order ticket.
4. Kitchen and customer output can later be reprinted independently.
5. After payment is recorded or corrected, the customer ticket can be reprinted so current payment information can be reflected where required.
6. Any retained live order is reprintable.
7. Completed annual archive orders remain queryable and reprintable.
8. A Hiboutik paste-created order uses the same ordinary print workflow and must not have a separate Hiboutik/emergency print template solely because of its origin.
9. Printing must not recreate the recurring multi-second UI stalls of the VBA workflow.

## 3. Printing architecture — approved technical direction

Printing follows the Phase 3 architecture baseline:

- WPF/.NET print-document generation;
- standard Windows print spooler / `PrintQueue` / `PrintTicket` integration;
- no direct raw printer protocol as the primary V1 design;
- kitchen/customer print-data generation is separated from physical printer submission so content can be tested independently.

The application renders an immutable print model from the latest committed order snapshot and then submits the generated document to the configured Windows printer queue.

Database commit and print submission are separate operations.

## 4. Automatic print sequence after confirmation

The normal confirmation sequence is:

1. validate the order under the approved business/lifecycle rules;
2. allocate/retain the normal order ID;
3. commit the complete order transaction to SQLite;
4. generate the kitchen print model from that committed state;
5. generate the customer print model from that committed state;
6. submit the kitchen ticket to its configured queue;
7. submit the customer ticket to its configured queue;
8. report any print-submission failure clearly without undoing the order.

The UI must remain responsive while Windows spooling/physical printing proceeds.

A failure of one print job does not suppress the ability to submit/retry the other ticket.

## 5. Kitchen ticket — approved baseline content

The kitchen ticket is an operational preparation document rather than a tax/payment receipt.

It must include at least:

- Sushi81 POS order ID;
- order creation/confirmation time;
- fulfilment mode (`Retrait` / `Livraison`);
- planned fulfilment date;
- planned fulfilment time when present;
- telephone when present;
- delivery address when present;
- full current order comment;
- ordered products in saved line order;
- quantity;
- product code;
- product name;
- selected product options and their labels;
- custom option/adjustment descriptions where operationally relevant;
- current authoritative order total.

The core product line remains operationally recognizable in the existing style:

`quantity - code - name`

Selected options should appear immediately beneath or otherwise visually attached to the relevant product line so kitchen staff cannot confuse an option with another item.

The kitchen ticket does **not** need to show CB/Espèce payment composition merely for payment-recording purposes.

## 6. Future-order safety on printed tickets

Because future orders are printed when first confirmed, the planned fulfilment date/time must be visible from the structured fields rather than being recoverable only from comments.

For an order whose planned fulfilment date is later than the current business date, both generated documents must make that future fulfilment date sufficiently prominent that staff cannot reasonably mistake the printout for an ordinary same-day order.

The exact font size, border, bolding or placement is a UI/print-layout implementation choice so long as the result is operationally obvious.

When the same order is later reprinted on its fulfilment day, the printed date remains the order's actual planned fulfilment date; the print model does not replace it with the reprint date.

## 7. Customer ticket / receipt — approved baseline content

The customer ticket is the customer-facing order/receipt document.

It must include at least:

- Sushi 81 business identity/details required by the approved receipt format;
- Sushi81 POS order ID;
- order date/time;
- fulfilment mode;
- planned fulfilment date/time where relevant;
- item quantity and description;
- product/options shown in a readable customer-facing form;
- applicable item/adjustment price information;
- authoritative final TTC total;
- VAT breakdown from the persisted `OrderTaxBreakdown` snapshot;
- Sushi 81 VAT identification information required by the receipt format;
- current payment information where the approved ticket version requires it.

Historical and archived reprinting must use the saved order/item/tax snapshots rather than current catalogue prices or current VAT settings.

If `manual_total_override_active = true`, the printed VAT breakdown uses the already-approved single 10% VAT snapshot rather than reconstructing VAT from current product lines.

## 8. Payment-updated customer reprint

Recording or correcting CB/Espèce information does not require rebuilding historical product pricing.

When the operator requests a customer-ticket reprint after payment information has changed:

- the latest committed order contents are used;
- the latest committed authoritative order total is used;
- the latest cumulative CB/Espèce result is used where payment information is printed;
- the existing order ID is retained;
- the action does not create a new order.

Payment entry itself does not automatically require a second kitchen ticket.

## 9. Selective reprint

From an existing order, the operator must have separate actions equivalent to:

- **Reprint kitchen ticket**;
- **Reprint customer ticket**.

The operator is not forced to reprint both when only one is needed.

A reprint uses the latest committed business state of the selected order.

If the operator currently has unsaved edits open, the application must not silently print those uncommitted values as though they were authoritative. The operator must first save/confirm the modification or explicitly cancel it and print the persisted version.

## 10. Saved modifications and cancelled orders

All non-cancelled orders remain modifiable under `order-lifecycle.md`.

### 10.1 Saved modification — approved Phase 4 rule

Saving a modification to an existing order does **not** automatically print either document.

After the modification is successfully committed, the operator may independently choose to:

- reprint the kitchen ticket;
- reprint the customer ticket;
- reprint neither.

This applies regardless of whether the saved change affects products, quantities, options, fulfilment information, customer information, comments, total or payment information.

The application must make the reprint actions readily available after save, but it must not infer that a second kitchen/customer printout is required.

This avoids accidental duplicate kitchen preparation and keeps the risk judgment with the operator.

Any subsequent reprint uses the latest committed order state and the same order ID.

This rule is frozen in `docs/decisions/order-modification-printing.md`.

V1 does not preserve prior business revisions merely for printing.

### 10.2 Cancelled orders — approved Phase 4 rule

A cancelled order remains retained, viewable and printable/reprintable.

For any kitchen or customer document generated from an order whose current status is `CANCELLED`:

- printing/reprinting remains available;
- the document must carry a very prominent `ANNULÉ` indication;
- the marking must be visually difficult to miss so the ticket cannot reasonably be mistaken for an active order;
- both kitchen and customer documents follow the same cancellation-marking rule;
- printing does not reactivate, duplicate or otherwise change the cancelled order.

The exact typography, size, border and placement of `ANNULÉ` are layout implementation choices, provided that the cancellation state is operationally obvious.

This rule is frozen in `docs/decisions/cancelled-order-reprinting.md`.

## 11. Archived-order reprinting

Annual archive databases preserve the same logical order/item/tax snapshots required for historical printing.

When an archived year is explicitly opened/selected:

- the application may hydrate the archive into a local read-only cache as defined by `storage-strategy.md`;
- kitchen and customer documents are regenerated from the archived snapshots;
- current catalogue data must not rewrite or substitute historical product/price/VAT information;
- archive reprinting performs no write to the archive database.

## 12. Printer configuration

Printer configuration is local to each Windows installation/device rather than shared business data.

V1 supports configuration of:

- kitchen-ticket Windows printer queue;
- customer-ticket Windows printer queue.

Both may point to the same physical/Windows printer, matching the current Sushi 81 setup, but the configuration is kept logically separate so a later hardware change does not require changing the business model.

The application should show installed Windows printer queues and store stable local queue identification where practical.

If a previously configured queue is unavailable, the application must report that state clearly and allow the operator to choose another installed queue.

Printer selection is technical/local configuration and is not synchronized through the business database lineage.

## 13. Print failure and retry

The application distinguishes at least:

- document-generation failure;
- Windows print-queue submission failure;
- printer/queue unavailable status detectable by Windows.

For any detected failure:

- the committed order remains intact;
- the UI identifies which document failed: kitchen or customer;
- the operator can retry that document independently;
- retry does not create a new order or change the order ID;
- technical diagnostics may be written to application logs without logging unnecessary customer content.

The application is not required to prove that paper physically emerged from the printer if Windows has already accepted the job successfully; physical paper/jam/out-of-paper handling remains within the printer/Windows queue capabilities unless a concrete supported printer integration later provides a reliable status signal.

## 14. Print-model determinism and testing

Kitchen/customer document generation must be testable without a physical printer.

Automated tests should cover at least:

- Retrait and Livraison;
- same-day and future orders;
- telephone/address present and absent;
- multiple products and quantities;
- structured options and custom adjustments;
- normal mixed VAT breakdown;
- manual-total 10% VAT override;
- payment information before/after correction;
- saved modifications without automatic printing;
- cancelled orders with mandatory prominent `ANNULÉ` marking;
- archived order snapshots;
- long comments and long product/option text;
- printer submission failure without order loss.

The same committed order state must generate the same business print content regardless of which paired authoritative device renders it, except for local printer/page-driver mechanics.

## 15. Non-authoritative read-only device — approved Phase 4 rule

A paired device that is currently non-authoritative/read-only may still print and reprint both kitchen and customer tickets from the live-data copy available on that device.

The application must not hard-block printing merely because the device may hold stale data.

Instead:

- the UI must clearly indicate that the device is non-authoritative and that the displayed order may not be the latest version;
- the operator remains free to continue with the print/reprint action after seeing that state;
- the final risk judgment belongs to the operator;
- printing from the non-authoritative device does not create or imply an authority transfer, synchronization success or database write;
- the printed document is generated strictly from the committed order state actually available on that device;
- the application must not claim that freshness has been verified when it has not.

Completed annual archive orders remain printable as immutable historical records.

This rule is frozen in `docs/decisions/non-authoritative-device-printing.md`.

## 16. Remaining operator-facing printing decisions

The remaining business-printing details to check sequentially are now limited to:

1. **Reprint marking:** whether an ordinary reprinted kitchen/customer document should visibly say `DUPLICATA / REPRINT`.
2. **Final statutory/customer receipt wording:** only those wording/layout details that cannot be safely derived from the existing approved/current receipt baseline.

Pure layout, pagination, font sizing, wrapping and Windows print implementation choices are technical design decisions unless they alter the business information communicated.

## 17. Approval rule

This document remains **Draft — Phase 4 working design** until the remaining operator/business printing decisions are frozen.

Implementation must preserve the central invariant:

**commit first -> print second -> print failure never loses the order.**
