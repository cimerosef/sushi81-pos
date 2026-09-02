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
