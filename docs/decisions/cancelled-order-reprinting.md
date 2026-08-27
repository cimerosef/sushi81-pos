# Cancelled-order reprinting

**Status:** Approved — Phase 4 decision  
**Date:** 2026-08-27  
**Applies to:** kitchen-ticket and customer-ticket reprinting for Cancelled orders

## Decision

Cancelled orders remain viewable and may still be printed/reprinted.

For any kitchen or customer document generated from an order whose current status is `CANCELLED`:

- printing/reprinting remains available;
- the document must carry a very prominent `ANNULÉ` indication;
- the cancellation marking must be visually difficult to miss and must not allow the document to be mistaken for an active order;
- both kitchen and customer documents are subject to this rule;
- the underlying order remains Cancelled and printing does not restore, duplicate or reactivate it.

The exact typography, border, size and placement are implementation/layout details, provided that the cancellation state is operationally obvious.

## Rationale

Keeping reprint capability supports historical verification and practical operational use, while the mandatory prominent `ANNULÉ` marking prevents kitchen or counter staff from treating a cancelled order as active.
