# V1 implementation status and acceptance traceability

**Status:** Active implementation control document  
**Initialized:** 2026-08-27  
**Current state:** Phase 6 M01 is Passed. The original M02 OneDrive competitive-acquisition design correctly ended Blocked and its evidence was merged through PR #2. The approved target-directed authority-handoff amendment and GitHub transport revalidation are Passed. M03 Catalogue and Settings implementation plus the review and manual-UI remediations through `M03-MANUAL-UI-LIVE-FILTER-FIX-12` are complete on `codex/m03-catalogue-settings`; the authorized AC-CAT-013 filtered bulk activation/deactivation extension is implemented with automated evidence, while the self-contained publish artifact and interactive Windows/WPF M03 acceptance remain explicit operator checks.

## 1. Status vocabulary

- `Not started` — no conforming implementation evidence yet.
- `In progress` — the explicitly authorized milestone is being implemented/revalidated or still has an open gate.
- `Partial` — some evidence exists, but the complete acceptance criterion/gate is not yet satisfied.
- `Passed` — automated/manual evidence required by the criterion is recorded and passes on the applicable build.
- `Blocked — amendment required` — a genuine material specification conflict prevents conforming implementation.
- `Blocked — architecture decision required` — the approved protocol remains safe, but a required transport capability is not documented by the current platform boundary.
- `Not applicable — amended` — allowed only when an approved specification amendment explicitly makes the criterion inapplicable.

Only `Passed` and properly approved `Not applicable — amended` satisfy the final V1 acceptance gate.

## 2. Milestone status

| Milestone | Status | Authorization / result |
|---|---|---|
| M01 — Foundation and safe persistence spine | Passed | Merged to `main` via PR #1 after automated verification and successful Windows/WPF manual re-verification. |
| M02 — remote handoff feasibility gate | Passed | Original competitive OneDrive model remains historically Blocked. Approved GitHub private Release Asset transport, automated failure evidence, real private-repository A → B v1 / B → A v2 round-trip evidence, and isolated destructive newest-three retention evidence are complete; the amended M02 gate is closed by evidence. |
| M03 — Catalogue and settings | Partial — automated scope complete; manual WPF check pending | Authorized by `CODEX_HANDOFF_READY: M03-IMPLEMENT-01`; migration 2, layered catalogue/settings services, localized WPF maintenance shell and automated evidence are complete. The manual checklist is intentionally not marked Passed until an operator verifies it on Windows. |
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
| AC-CAT-001 | M03 | Partial | Automated domain/integration coverage; manual WPF catalogue workflow pending |
| AC-CAT-002 | M03 | Passed | Normalized category uniqueness and rename tests in `tests/Sushi81.Pos.Infrastructure.IntegrationTests/M03CatalogueIntegrationTests.cs` |
| AC-CAT-003 | M03 | Partial | Current-product maintenance is implemented/tested; historical-order independence remains an M04 snapshot regression |
| AC-CAT-004 | M03 | Passed | Required-field, price and VAT boundary validation tests |
| AC-CAT-005 | M03 | Passed | Structured group/option validation, ordering, signed adjustments and aggregate persistence tests |
| AC-CAT-006 | M04 | Not started | — |
| AC-CAT-007 | M04 | Not started | — |
| AC-CAT-008 | M10 | Not started | — |
| AC-CAT-009 | M10 | Not started | — |
| AC-CAT-010 | M10 | Not started | — |
| AC-CAT-011 | M10 | Not started | — |
| AC-CAT-012 | M04 | Not started | — |
| AC-CAT-013 | M03 | Partial | Application/SQLite/presentation automation is implemented and green; manual filtered-bulk Windows/WPF checklist remains outstanding |

### Order creation and business rules

| Criterion | Owner | Status | Evidence |
|---|---:|---|---|
| AC-ORD-001 through AC-ORD-010 | M04 | Not started | Record individual tests before marking Passed |
| AC-ORD-011 | M03 | Partial | BusinessSettings UI/persistence contract and exact round-trip tests; pricing-consumer cross-check remains in M04 |

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
**Historical directed revalidation status:** Implemented correction pass; its OneDrive-only transport conclusion is superseded by the approved GitHub transport amendment below. Real private-repository A → B v1 and B → A v2 evidence and the isolated live-retention observation are recorded; the amended M02 gate is Passed.

Approved semantics:

- normal close distinguishes retain authority from transfer authority;
- normal transfer is bound to one selected paired target;
- source durably relinquishes business-write authority before the target-releasing marker can exist;
- source remains read-only/pending-transfer after that irreversible point and may retry only the same immutable transfer;
- only the exact target may acquire;
- target acquisition is durably recorded with exact identity/checksum/snapshot evidence before the centralized write gate can return true;
- source `RelinquishedBlocked` becomes `Released` only after GitHub snapshot and grant Release Asset server receipts are validated and that transition is durably committed. OneDrive `IN_SYNC` is historical M02 evidence only, not the current normal-handoff acknowledgement;
- non-target devices do not compete through file claims/election;
- unrecoverable target path uses explicit Disaster Recovery;
- no hosted/Graph/OAuth coordinator is introduced for normal transfer.

The amended M02 deterministic safety/liveness proof, transport architecture decision and isolated live-retention evidence are now recorded; the amended M02 gate is closed as Passed. The historical OneDrive observer still demonstrates a false negative rather than local confirmation and remains preserved as historical evidence.

M03 must not start until M02 revalidation is accepted and M03 receives an explicit detailed task definition.

## 8. Historical M02 directed-handoff revalidation evidence

Report: `docs/implementation/milestone-02-directed-handoff-revalidation-report.md`.

The historical directed protocol and durable persistence evidence were verified at implementation commit `0e5938d1f3fd7d7bc0af6bf2f25eed31759a01dc`; prior CI run #75 covered evidence head `49c9866576dee27b35af62b0e177464b176830eb`. That historical record includes the atomic durable target-acquisition store, restart reconstruction, strict wrong-target/stale/replay fail-closed checks, the narrow `IArtifactSyncObserver` boundary, source `Released` ordering, clean `RetainClose` cancellation, the persistent device-local authority cursor, the centralized lifecycle-aware write gate, and the independent-device lifecycle regression. It also records the real Home Device A OneDrive observation (`CF_PLACEHOLDER_STATE_NO_STATES`, raw 0) as a historical false negative: source remained `TransferPrepared` with null snapshot/marker evidence and no ready/grant markers. This historical OneDrive conclusion is superseded for normal handoff by the approved GitHub transport amendment; no AC was marked Passed from it. M03 remains Not started.

## 9. M02 GitHub transport revalidation (current)

**Status:** `Passed — amended GitHub target-directed handoff safety/liveness and live retention evidence complete`
**Implementation branch:** `codex/m02-directed-handoff-revalidation`
**Approved sources:** `docs/decisions/github-handoff-transport.md`, `docs/implementation/milestone-02-github-transport-revalidation.md`
**Historical evidence:** the original OneDrive competitive blocker and Home Device A `NO_STATES` false-negative remain above and in the dedicated M02 report; they are not erased or relabeled as passes.

The implementation adds direct .NET `HttpClient` GitHub Release Asset transport against a configurable dedicated private repository. The source flow requires HTTP 201, `uploaded`, exact name/size, asset ID and matching `sha256:<hex>` receipt for the snapshot before durable relinquishment; it then uploads/validates the exact-basename grant before `Released`. The target flow validates grant metadata, exact referenced asset identity including source device, authenticated download, local hash/size and SQLite integrity before the existing pending/evidence/final-cursor write gate. The GitHub target wrapper reconstructs the device-local source state so a returning device can validate its exact Released predecessor for B → A v2 and A → B v3. A valid receipt from an earlier completed transfer is ignored for a later transfer, while malformed or same-transfer receipts still fail closed. Retention validates complete snapshot+grant units release-wide and retains exactly the newest three by generation/handoff-version metadata; cleanup failure is retryable and cannot roll authority back, including reconvergence after a newer unit arrives during an old-plan retry. Grant upload failure and pre-`Released` commit interruption are restart-safe and do not duplicate an acknowledged asset. Gateway failures remove only exact-ID `starter` assets and retry once; receipt/grant artifacts use crash-atomic durable replacement and strict grant validation. Credentials are read only from `SUSHI81_GITHUB_HANDOFF_TOKEN` and are not serialized/logged. Recovery-hardening implementation code head: `edc7fdc5ccdd75f8d97f9cb0d6c2bb239a95dea8`. Evidence-closure documentation head: `b78fd34465f8ae49df52c43e2428eea3337512fb`; local verification is complete and the GitHub Actions check for this head is awaiting connector exposure.

The current automated GitHub transport evidence is synthetic/fake-HTTP: the latest correction pass records 153 passed, 0 failed and 0 skipped across the solution (Domain 3, Application 2, Infrastructure integration 16, Architecture 8, protocol 32, GitHub wrapper/harness 92), with 0 Release build warnings/errors and a self-contained win-x64 publish. Coverage includes A → B v1 → B → A v2 → A → B v3 through the GitHub wrappers, stale-receipt replacement, release-wide retention, grant restart/retry, exact-ID starter deletion after 502, crash-atomic receipt/grant persistence, strict malformed-grant rejection, post-plan retention reconvergence, target truncation/hash/SQLite corruption, duplicate-name 422, upstream 502/starter and timestamp collision. No token, credential, database or business data is committed. The report also preserves sanitized real historical Home Device A OneDrive evidence; that OneDrive `IN_SYNC` observation is historical only.

### Real private-repository operator evidence (sanitized)

Final evidence-closure documentation commit: `ed73fc99e77fed79e36707126ec3fda4ff2388c1`. The preceding `b78fd34465f8ae49df52c43e2428eea3337512fb` entry records the initial evidence commit; the final metadata alignment is included in the commit above.

- Dedicated private repository: `cimerosef/sushi81-pos-handoff`; release tag `sushi81-handoff-v1`; release ID `378925560`.
- A → B v1 (`device-a` → `device-b`, generation 7, version 1, lineage `ca9dfdd4-fa48-4002-b993-23ce5c52a141`, transfer `fc235936-64a6-460b-bcaf-f2f0b212790e`) completed with snapshot asset `534990583` (`20260829104435.snapshot.db`, 8192 bytes, digest `sha256:67ec69e5e1cbb73fd0b312a759390e6696f87563a2c0459eee1d299a260091e1`) and grant asset `534990601` (`20260829104435.grant.json`, 699 bytes, digest `sha256:b37034026a16b7f219068dfcd40754674e10bb5c9e4914e83b1b101116e72dab`). Device A reached durable `Released` and a fresh process confirmed `mayBusinessWrite=false`; Device B acquired the exact target-bound pair.
- B → A v2 (same lineage/generation, version 2, transfer `e55cc196-cd67-4b9a-a15e-662ac3e0f0ea`) completed with snapshot asset `535105715` (`20260829125825.snapshot.db`, 8192 bytes, digest `sha256:67ec69e5e1cbb73fd0b312a759390e6696f87563a2c0459eee1d299a260091e1`) and grant asset `535105732` (`20260829125825.grant.json`, 699 bytes, digest `sha256:5bb7c3f441990a58121538ed97a574c5e074416260308ff41b86c22ae21e5bbd`). Device A validated protocol version 1, exact source/target, and `IsValid=true`; acquisition returned `target-acquired`, durable `Acquired`, `mayBusinessWrite=true`, and the exact checksum/8192-byte snapshot. A fresh process repeated the run as `already-acquired` with the same durable state.
- The earlier real release inspection found exactly four assets (two complete handoff units), so newest-three retention had no eligible deletion at that stage. The separate disposable-release drill below supplies the now-complete destructive live-retention evidence. No token, PAT, authorization header, business snapshot contents or personal file listing was recorded.

The earlier `ca9dfdd4-fa48-4602-b993-23ce5c52a141` command is retained only as fail-closed historical evidence: it returned `transfer-state-mismatch`, left the source prepared, and created no remote asset. It is not the identity of the successful transfers above. M03 remains Not started.

### Retention observation preparation and completed drill

The current CLI was audited for a safe same-home drill. GitHub target acquisition writes only the target directory's durable cursor; the existing transport-independent `directed-target-promote` then advances that local cursor to source authority. No authority JSON needs to be copied or forged, so the drill is feasible only with two fresh synthetic state directories and a separate disposable GitHub release. The real `sushi81-handoff-v1` release is explicitly excluded: retention is release-wide and a four-unit drill there could delete the real v1 pair (`534990583`/`534990601`) and potentially other real evidence assets.

The detailed one-command-at-a-time sequence is recorded in `docs/implementation/milestone-02-directed-handoff-revalidation-report.md`. It used an automatically generated release tag and GUID lineage, printed a token-free preflight summary for operator confirmation, and stored separate state directories under the user's profile (outside AppData/TEMP and never under the real `Sushi81-M02-Test\device-a|device-b` paths). The completed disposable drill moved `0 → 2 → 4 → 6` complete assets for A → B v1, B → A v2 and A → B v3 with no deletion; B → A v4 logically reached `8` inside source completion and converged externally to exactly `6`, deleting only the recorded disposable oldest pair. The transient eight-asset state was not externally inspectable because cleanup is part of the source command; this limitation is recorded rather than bypassed. The disposable Release and synthetic state directories remain preserved as audit evidence, and the M02 gate is Passed.

The completed drill used repository `cimerosef/sushi81-pos-handoff`, release ID `378975662`, tag `sushi81-retention-prep-20260829142002`, lineage `3cdde18b-048e-4a0e-b894-fb901d54e6c4`, generation `1`. It retained v2/v3/v4 pairs (`535191449`/`535191462`, `535193129`/`535193154`, `535195062`/`535195080`) and removed only the disposable v1 pair (`535189220`/`535189245`). Final A v4 acquisition returned `target-acquired`, `acquisitionSucceeded=true`, `mayBusinessWrite=true`; B remained the read-only v4 source. The successful real contract lineage remains `ca9dfdd4-fa48-4002-b993-23ce5c52a141`; `...4602...` remains historical fail-closed evidence only.

## 10. M03 catalogue and settings implementation evidence

**Milestone:** M03 — Catalogue and business settings
**Implementation branch:** `codex/m03-catalogue-settings`
**Handoff authorization:** `CODEX_HANDOFF_READY: M03-IMPLEMENT-01` on the active M03 implementation PR
**Implementation/evidence commits:** `466dd06e5f9e7d1e2ea7d75b8a02d14d5f569a64`, `5a36bf9e6e656db842c4bebc464289ade3e1b436`, `80647b131d7a52b410675ba2802d68be29f3d891`
**Remediation handoffs:** `CODEX_HANDOFF_READY: M03-REVIEW-FIX-02`; post-fix implementation head `710d95b2dcb75995428ace25cc38081afd48127a` passed GitHub Actions Continuous integration run **#133** (success). Follow-up `CODEX_HANDOFF_READY: M03-MANUAL-UI-FILTER-FIX-03` is implemented at head `e6fc0ddeb8077cab33758da9f62cb615300b2cb7` and passed CI run **#137** (success; durable completion records include check URLs). The category-binding remediation `CODEX_HANDOFF_READY: M03-MANUAL-UI-CATEGORY-BINDING-FIX-04` is implemented at head `1053c9b210cac15343959aac8f9ffa2c13ccd9b8`; its final CI result is recorded in the matching PR completion evidence. The first status-key remediation `CODEX_HANDOFF_READY: M03-MANUAL-UI-STATUS-BINDING-FIX-05` is implemented at head `e56ec77b4d13898c40d57be600652309d77938df`; the follow-up lifecycle remediation `CODEX_HANDOFF_READY: M03-MANUAL-UI-STATUS-LIFECYCLE-FIX-06` is implemented at head `a3bab3de2b43e14a415f4251ab1b6dd77cf61aa0`; the category-manager layout remediation `CODEX_HANDOFF_READY: M03-MANUAL-UI-CATEGORY-LAYOUT-FIX-07` is implemented in the current final head; `CODEX_HANDOFF_READY: M03-MANUAL-UI-CATEGORY-CREATE-ACTION-FIX-08` is implemented in the current final head; `CODEX_HANDOFF_READY: M03-MANUAL-UI-CATALOGUE-HEADER-FIX-09` is implemented at head `9bd4e057400abe0110ae95fdeb84efed43eb80a4`; `CODEX_HANDOFF_READY: M03-MANUAL-UI-OPTION-GROUP-ADD-CRASH-FIX-10` is implemented at head `64dc110bd2d6c6b9a623d4c3ba487210e014c178` and passed CI run **#173** (success); `CODEX_HANDOFF_READY: M03-MANUAL-UI-OPTION-GROUP-LAYOUT-FIX-11` is implemented in the current remediation head, with its final CI result recorded in the matching PR completion evidence; and `CODEX_HANDOFF_READY: M03-MANUAL-UI-LIVE-FILTER-FIX-12` implementation/evidence head `b3e34d0d79a4efd39dd3817d19cd107a0f7af50c` passed CI run **#189** (success). Each earlier final CI result is recorded in the matching PR completion evidence.
**Status:** Partial pending the operator's interactive Windows/WPF checklist; no automated blocker remains.

### Delivered scope

- Domain records and validation for Category, Product, OptionGroup, Option and BusinessSettings, including canonical
  Unicode/case/whitespace normalization, decimal VAT/rate boundaries, signed integer-cent money and required-choice
  satisfiability.
- Application-owned, narrow catalogue/settings contracts and services. All product aggregate writes use the existing
  transaction boundary; no generic repository or speculative feature framework was added.
- SQLite migration **version 2**, `create-catalogue-and-business-settings`, creates only the five M03 tables and inserts
  the singleton defaults exactly once. Product/group/option writes preserve opaque identities, created timestamps,
  explicit deletions and deterministic contiguous order; uniqueness/FK races return stable validation results.
- Localized WPF administration shell with exactly Catalogue and Settings destinations, French/zh-CN resources, category
  manager, product/group/option editor, explicit activation/deactivation and permanent-delete confirmation, empty-state
  and status/category filters, and persisted settings editing. No order, payment, pricing engine, printing, import/export,
  Hiboutik, handoff UI, recovery UI or M04 code was added.

### Verification evidence

- Environment: Windows x64, .NET SDK 10.0.400 (runtime 10.0.11).
- `dotnet restore Sushi81.Pos.sln`: passed.
- `dotnet build Sushi81.Pos.sln -c Release --no-restore`: passed with 0 warnings and 0 errors.
- `dotnet test Sushi81.Pos.sln -c Release --no-build`: **215 passed, 0 failed, 0 skipped** (Domain 10; Application 11;
  Infrastructure integration 33; Architecture/localization 37; protocol 32; GitHub wrapper/harness 92, including the
  solution's existing wrapper test project instance).
- `dotnet publish src/Sushi81.Pos.Desktop/Sushi81.Pos.Desktop.csproj -c Release -r win-x64 --self-contained true
  -p:PublishSingleFile=false`: passed; output remains under the ignored Desktop publish directory.
- Tests use isolated temporary SQLite paths and synthetic/sanitized catalogue values only. No database, logs, local
  configuration, credentials, tokens, build artifacts or real customer/order/payment data are committed.

### Remediation evidence

The correction pass closes review findings A–I: read-only query paths with no missing-DB creation; strict non-coercing numeric
parsing; stable localized field validation; dirty product close protection; explicit category Create/Rename Save/Cancel; stateful
activation action labels; immediate localized All-filter/label refresh; deterministic §16 test inventory; and refreshed worklog/status
evidence. The subsequent `M03-MANUAL-UI-FILTER-FIX-03` remediation adds explicit All-filter selection, semantic filter preservation
across language/refresh, deterministic missing-category fallback, and no-selection guards for Edit/Delete. The
`M03-MANUAL-UI-CATEGORY-BINDING-FIX-04` remediation replaces object-instance category selection with the stable
`SelectedCategoryId`/`SelectedValuePath="Id"` binding shape, tolerates transient null writes during collection replacement, and
adds binding-facing regressions for empty-state All selection, localized label replacement, real-category preservation and
removal fallback. The `M03-MANUAL-UI-STATUS-BINDING-FIX-05` remediation applies the same stable-key approach to status filters:
`SelectedStatusKey`/`SelectedValuePath="Key"` preserves All/Active/Inactive through localized collection replacement, ignores
transient null writes while empty, and deterministically falls back to All. Binding-facing tests cover All round trips plus
Active/Inactive localization and refresh preservation. The follow-up `M03-MANUAL-UI-STATUS-LIFECYCLE-FIX-06` removes the
collection-replacement hazard entirely: the three semantic options are stable bindable objects for the view-model lifetime and
localization mutates only their labels in place. Tests capture and verify object identity/position across both language
directions and refresh, plus All/Active/Inactive preservation and invalid/null fallback. The follow-up
`M03-MANUAL-UI-CATEGORY-LAYOUT-FIX-07` replaces the category-manager DockPanel with a star/Auto Grid layout and a top-aligned
WrapPanel action area with content-sized rows and explicit minimum button sizes; its structural presentation regression is included
in the architecture suite. The follow-up `M03-MANUAL-UI-CATEGORY-CREATE-ACTION-FIX-08` exposes the category edit action state as
testable presentation semantics, focuses and enables the name field on Create/Rename, disables conflicting commands while editing,
and restores the actionable state after Cancel or successful Save. The follow-up `M03-MANUAL-UI-CATALOGUE-HEADER-FIX-09` removes
the unsupported DataGridColumn-to-Window header bindings, applies all six localized labels through a direct presentation seam on
construction/load/language change, and gives the grid fixed Code/TTC/VAT/Active widths plus flexible Name/Category star sizing.
The deterministic regression proves effective French → zh-CN → French labels from the real resource dictionaries and the grid
presentation contract. M03 remains Partial because the complete post-remediation Windows/WPF checklist remains an operator
gate; M04 is not started and is not authorized.

The follow-up `M03-MANUAL-UI-OPTION-GROUP-ADD-CRASH-FIX-10` addresses the next operator-reproduced crash in the shown Product
Editor. `GroupEditor` now exposes and owns its Border container; all dynamic group add/remove/reorder operations use that
container instead of inserting its already-parented StackPanel child. The child editor captures the shell localization
dictionary explicitly, and SelectionMode has dedicated French/zh-CN resources. A real STA WPF modal-dialog regression clicks
Add Group, exercises SINGLE/MULTI min/max state, adds an option, adds a second group, moves/removes it, then invokes Cancel and
asserts no product write. The retrospective and adjacent event-wiring audit are recorded in the M03 worklog. Manual WPF
acceptance is still required and remains intentionally unmarked.

The follow-up `M03-MANUAL-UI-OPTION-GROUP-LAYOUT-FIX-11` addresses the next operator-reproduced clipping defect in the shown
Product Editor. The fixed-width French SelectionMode label is replaced with an Auto/gap/Star Grid; min/max and group action
rows are content-sized WrapPanels with consistent peer-button margin/padding; OptionEditor action buttons use the same
horizontal padding and margin alignment while retaining their approved input widths and behavior. A real STA WPF regression
adds groups/options after render and asserts actual visual-tree widths against detached natural text DesiredSize and
margin-aware button DesiredSize plus peer-button height spread in both French and zh-CN at normal and resized dimensions. FIX-10 lifecycle, localization and
safe Cancel/no-write behavior remain covered. The retrospective and complete fixed-width/action audit are recorded in the
M03 worklog. The initial FIX-11 implementation/evidence head `2ad30a7d1a4d12c256fc274b808873ef1ff8278e` passed GitHub Actions
Continuous integration run **#179** (success); the addendum alignment head `96f9330ceae21c0fc224c0eb9a6083ecfcebcc10` passed run
  **#183** (success). Manual WPF acceptance is still required and remains intentionally unmarked.

The follow-up `M03-MANUAL-UI-LIVE-FILTER-FIX-12` addresses the operator-reproduced live-filter defect: the visible search,
category and status controls previously changed bound state without querying until the manual refresh button was pressed.
Status/category changes now trigger immediate product reloads, search is debounced at 250 ms, and each request carries an
immutable filter snapshot plus monotonic version/cancellation state. New requests cancel older ones; stale stores that ignore
cancellation cannot overwrite Products or Categories, overlapping full refreshes cannot clear a newer `IsBusy` state, and
category collection rebuilds suppress transient WPF binding callbacks. Language changes preserve semantic category/status keys
without issuing duplicate filter queries, selected products are cleared when excluded, and manual refresh remains a force
reload. A localized generic error event keeps automatic query failures on the existing safe UI reporting path. Four synthetic
architecture regressions cover status/category transitions, debounce/latest-wins, localization semantics, manual reload and
stale full-refresh protection. Manual WPF acceptance is still required and remains intentionally unmarked.

FIX-12 implementation head `b3e34d0d79a4efd39dd3817d19cd107a0f7af50c` passed CI **#189**; the final evidence-documentation
head `47a21237f7296c002986807918c733fbc58e5858` passed CI **#193** (success).

### AC-CAT-013 filtered bulk activation/deactivation extension

The approved amendment and authorization are present on the branch. The Application boundary now validates immutable captured
Product IDs/expected active states, computes matched/effective counts and avoids persistence for an all-no-op request. The SQLite
store revalidates all captures inside one transaction, skips already-target Products, uses one operation timestamp for changed rows,
and rolls back on missing/stale targets or injected mid-operation failure; only `is_active` and changed-row `updated_at_utc` are written.
The WPF shell exposes exactly localized bulk Activate/Deactivate actions, waits for the latest live/debounced composed filter result,
shows target/matched/effective confirmation counts, supports cancel/no-op/error feedback and refreshes while preserving filters.
Automated Application, SQLite integration and desktop presentation tests cover these invariants, including option aggregate and
unrelated-field preservation. No migration/schema change was made. Manual Windows/WPF execution of the extension checklist remains
outstanding, so M03 and AC-CAT-013 remain `Partial` rather than `Passed`.

### Acceptance mapping and remaining checks

- **Passed by automated evidence:** AC-CAT-002, AC-CAT-004, AC-CAT-005 and the current-catalogue/settings portions of
  AC-ORD-011; migration, normalized uniqueness, FK/constraint, aggregate rollback, ordering, cascade deletion, code
  reuse and exact settings round-trip are covered in
  `tests/Sushi81.Pos.Infrastructure.IntegrationTests/M03CatalogueIntegrationTests.cs` and
  `tests/Sushi81.Pos.Domain.Tests/CatalogueDomainTests.cs`.
- **Partial by design:** AC-CAT-001 awaits interactive catalogue-screen verification; AC-CAT-003 current-product
  maintenance is implemented, while historical-order independence remains the M04 snapshot regression; AC-ORD-011
  pricing-consumer integration is re-exercised by M04.
- **Manual Windows/WPF verification:** not performed after `M03-MANUAL-UI-LIVE-FILTER-FIX-12` in this non-interactive
  automation run. Earlier operator evidence predates the header/layout/filter remediations; the operator must launch the latest
  self-contained artifact and rerun the M03 checklist (including visual French and zh-CN header labels, automatic debounced
  search/category/status filtering, latest-wins rapid changes, `Tous`/`全部` selection across both language directions and
  refresh, category-manager/Product Editor readability, category/product/group/option CRUD and ordering, activation/deletion,
  settings restart round-trip, and confirmation that no future features appear). This is intentionally not marked Passed.
- **M04:** not started and not authorized by this handoff. **Blockers:** none for the automated M03 implementation; only
  the explicitly outstanding manual WPF acceptance remains.
