# M04 implementation worklog

**Status:** Authorized / `M04-REVIEW-FIX-06` complete pending ChatGPT review, manual rerun and project-owner merge approval
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
- GitHub issue #4 is OPEN for the authorized `M04-REVIEW-FIX-06` remediation; no later milestone is being inferred.
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

## Implementation evidence — `M04-REVIEW-FIX-04`

- The WPF Caisse now presents a category-first two-pane navigator: category names remain visible beside the active product list, with code/name search retained as the secondary filter. Product headers are assigned through the existing presentation seam so French and Simplified Chinese remain deterministic after language changes.
- Simple products whose options workflow is disabled are added directly from the actual WPF Add and product-row double-click paths. Products with options enabled continue through the existing option-selection dialog; no shortcut category state or `RaccourciCat` persistence was introduced.
- Planned time was initially implemented as a broad structured selection; the superseding addendum below now requires an approved hour/minute slot for every new POS confirmation while retaining exact `TimeOnly` conversion and historical null-time readability. New-order dates are initialized and UI-limited from `IBusinessClock.BusinessDate`, while `OrderEntryService` rejects any past planned date with a stable localized validation code. Same-day and future dates remain valid.
- The cart template uses a stretched row with fixed total/action columns so quantity/remove controls share one right edge across rows. Address/comment inputs receive usable stretch width, and the ID/snapshot panel stays visible so a successful confirmation is immediately discoverable and reloadable.
- Automated evidence includes the new Application past-date no-write/no-dispatch test and an STA/WPF operator-path regression covering visible categories, localized product headers, direct Add, direct double-click, fixed cart alignment, date guard, address width and immediate saved-order discovery. Full local verification is recorded below; no manual Windows/WPF acceptance is claimed because the prior operator run stopped on the recorded first-use findings and must be rerun on the published artifact.
- `POST_TASK_POWER_ACTION: NONE`.

## Implementation evidence — `M04-REVIEW-FIX-04-TIME-ADDENDUM-01`

- New POS confirmation requires `PlannedFulfilmentTime` for both Retrait and Livraison. Missing or programmatically invalid time fails before persistence and dispatch; a manual total override cannot bypass the requirement.
- The WPF Caisse exposes exactly the approved hour choices `11, 12, 13, 14, 18, 19, 20, 21, 22` and minute choices `00, 05, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55` through two click-friendly selectors. There is no free-form time textbox, a new order starts unselected, blank time keeps confirmation unavailable, and time-only changes preserve a manual total override.
- FR → zh-CN → FR switching preserves the selected time. The nullable persistence field is unchanged so historical snapshots with a null planned time remain readable; no destructive migration was introduced.
- Automated application and STA/WPF coverage for the addendum is recorded in the delivery record below after verification. No manual acceptance is claimed.
- `POST_TASK_POWER_ACTION: NONE`.

## Implementation evidence — `M04-REVIEW-FIX-05`

- The Application approved-time predicate now requires the approved hour, a five-minute minute value, and zero seconds/sub-minute `TimeOnly` ticks. Invalid programmatic values such as `11:05:30` and `11:05:00.0000001` fail with `PlannedTimeInvalid` before persistence or dispatch; exact `11:05:00` remains valid.
- Focused Application tests cover the exact approved success, non-zero seconds rejection, non-zero sub-second tick rejection, zero persistence/dispatch on both failures, and the existing missing-time/invalid-hour boundaries. WPF slot tests remain green and unchanged.
- No WPF choice, historical null-time compatibility, schema, or M05 scope changed. Manual acceptance remains unclaimed.
- `POST_TASK_POWER_ACTION: NONE`.

## Delivery record

- Prior implementation/evidence head: `358d7105bf51f39b5de4b514daafcaed63730a3b`.
- Review-fix implementation commit: `23d4ef35554ded423f23cf9f8fd63fa901bd848b` (`fix: close M04 review remediation gaps`).
- Migration identity: version `3`, `create-orders-and-sale-snapshots`.
- Release restore: `dotnet restore Sushi81.Pos.sln --verbosity:minimal` passed; the initial sandboxed attempt was blocked by NuGet vulnerability-feed network access (`NU1900`) and the authorized retry passed. The desktop runtime restore `dotnet restore src/Sushi81.Pos.Desktop/Sushi81.Pos.Desktop.csproj -r win-x64 --verbosity:minimal` also passed.
- Release build: `dotnet build Sushi81.Pos.sln -c Release --no-restore --verbosity:minimal` passed with 0 warnings and 0 errors.
- Release tests: `dotnet test Sushi81.Pos.sln -c Release --no-restore --no-build --verbosity:minimal` passed with 268 tests: Domain 26, Application 22, Infrastructure integration 41, Architecture 55, OneDrive feasibility 32, and OneDrive feasibility tools 92; 0 failed and 0 skipped.
- Self-contained publish: `dotnet publish src/Sushi81.Pos.Desktop/Sushi81.Pos.Desktop.csproj -c Release -r win-x64 --self-contained true --no-restore --verbosity:minimal` passed to `src/Sushi81.Pos.Desktop/bin/Release/net10.0-windows/win-x64/publish/`.
- CI: GitHub Actions `Continuous integration` run **#244** completed successfully for pushed head `7f1946b8e91211017c93ee7fcbd94c6889690312` (the final implementation/evidence head before this status-only CI-record update). Restore, build and test steps passed.
- PR state: active M04 PR remains open and unmerged; project-owner merge approval is still required.
- Review-fix implementation commit: `f73a3939417642cc9633f2c779d13a45febd58b9` (`fix: remediate M04 order entry ergonomics`).
- Release restore: `dotnet restore Sushi81.Pos.sln --verbosity:minimal` passed after the initial sandboxed NuGet vulnerability-feed failure (`NU1900`) was retried with authorized network access. The runtime-specific Desktop restore for `win-x64` likewise passed after the sandboxed repository-signature access failure (`NU1301`) was retried with authorized network access.
- Release build: `dotnet build Sushi81.Pos.sln -c Release --no-restore --verbosity:minimal` passed with 0 warnings and 0 errors.
- Release tests: `dotnet test Sushi81.Pos.sln -c Release --no-restore --no-build --verbosity:minimal` passed with 270 tests: Domain 26, Application 23, Infrastructure integration 41, Architecture 56, OneDrive feasibility 32, and OneDrive feasibility tools 92; 0 failed and 0 skipped.
- Self-contained publish: `dotnet publish src/Sushi81.Pos.Desktop/Sushi81.Pos.Desktop.csproj -c Release -r win-x64 --self-contained true --no-restore --verbosity:minimal` passed to `src/Sushi81.Pos.Desktop/bin/Release/net10.0-windows/win-x64/publish/`.
- CI: GitHub Actions `Continuous integration` run **#252** completed successfully for pushed head `f73a3939417642cc9633f2c779d13a45febd58b9`; the `build-and-test` job Restore, Build and Test steps all passed.
- PR state: active M04 PR remains open and unmerged; project-owner merge approval is still required.
- Execution gate: OPEN for `M04-REVIEW-FIX-04-TIME-ADDENDUM-01` at the last durable check.

## Delivery record — `M04-REVIEW-FIX-04-TIME-ADDENDUM-01`

- Implementation commit: `09d289b7499258dc550f463fcb05bce1d931fd23` (`fix: enforce approved M04 planned time slots`); branch `codex/m04-order-entry` was pushed to PR #6.
- Release build: `dotnet build Sushi81.Pos.sln -c Release --no-restore --verbosity:minimal` passed with 0 warnings and 0 errors.
- Release tests: `dotnet test Sushi81.Pos.sln -c Release --no-restore --no-build --verbosity:minimal` passed with 274 tests: Domain 26, Application 27, Infrastructure integration 41, Architecture 56, OneDrive feasibility 32, and OneDrive feasibility tools 92; 0 failed and 0 skipped.
- Self-contained publish: `dotnet publish src/Sushi81.Pos.Desktop/Sushi81.Pos.Desktop.csproj -c Release -r win-x64 --self-contained true --no-restore --verbosity:minimal` passed to `src/Sushi81.Pos.Desktop/bin/Release/net10.0-windows/win-x64/publish/`.
- CI: GitHub Actions `Continuous integration` run **#256** completed successfully for implementation commit `09d289b7499258dc550f463fcb05bce1d931fd23`; Restore, Build and Test all passed.
- Manual acceptance remains unclaimed. PR #6 remains open and unmerged, `POST_TASK_POWER_ACTION: NONE`, and M05 was not started.

## Implementation evidence — `M04-REVIEW-FIX-06`

- Added the approved additive schema version 4 migration `add-category-short-codes-and-planned-order-index`. It adds nullable `categories.short_code` and `normalized_short_code`, a partial unique normalized-code index, and the narrow `(planned_fulfilment_date, planned_fulfilment_time, order_id)` order-browser index. Existing v3 reads and legacy category mutations remain compatible until the v4 migration runs; migration tests preserve existing categories, products and orders and verify blank/null migrated codes.
- Category short codes are optional, independent from the full category name, trim-preserving for display, case-insensitive/Unicode-normalized for uniqueness, length-bounded, editable and explicitly clearable. Caisse uses the code as primary navigation label with full-name fallback; maintenance shows code plus full name and uses deterministic coded-first ordering.
- The read-only order browser queries persisted `planned_fulfilment_date` for past, current or future dates and returns planned time, fulfilment mode, status, Total TTC and telephone. It does not filter by lifecycle status, loads the selected row through exact stable-ID snapshot reload, preserves the selected date/row/snapshot across refresh and language changes, and selects the newly committed same-date row while retaining earlier rows.
- All planned-time presentation paths now share invariant `HH:mm` formatting; the evening regression asserts `18:25`, never `06:25`.
- New domain, application, infrastructure and STA/WPF regressions cover code validation/forwarding, v3→v4 preservation, normalized code uniqueness/edit/clear, date filtering/order/status/null-time/restart behavior, date-picker scope, browser columns, exact selection, post-confirmation refresh, language preservation and 24-hour rendering. Manual Windows/WPF acceptance is still unclaimed; the implementation remains limited to M04 and M05 was not started.
- `POST_TASK_POWER_ACTION: NONE`.

## Delivery record — `M04-REVIEW-FIX-06`

- Final implementation/evidence SHA, release verification totals, self-contained publish result and CI run are recorded here after the final local and remote checks.
- PR #6 remains open and unmerged; project-owner merge approval is required. The execution gate was OPEN for this handoff.

## Delivery record — `M04-REVIEW-FIX-05`

- Implementation commit: `bf0a6e7888619bcb573500591500c46eae0434ef` (`fix: reject non-exact planned time values`); branch `codex/m04-order-entry` was pushed to PR #6.
- Release build: `dotnet build Sushi81.Pos.sln -c Release --no-restore --verbosity:minimal` passed with 0 warnings and 0 errors.
- Release tests: `dotnet test Sushi81.Pos.sln -c Release --no-restore --no-build --verbosity:minimal` passed with 277 tests: Domain 26, Application 30, Infrastructure integration 41, Architecture 56, OneDrive feasibility 32, and OneDrive feasibility tools 92; 0 failed and 0 skipped.
- Self-contained publish: `dotnet publish src/Sushi81.Pos.Desktop/Sushi81.Pos.Desktop.csproj -c Release -r win-x64 --self-contained true --no-restore --verbosity:minimal` passed to `src/Sushi81.Pos.Desktop/bin/Release/net10.0-windows/win-x64/publish/`.
- CI: GitHub Actions `Continuous integration` run **#259** completed successfully for implementation commit `bf0a6e7888619bcb573500591500c46eae0434ef`; Restore, Build and Test all passed.
- Manual acceptance remains unclaimed. PR #6 remains open and unmerged, `POST_TASK_POWER_ACTION: NONE`, and M05 was not started.
