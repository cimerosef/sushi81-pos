# M05 final Windows/WPF manual acceptance

**Status:** Passed  
**Milestone:** M05 — Lifecycle, payments, search and operational dashboard  
**PR:** #10 — `M05: lifecycle payments search and operational dashboard`  
**Accepted branch:** `codex/m05-lifecycle-payments-search-dashboard`  
**Accepted production-code head:** `84c1c534c1df105ccb1839cbc6dfc9e0e055bb70`  
**Operator acceptance date:** 2026-09-06  
**Project owner:** accepted manually  

## 1. Final operator result

The project owner republished and exercised the reviewed M05 Windows/WPF application after the complete remediation sequence through FIX-19 and confirmed that the remaining issues are resolved.

The final operator acceptance includes the previously remediated M05 areas, including:

- dedicated Commandes browse/search/detail/edit workflow;
- human-readable immutable order references and live reference/telephone/comment search;
- same-order modification, Abandon restore, saved modification persistence and restart behavior;
- cumulative CB/Espèce editing, explicit Close, payment effective-date behavior and edit-only presentation;
- retained cancellation and operational/financial exclusion behavior;
- Future, due-today advance and overdue-unsettled operational views plus clean return to ordinary date browsing;
- D2 historical snapshot authority for quantity-only edits and deliberate current-Catalogue authority for new lines / explicit reconfiguration;
- quantity `−` / `+`, 1→0 removal semantics, numeric select-all and comma/dot decimal input;
- compact Caisse/Commandes numeric/date layouts and FR/zh-CN presentation;
- dashboard visual emphasis for operational counts, turnover, CB and cash;
- Caisse category filtering/selection stability;
- shortened French payment-date wording `Date d'encaissement`, compact normal edit-column placement, and complete hiding outside edit mode;
- broad application responsiveness after removal of the global `commandesGrid.LayoutUpdated` width-feedback path.

The owner specifically confirmed after FIX-19 that all previously reported issues are fixed. The earlier multi-second application-wide stalls are no longer present in the accepted local build.

## 2. Automated evidence associated with the accepted head

The accepted production head `84c1c534c1df105ccb1839cbc6dfc9e0e055bb70` has:

- Release build: 0 warnings / 0 errors;
- complete Release test suite: 340/340 passed, 0 failed, 0 skipped;
- focused FIX-19 / retained FIX-18/FIX-16 STA/WPF regression set: 4/4 passed;
- win-x64 self-contained evidence publish: passed;
- GitHub Actions Continuous integration run `34034418001`: success on the exact production head.

FIX-19 changed only the payment-date label edit-mode visibility and its STA/WPF assertions. The FIX-18 responsiveness remediation remains intact: the global `commandesGrid.LayoutUpdated` sizing path stays removed, bounded Loaded/SizeChanged sizing remains, and the opt-in `SUSHI81_POS_PERF_TRACE=1` diagnostic path remains available but was not required for final acceptance.

## 3. Acceptance meaning

This manual acceptance closes the outstanding M05 Windows/WPF operator gate for the implemented M05 scope.

It does **not** itself authorize merging PR #10. Merge still requires the project owner's explicit merge approval under the controlled execution contract.

The annual-archive portion of AC-LIFE-015 remains owned by M12 and must not be marked as delivered by M05; only the M05 live-search portion is accepted here.

M06 remains unauthorized and must not start until M05 status/document closure is complete, PR #10 is explicitly approved and merged, and M06 is prepared from the resulting `main` in separate new ChatGPT and Codex conversations.

## 4. Scope guard

No further M05 product/production-code change is implied by this acceptance record. Final M05 work after this point is documentation/status closure and, only after explicit owner approval, merge of PR #10.
