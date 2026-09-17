# Post-M09 Hiboutik daily payment dashboard — worklog

**Status:** PASSED — owner Windows/WPF manual acceptance completed; awaiting separate merge approval
**Opened:** 2026-09-16  
**Issue:** #18  
**Branch:** `codex/post-m09-hiboutik-daily-payment-dashboard`  
**Execution gate:** GitHub Issue #4

## Control baseline

- M09 Passed / merged through PR #17 at `d840066d8d2ffa1856c4fcd88dbfdd3c8f2a1be5`.
- Approved decision: `docs/decisions/post-m09-hiboutik-daily-payment-dashboard.md`.
- Approved acceptance amendment: `docs/acceptance-criteria-amendment-post-m09-hiboutik-daily-payment-dashboard.md`.
- Implementation authorization: `docs/implementation/post-m09-hiboutik-daily-payment-dashboard-authorization.md`.
- Implementation contract: `docs/implementation/post-m09-hiboutik-daily-payment-dashboard.md`.
- Owner manual checklist: `docs/implementation/post-m09-hiboutik-daily-payment-dashboard-manual-acceptance.md`.
- M10 and later milestones remain unauthorized.

## Authorized outcome

Exactly two passive top-Caisse values:

- Hiboutik CB today;
- Hiboutik Espèce today.

Both use signed `PaymentAdjustment` deltas attributed by effective business date, include only non-Cancelled `HIBOUTIK_PASTE` orders, remain separate from ordinary POS dashboard amounts, and add no schema/write workflow.

## Evidence log

The mandatory docs-first reconciliation was completed and pushed in the first commit of this handoff, `ecee6a1ec9b63e1139f62131a01f131b87b1fdb3`. It records the M09 merge baseline, the authorized independent post-M09 scope, the 2026-09-16 freeze amendment, the narrow product/acceptance/lifecycle/data-model semantics and the current PR #19 / Issue #4 execution state. No production code, tests, schema, migration, dependency, workflow or business behavior was changed by that commit.

The production implementation then extended the existing operational-summary seam with two signed, effective-business-date Hiboutik payment aggregates, read-only view-model projection, two passive Caisse bindings and FR/zh-CN resources. The existing POS summary query and payment/write paths remain unchanged; no schema, migration, dependency, new entity or Hiboutik workflow was added.

Implementation commit: `b10c06e06093f16d2df6335d8431299efe00b081`.

Focused evidence passed:

- Post-M09 SQLite reporting test: 1/1 passed, including POS/Hiboutik separation, Open/Closed eligibility, cancellation exclusion with retained rows, signed corrections, effective-date attribution and recorded-date non-attribution.
- Relevant M05 integration tests: 8/8; M09 WP1: 4/4; M09 WP5: 7/7; M05 desktop: 53/53.
- Presentation/fallback/resource/XAML checks passed: 2 presentation refresh tests, 1 application fallback test, 1 passive-binding test and 1 FR/zh-CN localization test.
- Full Release solution suite: 654/654 passed, 0 failed, 0 skipped. Release build: 0 warnings, 0 errors.
- Self-contained `win-x64` `PublishSingleFile=false` verification publish passed from checkout `dbab706a3f535b521a4a0fc67c318bf0c14b60bb` (the final implementation checkout before this evidence-only update). EXE `Sushi81.Pos.Desktop.exe`: 162,816 bytes, SHA-256 `5DA02D47C0854F993FC461AF1936300BB1DB455B9EABAC30CACABE31FC4FB7AB`. ZIP `post-m09-dashboard-win-x64.zip`: 67,042,116 bytes, SHA-256 `97D9ACC23C6356A68158E749762EEAF4689BA5803BCF0E237AC73A6E64B48145`.

At the time of this implementation evidence, owner manual acceptance remained pending and had to be performed against the exact candidate. The later owner disposition is recorded in the final-closure section below; Codex does not create or infer the owner's observations.

## Evidence-gap remediation

The controller identified two evidence-only gaps in comment `5705566720`. This remediation remains test/docs-only and does not change production source, behavior, schema, migration, dependency, write path, authority path or the owner candidate.

- Added a direct `SqliteOrderStore.GetOperationalSummaryAsync` no-match test proving both Hiboutik daily values are exactly `Money.Zero` while the ordinary summary remains zero for an empty synthetic database; the focused class passed 2/2.
- Added an STA presentation/localization test with non-zero Hiboutik values. It switches FR -> zh-CN -> FR through `ShellViewModel.ChangeLanguageAsync`, verifies the localized labels, compares numeric values using the active culture rather than localized strings, and proves no summary requery or write occurred; the focused test passed 1/1 and the full M07 presentation-refresh class passed 3/3.
- Relevant post-M09 M09 Hiboutik desktop tests passed 8/8. The full Release solution suite passed 656/656: Domain 33, Application 119, Infrastructure 235, Architecture 145, and OneDrive feasibility 32 + 92; 0 failed and 0 skipped. The Release build passed with 0 warnings and 0 errors, and `git diff --check` passed.
- Updated the manual-acceptance candidate header with the already-built owner candidate identity from checkout `dbab706a3f535b521a4a0fc67c318bf0c14b60bb`; this remains `NOT YET EXECUTED / owner execution pending`.
- The later remediation head is not a replacement executable candidate; no rebuild or republish was performed.

## Final closure / owner acceptance

The controller accepted the automated evidence remediation in PR #19 comment `5705723428`, reviewing exact head `5b0009f805fcbae6b7389e33d337eb830a6b08a8`. The owner then completed the exact Windows/WPF candidate acceptance and recorded `OWNER_FINAL_ACCEPTANCE` in PR #19 comment `5715414940`; sections A–F and the final manual-acceptance disposition are all `PASSED`.

The accepted candidate remains the unchanged build from checkout `dbab706a3f535b521a4a0fc67c318bf0c14b60bb`: `Sushi81.Pos.Desktop.exe` is 162,816 bytes with SHA-256 `5DA02D47C0854F993FC461AF1936300BB1DB455B9EABAC30CACABE31FC4FB7AB`; `post-m09-dashboard-win-x64.zip` is 67,042,116 bytes with SHA-256 `97D9ACC23C6356A68158E749762EEAF4689BA5803BCF0E237AC73A6E64B48145`. No production candidate was rebuilt or replaced after the test/docs-only remediation head.

PR #19 remains OPEN / unmerged. The post-M09 enhancement is `PASSED` on the accepted candidate, while merge remains a separate explicit project-owner decision. M10 and later milestones remain unauthorized.

## Merge boundary

Implementation completion/manual acceptance never authorizes merge automatically. Separate project-owner merge approval remains required.
