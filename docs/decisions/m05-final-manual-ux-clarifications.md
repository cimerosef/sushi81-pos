# M05 final manual-acceptance UX clarifications

**Status:** Approved  
**Decision date:** 2026-09-06  
**Approved by:** project owner  
**Applies to:** M05 — Lifecycle, payments, search and operational dashboard

This record freezes the operator-usability clarifications accumulated during final Windows/WPF manual acceptance. It does not change the frozen M05 business semantics, persistence model, payment ledger meaning, pricing rules, migration 5, or M06+ scope.

## 1. Exit operational views cleanly

Entering Commandes from the Caisse Future / due-today advance / overdue-unsettled dashboard entry points must never trap the operator in a hidden persistent filter.

Required behavior:

- Commandes must expose an explicit localized action to return to ordinary date-based browsing;
- manually choosing a browse date must clear any active operational-view filter before refresh;
- returning to date browsing must preserve the selected browse date and show the ordinary planned-date result set for that date;
- no hidden Future/Due/Overdue filter may silently remain active after the operator has returned to ordinary browsing.

## 2. Select-all on focus for editable numeric inputs

Any operator-editable numeric text input should select its current value when it receives focus so the next typed value replaces the old value directly.

This applies consistently to numeric text controls including, where present:

- order-line quantity;
- Total TTC / manual total;
- CB;
- Espèce;
- settings numeric values and other comparable editable numeric text fields.

Normal keyboard navigation and mouse usage must remain usable. This is a presentation behavior only and must not alter validation or business meaning.

## 3. Quantity increment/decrement controls and zero semantics

Where order-line quantity is editable, provide compact `+` and `−` controls in addition to direct numeric entry.

Rules:

- `+` increments by one;
- `−` decrements by one;
- quantity must never become negative;
- decrementing from `1` to `0` is defined as removing that draft line, equivalent to the existing line-delete action;
- for modification of an existing persisted order, removal remains part of the in-memory edit until Save; Abandon must restore the persisted line;
- do not persist an order line with quantity `0`.

## 4. Decimal separator input

All editable decimal numeric inputs, especially monetary fields, must accept both `,` and `.` as the decimal separator from the keyboard, including the decimal point key on a numeric keypad.

Requirements:

- `12,50` and `12.50` represent the same numeric value;
- this applies in French and Simplified Chinese UI;
- display formatting may remain locale-appropriate;
- do not loosen unrelated validation or introduce ambiguous thousands-separator parsing;
- integer-only quantity fields remain integer-only.

## 5. Compact field widths — Commandes

The Commandes order-editing panel must stop stretching short-value controls across the full available width.

Use practical compact widths appropriate to their content for at least:

- Total TTC;
- CB;
- Espèce;
- payment effective date (`Date d'encaissement`).

The layout must remain readable in FR and zh-CN and usable at the supported small-window size.

## 6. Compact field widths — Caisse

The Caisse order-entry form must likewise use practical optimized widths instead of full-row stretching for at least:

- fulfilment mode;
- planned date;
- telephone;
- Total TTC / authoritative total display or input where applicable.

Do not reduce the useful width of genuinely long text fields such as delivery address or comment merely for visual symmetry.

## 7. Caisse dashboard visual emphasis

In the compact Caisse dashboard strip:

- the three clickable operational counts that navigate to Commandes — Future, due-today advance and overdue-unsettled — must display their numeric values in red;
- today's operational turnover numeric value must use bold font weight;
- today's received CB numeric value must display in blue;
- today's received Espèce numeric value must display in green;
- today's received total remains normal/default unless another approved rule already applies.

The styling must preserve legibility in the normal Windows theme and in both FR and zh-CN.

## 8. Payment effective-date UX

The payment effective date is not an order-level historical 'last payment date'. It is the attribution date used for payment deltas created by the current save. The UI must make that meaning clear.

Approved presentation behavior:

- outside modification mode, do not show a read-only date control that appears to claim the order's payment date;
- when the operator enters modification mode, show the payment effective-date control with current BusinessDate as the default for the next payment adjustment;
- use a localized label/help text that makes clear it applies to payment changes in the current modification/save, e.g. French `Date d'encaissement de cette modification` and Simplified Chinese `本次收款日期` (wording may be refined while preserving the meaning);
- after save exits modification mode, the control should no longer present today's date as though it were persisted order history;
- changing only this date while CB/Espèce cumulative values are unchanged must continue to create no PaymentAdjustment, per the frozen M05 rule;
- do not invent an order-level payment-date column or change the signed PaymentAdjustment ledger semantics.

## 9. Scope guard

This clarification authorizes only M05 presentation/usability remediation and the operational-view navigation-state fix described above.

It does not authorize:

- schema or migration changes;
- new payment/refund semantics;
- new order statuses;
- changes to snapshot/Catalogue authority;
- printing implementation;
- M06 or later milestone work;
- merge of PR #10 without explicit project-owner approval.
