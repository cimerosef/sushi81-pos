# V1 implementation status and acceptance traceability

**Status:** Active implementation control document  
**Initialized:** 2026-08-27  
**Current state:** Phase 6 M01 is Passed. The original M02 OneDrive competitive-acquisition design correctly ended Blocked and its evidence was merged through PR #2. The user approved the target-directed authority-handoff specification amendment on 2026-08-28; amended baseline documents are now authoritative and M02 directed-handoff revalidation is explicitly authorized. M03 remains Not started.

## 1. Status vocabulary

- `Not started` — no conforming implementation evidence yet.
- `In progress` — the explicitly authorized milestone is being implemented/revalidated or still has an open gate.
- `Partial` — some evidence exists, but the complete acceptance criterion/gate is not yet satisfied.
- `Passed` — automated/manual evidence required by the criterion is recorded and passes on the applicable build.
- `Blocked — amendment required` — a genuine material specification conflict prevents conforming implementation.
- `Not applicable — amended` — allowed only when an approved specification amendment explicitly makes the criterion inapplicable.

Only `Passed` and properly approved `Not applicable — amended` satisfy the final V1 acceptance gate.

## 2. Milestone status

| Milestone | Status | Authorization / result |
|---|---|---|
| M01 — Foundation and safe persistence spine | Passed | Merged to `main` via PR #1 after automated verification and successful Windows/WPF manual re-verification. |
| M02 — OneDrive feasibility gate | In progress | Original generic-acquisition model: Blocked with evidence merged via PR #2. Approved target-directed amendment is now incorporated; revalidation explicitly authorized by `docs/implementation/milestone-02-directed-handoff-revalidation.md`. |
| M03 — Catalogue and settings | Not started | Pending successful M02 amended-protocol feasibility gate and explicit M03 authorization |
| M04 — Order-entry vertical slice | Not started | Pending M03 |
| M05 — Lifecycle/payments/search/dashboard | Not started | Pending M04 |
| M06 — Local recovery/read-only enforcement | Not started | Pending M05 |
| M07 — Handoff and disaster recovery | Not started | Pending M06; must implement amended target-directed protocol |
| M08 — Printing and reprinting | Not started | Pending M07 |
| M09 — Hiboutik paste fallback | Not started | Pending M08 |
| M10 — Catalogue `.xlsx` | Not started | Pending M09 |
| M11 — Gestion export | Not started | Pending M10 |
| M12 — Annual archive/historical access | Not started | Pending M11 |
| M13 — Installer and final acceptance | Not started | Pending M12 |

## 3. Acceptance ownership matrix

The owner milestone is responsible for closing the criterion. Earlier milestones may provide foundations/feasibility evidence and later M13 performs the final production-target regression.

### Product

| Criterion | Owner | Status | Evidence |
|---|---:|---|---|
| AC-PROD-001 | M13 | Not started | — |
| AC-PROD-002 | M07 | Not started | M02 revalidation prepares amended handoff/offline boundaries |
| AC-PROD-003 | M13 | Not started | — |
| AC-PROD-004 | M13 | Not started | — |

### Catalogue

| Criterion | Owner | Status | Evidence |
|---|---:|---|---|
| AC-CAT-001 | M03 | Not started | — |
| AC-CAT-002 | M03 | Not started | — |
| AC-CAT-003 | M03 | Not started | Historical-snapshot regression closes in M04 |
| AC-CAT-004 | M03 | Not started | — |
| AC-CAT-005 | M03 | Not started | — |
| AC-CAT-006 | M04 | Not started | — |
| AC-CAT-007 | M04 | Not started | — |
| AC-CAT-008 | M10 | Not started | — |
| AC-CAT-009 | M10 | Not started | — |
| AC-CAT-010 | M10 | Not started | — |
| AC-CAT-011 | M10 | Not started | — |
| AC-CAT-012 | M04 | Not started | — |

### Order creation and business rules

| Criterion | Owner | Status | Evidence |
|---|---:|---|---|
| AC-ORD-001 through AC-ORD-010 | M04 | Not started | Record individual tests before marking Passed |
| AC-ORD-011 | M03 | Not started | Pricing integration also exercised in M04 |

### Lifecycle, payments and operational views

| Criterion | Owner | Status | Evidence |
|---|---:|---|---|
| AC-LIFE-001 | M04 | Not started | Windows printing cross-check in M08 |
| AC-LIFE-002 | M04 | Not started | — |
| AC-LIFE-003 through AC-LIFE-014 | M05 | Not started | Record individual tests before marking Passed |
| AC-LIFE-015 | M12 | Not started | Live-search portion implemented in M05; archive portion closes in M12 |

### Hiboutik paste fallback

| Criterion | Owner | Status | Evidence |
|---|---:|---|---|
| AC-HIB-001 through AC-HIB-009 | M09 | Not started | AC-HIB-008 export cross-check in M11 |

### Printing

| Criterion | Owner | Status | Evidence |
|---|---:|---|---|
| AC-PRINT-001 through AC-PRINT-008 | M08 | Not started | Record individual model/integration/manual evidence |
| AC-PRINT-009 | M12 | Not started | — |
| AC-PRINT-010 | M08 | Not started | — |
| AC-PRINT-011 | M08 | Not started | — |

### Export

| Criterion | Owner | Status | Evidence |
|---|---:|---|---|
| AC-EXP-001 through AC-EXP-011 | M11 | Not started | Record individual workbook/ledger tests |

### Storage, handoff, recovery and archive

| Criterion | Owner | Status | Evidence |
|---|---:|---|---|
| AC-STO-001 | M01 | Passed | `tests/Sushi81.Pos.Infrastructure.IntegrationTests/InfrastructureIntegrationTests.cs` covers local paths, SQLite PRAGMAs, migrations, transactional rollback and validated local recovery snapshots. |
| AC-STO-002 through AC-STO-005 | M07 | Not started | Original M02 blocker evidence preserved; amended target-directed feasibility revalidation now authorized |
| AC-STO-006 | M06 | Not started | Snapshot primitive begins in M01 |
| AC-STO-007 through AC-STO-009 | M07 | Not started | M02 revalidation prepares target-binding/transport/failure evidence |
| AC-STO-010 | M06 | Not started | M02 revalidation prepares pending-transfer authority-state semantics; printing exception cross-check in M08 |
| AC-STO-011 through AC-STO-014 | M12 | Not started | — |

### Architecture and deployment

| Criterion | Owner | Status | Evidence |
|---|---:|---|---|
| AC-ARCH-001 through AC-ARCH-004 | M01 | Passed | `tests/Sushi81.Pos.ArchitectureTests/DependencyBoundaryTests.cs`; Release build and win-x64 self-contained publish evidence in section 5. |
| AC-ARCH-005 | M11 | Not started | Catalogue half implemented in M10; export half closes in M11 |
| AC-ARCH-006 | M08 | Not started | — |
| AC-ARCH-007 | M13 | Not started | Durable authority/transfer state preservation added by 2026-08-28 amendment |

### Reliability, security and performance

| Criterion | Owner | Status | Evidence |
|---|---:|---|---|
| AC-NFR-001 | M13 | Not started | Enforced continuously from M01 |
| AC-NFR-002 | M13 | Not started | Test suite grows each milestone |
| AC-NFR-003 | M13 | Not started | Targeted checks begin in M04/M08 |
| AC-NFR-004 | M13 | Not started | Failure paths added each milestone; amended handoff failure paths begin in M02 revalidation |

## 4. Milestone evidence template

For each completed milestone append a short record containing:

- milestone and completion commit SHA;
- production-target framework/runtime and Windows build environment;
- Release build command/result;
- test command/result and total passed/failed/skipped;
- acceptance criteria closed or left Partial;
- manual checks performed and environment;
- migration/data-safety/failure-injection evidence where applicable;
- known non-blocking limitations belonging to later milestones;
- confirmation that no real customer/order/payment/credential data was added.

Do not mark an AC Passed using only a planned test name or an unexecuted checklist.

## 5. M01 implementation and re-verification evidence

**Milestone:** M01 — Executable foundation and safe persistence spine  
**Implementation branch:** `codex/m01-foundation`  
**Accepted implementation head before status-only closure commit:** `000d6feb9fd2975784c69661f6362366843b682a`  
**Latest repair commit:** `2a7e5f9e00ff7356fd9bd2d024e0b9a615b1f4f7`  
**PR #1 final head:** `8a21b137d354f02a8ac2649c78b11c136dda8567`  
**Merged to `main`:** `b8590d1d0a2aee4ec6554ddee43587a257cedc47`  
**Environment:** Windows 10.0.26200 x64; .NET SDK 10.0.400; .NET/WindowsDesktop runtime 10.0.11.

### Delivered structure and dependencies

- Production projects: `Sushi81.Pos.Domain` (`net10.0`), `Sushi81.Pos.Application` (`net10.0`), `Sushi81.Pos.Infrastructure` (`net10.0-windows`) and WPF `Sushi81.Pos.Desktop` (`net10.0-windows`).
- Test projects: Domain (3), Application (2), Infrastructure integration (16) and architecture/localization (8).
- Exact NuGet versions: `Microsoft.Data.Sqlite` 10.0.11, `Microsoft.Extensions.Logging.Abstractions` 10.0.0 and `MSTest` 4.0.2. Central package management pins all direct dependencies.

### Verification

- `dotnet restore Sushi81.Pos.sln`: Passed.
- `dotnet build Sushi81.Pos.sln -c Release --no-restore`: Passed, 0 warnings and 0 errors.
- `dotnet test Sushi81.Pos.sln -c Release --no-build`: Passed: Domain 3/0/0, Application 2/0/0, Infrastructure integration 16/0/0, Architecture/localization 8/0/0 (passed/failed/skipped), 29 total.
- `dotnet publish src/Sushi81.Pos.Desktop/Sushi81.Pos.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false`: Passed; output is generated under the ignored Desktop `bin/Release/net10.0-windows/win-x64/publish/` path.
- GitHub Actions Continuous integration for the accepted implementation head completed successfully; restore, build and test steps all passed.
- The final documentation-only PR head also completed CI successfully before merge.

### Acceptance and safety evidence

- **Passed:** AC-ARCH-001 through AC-ARCH-004 and AC-STO-001. Evidence is in the project-specific test files above, notably dependency/WPF-SQLite boundary checks, path/configuration/authority tests, active SQLite PRAGMA checks, migration failure/rollback/history checks, transaction atomicity, validated WAL-safe recovery snapshots, retention and recovery scheduler tests.
- **Partial:** AC-STO-006 (safe snapshot and scheduler primitives are implemented/tested; business mutation triggers are deferred to M06); AC-PROD-004, AC-NFR-001, AC-NFR-002 and AC-NFR-004 (M01 foundations only; their owner milestones remain unchanged).
- No Catalogue, BusinessSettings, Order, Payment, pricing/VAT, printing, Hiboutik parsing, ClosedXML, export, OneDrive handoff, pairing/disaster-recovery, archive, installer or legacy emergency-model code was added.
- Tests use only synthetic data. No database, log, local configuration, build output, credentials or business data is committed.

### Windows/WPF manual verification

- First interactive check: application launched and French default interface displayed correctly, but selecting Simplified Chinese caused a repeatable UI hang on two attempts. M01 remained blocked at that point.
- Confirmed root cause: synchronous waiting on asynchronous local-configuration persistence from the WPF language-selection path.
- Repair: explicit awaitable language-change operation; persistence completes before localized resources are refreshed, failed persistence preserves the previous selection, and regression tests cover real configuration persistence and synchronization-context behavior.
- Second interactive Windows/WPF check on 2026-08-28: application launched normally; default French interface displayed correctly; the language selector opened normally; switching to Simplified Chinese completed successfully and displayed the Chinese interface without hanging or becoming unresponsive.
- The application was then closed and relaunched; the previously selected Simplified Chinese culture remained selected and the Chinese interface was restored successfully.
- This second manual check closes the previously observed WPF localization blocker. Together with the passing automated suite and CI, M01 is accepted as `Passed`.

## 6. M02 original feasibility evidence

**Milestone:** M02 — OneDrive single-writer feasibility gate  
**Original task definition:** `docs/implementation/milestone-02-onedrive-feasibility.md`  
**Task-definition commit:** `a158e6a49faff831c6236df06e285056410e6225`  
**Original final gate:** `BLOCKED — specification/architecture amendment required`  
**Correction code/tests verified at:** `5f44e34ec4a98b767d01a35846deec75e6d29825`  
**PR #2 final head:** `21a88d51b558ceabe563ddfb80752a37d1dccae4`  
**Merged to `main`:** `5bacafa0e4ca906d8ff058e34586dee43503bc42`.

### Original M02 evidence record

`docs/implementation/milestone-02-feasibility-report.md` records:

- corrected Microsoft `CF_PLACEHOLDER_STATE` values and fail-closed interpretation;
- strict synthetic snapshot/marker metadata, checksum and SQLite validation;
- publication-order tests;
- deterministic N-device delayed/reordered claim simulation;
- executable double-writer counterexample for competitive claim-only acquisition;
- fail-closed protocol safety only by refusing N-device write activation without an external atomic grant;
- Release verification of 67 passed / 0 failed / 0 skipped with 0 build warnings/errors;
- CI success;
- no business/sensitive data committed.

This evidence is retained as the rationale for the amendment. It is not erased or reclassified as a successful proof of the superseded competitive-acquisition model.

## 7. Approved 2026-08-28 M02 specification amendment

**Decision record:** `docs/decisions/target-directed-authority-handoff.md`  
**Amended baseline:** `docs/architecture.md`, `docs/storage-strategy.md`, `docs/acceptance-criteria.md`, `docs/v1-specification-freeze.md`  
**Revalidation task:** `docs/implementation/milestone-02-directed-handoff-revalidation.md`  
**Revalidation status:** Implemented correction pass; final verification/PR head recorded below. Gate remains Partial pending real multi-device transport evidence.

Approved semantics:

- normal close distinguishes retain authority from transfer authority;
- normal transfer is bound to one selected paired target;
- source durably relinquishes business-write authority before the target-releasing marker can exist;
- source remains read-only/pending-transfer after that irreversible point and may retry only the same immutable transfer;
- only the exact target may acquire;
- target acquisition is durably recorded with exact identity/checksum/snapshot evidence before the centralized write gate can return true;
- source `RelinquishedBlocked` becomes `Released` only after both target-bound marker artifacts are observer-confirmed `IN_SYNC` and that transition is durably committed;
- non-target devices do not compete through file claims/election;
- unrecoverable target path uses explicit Disaster Recovery;
- no hosted/Graph/OAuth coordinator is introduced for normal transfer.

M02 remains open until the amended deterministic safety/liveness proof and required real OneDrive transport evidence reach one of the authorized gate conclusions.

M03 must not start until M02 revalidation is accepted and M03 receives an explicit detailed task definition.

## 8. M02 directed-handoff revalidation evidence

Report: `docs/implementation/milestone-02-directed-handoff-revalidation-report.md`.

The directed protocol and durable synthetic persistence proof are complete on `codex/m02-directed-handoff-revalidation` at implementation commit `0e5938d1f3fd7d7bc0af6bf2f25eed31759a01dc` (the final evidence/status commit follows). The correction pass includes an atomic durable target-acquisition store, restart reconstruction, strict wrong-target/stale/replay fail-closed checks, the narrow `IArtifactSyncObserver` boundary, source `Released` transition ordering, clean `RetainClose` cancellation of an uncommitted transfer, and a persistent device-local `DurableLocalAuthorityCursor` (`local-authority-cursor.json`) preserving device ID, lineage, generation, high-water version, current role, transfer ID and revision. `DirectedLifecycleAuthorityGate` is the centralized source/target business-write decision boundary: it reads only local source/target evidence plus the persistent current cursor and never requires a shared mutable lifecycle ledger. Historical target evidence is retained but becomes logically stale after a newer local source release, while the current target cursor advances atomically from v1 to v3. Target-to-next-source promotion uses only the exact local target/source cursor; the source CLI supports restart/resume and the directed lifecycle CLI no longer requires cross-device state. Device B's target path records observer-confirmed Cloud Files `IN_SYNC` observations for the snapshot, ready marker and grant marker before durable acquisition; pending observations remain blocked. The optional append-only ledger is diagnostic/audit history only. Missing or malformed source state, target evidence or local cursor after prior participation is unresolved and cannot be interpreted as a virgin device; `InitializeAuthoritative` rejects reinitialization when a cursor or `target*.json` evidence remains. Target acquisition uses `AcquisitionPending` before target evidence and finalizes `AcquiredTarget` only after both durable facts exist; a crash between those writes remains blocked and an exact retry completes safely. The target CLI now returns failure if persisted acquisition cannot reconstruct a writable local gate. The pure protocol maps `PreparingTransfer` ↔ durable `TransferPrepared`, `RelinquishedPendingGrant` ↔ durable `RelinquishedBlocked`, `TransferReleased` ↔ durable `Released`, and acquired target ↔ durable target state. Release build passed with 0 warnings and 0 errors; the full solution test run passed 118/118 with 0 failed and 0 skipped (Domain 3, Application 2, Infrastructure 16, Architecture 8, protocol 32, directed durable handoff/target/lifecycle 57); the required self-contained `win-x64` publish passed with `PublishSingleFile=false`. GitHub Actions Continuous Integration has completed successfully for the pushed branch; the PR checks remain authoritative for the final pushed head. The harness observed 0 registered sync roots, so no real two-device OneDrive transport evidence was available. M02 revalidation gate: **Partial — real multi-device evidence still required**. No amended AC-STO criterion is marked Passed; this is preparation evidence for the M07 owner milestone. M03 remains Not started.
