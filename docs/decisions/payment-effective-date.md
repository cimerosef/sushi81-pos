# Payment effective-date attribution

**Status:** Approved — Phase 5 decision  
**Date:** 2026-08-27  
**Applies to:** payment correction/back-entry, daily received-payment summaries, `PaymentAdjustment.effective_at`

## Context

Sushi81 POS records current cumulative CB and Espèce amounts while internally retaining signed dated adjustments so daily received-payment summaries represent money actually received on each business date.

A payment may sometimes be entered or corrected in the POS later than the date on which the money was actually received. The specification therefore needs to distinguish business attribution from the technical time at which the correction is recorded.

## Decision

V1 uses the following rule:

1. Every payment adjustment has an **effective payment date/time** used for business reporting and a separate **recorded timestamp** used for technical traceability.
2. In the normal payment-entry workflow, the effective payment date defaults to the current business date so ordinary same-day payment entry requires no extra step.
3. When entering or correcting a payment after the fact, the operator may change the effective payment date to the date on which the money was actually received.
4. Daily total received, CB and Espèce summaries are attributed using the effective payment date, not the later technical recording date.
5. `recorded_at` is application-generated and preserves when the adjustment was actually persisted; changing the effective payment date must not rewrite or falsify that technical timestamp.
6. The operator-facing workflow does not need to expose the internal adjustment ledger. It only needs a practical way to specify the effective date when the default current date is not correct.
7. Changing the effective date affects reporting attribution only; it does not create a second payment, change the stable order ID or alter the external bank-terminal transaction.

### Example

A €20 CB payment was actually received on 2026-08-26 but was forgotten and entered in the POS on 2026-08-27.

The operator records the payment with effective date `2026-08-26`.

Result:

- the €20 contributes to the 2026-08-26 CB/received-payment summary;
- `recorded_at` remains 2026-08-27;
- the current cumulative CB amount on the order becomes €20;
- no duplicate payment is created.

## Rationale

This preserves the approved meaning of the daily summaries: they describe when money was actually received, while still keeping ordinary same-day entry fast and preserving technical traceability of later corrections.

## Consequences

- `order-lifecycle.md` must state that the effective date defaults to today but is operator-adjustable for back-entry/correction.
- `data-model.md` must treat `effective_at` and `recorded_at` as distinct required facts rather than leaving back-dating behavior as an implementation choice.
- `acceptance-criteria.md` must verify both same-day default behavior and back-dated attribution.
