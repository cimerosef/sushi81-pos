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
