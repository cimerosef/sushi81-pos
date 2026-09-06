# M05 manual Windows/WPF acceptance findings — batch 06

**Milestone:** M05 — Lifecycle, payments, search and operational dashboard  
**Date:** 2026-09-06  
**Performed by:** project owner  
**PR:** #10 — `M05: lifecycle payments search and operational dashboard`  
**Production-code baseline under test:** `b51a4bbb02605ec7aea5a5eacb1160738a46b080`  
**Overall result:** Passed for the tested cumulative-payment and lifecycle paths below. Additional operator-usability improvements for quantity editing were requested and intentionally deferred until the current manual-acceptance round reaches a suitable consolidation point.

## Payment/lifecycle manual acceptance — passed

The project owner created fresh simple orders and manually verified the following behaviors:

1. **CB-only payment** — cumulative CB could be set to the exact order total with Espèce at zero; paid total/difference updated correctly; an exactly paid Open order remained Open until explicit Close.
2. **Downward payment correction on Closed order** — reducing cumulative CB from the exact paid amount to a smaller non-negative amount persisted as a valid correction and automatically returned the order from Closed to Open with the expected unpaid difference.
3. **Negative cumulative payment rejection** — attempting to set cumulative CB below zero was rejected; the previously committed non-negative cumulative amount remained authoritative.
4. **Equality-preserving Closed save** — after restoring exact payment and explicitly Closing again, a later non-price-affecting order modification that preserved exact payment kept the order Closed.
5. **Espèce-only payment** — cumulative Espèce equal to the order total behaved correctly; exactly paid remained Open until explicit Close.
6. **Mixed CB + Espèce payment** — split cumulative CB/Espèce equal to the order total behaved correctly; exactly paid remained Open until explicit Close.

These observations manually support the frozen M05 rules for cumulative non-negative payments, signed downward corrections, explicit Close, no auto-close on exact payment, automatic Closed-to-Open transition when a successful saved correction breaks exact reconciliation, and equality-preserving Closed saves.

## Deferred quantity-editing usability request

The project owner requested the following usability enhancement, but explicitly asked not to interrupt the current manual-acceptance round for implementation yet:

1. Wherever an operator edits an order-line quantity in a numeric text control, clicking/focusing the control should automatically select the existing numeric value so typing immediately replaces it without a separate manual selection step.
2. Quantity-editing areas that currently rely on direct numeric entry should gain compact `+` and `−` controls for increment/decrement.
3. The decrement path must never produce a negative quantity.

The exact business meaning of reaching quantity `0` must remain consistent with the approved order-line/domain rules and should be resolved explicitly at implementation time rather than silently changing deletion semantics. No production change is authorized by this findings record alone.

## Acceptance state

- CB-only cumulative payment: Passed;
- Espèce-only cumulative payment: Passed;
- mixed cumulative payment: Passed;
- downward signed correction with non-negative resulting cumulative value: Passed;
- negative cumulative value rejection: Passed;
- fully paid Open does not auto-close: Passed;
- explicit Close at exact reconciliation: Passed;
- Closed -> Open after incompatible saved payment correction: Passed;
- equality-preserving Closed save remains Closed: Passed;
- quantity-editing usability improvements: deferred follow-up, not an acceptance blocker for the behaviors above.

M05 as a whole remains under manual acceptance. Backdated effective-payment attribution, cancellation/exclusion, reuse, future/due/overdue operational views, dashboard financial semantics and final FR/zh-CN end-to-end state preservation remain to be completed. PR #10 remains open/unmerged. M06 remains not authorized.
