# M04 implementation worklog

**Status:** Authorized / implementation in progress; automated verification passing
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
- GitHub issue #4 is OPEN for the single authorized `M04-IMPLEMENT-01` handoff; no later handoff or milestone is being inferred.
- M05 is not authorized.

## Evidence policy

Codex must append/update this record truthfully as implementation proceeds. Record exact implementation heads, migration identity, test/build/publish/CI results, failure-path evidence, acceptance mapping, execution topology and outstanding/complete Windows/WPF operator acceptance.

Manual acceptance must never be fabricated. PR merge remains explicitly reserved for project-owner approval after ChatGPT review.

## Implementation evidence — current working head

- Domain pricing and immutable sale snapshots are implemented in `src/Sushi81.Pos.Domain/OrderEntry.cs`.
- Application order-entry catalogue, pricing, confirmation, exact-ID reload and post-commit dispatch seams are implemented in `src/Sushi81.Pos.Application/OrderEntry/OrderEntryContracts.cs`.
- SQLite migration 3 and transactional snapshot persistence are implemented in `src/Sushi81.Pos.Infrastructure/Migrations/M04Migrations.cs` and `src/Sushi81.Pos.Infrastructure/Order/SqliteOrderStore.cs`.
- The WPF Caisse destination preserves M03 Catalogue/Settings and exposes active-product browse, option/custom configuration, cart editing, fulfilment, total override, confirmation and exact-ID reload.
- A1/B1/C1, option validation, Livraison fee/minimum, manual total tax bucket, v2→v3 migration, historical independence, rollback and dispatch-failure tests are present in the Domain and Infrastructure test projects.
- `POST_TASK_POWER_ACTION: NONE`.
- The Windows/WPF helper launch was attempted, but the built application did not expose a target window in this environment; no manual operator acceptance is claimed. The remaining manual checklist in `milestone-04-order-entry.md` must be completed on a usable Windows session.
- M05+ implementation was not started.

## Delivery record

- Implementation commit: `b1b6770` (`feat: implement M04 order-entry vertical slice`).
- Migration identity: version `3`, `create-orders-and-sale-snapshots`.
- Verification: `dotnet test Sushi81.Pos.sln --no-restore` passed with 232 tests; `dotnet publish src/Sushi81.Pos.Desktop/Sushi81.Pos.Desktop.csproj -c Release -r win-x64 --self-contained true --no-restore` passed.
- CI: not yet observed for the pushed commit.
- PR state: active M04 PR remains open and unmerged; project-owner merge approval is still required.
- Execution gate: OPEN for `M04-IMPLEMENT-01` at the last durable check.
