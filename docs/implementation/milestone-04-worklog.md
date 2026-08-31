# M04 implementation worklog

**Status:** Authorized / remediation complete pending ChatGPT review and project-owner merge approval
**Milestone:** M04 — First complete order-entry vertical slice  
**Implementation branch:** `codex/m04-order-entry`  
**Approved branch base:** `0926decdcae59ed0aa4ea95c2d0f9d79aa44a36e`  
**Authoritative contract:** `milestone-04-order-entry.md`  
**Authorization:** `milestone-04-authorization.md`  
**Approved clarification:** `../decisions/m04-order-entry-pricing-clarifications.md`

This worklog is the durable implementation/evidence record for M04.

## Preparation state

- M03 is Passed and merged through PR #5.
- M04 contract is approved.
- A1/B1/C1 clarification is approved and durable.
- M04 branch is created from the documented latest preparation `main` baseline.
- Production implementation is now present on the implementation branch; final merge remains pending project-owner review.
- GitHub issue #4 is OPEN for the authorized `M04-REVIEW-FIX-02` remediation; no later handoff or milestone is being inferred.
- M05 is not authorized.

## Evidence policy

Codex must append/update this record truthfully as implementation proceeds. Record exact implementation heads, migration identity, test/build/publish/CI results, failure-path evidence, acceptance mapping, execution topology and outstanding/complete Windows/WPF operator acceptance.

Manual acceptance must never be fabricated. PR merge remains explicitly reserved for project-owner approval after ChatGPT review.

## Implementation evidence — remediation head `c272e52aa33359206cb300844459b90ef725eae8`

- Domain pricing and immutable sale snapshots are implemented in `src/Sushi81.Pos.Domain/OrderEntry.cs`.
- Application order-entry catalogue, pricing, confirmation, exact-ID reload and post-commit dispatch seams are implemented in `src/Sushi81.Pos.Application/OrderEntry/OrderEntryContracts.cs`.
- SQLite migration 3 and transactional snapshot persistence are implemented in `src/Sushi81.Pos.Infrastructure/Migrations/M04Migrations.cs` and `src/Sushi81.Pos.Infrastructure/Order/SqliteOrderStore.cs`.
- The WPF Caisse destination preserves M03 Catalogue/Settings and exposes active-product browse, option/custom configuration, cart editing, fulfilment, total override, confirmation and exact-ID reload.
- A1/B1/C1, complete current-catalogue option revalidation, option bounds, custom adjustments, threshold/fee/VAT/manual-total cases, v2→v3 migration, failed-upgrade preservation, historical independence, per-stage rollback and dispatch-failure tests are present in the Domain/Application/Infrastructure test projects.
- The remediation closes the review blockers for dormant `OptionsEnabled=false` dialogs, explicit New Order reset, visible exact-ID snapshot display, fulfilment-gated pickup discount, invalid planned-time clearing, localized quantity/fulfilment/status/messages, explicit manual-total state, and M03 SettingsSaved → M04 draft repricing.
- Actual STA/WPF lifecycle tests cover the dormant-option dialog, localized fulfilment choices, quantity rendering, discount enablement, two successive confirmations with distinct IDs, catalogue-mutated snapshot reload display, FR→zh-CN→FR, and settings-save repricing. These are automated UI tests; operator acceptance is still separate.
- `POST_TASK_POWER_ACTION: NONE`.
- The Windows/WPF helper launch was attempted, but the built application did not expose a target window in this environment; no manual operator acceptance is claimed. The remaining manual checklist in `milestone-04-order-entry.md` must be completed on a usable Windows session.
- M05+ implementation was not started.

## Delivery record

- Prior implementation/evidence head: `2de4ed78696e9e437f9feac5491706669ff8c53f`.
- Remediation implementation commit: `c272e52aa33359206cb300844459b90ef725eae8` (`fix: close M04 order-entry review blockers`).
- Migration identity: version `3`, `create-orders-and-sale-snapshots`.
- Release restore: `dotnet restore Sushi81.Pos.sln --verbosity:minimal` passed; the initial sandboxed attempt was blocked by NuGet vulnerability-feed network access (`NU1900`) and the authorized retry passed. The desktop runtime restore `dotnet restore src/Sushi81.Pos.Desktop/Sushi81.Pos.Desktop.csproj -r win-x64 --verbosity:minimal` also passed.
- Release build: `dotnet build Sushi81.Pos.sln -c Release --no-restore --verbosity:minimal` passed with 0 warnings and 0 errors.
- Release tests: `dotnet test Sushi81.Pos.sln -c Release --no-restore --no-build --verbosity:minimal` passed with 250 tests: Domain 21, Application 18, Infrastructure integration 38, Architecture 49, OneDrive feasibility 32, and OneDrive feasibility tools 92; 0 failed and 0 skipped.
- Self-contained publish: `dotnet publish src/Sushi81.Pos.Desktop/Sushi81.Pos.Desktop.csproj -c Release -r win-x64 --self-contained true --no-restore --verbosity:minimal` passed to `src/Sushi81.Pos.Desktop/bin/Release/net10.0-windows/win-x64/publish/`.
- CI: GitHub Actions `Continuous integration` run [238](https://github.com/cimerosef/sushi81-pos/actions/runs/33402197941) completed successfully for pushed head `75b0a7cd2c5ed140330cb093ec3678e4482a031e`; the code remediation is `c272e52aa33359206cb300844459b90ef725eae8` and this head adds evidence documentation only.
- PR state: active M04 PR remains open and unmerged; project-owner merge approval is still required.
- Execution gate: OPEN for `M04-REVIEW-FIX-02` at the last durable check.
