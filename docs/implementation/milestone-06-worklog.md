# M06 implementation worklog — local recovery and authoritative/read-only enforcement

**Milestone:** M06 — Local recovery and authoritative/read-only enforcement  
**Branch:** `codex/m06-local-recovery-read-only`  
**Base:** M05 merge commit `79499d7c6ed65a74f524097c1507ca648dc151c3`  
**Contract:** `milestone-06-local-recovery-read-only-enforcement.md`  
**Authorization:** `milestone-06-authorization.md`  
**Initial handoff:** `CODEX_HANDOFF_READY: M06-IMPLEMENT-01`  
**Remediation handoff:** `CODEX_HANDOFF_READY: M06-REVIEW-REMEDIATION-02`
**Third remediation handoff:** `CODEX_HANDOFF_READY: M06-REVIEW-REMEDIATION-03`
**Fourth remediation handoff:** `CODEX_HANDOFF_READY: M06-REVIEW-REMEDIATION-04`
**POST_TASK_POWER_ACTION:** `NONE`

## Preparation state

M05 is Passed and merged through PR #10. Accepted M05 production-code head: `84c1c534c1df105ccb1839cbc6dfc9e0e055bb70`. Final M05 docs head: `217d187dd3ef5498c11f21bc516eccc6737fa952`. Main merge commit: `79499d7c6ed65a74f524097c1507ca648dc151c3`.

The M05 merge occurred after the final status documents were written. Current-state references have been updated to the merge commit;
older acceptance/findings paragraphs retain their historical as-of wording and are not rewritten as evidence.

M06 preparation review found no unresolved product/business/data-semantic decision. The frozen specification is sufficient for controlled implementation.

The initial implementation and prior remediations were reviewed before owner manual acceptance. This fourth controlled remediation pass
closes the remaining startup-safety finding on the same PR/branch; project-owner Windows/WPF manual acceptance remains `Pending` and is
not claimed here.

## Scope boundary

M06 implements:

- production local recovery wiring for all durable business mutations currently present through M05;
- committed-change notification/debounce/single-flight/shutdown flush;
- validated latest-five local recovery retention and failure behavior;
- durable local authority/read-only state and safe startup reconstruction;
- centralized Application-level business-write blocking;
- persistent localized WPF read-only/pending/recovery-required presentation;
- read-only consultation while mutation controls are blocked.

M06 does not implement M07 pairing/real handoff/GitHub transfer/target acquisition/cloud DR/Disaster Recovery, M08 printing, M10 catalogue workbook import, M12 annual archive or later milestones.

## Acceptance mapping

| Criterion | M06 responsibility | Final evidence status |
|---|---|---|
| AC-STO-006 | Owner — local recovery generation/retention and all current durable mutation triggers; preserve mandatory seam for M10 import cross-check | In progress — M01 recovery primitives are wired through the M03–M05 Application mutation boundaries; final evidence pending |
| AC-STO-010 | Owner — persistent non-authoritative/pending read-only state, centralized blocking and WPF presentation; M08 printing/M12 archive later cross-checks | In progress — durable state, fail-closed startup, centralized guard and localized banner implemented; manual gate pending |
| AC-PROD-002 | Partial — authoritative local operation and local authority/recovery enforcement remain network-independent | In progress — M06 local authority path implemented; final owner remains M07 |
| AC-NFR-004 | Supporting failure-path evidence; final owner M13 | In progress — M06 failure-path regressions added; final owner remains M13 |

## Final current mutation inventory

The M05 merged baseline has these durable mutation paths. Each Application service calls the single `IWriteAuthorityGuard`
before delegating; the existing SQLite transaction is the commit point; and `IDurableChangeNotifier` is called only after a
successful store result. Notification exceptions cannot roll back or report a committed business mutation as failed.

| Mutation | Application path | No-op / recovery rule |
|---|---|---|
| New POS order | `OrderEntryService.ConfirmNewOrderAsync` | Guard before write; notify after `SqliteOrderStore.SaveAsync` commit; validation, rollback and output-only failure do not notify. |
| Existing-order save, payment update and auto-reopen | `OrderLifecycleService.SaveModificationAsync` → common `SaveAsync` | Atomic order/payment commit; true business no-op returns current snapshot without write/notify. |
| Close and Cancel | `OrderLifecycleService.CloseAsync` / `CancelAsync` → common `SaveAsync` | Common Application guard; idempotent Closed/Cancelled operations do not notify. |
| Category create/rename | `CatalogueService.CreateCategory*` / `RenameCategory*` | Guard before catalogue transaction; validation and authority rejection do not notify. |
| Product create/update/delete | `CatalogueService.CreateProductAsync` / `UpdateProductAsync` / `DeleteProductAsync` | Guard before transaction; effective no-op update is not written/notified. |
| Product activation and filtered bulk state | `CatalogueService.SetProductActiveAsync` / `BulkSetProductsActiveAsync` | Zero-effective single/bulk changes do not notify. |
| Option group/option aggregate changes | Product create/update aggregate transaction | Same guard, transaction and notifier seam; no independent M05 writer exists. |
| Business settings | `BusinessSettingsService.UpdateAsync` | Persistence-only `UpdatedAt` difference is ignored for recovery; the existing settings-save transaction is preserved, but a true no-op is not notified. |

All four production mutation services now have public constructors requiring both `IWriteAuthorityGuard` and
`IDurableChangeNotifier`; no production constructor supplies a silent nullable guard or no-op notifier. The internal compatibility
constructors are explicitly test-only and are covered by the public-constructor dependency regression.

After a store reports a successful durable commit, every current M03-M05 service invokes the notifier with
`CancellationToken.None`. Caller/UI cancellation still applies before and during the business transaction, but cannot suppress
the committed-change sequence allocation or scheduler notification. Deterministic regressions cover Catalogue, settings, new-order,
and lifecycle commits cancelled immediately after the store commit; each notifier receives exactly one non-cancelled post-commit call.

Future M10 import and M12 archive writers must use this same seam. The preparation list below is retained as historical scope
reference; the audited current paths above are authoritative for this implementation.

Expected minimum:

- new-order confirmation;
- same-ID existing-order save/modification;
- cumulative CB/Espèce payment update;
- Close;
- Cancel;
- Category create/update/delete;
- Product create/update/delete/activate/deactivate;
- filtered bulk product Activate/Deactivate;
- OptionGroup create/update/reorder/delete;
- Option create/update/reorder/activation/deactivation/delete;
- BusinessSettings save;
- any additional durable business write found on the M05 merged baseline.

For each final entry record:

- Application use-case/service path;
- authority-guard point;
- transaction/commit point;
- durable-change notification point;
- no-op/rollback behavior;
- automated evidence path/test.

## Implementation topology

This handoff was executed serially by the main Codex worker. No subagents or parallel worktrees were used. The existing M01
authority guard, recovery scheduler and snapshot service were reused; the shared Application notifier seam was then wired through
M03-M05, followed by CompositionRoot/WPF integration, tests and documentation review. Integration ownership and final diff review
remain with the main worker.

Record:

- main-agent/subagent topology;
- any safely parallel workstreams;
- shared seam stabilized before parallel delegation;
- integration ownership and final review performed by the main agent.

## Durable authority-state design

M06 implementation record:

`JsonAuthorityStateStore` persists schema version `1` at `Config/authority-state.json`, a separate
`Config/authority-bootstrap.marker`, and an independent `Data/authority-bootstrap.anchor`. All writes use create-new temporary
files followed by atomic move/replace. The coordinator loads the document after successful SQLite migration and before business
surfaces are created. Legacy M01-M05 evidence is captured before startup migration and passed into the coordinator; a freshly created
migration history therefore cannot qualify a new database for legacy bootstrap. Once established, every authority-state load requires
both the marker and independent data-directory anchor, so deletion of the anchor cannot silently restore writable authority.

Before the migration runner opens the live database, `AuthorityStartupPreflight` captures whether a non-empty pre-existing
`Data/live.db` is present and whether any persisted authority artifact exists. An established state, marker or anchor without that
pre-existing database blocks startup before SQLite can create a replacement. The same pre-migration database evidence is passed into
`AuthorityStateCoordinator` as defense in depth, so an established document cannot become writable if the production startup path
reports that the live database was absent. No missing production database is deleted, reset, replaced, seeded or restored automatically.

Once bootstrap has completed, missing state, missing marker, missing anchor, malformed JSON, unsupported schema, invalid enum or
persistence failure resolves the single guard to `RecoveryRequired`; it never silently restores writable authority.

`Authoritative`, `NonAuthoritativeReadOnly`, `Transitioning` and `RecoveryRequired` reconstruct directly on restart. All except
`Authoritative` fail `RequireWriteAuthority()`. No M07 pairing, generation, target or force-acquire metadata/UI was added.

Record:

- persistent state file/path and schema/version;
- atomic write strategy;
- runtime state/reason mapping;
- one-time accepted M01–M05 legacy single-device bootstrap condition;
- post-bootstrap missing/corrupt/future/contradictory fail-closed behavior;
- startup ordering;
- restart reconstruction evidence.

## Recovery mutation/scheduler design

`IDurableChangeNotifier` is the common post-commit seam. `DurableChangeNotifier` persists a monotonic sequence under
`Config/recovery-sequence.json`, reconciles startup state with the highest independently validated local Recovery metadata
sequence, then calls the existing `DebouncedRecoveryScheduler`. Application services call it only after a successful durable store
result; validation, authority rejection, rollback, persistence failure and true no-op paths do not call it. The scheduler coalesces
nearby changes for three seconds, permits only one snapshot at a time, preserves a newer pending sequence if a change arrives
during an active snapshot, and flushes during orderly shutdown. Snapshot creation, validation, promotion and retention failures
preserve the committed live database and prior valid units; later commit or shutdown can retry.

The production WPF close path no longer synchronously waits on `DisposeAsync`. `AsyncCloseCoordinator` cancels the first window
close, awaits recovery scheduler disposal without blocking the Dispatcher, and requests the close again on the Dispatcher only after
the flush has completed. The application disposes the notifier/logger from `Application.Exit` after that orderly flush. A bounded
real-STA/WPF regression creates a pending committed sequence, initiates the actual window close path, and proves the sequence reaches
the snapshot service before the window closes.

Record:

- common committed-business-change seam chosen;
- how no-op/rollback/blocked attempts are excluded;
- debounce behavior;
- single-flight/latest-sequence behavior;
- shutdown flush behavior;
- snapshot failure behavior after a successful business commit;
- latest-five retention behavior.

## WPF/read-only design

`ShellViewModel` exposes the resolved authority state and a persistent top-level localized banner. FR and zh-CN resources cover
ordinary read-only, transitioning and recovery-required messages; each states that the local displayed copy may be stale where
M06 cannot prove freshness and no timestamp/version is fabricated.
Catalogue/settings mutation controls use `CanWrite`; order confirmation and lifecycle mutation properties are also guarded, while
order search/date browse/detail, dashboard and catalogue queries remain available. The central Application guard remains the
safety boundary if a stale command is invoked. No M07 target-selection, retarget or force-acquire UI was added.

Record:

- persistent banner/status implementation;
- FR/zh-CN localization keys;
- ordinary read-only vs pending/transitioning vs recovery-required presentation;
- which mutation controls are disabled/unavailable;
- which read-only search/view/dashboard routes remain available;
- stale-data wording and available freshness/version information;
- confirmation no M07 target-selection/force-acquire UI was added.

The real STA/WPF regression `M06DesktopTests.RealShellShowsStaleReadOnlySafetyBoundaryAcrossStatesAndSupportedSizesOnSta`
constructs the actual production-shaped Shell with the same explicit guard/notifier injected into Catalogue, Settings, Order Entry
and Order Lifecycle services. It renders the actual `MainWindow` at 760x520, 980x680 and 1400x900 for Authoritative,
NonAuthoritativeReadOnly, Transitioning and RecoveryRequired states. It checks the persistent banner, localized FR → zh-CN → FR
rerender, representative mutation-control disablement, direct Application guard rejection and enabled consultation/search controls.
The architecture count therefore increases through genuine WPF coverage rather than resource-string-only assertions.

## Required failure-injection evidence

Implemented/passing locally: transaction rollback and authority rejection do not notify; snapshot failure does not claim a new
snapshot; validated latest-five retention and failed sixth preservation; incomplete/staging exclusion; nearby-change debounce;
active-snapshot commit preservation; shutdown flush; post-commit notifier failure preserves the business success; malformed/future
authority state fails closed; direct Application catalogue/lifecycle bypass attempts are blocked; read-only query paths remain
separate from mutation guards. The existing M01 checksum, integrity, promotion, cleanup and metadata-validation tests remain in
the Infrastructure suite. The final remediation-head Release suite below records the complete automated result; project-owner
STA/WPF acceptance remains a separate pending manual gate.

Record exact test names/results for:

- transaction rollback → no notifier/snapshot;
- authority rejection → zero business writes/no notifier;
- staging/backup failure;
- integrity failure;
- checksum/metadata mismatch;
- recovery promotion failure;
- retention cleanup failure;
- failed sixth snapshot preserves prior five;
- incomplete/staging unit exclusion;
- nearby-commit debounce;
- durable change during active snapshot not lost;
- shutdown flush;
- post-commit snapshot failure preserves business data;
- read-only/pending/recovery-required restart persistence;
- malformed/future authority state fail closed;
- direct Application bypass attempt blocked;
- read-only query/view/dashboard usability;
- FR/zh-CN transition preserves authority and business state.

Remediation-specific evidence includes:

- `CatalogueApplicationTests.CatalogueCommitNotifiesWithNonCancellableTokenAfterCallerCancellation`;
- `CatalogueApplicationTests.SettingsCommitNotifiesWithNonCancellableTokenAfterCallerCancellation`;
- `OrderEntryApplicationTests.OrderCommitNotifiesWithNonCancellableTokenAfterCallerCancellation`;
- `OrderLifecycleApplicationTests.LifecycleCommitNotifiesWithNonCancellableTokenAfterCallerCancellation`;
- `DependencyBoundaryTests.M06MutationServicesExposeMandatoryAuthorityAndRecoverySeams`;
- `InfrastructureIntegrationTests.AuthorityBootstrapIsDurableAndMissingStateFailsClosed`,
  `MissingAuthorityStateWithoutLegacyEvidenceFailsClosedInsteadOfRebootstrapping`,
  `EstablishedAuthorityWithoutIndependentAnchorFailsClosed`,
  `FreshMigratedDatabaseDoesNotQualifyAsLegacyBootstrapEvidence`,
  `EstablishedAuthorityStatesRoundTripDurablyAcrossRestart`, and
  `DurableChangeNotifierReconcilesCorruptSequenceWithValidatedRecoveryMetadataAfterRestart`;
- `M05EvidenceClosureIntegrationTests.RealApplicationSqliteMutationNotifierSchedulerAndRecoverySnapshotShareOneSeam`, which
  exercises real SQLite catalogue and order mutations through the real notifier, scheduler and validated recovery snapshot and
  proves blocked/no-op paths do not advance the recovery sequence.
- `M06DesktopTests.RealStaWindowCloseFlushesPendingRecoveryBeforeCompleting`, which proves the production close-coordination
  seam flushes the pending sequence on a real STA/WPF Dispatcher within a bounded timeout.
- `InfrastructureIntegrationTests.EstablishedAuthorityWithoutPreExistingLiveDatabaseIsBlockedBeforeReplacementCreation`, which
  proves the pre-migration startup preflight blocks established authority artifacts before a missing `Data/live.db` can be created,
  and the coordinator defense-in-depth path remains read-only.
- `InfrastructureIntegrationTests.AuthorityStartupPreflightAllowsSupportedExistingLiveDatabase`, which proves the accepted
  existing-local-database path remains eligible for migration/authority resolution when no authority artifacts are present.

## Automated verification

Current local implementation verification for remediation-04 is recorded on evidence head `a9c0a7a`
(`M06: block authority restoration after live database loss`). The remediation-03 values above remain historical evidence for that
handoff.

- `dotnet --info`: Passed — SDK 10.0.400, Windows 10.0.26200 x64.
- `dotnet restore Sushi81.Pos.sln --locked-mode -r win-x64`: Passed.
- `dotnet build Sushi81.Pos.sln -c Release --no-restore`: Passed, 0 warnings / 0 errors.
- Full Release tests: Passed — 363/363, 0 failures, 0 skips: Domain 33, Application 47, Infrastructure 64, Architecture 95,
  `tests/Sushi81.Pos.OneDriveFeasibility.Tests` 32 and `tools/Sushi81.Pos.OneDriveFeasibility.Tests` 92. Remediation-04 adds
  two Infrastructure startup-continuity regressions; all prior M06 application, infrastructure, dependency and STA/WPF evidence
  remains passing.
- Self-contained `win-x64` publish: Passed to ignored `artifacts/m06-remediation-04-publish`.
- `git diff --check`: Passed.
- Existing snapshot retention, scheduler, rollback and incomplete-unit tests: Passed within the infrastructure result above.
- Exact-head GitHub Actions CI: Passed. `Continuous integration` push run **#512** (`34048813258`) checked raw head
  `a9c0a7a52d530f3517e37a4020d2755ee4783f24`; pull-request run **#513** (`34048815532`) checked PR #11 and the same head.
  Both `build-and-test` jobs succeeded; GitHub recorded one existing Node.js 20 deprecation warning annotation and no
  test/build failure.

## Windows/WPF project-owner acceptance

Durable checklist: `milestone-06-final-manual-acceptance.md`.

Status: **Pending project-owner execution.**

Codex must not mark this Passed by simulation or automated tests alone.

## Later-milestone cross-checks

Record but do not implement:

- M07 must drive the durable M06 authority state through real pairing/handoff/target-acquisition/DR transitions;
- M08 must verify `AC-PRINT-010` printing remains permitted from read-only local data with stale warning;
- M10 catalogue workbook import must use the M06 authority/recovery mutation seam;
- M12 annual archive execution must be blocked in read-only state and archive read-only consultation must remain compatible;
- M13 performs final production-target regression.

## Data safety

Final completion record must confirm:

- tests used only synthetic/sanitized values;
- no real customer/order/payment data was added;
- no credentials/tokens were committed;
- no local `live.db`, Recovery files, authority-state files, logs or publish artifacts were committed;
- authority/recovery failures never auto-delete/recreate the production database.

## Fourth remediation evidence

The fourth controlled remediation adds a pre-migration authority-continuity preflight and passes its captured live-database
evidence into the authority coordinator. The established-state path therefore cannot restore `Authoritative` after the persisted
authority document/marker/anchor survive while the supported pre-existing `Data/live.db` is gone. The missing database remains
missing, and startup fails closed before the migration runner can create a replacement. The accepted one-time M01-M05 bootstrap path
continues to use genuinely pre-existing schema evidence.

The final evidence head `a9c0a7a`, Release counts, publish result, exact-head CI run IDs and ref semantics are recorded in the matching
`CODEX_DONE: M06-REVIEW-REMEDIATION-04` PR comment. Project-owner Windows/WPF acceptance remains `Pending`; PR #11 remains
open/unmerged and M07 remains unauthorized.

## Completion state

Fourth remediation implementation is complete on its pushed evidence head; PR #11 must remain open/unmerged. Project-owner
Windows/WPF acceptance is still pending and M07 is not authorized by M06 completion.
