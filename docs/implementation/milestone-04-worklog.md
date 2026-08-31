# M04 implementation worklog

**Status:** Authorized / `M04-REVIEW-FIX-03` complete pending ChatGPT review and project-owner merge approval
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
- GitHub issue #4 is OPEN for the authorized `M04-REVIEW-FIX-03` remediation; no later handoff or milestone is being inferred.
- M05 is not authorized.

## Evidence policy

Codex must append/update this record truthfully as implementation proceeds. Record exact implementation heads, migration identity, test/build/publish/CI results, failure-path evidence, acceptance mapping, execution topology and outstanding/complete Windows/WPF operator acceptance.

Manual acceptance must never be fabricated. PR merge remains explicitly reserved for project-owner approval after ChatGPT review.

## Implementation evidence — review-fix head `23d4ef35554ded423f23cf9f8fd63fa901bd848b`

- Domain pricing and immutable sale snapshots are implemented in `src/Sushi81.Pos.Domain/OrderEntry.cs`.
- Application order-entry catalogue, pricing, confirmation, exact-ID reload and post-commit dispatch seams are implemented in `src/Sushi81.Pos.Application/OrderEntry/OrderEntryContracts.cs`.
- SQLite migration 3 and transactional snapshot persistence are implemented in `src/Sushi81.Pos.Infrastructure/Migrations/M04Migrations.cs` and `src/Sushi81.Pos.Infrastructure/Order/SqliteOrderStore.cs`.
- The WPF Caisse destination preserves M03 Catalogue/Settings and exposes active-product browse, option/custom configuration, cart editing, fulfilment, total override, confirmation and exact-ID reload.
- A1/B1/C1, complete current-catalogue option revalidation, option bounds, custom adjustments, threshold/fee/VAT/manual-total cases, v2→v3 migration, failed-upgrade preservation, historical independence, per-stage rollback, commit-failure rollback and dispatch-failure tests are present in the Domain/Application/Infrastructure test projects.
- This review fix makes the M03 SettingsSaved handoff carry before/after settings, preserves committed order state, preserves manual totals for unchanged/irrelevant settings, and clears/reprices only when the applicable uncommitted pricing result changes. The remaining hard-coded inactive-product message now uses the active FR/zh-CN resource.
- Explicit matrix evidence is named in `OptionSelectionMatrixCoversEveryConfiguredBoundary`, `A1MultipliesPositiveAndNegativePredefinedAndCustomAdjustmentsPerUnit`, `RetraitDiscountUsesEligibleLinesAndSignedComponentsAtEachThreshold`, `LivraisonBoundariesFeesAndSignedCommercialAmountAreExplicit`, and `MixedVatAndIncludedVatHalfUpRoundingSurvivePricingVariants`.
- Application evidence includes active catalogue code/name/category filtering, optional telephone/address, stable POS IDs/future marker, and dispatch of the persisted snapshot. Infrastructure evidence includes populated v2 failed-v3 preservation/retry, sale-time catalogue independence, mixed-tax save/reload, manual 10% tax save/reload, commit-failure rollback and dispatch-failure reloadability.
- Actual STA/WPF tests cover the dormant-options dialog, localized fulfilment choices, quantity rendering, discount enablement, two successive confirmations with distinct IDs, catalogue-mutated snapshot reload display, FR→zh-CN→FR, settings-save repricing, main-window quantity/remove/fulfilment/time/manual/New Order controls, and the real option dialog multi-selection/custom-adjustment controls. Main catalogue Add/double-click modal orchestration remains an operator check because the test host cannot safely inject interaction into that nested modal loop; no manual acceptance is claimed.
- `POST_TASK_POWER_ACTION: NONE`.
- The Windows/WPF helper launch was attempted, but the built application did not expose a target window in this environment; no manual operator acceptance is claimed. The remaining manual checklist in `milestone-04-order-entry.md` must be completed on a usable Windows session.
- M05+ implementation was not started.

## Delivery record

- Prior implementation/evidence head: `358d7105bf51f39b5de4b514daafcaed63730a3b`.
- Review-fix implementation commit: `23d4ef35554ded423f23cf9f8fd63fa901bd848b` (`fix: close M04 review remediation gaps`).
- Migration identity: version `3`, `create-orders-and-sale-snapshots`.
- Release restore: `dotnet restore Sushi81.Pos.sln --verbosity:minimal` passed; the initial sandboxed attempt was blocked by NuGet vulnerability-feed network access (`NU1900`) and the authorized retry passed. The desktop runtime restore `dotnet restore src/Sushi81.Pos.Desktop/Sushi81.Pos.Desktop.csproj -r win-x64 --verbosity:minimal` also passed.
- Release build: `dotnet build Sushi81.Pos.sln -c Release --no-restore --verbosity:minimal` passed with 0 warnings and 0 errors.
- Release tests: `dotnet test Sushi81.Pos.sln -c Release --no-restore --no-build --verbosity:minimal` passed with 268 tests: Domain 26, Application 22, Infrastructure integration 41, Architecture 55, OneDrive feasibility 32, and OneDrive feasibility tools 92; 0 failed and 0 skipped.
- Self-contained publish: `dotnet publish src/Sushi81.Pos.Desktop/Sushi81.Pos.Desktop.csproj -c Release -r win-x64 --self-contained true --no-restore --verbosity:minimal` passed to `src/Sushi81.Pos.Desktop/bin/Release/net10.0-windows/win-x64/publish/`.
- CI: The post-push GitHub Actions result for the final documentation head will be recorded here after the branch update. The local Release build and self-contained win-x64 publish passed before delivery.
- PR state: active M04 PR remains open and unmerged; project-owner merge approval is still required.
- Execution gate: OPEN for `M04-REVIEW-FIX-03` at the last durable check.
