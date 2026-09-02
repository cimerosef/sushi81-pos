# M05 implementation worklog — lifecycle, payments, search and operational dashboard

**Milestone:** M05 — Lifecycle, payments, search and operational dashboard  
**Branch:** `codex/m05-lifecycle-payments-search-dashboard`  
**Pull request:** #10  
**Authorization:** `CODEX_HANDOFF_READY: M05-IMPLEMENT-01`; issue #4 was verified OPEN before execution  
**POST_TASK_POWER_ACTION:** `NONE`

## Scope delivered

The implementation keeps the M04 Order record and four-layer boundaries. It adds:

- Domain payment buckets, cumulative payment state, signed dated adjustments and immutable `YYYYMMDD-NNN` references;
- additive SQLite migration 5 with deterministic M04 reference backfill, per-business-date transactional allocation, payment ledger and search/lifecycle indexes;
- one atomic existing-order lifecycle application service for structured retrieval, date browse, live reference/telephone/comment search, same-ID modification, signed payment deltas, explicit Close, automatic reopen after an incompatible Closed save, retained Cancel, and operational summary queries;
- D2-preserving quantity/removal behavior plus explicit current-Catalogue add/reconfigure paths, without rewriting historical product snapshots silently;
- a dedicated localized French / Simplified Chinese Commandes master/detail workspace and compact Caisse dashboard entry strip;
- explicit reuse of telephone/address/comment into a fresh Caisse draft, with confirmation before replacing a non-empty uncommitted draft;
- synthetic integration and WPF architecture regressions while preserving the M04 legacy-schema compatibility test path.

No archive, recovery/authority enforcement, final Windows printing, Hiboutik parser, export, installer or M06 behavior was added.

## Verification

Environment: Windows x64, .NET SDK 10.0.400, repository target frameworks `net10.0` / `net10.0-windows`.

- `dotnet restore Sushi81.Pos.sln --locked-mode`: passed with the approved network escalation after the sandbox could not reach NuGet vulnerability metadata.
- `dotnet build Sushi81.Pos.sln --configuration Release --no-restore`: passed with 0 warnings and 0 errors.
- `dotnet test Sushi81.Pos.sln --configuration Release --no-build --logger "console;verbosity=minimal"`: passed 285 tests, 0 failed, 0 skipped — Domain 27, Application 32, Architecture 57, Infrastructure integration 45, OneDrive feasibility 32, and OneDrive feasibility tools 92.
- `dotnet publish src/Sushi81.Pos.Desktop/Sushi81.Pos.Desktop.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=false -o artifacts/m05-publish`: passed; the ignored artifact contains `Sushi81.Pos.Desktop.exe`.
- `git diff --check`: passed before delivery.

The M05 integration evidence includes stable same-day references, signed CB persistence, operational turnover/received-payment values, same-ID modification with automatic Closed-to-Open transition, cancellation retention/exclusion, live search, and migration v4-to-v5 preservation. Existing M04 transaction failure injection remains green; the M05 store also injects payment-stage failures into the same transaction boundary.

## Acceptance boundary

AC-LIFE-003 through AC-LIFE-014 and the live-search portion of AC-LIFE-015 are implemented with automated evidence and are recorded as `Partial` in `docs/implementation-status.md` until the project-owner Windows/WPF M05 checklist is executed. M05 does not claim the M12 annual archive portion of AC-LIFE-015. No real customer/order/payment/credential data was added.

## Delivery state

The final pushed implementation SHA and GitHub Actions result are recorded in the matching top-level `CODEX_DONE: M05-IMPLEMENT-01` PR comment after delivery. The PR remains open and must not be merged without explicit project-owner approval.

## Review remediation — `M05-REVIEW-REMEDIATION-02` — 2026-09-02

The follow-up pass closes the identified M05 lifecycle and Commandes gaps without changing the frozen order model or starting M06:

- an overdue existing order retains its persisted historical planned date while ordinary payment/detail corrections remain saveable; explicitly moving it to a different past date is still rejected;
- price-affecting existing-order saves reprice from persisted sale-time line snapshots through the shared pricing rules and the current `BusinessSettings`, clear a previous manual total override, and preserve historical product/option values;
- the actual Commandes WPF detail now exposes a localized effective-payment `DatePicker`, retains the selected date during unrelated edit changes, and shows live paid, difference and close-eligibility feedback;
- the actual Commandes WPF detail now exposes the localized Retrait discount request input; its enabled state follows editing and fulfilment mode, and the adjacent Catalogue picker audit confirmed the existing bottom-docked action row order.

Focused evidence was added in `tests/Sushi81.Pos.Domain.Tests/OrderPricingTests.cs`, `tests/Sushi81.Pos.Application.Tests/OrderLifecycleApplicationTests.cs` and `tests/Sushi81.Pos.ArchitectureTests/M05DesktopTests.cs`. The STA/WPF regression covers the actual Commandes controls, overdue date display, effective-date persistence across FR → zh-CN → FR, live payment feedback and discount-control state. The application/domain regressions cover overdue correction, rejection of a newly past date and current-settings repricing from historical line snapshots.

Remediation verification on pushed head `661afe5dd34a2af584bb35b544f282c4604abac9`: locked restore passed after the approved NuGet network escalation; Release build passed with 0 warnings and 0 errors; the full Release suite passed 290 tests, 0 failed and 0 skipped; a self-contained `win-x64` publish passed to the ignored `artifacts/m05-remediation-publish/` directory; and GitHub Actions `Continuous integration` run **#296** passed Restore, Build and Test. Manual Windows/WPF acceptance remains pending and is not claimed here. The PR remains open/unmerged, M06 and later milestones remain unauthorized, and `POST_TASK_POWER_ACTION: NONE`.

## Test-evidence closure — `M05-TEST-EVIDENCE-CLOSURE-03` — 2026-09-02

The evidence-closure pass adds real populated-v4 migration coverage, SQLite transaction-failure coverage, exact payment/query assertions, and actual STA/WPF operator-route regressions. The implementation remains within M05; no archive or M06 behavior was added.

Exact new integration tests in `tests/Sushi81.Pos.Infrastructure.IntegrationTests/M05EvidenceClosureIntegrationTests.cs`:

- `PopulatedV4ToV5BackfillPreservesSnapshotsAndStableReferencesAcrossRestart`: starts from a populated v4 database with four orders, equal timestamps/GUID tie-break, item/adjustment/tax snapshots, future planned dates, zeroed cumulative payments, stable restart reads, and continued per-date reference allocation.
- `PopulatedV4FailedV5MigrationRollsBackAndRealRetryPreservesData`: injects a post-apply v5 failure after the populated schema/data changes have begun, proves the live database remains v4 without partial v5 objects, then retries the real migration without reset or data loss.
- `NewReferenceAndExistingSnapshotFailuresRollbackAtRealSqliteBoundary`: proves new-order reference allocation and parent creation rollback without consuming the sequence, retry reference continuity, and exact existing parent/child/tax snapshot restoration after a parent-update failure.
- `PaymentFailuresRollbackSingleAndTwoChannelLedgersAndSignedCorrectionIsExact`: proves one-channel and between-two-channel payment rollback, signed negative CB correction, exact ledger counts, and two-channel/zero-delta save behavior.
- `LifecycleCommitAndStateTransitionFailuresLeaveFreshReloadUnchanged`: proves commit-failure rollback for Close and Cancel and rollback of an incompatible Closed-to-Open modification, with fresh-connection snapshot reloads.
- `PaymentValidationQueriesAndOperationalSummariesUseExactCentsAndSources`: proves both negative cumulative payment guards, search by reference/raw-or-normalized phone/comment including Cancelled, future/due/overdue filtering, effective-date received totals, planned-date turnover, and HiboutikPaste exclusion.

Exact new STA/WPF tests in `tests/Sushi81.Pos.ArchitectureTests/M05DesktopTests.cs`:

- `MainWindowUsesRealCommandesSelectionAbandonReuseAndDashboardRoutesOnSta`: drives the actual Commandes DataGrid selection, Modify/Abandon controls, clean-draft customer reuse into Caisse, tab draft preservation, and the dashboard Future entry point back to Commandes.
- `LiveSearchAndDateRefreshCannotLetAnOlderLifecycleQueryOverwriteTheLatest`: holds an older live-search result and proves the newer query remains authoritative after both complete.
- `LifecycleOperationsDoNotOwnAutomaticPrintingAndCloseEligibilityIsExactOnSta`: verifies the lifecycle service has no print-dispatch dependency, exact live payment equality enables the close feedback, and Abandon restores the non-eligible state.

The only minimal production adjustments in this closure are count-aware `order-after-parent` and `payment-after-N` failure-injection stages for transaction evidence, stable generated names for the existing dashboard button controls, and phone search accepting both stored normalized and raw forms. Existing production behavior and the frozen M05 business rules are otherwise unchanged. AC-LIFE-003 through AC-LIFE-014 and live-search AC-LIFE-015 remain `Partial` pending the project-owner Windows/WPF checklist; the M12 archive portion remains out of scope.

## Acceptance-publish process correction — 2026-09-02

A post-review attempt to prepare the project-owner acceptance artifact introduced unnecessary synthetic Windows-profile / `LOCALAPPDATA` isolation. That isolation did not prove a production-code startup defect and complicated a routine local publish/launch path. The project owner stopped the task before completion.

The mailbox record formerly named `CODEX_HANDOFF_READY: M05-WINDOWS-STARTUP-BLOCKER-04` was converted to `CANCELLED_HANDOFF: M05-WINDOWS-STARTUP-BLOCKER-04`; no `CODEX_DONE` is expected and no production-code change from that cancelled task is accepted or required. Issue #4 was returned to CLOSED after cancellation.

The durable process rule is now recorded in `docs/implementation/agent-execution-contract.md`:

- Codex still performs self-contained win-x64 publish during milestone verification as evidence that the exact implementation head is publishable.
- Routine project-owner Windows/WPF acceptance deployment is not a Codex implementation task by default; use the already-proven direct local PowerShell `dotnet publish` and normal Windows launch path for the exact reviewed head.
- Do not invent synthetic `LOCALAPPDATA`/profile redirection solely for ordinary acceptance. If important local application data may be migrated or mutated, protect it first with the approved backup/recovery procedure.
- If the normal local publish/launch later reveals a reproducible software defect, create a new distinct diagnostic/remediation handoff.

M05 manual Windows/WPF acceptance remains pending. PR #10 remains open/unmerged and M06 remains unauthorized.

## Manual-acceptance search/layout remediation — `M05-MANUAL-ACCEPTANCE-SEARCH-LAYOUT-FIX-05` — 2026-09-02

This remediation follows the approved search/layout clarification in `docs/decisions/m05-manual-acceptance-search-and-layout-clarifications.md`. It remains limited to PR #10 and does not change the order/payment model, migrations 1–5, archive boundary or M06 scope.

### Defect-escape retrospective

- The partial-phone defect escaped the earlier tests because persistence normalizes an ordinary ten-digit French local number to spaced pairs (for example `06 12 34 56 78`), while the earlier query asserted only raw/normalized whole-value `LIKE` forms. The new shared search normalization compares digits-only fragments and both local `0…` and international `33…` prefixes without changing persisted meaning.
- Exact-only reference search remained because the earlier SQL used equality for `order_reference`; the new parameterized query uses an escaped substring pattern for references and comments.
- The application fallback previously searched only reference and raw telephone text, so it could diverge from the SQLite production path. It now uses the same shared telephone terms and includes comments; SQLite remains the production query path with a compact single-row projection.
- The Caisse visual audit found the duplicated saved/reloaded GUID group, reload row and date browser/list. Those controls are removed from the actual WPF tree while the underlying compatibility seams remain available for existing regressions.

### Changes and evidence

- `OrderBrowserRow` now carries only the approved compact fulfilment mode/comment/address additions; SQLite date/live queries project those fields in one query without full snapshot/N+1 loads.
- Commandes is now actual top controls → full-width horizontally/vertically scrollable list → vertically scrollable selected-order detail. It retains the existing columns and adds only Mode, Commentaire and Adresse; no financial columns were added.
- Caisse retains the dashboard strip and fast-entry fields/actions, removes the duplicate M04 browser/reload UI, gives Panier a substantially larger available region with its own scroll, and reports the persisted human reference in normal success/output-failure feedback, with a technical ID only for abnormal reload fallback.
- Focused infrastructure/application and actual STA/WPF regressions cover partial reference/telephone/comment search (including local/international telephone forms), literal `%`/`_`, compact date/live projection, stale-search protection, FR/zh-CN headers, selection/detail and Modify/Abandon, dashboard routing, minimum/larger windows, Caisse control absence, cart scrolling and human-reference feedback.

Verification on the working head before delivery: locked restore passed after the approved NuGet network retry; Release build passed with 0 warnings and 0 errors; the full Release suite passed 302 tests, 0 failed and 0 skipped; `git diff --check` passed; and a fresh self-contained `win-x64` publish passed to the ignored evidence directory `artifacts/m05-manual-acceptance-search-layout-fix-05-publish/`. The artifact was not deployed to an acceptance directory and was not launched. Final pushed SHA and CI status are recorded in the matching completion comment.

M05 remains `Partial` pending the project-owner Windows/WPF manual checklist. No real customer/order/payment/credential data was read, migrated or written by this remediation. PR #10 remains open/unmerged, issue #4 was OPEN for execution, M06 remains unauthorized, and `POST_TASK_POWER_ACTION: NONE`.

## Manual-acceptance follow-up localization/time/layout remediation — `M05-MANUAL-ACCEPTANCE-LOCALIZATION-TIME-LAYOUT-FIX-06` — 2026-09-02

This follow-up closes the three findings recorded in `docs/implementation/milestone-05-manual-acceptance-findings-02.md` while preserving FIX-05 and the frozen M05 boundary.

### Defect-escape retrospective

- Static resources and labels were tested, but an already-visible validation message was not tested across a live FR ↔ zh-CN switch; the message therefore retained its old language and a ComboBox collection refresh could transiently clear the selected mode.
- The prior Commandes filler/scroll checks passed positive and scrollability assertions without asserting useful viewport occupation, so the two long-text columns could remain at their minimum while leaving empty viewport space.
- Mandatory planned time was copied from M04 §4.5 even though FR-017 and the Data Model allowed a null time; the M04 contract's stop-on-conflict rule was not carried through to the implementation handoff.

### Changes and evidence

- `OrderEntryShellViewModel` and `OrderLifecycleShellViewModel` now retain fulfilment, date/time selector and draft state while localized choice collections are refreshed, and retain structured pricing/operation validation issues so currently visible messages rerender immediately in the selected language. User-entered telephone, address and comment text is not translated.
- Planned time is now optional at both entry and lifecycle persistence layers: date and fulfilment mode remain required; both selectors unset means `null`; both selected values must use hours 11–14 or 18–22 and five-minute minutes; half-selected or out-of-slot values remain invalid. Clearing either selector clears the other to avoid an inconsistent half-selection.
- Commandes keeps exactly nine columns, with only Commentaire and Adresse treated as long-text columns. The WPF window-size/layout handlers assign their leftover wide-window viewport equally and keep their minimum widths so narrow windows retain horizontal scrolling; the calculation is idempotent across hosted-runner layout timing.
- `M03Presentation` now maps payment-negative validation for lifecycle message rerendering. Tests cover null-time Retrait/Livraison confirmation and reload, valid selected time, invalid partial time, date/mode requirements, future operational semantics, null-time modification, live localization state preservation, and actual Commandes widths at 980x680, 1280x900 and 760x520. Migration files 1–5 were not changed.
- The independent read-only Luna audit found the same direct-message/state and selector risks in the broader desktop surface, plus the need for actual width assertions; it made no edits. This handoff integrates the focused fixes required here and leaves unrelated historical UI paths unchanged.

The first three final-head GitHub Actions runs (#325, #328 and #330) reproduced the wide-window assertion issue on the hosted Windows runner; diagnostic run #332 confirmed that the runner clamps the requested 1280px window to an actual 1044px width and cannot physically provide the approximately 1091px minimum business-column extent. The final correction keeps the Window-size calculation plus idempotent layout-completion fallback without changing the business column set, and the WPF regression now asserts wide expansion when the host can provide it while asserting minimum widths and horizontal scrolling on a host-limited screen. After that correction, the focused WPF width test and the full local Release suite passed again. Locked restore passed, Release solution build passed with 0 warnings and 0 errors, and the full Release suite passed 307 tests, 0 failed and 0 skipped. A self-contained `win-x64` publish passed with `dotnet publish src/Sushi81.Pos.Desktop/Sushi81.Pos.Desktop.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=false -o artifacts/m05-manual-acceptance-localization-time-layout-fix-06-final-publish --no-restore`; its complete output is evidence-only at `artifacts/m05-manual-acceptance-localization-time-layout-fix-06-final-publish/` and was not deployed or launched. The final pushed-head CI result is recorded in the matching completion comment. No acceptance directory, real business database, real order data, account, profile redirection or M06 work was used. Manual Windows/WPF acceptance remains pending; PR #10 remains open/unmerged, issue #4 was OPEN for execution, and `POST_TASK_POWER_ACTION: NONE`.
