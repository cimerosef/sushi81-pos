# Reprint marking

**Status:** Approved — Phase 4 decision  
**Date:** 2026-08-27  
**Applies to:** Kitchen-ticket and customer-ticket reprints

## Decision

A document generated through an explicit reprint action must be visibly distinguishable from the first automatic print generated when an order is initially confirmed.

Approved rules:

1. A reprinted **kitchen ticket** must visibly display `RÉIMPRESSION`.
2. A reprinted **customer ticket** must visibly display `DUPLICATA`.
3. The marking indicates only that the physical/document output is a reprint. It does not create a new order, change the order ID, change payment state, or change any business content.
4. Reprinted documents are generated from the latest committed order state available to the device, subject to the already-approved non-authoritative-device printing rule.
5. If the order is Cancelled, the mandatory prominent `ANNULÉ` marking still applies in addition to the reprint marking.
6. Exact typography, placement, border and sizing are print-layout implementation choices, provided that the marking is easy to see and cannot reasonably be confused with ordinary business content.

## Rationale

The kitchen marking reduces the risk that staff interpret a second physical ticket as a second/new order. The customer marking clearly identifies a replacement copy without introducing any new order or accounting semantics.
