# M05 manual Windows/WPF acceptance findings — batch 08

**Milestone:** M05 — Lifecycle, payments, search and operational dashboard  
**Date:** 2026-09-06  
**Performed by:** project owner  
**PR:** #10 — `M05: lifecycle payments search and operational dashboard`  
**Production-code baseline under test:** `b51a4bbb02605ec7aea5a5eacb1160738a46b080`  
**Overall result:** Future-order and due-today advance-order semantics passed through step 4. Manual acceptance then exposed a Commandes navigation-state defect that prevents normal date browsing after entering an operational view.

## Operational views / turnover-vs-received — passed through step 4

The project owner manually verified the following scenario:

1. A new ordinary POS order planned for the next BusinessDate increased the Future count while leaving current-day operational turnover and received-payment summaries unchanged.
2. Entering that Future order through the Future dashboard entry point navigated to Commandes and surfaced the expected order.
3. Recording full CB payment with current-day effective date on the future order increased current-day received-payment summary but did not increase current-day operational turnover; the order remained in Future until its planned date changed.
4. Changing that already-paid/Closed order's planned fulfilment date from the future date to current BusinessDate removed it from Future, increased current-day operational turnover by the order total without changing received-payment summary, and retained the sticky advance-order marker. The order then appeared in the due-today advance-order view even though it was Closed and exactly paid.

These observations support the frozen semantics that turnover follows planned fulfilment date, received-payment follows signed PaymentAdjustment effective date, and due-today advance reminders remain present regardless of Closed/payment state.

## Blocker discovered — operational view cannot be exited through normal date browsing

After entering Commandes from the Future and due-today dashboard entry points, the owner could not return to ordinary date-based order browsing from the visible Commandes date control. The list remained constrained to the operational view, preventing the planned overdue-unsettled manual step from being reached through the ordinary UI.

Code review of the current branch identifies the presentation-state cause:

- `SelectOperationalView(...)` stores a non-null `operationalView` mode;
- `RefreshAsync(...)` prioritizes that stored operational mode whenever search text is blank;
- the `BrowseDate` setter changes the date and refreshes but does not clear `operationalView`;
- therefore changing the visible browse date does not exit Future/Due/Overdue mode;
- for due-today, even selecting today's date cannot provide an escape path because it is already the same date.

This is a real Commandes navigation-state defect, not a failure of the already-passed Future/Due business semantics. M05 needs an explicit, localized and test-covered way to return from an operational view to ordinary date browsing, and manual date selection should not leave a hidden operational filter silently active.

## Acceptance state

- future-order count/list navigation: Passed;
- future order does not affect current-day turnover: Passed;
- current-day receipt on future order affects received summary only: Passed;
- moving paid/Closed future order to today updates turnover but not received summary: Passed;
- sticky advance marker survives future -> today move: Passed;
- due-today advance includes Closed/exactly-paid order: Passed;
- operational-view exit / return to ordinary date browsing: Failed / blocker;
- overdue-unsettled count/list and settled-removal path: Pending after navigation fix.

The previously deferred numeric-entry/select-all, quantity +/- and compact field-width usability changes remain queued for a consolidated UX remediation. PR #10 remains open/unmerged. M06 remains not authorized.
