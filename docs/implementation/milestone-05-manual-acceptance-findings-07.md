# M05 manual Windows/WPF acceptance findings — batch 07

**Milestone:** M05 — Lifecycle, payments, search and operational dashboard  
**Date:** 2026-09-06  
**Performed by:** project owner  
**PR:** #10 — `M05: lifecycle payments search and operational dashboard`  
**Production-code baseline under test:** `b51a4bbb02605ec7aea5a5eacb1160738a46b080`  
**Overall result:** Passed for cancellation/financial exclusion and customer-text reuse paths described below.

## Cancellation and active-financial exclusion — passed

The project owner manually verified a current-day ordinary POS order with a known total and current-day CB payment, using pre-test Caisse dashboard values as the baseline.

The following behaviors passed:

1. Before cancellation, the order contributed the expected amount to current-day operational turnover, received total and received CB.
2. Cancellation required explicit operator confirmation.
3. After cancellation, the order remained durable and visible with status `Cancelled` / localized equivalent rather than being deleted.
4. The cancelled order retained its immutable human reference, saved lines and payment facts.
5. The cancelled order remained searchable by its reference/comment.
6. No ordinary Uncancel action was exposed.
7. After cancellation, the order's contribution was removed from active current-day operational turnover and received-payment dashboard summaries, returning those values to the pre-test baseline.

This manually confirms the frozen M05 rule that cancellation retains historical business/payment facts while excluding the order from active operational/financial treatment.

## Reuse customer text into a fresh Caisse draft — passed

The project owner manually verified the existing-order reuse action using an order containing telephone, delivery address and comment.

With an empty Caisse draft, reuse passed with exactly the approved semantics:

- telephone copied;
- delivery address copied;
- comment copied;
- cart remained empty;
- no order lines were inherited;
- no payment amounts were inherited;
- no status was inherited;
- no GUID/reference was inherited;
- fulfilment mode and planned time were not inherited.

The project owner then repeated reuse while an unrelated non-empty uncommitted Caisse draft existed. The application presented an explicit overwrite confirmation. Choosing the negative/cancel path preserved the existing Caisse draft without silent loss or replacement.

## Acceptance state

- cancellation confirmation: Passed;
- cancelled history retained/searchable: Passed;
- cancelled order excluded from active turnover/received summaries: Passed;
- no Uncancel action: Passed;
- reuse copies only telephone/address/comment: Passed;
- reuse creates no inherited cart/payment/status/reference/schedule state: Passed;
- non-empty unrelated Caisse draft is not silently overwritten: Passed.

M05 as a whole remains under manual acceptance. Future-order, due-today advance-order, overdue-unsettled operational views/count navigation, remaining turnover-versus-received dashboard semantics, deferred numeric/quantity UX remediation, and final FR/zh-CN end-to-end state/layout acceptance remain pending. PR #10 remains open/unmerged. M06 remains not authorized.
