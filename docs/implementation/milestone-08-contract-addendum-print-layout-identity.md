# M08 contract addendum — owner print layout and receipt identity

**Status:** Prepared/controlling addendum — implementation still NOT AUTHORIZED  
**Date:** 2026-09-12  
**Milestone:** M08 — Printing and reprinting  
**Applies with:** `milestone-08-printing-reprinting.md`  
**Owner decision:** `../decisions/m08-print-layout-and-receipt-identity.md`

## 1. Superseding effect

The owner-approved decision `docs/decisions/m08-print-layout-and-receipt-identity.md` resolves the preparation blocker described in section 3.1 of the original M08 contract.

Therefore:

- M08-D1 is resolved;
- customer receipt identity must use the exact approved Sushi 81 values from that decision;
- the identity is authoritative SQLite business configuration, not local printer configuration;
- an additive business-database migration/settings extension is now in M08 scope;
- the owner-selected kitchen/customer visual targets are mandatory acceptance references;
- the exact named Hiboutik font is not a blocker because the reference appearance is printer-resident; actual printed visual similarity is verified during owner acceptance.

Any original M08-contract wording that says receipt identity is still unresolved is superseded by this addendum.

## 2. Additional implementation requirements

Codex must implement the following together with the base M08 contract:

1. persist the approved receipt identity values in the authoritative SQLite business lineage;
2. use those committed settings when building customer print models;
3. protect identity editing with the existing write-authority guard;
4. allow non-authoritative devices to read/use their locally available committed identity for printing while remaining unable to edit it;
5. preserve printer queue selection as local technical configuration;
6. implement kitchen layout composition following owner-selected page-1 structure;
7. implement customer layout composition following owner-selected page-2 Hiboutik-style structure without Hiboutik branding/attribution;
8. do not add a customer-name data model solely to imitate the reference;
9. implement deterministic wrapping for long product/option/address/comment text;
10. calibrate actual paper output against the owner reference during Windows/hardware acceptance.

## 3. Migration and settings scope amendment

The base contract's statement that no SQLite migration is needed **solely for printer queue configuration** remains correct.

However, the newly owner-approved receipt identity is business configuration and therefore requires whichever additive SQLite schema/settings migration is appropriate for the chosen physical representation.

Migration must:

- preserve all existing orders/payments/catalogue/business settings;
- seed the exact approved Sushi 81 identity values;
- be restart-safe and covered by migration tests;
- remain inside normal business-revision/recovery semantics when later edited.

## 4. Visual acceptance amendment

The final owner checklist must compare real printed output against the two selected reference pages.

Kitchen acceptance must confirm:

- page-1 compact `CUISINE` structure;
- phone/address/comment/order information between the two upper dashed separators;
- compact quantity/code/name product lines;
- options clearly attached to the parent product;
- prominent total;
- future/reprint/cancel markings remain operationally obvious.

Customer acceptance must confirm:

- page-2 centered Sushi 81 identity/ticket structure;
- dense monospaced thermal-receipt appearance;
- item/price alignment and wrapping are practical;
- HT/VAT/Total hierarchy resembles the reference;
- total is strongly prominent;
- payment information is readable;
- no false Hiboutik branding/footer is printed;
- visual typeface/spacing is sufficiently close to the Hiboutik reference on the actual printer.

## 5. Governance

This addendum does not authorize implementation.

Issue #4 remains CLOSED until separate project-owner M08 implementation approval is recorded and the dedicated M08 branch/PR/mailbox/handoff prerequisites are complete.
