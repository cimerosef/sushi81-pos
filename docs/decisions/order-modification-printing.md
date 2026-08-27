# Order modification printing

**Status:** Approved — Phase 4 decision  
**Date:** 2026-08-27  
**Applies to:** Saved modifications of existing orders and ticket reprinting

## Decision

Saving a modification to an existing order does **not** automatically print either the kitchen ticket or the customer ticket.

After the modification is successfully saved, the operator may choose independently to:

- reprint the kitchen ticket;
- reprint the customer ticket;
- reprint neither.

The normal new-order rule remains unchanged: initial confirmation of a newly created order automatically prints both the kitchen and customer tickets after persistence succeeds.

## Rationale

Automatic reprinting after every modification can create operational confusion, especially in the kitchen where a second ticket may be mistaken for a new order.

Not every modification requires a kitchen reprint: telephone, address, payment or other non-preparation changes may not justify another kitchen ticket.

Operator-controlled reprinting preserves flexibility while avoiding accidental duplicate preparation.

## Safety boundary

A reprint always uses the latest successfully committed order state.

Unsaved edits are never printed as authoritative business data.

This decision does not alter the order lifecycle, order ID or persistence rules.
