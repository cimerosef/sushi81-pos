# M05 manual Windows/WPF acceptance findings — batch 06

**Milestone:** M05 — Lifecycle, payments, search and operational dashboard  
**Date:** 2026-09-06  
**Performed by:** project owner  
**PR:** #10 — `M05: lifecycle payments search and operational dashboard`  
**Production-code baseline under test:** `b51a4bbb02605ec7aea5a5eacb1160738a46b080`  
**Overall result:** Passed for the tested cumulative-payment and lifecycle paths below. Backdated effective-payment attribution remains under manual verification. Additional operator-usability improvements for numeric entry and compact field sizing were requested and intentionally deferred until the current manual-acceptance round reaches a suitable consolidation point.

## Payment/lifecycle manual acceptance — passed

The project owner created fresh simple orders and manually verified the following behaviors:

1. **CB-only payment** — cumulative CB could be set to the exact order total with Espèce at zero; paid total/difference updated correctly; an exactly paid Open order remained Open until explicit Close.
2. **Downward payment correction on Closed order** — reducing cumulative CB from the exact paid amount to a smaller non-negative amount persisted as a valid correction and automatically returned the order from Closed to Open with the expected unpaid difference.
3. **Negative cumulative payment rejection** — attempting to set cumulative CB below zero was rejected; the previously committed non-negative cumulative amount remained authoritative.
4. **Equality-preserving Closed save** — after restoring exact payment and explicitly Closing again, a later non-price-affecting order modification that preserved exact payment kept the order Closed.
5. **Espèce-only payment** — cumulative Espèce equal to the order total behaved correctly; exactly paid remained Open until explicit Close.
6. **Mixed CB + Espèce payment** — split cumulative CB/Espèce equal to the order total behaved correctly; exactly paid remained Open until explicit Close.

These observations manually support the frozen M05 rules for cumulative non-negative payments, signed downward corrections, explicit Close, no auto-close on exact payment, automatic Closed-to-Open transition when a successful saved correction breaks exact reconciliation, and equality-preserving Closed saves.

## Backdated effective-payment-date observation — verification still in progress

During the first manual backdated-payment step, the project owner changed the payment effective date from `2026-09-06` to `2026-09-05` and saved. Immediately after the successful save, the visible `Date d’encaissement` control displayed `2026-09-06` again, which initially appeared to make backdating impossible.

Code review shows that the selected effective date is passed into `OrderLifecycleService.SaveModificationAsync(...)` before save, while the post-save editor reload intentionally calls `LoadEditableFields(...)`, which resets the next-entry default `EffectivePaymentDate` to the current `BusinessDate`. Therefore the visible post-save reset alone is not evidence that the persisted PaymentAdjustment used the wrong effective date.

Manual verification must continue by checking the operational received-payment summary against the pre-test baseline: if the saved `+4.00` CB delta was attributed to `2026-09-05`, today's (`2026-09-06`) received/CB dashboard figures must remain unchanged. The later steps should then verify a current-day `+3.00` delta, zero-delta date-only save, and current-day `-2.00` correction.

## Deferred numeric-entry and quantity-editing usability request

The project owner requested the following usability enhancements, but explicitly asked not to interrupt the current manual-acceptance round for implementation yet:

1. **Select-all on focus for editable numeric values.** Any operator-editable numeric text field should automatically select its current value when it receives mouse/keyboard focus, so typing immediately replaces the existing number without manual selection. This includes at minimum order quantity, Total TTC, CB and Espèce, and should be applied consistently to other comparable editable numeric controls where technically appropriate.
2. **Quantity `+` / `−` controls.** Quantity-editing areas that currently rely on direct numeric entry should gain compact increment/decrement controls. The decrement path must never produce a negative quantity.
3. **Quantity zero semantics.** The exact business meaning of reaching quantity `0` must remain consistent with the approved order-line/domain rules and must be resolved explicitly at implementation time rather than silently inventing deletion semantics.
4. **Compact payment/value field widths.** In the Commandes order-editing panel, the input controls for Total TTC, CB, Espèce and `Date d’encaissement` are visually too wide because they stretch across the available column. They should be given practical compact widths appropriate to their content while preserving FR/zh-CN usability and small-window layout.

No production change is authorized by this findings record alone. These items are deferred for a consolidated UX remediation after the current manual-acceptance round reaches a suitable stopping point.

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
- backdated effective-payment attribution: Pending manual dashboard verification; visible post-save date reset is currently understood as next-entry default reset, not yet a failure;
- numeric/select-all, quantity +/- and compact-width usability improvements: deferred follow-up, not an acceptance blocker for the already-passed behaviors above.

M05 as a whole remains under manual acceptance. Backdated effective-payment attribution, cancellation/exclusion, reuse, future/due/overdue operational views, dashboard financial semantics and final FR/zh-CN end-to-end state preservation remain to be completed. PR #10 remains open/unmerged. M06 remains not authorized.
