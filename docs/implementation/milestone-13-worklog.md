# M13 — Installer, localization completion and final V1 acceptance — worklog

**Status:** Preparation  
**Branch:** `codex/m13-installer-final-acceptance-authorized`  
**Draft PR:** #26 — M13: Installer, localization completion and final V1 acceptance  
**Start baseline:** `f59663c6b47ab21114c24360544e4e25094f4722`

This file is append-only for M13 package/evidence history. Historical failures are not rewritten away.

## 2026-09-24 — controller preparation and baseline reconstruction

Verified current GitHub baseline:

- PR #25 closed after M12 merge;
- `main` = `f59663c6b47ab21114c24360544e4e25094f4722`;
- M12 final pre-merge head = `49a0fe69e23e68c5591ef36ba5c56ef09a9d88b3`;
- CI #862 / run `36021889765`: success on the final pre-merge head;
- CI #863 / run `36023054757`: post-merge build-and-test success;
- Issue #4 closed; active executable handoff = none;
- M12 populated real-archive manual verification remains explicitly deferred under owner waiver;
- M13 Gestion export retention/compaction owner decision = PR #25 comment `5792796519`.

Controller prepared:

- Approved decision record for M13 export retention/compaction;
- matching acceptance amendment;
- M13 readiness, implementation contract, authorization, worklog and final owner acceptance checklist;
- work-package sequence WP1–WP6.

Security finding:

- GitHub currently reports `cimerosef/sushi81-pos` visibility = `public`, conflicting with the project's stated private-repository expectation.
- Preparation may continue, but Issue #4 remains CLOSED and no executable READY is published until owner/admin resolves that discrepancy.

Next intended executable package after resolution: WP1 — Gestion export retention/compaction core only.

## 2026-09-24 — durable M13 mailbox established

- Draft PR #26 was opened from `codex/m13-installer-final-acceptance-authorized` to `main`.
- PR start baseline remains `f59663c6b47ab21114c24360544e4e25094f4722`.
- Issue #4 remains CLOSED; active executable handoff = none.
- No READY was published because the source-repository visibility discrepancy remains unresolved.


## 2026-09-24 — owner repository-visibility disposition

The project owner explicitly instructed the controller not to block M13 because `cimerosef/sushi81-pos` is public and stated that the public state is intentional.

Controller disposition:

- repository visibility is no longer an M13 blocker;
- no visibility change is required for M13;
- preparation/control package remains valid;
- the next executable handoff is WP1 Gestion export retention/compaction core only;
- Issue #4 may be reopened only after the exact WP1 READY is durably published on PR #26.


## 2026-09-24 — controller WP1 technical decision

For safe compaction, absence from `orders` is not accepted as proof of M12 archival.

WP1 is directed to add/use compact durable per-order archive proof in `live.db`, populated atomically with M12 live-order deletion/completion. This proof follows normal live-database handoff and avoids depending on local Archive files that do not transfer with authority. Missing/legacy proof fails closed and retains export history. The proof itself may remain long-term; only obsolete full M11 payload/history is subject to the approved compaction policy.

## 2026-09-24 — WP1 implementation and automated evidence

Handoff `M13-WP1-GESTION-EXPORT-RETENTION-COMPACTION-01` was consumed from the active PR #26 mailbox while Issue #4 was OPEN. Implementation stayed within WP1 Gestion export retention/compaction core.

Implemented:

- Production migration 10, `create-annual-archive-order-proof-ledger`, adds the durable per-order M12 archive proof table in `live.db`.
- M12 finalization records the archive completion and one proof per removed order in the same transaction as exact live-order deletion. A failure before commit rolls all of them back.
- `SqliteGestionExportCompactionService` applies the inclusive 30-day threshold and prunes a complete SUCCESS batch only when its payload/ledger/emissions validate, each order is absent from live orders with matching positive M12 proof, and no PREPARED dependency covers the order. Emissions are deleted before the referenced batch in one authority-guarded transaction.
- Focused real-SQLite evidence covers migration preservation, archive proof success/rollback, younger and exact-age boundaries, PREPARED and live dependencies, missing/malformed proof, mixed batches, valid whole-batch pruning, live-order selection equivalence, foreign keys, rollback, idempotence and non-authoritative rejection.

Local verification on this worktree:

- Focused M13 WP1 integration tests: 6 passed, 0 failed, 0 skipped.
- Focused M11/M12 regressions: 64 passed, 0 failed, 0 skipped (49 infrastructure integration, 15 application tests).
- Full `dotnet test Sushi81.Pos.sln -c Release --no-restore --nologo`: 887 passed, 0 failed, 0 skipped.
- Full Release build: 0 warnings, 0 errors.
- `git diff --check`: passed.
- Exact-head GitHub CI is required after push and must pass before the matching `CODEX_DONE` is published. PR #26 remains unmerged; no successor M13 work is authorized by this handoff.

## 2026-09-24 — WP1 final verification update

Final review added an exact per-order proof-set assertion and a failure injection immediately after archive-proof insertion but before live-order deletion. The M12/M11 regression filter then passed 65 tests (50 infrastructure integration and 15 application), with 0 failures and 0 skipped. The final full Release test command passed 888 tests, 0 failed, 0 skipped. The final full Release build completed with 0 warnings and 0 errors; `git diff --check` passed. Exact-head GitHub CI remains the final completion gate after pushing this head.

## 2026-09-24 — WP2 startup cleanup and history behavior

Handoff `M13-WP2-RETENTION-RUNTIME-HISTORY-02` was executed on the authorized PR #26 branch after the owner approved the bounded startup cleanup.

Implemented:

- Application startup now attempts one Gestion export-history compaction after authority resolution and the M12 annual archive startup attempt.
- Compaction is skipped unless this installation has authoritative write access. A failed compaction is logged as retryable and does not fail application startup; cancellation still propagates.
- Startup/history integration evidence covers: pruned batches disappearing from history and regeneration; same-startup archive proof before pruning; non-authoritative no-op; unresolved PREPARED dependencies; live-order selection equivalence; retained payload and workbook regeneration equivalence; transactional failure rollback and retry.
- No timer, background loop, manual trigger, shutdown trigger, or later M13 work package was added.

Local verification:

- Focused WP2 integration class: 13 passed, 0 failed, 0 skipped.
- Focused startup composition architecture tests: 3 passed, 0 failed, 0 skipped.
- Focused M11/M12 regression filter: 76 passed, 0 failed, 0 skipped (50 infrastructure integration, 15 application, 11 architecture).
- Full `dotnet test Sushi81.Pos.sln --configuration Release --no-restore --nologo`: 896 passed across six test assemblies, 0 failed, 0 skipped.
- Full Release build: 0 warnings, 0 errors.
- `git diff --check`: passed.
- Exact-head GitHub CI is checked after push and before publishing the matching `CODEX_DONE`; its run and result are recorded there. PR #26 remains unmerged, and this handoff does not authorize a successor work package.


## 2026-09-25 — WP3 localization completion implementation and local verification

Handoff M13-WP3-LOCALIZATION-COMPLETION-03 was consumed from active PR #26 while Issue #4 was OPEN. Work stayed within WP3 French / Simplified Chinese localization completion.

Implemented and audited:

- Added paired OperationFailed resources and included the key in the normal ShellViewModel.Localized refresh seam.
- Replaced all 15 operator-visible raw exception-message sinks across MainWindow, order entry and lifecycle surfaces with the safe localized operation-failure message. The internal Disaster Recovery result diagnostics remain internal and continue to be mapped to localized operator messages.
- French and zh-CN each contain 466 resource keys; exact key-set parity passed. Every resource is non-empty, loads through ResourceManager, contains no tested replacement/mojibake marker, and has a valid CompositeFormat signature with matching placeholder indexes.
- The Desktop source audit found 140 unique static localization-helper keys; all resolve to non-empty French and zh-CN resources.
- The XAML visible-attribute audit found 24 direct literals. All 24 are in the explicit allowlist: punctuation, action glyphs, and the fixed external BatchId identifier. No unexplained XAML literal or raw exception-to-UI sink remains.
- Representative shell/authority/startup, pairing/recovery, catalogue/import/export, order/lifecycle/search/dashboard, printing, Hiboutik, Gestion export, archive and validation resources load in both cultures. Runtime switching changes labels in both directions; date/number formatting and CREATE/UPDATE/CANCEL identifiers remain correct.
- A synthetic SQLite-backed culture-switch regression confirmed stored category/product values, order product/category snapshots, telephone, address and comment remain unchanged.
- No business/schema/export semantics were changed. Full visual review of all major screens in both cultures remains for the final Windows owner-acceptance pass; no headless visual-proof claim is made.

Local verification:

- Focused WP3 localization/resource architecture tests: 11 passed, 0 failed, 0 skipped.
- M11 DatePicker and related WPF regressions (M01Wp3DesktopTests): 20 passed, 0 failed, 0 skipped.
- Full dotnet test Sushi81.Pos.sln -c Release --no-restore --nologo: 900 passed across six test assemblies, 0 failed, 0 skipped.
- Full Release solution build: 0 warnings, 0 errors.
- git diff --check: passed before this append; rechecked on the final staged diff before commit.
- Exact-head GitHub CI is required after push and will be reported in the matching CODEX_DONE before completion delivery. PR #26 remains unmerged; no successor package is authorized by this handoff.


## 2026-09-25 — WP4 production publish and per-user installer

Handoff `M13-WP4-PRODUCTION-PUBLISH-INSTALLER-04` was consumed from the active PR #26 mailbox while Issue #4 was OPEN at the exact WP3 head `e40a1f882d4c0557fc0fe4e30cb88395be290c89`. Work is limited to production packaging, the per-user installer, its evidence, and the migration fail-safe regression; no WP5/WP6 or merge work is included.

Implemented:

- Added the pinned release configuration for product `1.0.0`, file version `1.0.0.0`, `win-x64`, self-contained .NET 10, `PublishSingleFile=false`, Inno Setup 6.7.3, stable AppId `C7A1B9E2-1E62-4B4B-A2EA-7802814408FC`, and 90-day workflow-artifact retention.
- Added `installer/sushi81-pos.iss`: `PrivilegesRequired=lowest`, x64-compatible install mode, stable binary root `{localappdata}\Programs\Sushi81 POS`, safe in-place file replacement, and a per-user Start Menu shortcut. It has no data-root deletion, SQL/schema actions, updater, service, scheduled task, or uninstall remove-all-data rule. Durable application data remains at `%LOCALAPPDATA%\Sushi81 POS` outside the installer-owned program directory.
- Added a packaging script that requires the checkout HEAD to equal the requested full source SHA; runs the Release `win-x64` self-contained publish with explicit product/file/informational version metadata; verifies the EXE metadata, bundled .NET and Windows Desktop runtimes, French neutral-resource fallback for `fr-FR`, and the `zh-CN` satellite; safety-scans staged files; compiles with the pinned Inno compiler; and records `release-provenance.json`, installer byte size/SHA-256, executable SHA-256, and a machine-readable package summary. Provenance is included inside the installed binary tree and beside the uploaded installer.
- Replaced the M11 owner-candidate artifact workflow with an exact-source-head M13 installer job while retaining normal Release build/test. PR jobs checkout and verify `github.event.pull_request.head.sha`; push jobs use and verify `github.sha`. The authorized implementation branch has a push trigger. The workflow downloads the pinned upstream Inno Setup 6.7.3 release, checks its Authenticode signature and Pyrsys B.V. signer, and uploads the production installer, provenance, package summary, and lifecycle summary for 90 days. The GitHub artifact ID and per-run installer size/hash are emitted in the workflow summary and matching CODEX_DONE after upload.
- Added hosted-runner lifecycle verification using only synthetic files beneath that runner user's profile: clean install, same-version repair, uninstall preservation, reinstall, a `0.9.0` installer-mechanics fixture built from accepted WP3 source `e40a1f882d4c0557fc0fe4e30cb88395be290c89`, in-place upgrade, and post-upgrade uninstall/reinstall. Data hashes are checked after each step for `Data\live.db`, Archive, Recovery, Config, device identity, authority/transfer-state, and printer/settings fixtures. The script verifies HKCU uninstall metadata, the distinct per-user roots, installed provenance/resources, no Sushi81 service/task/updater, and no forbidden file in the installed tree. The WP3 fixture proves installer mechanics only; it does not claim WP3 historically shipped an installer.
- Added targeted forbidden-content scanning for SQLite/business data, recovery/archive fixtures, local settings and identity/authority/handoff fixtures, credentials/secrets, operator workbooks/CSV, logs, PDBs, tests, and source/build residue. Runtime JSON and .NET resource/runtime files remain allowed.
- Extended the future-schema migration test to prove that the known business sentinel row and future migration marker remain intact when this application rejects a schema version newer than it supports.

Verification in the isolated WP4 worktree:

- Focused WP4 package/lifecycle architecture tests: 3 passed, 0 failed, 0 skipped.
- Future-schema migration preservation regression: 1 passed, 0 failed, 0 skipped.
- Full `dotnet test Sushi81.Pos.sln -c Release --no-restore --nologo`: 903 passed across six test assemblies, 0 failed, 0 skipped.
- Full Release solution build: 0 warnings, 0 errors.
- Release `win-x64` self-contained publish probe: completed; product/file/informational EXE version fields, bundled .NET / Windows Desktop runtimes, `zh-CN` resources, and 417-file forbidden-content scan verified. French app resources are embedded in the main Desktop assembly and are the existing neutral fallback for `fr-FR`; no artificial duplicate French satellite was introduced.
- PowerShell parser: all four installer scripts parsed successfully. `git diff --check`: passed.
- The local publish probe used a temporary in-progress worktree and is not accepted as exact final-source provenance. Final installer bytes, SHA-256, artifact ID, and lifecycle results come from the post-push exact-head CI job and are recorded in its run summary and matching CODEX_DONE. Interactive WPF launch/UI remains owner acceptance; no headless UI result is claimed.

Release identity and deferred owner/compliance checks:

- Installer filename pattern: `Sushi81POS-Setup-1.0.0-<short-source-sha>.exe`; GitHub artifact name: `Sushi81-POS-production-installer-win-x64-1.0.0-<short-source-sha>`; retention: 90 days. The upload action assigns the run-specific artifact ID after packaging; the final ID and installer byte/hash values are recorded in the matching CODEX_DONE rather than fabricated in this append-only source commit.
- Inno Setup's upstream 6.7.3 revision notes ask commercial users to purchase a license; no license key or purchase is included in this handoff. The pinned public compiler and its signer are verified by CI. Owner/compliance review of that vendor request remains separate from this installer-mechanics work package and must be resolved before commercial distribution if applicable.
- The installer does not launch the app or migrate data. First-launch schema migration remains application-controlled and fail-closed. Real-device install/upgrade, re-pair/authority acceptance, and interactive UI review remain owner checks for final V1 acceptance.
- Exact-head normal CI and the production-installer lifecycle/artifact job are required after push and will be captured in CODEX_DONE. PR #26 remains open/unmerged; this handoff does not authorize WP5/WP6 or merge.


## 2026-09-25 — WP5 diagnostics, performance, repository security and hardening

Handoff `M13-WP5-DIAGNOSTICS-PERFORMANCE-SECURITY-HARDENING-05` was consumed from active PR #26 while Issue #4 was OPEN. Work is limited to WP5 diagnostics, safe repository scanning, dependency auditing, workflow action maintenance, and evidence-led hot-read performance. No WP6 work or merge is included.

Implemented and reviewed:

- Added a stable `DesktopOperationFailure` diagnostic event (ID 1300) at generic desktop-operation catches covering shell, order entry/lifecycle, catalogue filtering, localization, annual archive access, printing, and Gestion export. Records include the stable operation identifier, exception type, HRESULT, and a controlled safe classification. Arbitrary exception messages and exception objects are omitted because they can contain order/customer payloads, credentials, or workbook contents. Cancellation is not reported as an error. Logger failures are swallowed at the diagnostic seam and rolling-file provider; existing logs remain bounded to 10 MiB per file and 14 retained files.
- Added redaction/failure regression coverage for synthetic phone, email, bearer, URL credentials, query credentials, API-token, customer/order text, cancellation, logger failure, and WPF synchronous blocking primitives. Existing diagnostic tests construct credential vectors at runtime so the repository scanner does not need a broad test-file exception.
- Added `.github/scripts/Test-RepositorySafety.ps1` with a 16-case synthetic self-test and a narrow content-fixture allowlist at `tests/**/Fixtures/SyntheticSecurity/`; business-data and credential filename rules still apply under that directory. The scan checks tracked and non-ignored files, high-confidence private-key/GitHub/Slack/Google/AWS/URL credential forms, production database and SQLite sidecars, workbooks/CSV/logs, local environment files, and machine-local state. It fails closed on oversized potentially textual files rather than silently skipping their content. The WP4 installer-tree forbidden-content scan remains intact.
- Added `.github/scripts/Audit-NuGetVulnerabilities.ps1` to fail if the restored direct/transitive package graph has audit errors, missing graph data, or a vulnerability. The current restored solution audit reports no findings.
- Updated CI to stable `actions/checkout@v7`, `actions/setup-dotnet@v6`, and `actions/upload-artifact@v7`, each verified to use Node 24. Source-head SHA checks, least-privilege `contents: read`, installer identity, lifecycle verification, and 90-day artifact retention remain in place. Official references reviewed 2026-09-25: [checkout changelog](https://github.com/actions/checkout/blob/main/CHANGELOG.md), [setup-dotnet v6 metadata](https://github.com/actions/setup-dotnet/blob/v6/action.yml), [upload-artifact v7 metadata](https://github.com/actions/upload-artifact/blob/v7/action.yml).
- Added migration 11 indexes only where query-plan evidence showed a sort: case-insensitive product-code catalogue ordering and planned-fulfilment date/time with null times last. Reordered successful export-history/compaction candidates to use the existing status/time index without changing batch eligibility or retention semantics.
- Added a populated SQLite query-plan regression starting from schema v10: 10,001 products, 50,000 orders, 50,000 payment adjustments, 50,000 order items, and 2,000 export batches/emissions. It verifies migration preservation and recovery snapshot sequence 10, and examines catalogue list/lookup, planned orders, order reference and phone search, dashboard, payment summaries, export history/regeneration, compaction dependencies, and annual-archive proof/year/search access. The two evidenced temporary sort plans are removed; existing reference, dashboard/payment, export, dependency, and archive indexes remain selected. Phone search retains its punctuation-normalized contains semantics and therefore scans; no speculative full-text/trigram behavior or maintenance subsystem was introduced.
- Reviewed startup composition and M12 archive/M13 compaction boundaries; no startup migration/recovery ordering or data-retention behavior was changed by this work. Rechecked the explicit V1 exclusion list; WP5 introduces no excluded product feature. The real populated-archive owner verification remains deferred under the existing M12 owner waiver and is not represented as Passed.
- Reviewed current upstream Inno Setup licensing guidance: the vendor says purchase is not strictly required but requests purchase from commercial users and expects at least one user license when the compiler is invoked in CI. This is recorded as an owner/compliance follow-up, not a technical build/test failure. No purchase or license key is included. Reference: [Inno Setup commercial-license FAQ](https://jrsoftware.org/isorder.php).

Local verification in the isolated WP5 worktree:

- Full Release solution tests: 909 passed across six test assemblies, 0 failed, 0 skipped. This includes M06/M07 recovery/authority, M11/M12, M13 WP1-WP5, and existing V1 regressions.
- Full Release solution build: 0 warnings, 0 errors.
- Repository safety scanner self-test: 16 synthetic cases passed; full scan: 438 tracked/non-ignored files, no findings.
- Direct/transitive NuGet vulnerability audit: passed, no findings.
- `git diff --check`: passed.
- Exact-head GitHub normal CI and the production-installer lifecycle/artifact workflow remain mandatory after push. Their exact source SHA, workflow results, installer artifact identity/hash, and 90-day retention are recorded in the matching `CODEX_DONE`. Owner visual FR/zh-CN, real-device install/upgrade/recovery, and real populated-archive acceptance remain owner checks. PR #26 remains unmerged; no WP6 work is authorized by this handoff.

## 2026-09-25 — WP6 final candidate documentation and owner-acceptance preparation

Handoff `M13-WP6-FINAL-CANDIDATE-OPERATING-GUIDE-06` was consumed from active PR #26 comment `5831103818` while Issue #4 was OPEN. The exact starting head is `aa734cc94bb6500be42cbaa00407e8fc8ec7a1fd`. Scope is final-candidate documentation, operating guide, living-state reconciliation and owner-acceptance preparation only.

Prepared:

- Added `docs/operating-guide.md` with installation/data-path/provenance/log guidance; authoritative/read-only/transfer/Disaster Recovery boundaries; Caisse, order/payment/printing, Catalogue, Hiboutik paste, Gestion export and annual-archive operation; FR/zh-CN label mapping; troubleshooting and explicit V1 exclusions.
- Reconciled the root README, `docs/README.md`, implementation status and implementation index to the current M13 state. WP1–WP5 remain controller-accepted at their exact heads and CI runs; repository public visibility is owner-confirmed intentional; Issue #4 is OPEN for this WP6 handoff; PR #26 remains unmerged.
- Changed the final manual-acceptance record to `CANDIDATE PREPARED / OWNER ACCEPTANCE PENDING`, with the short owner A–F checklist and explicit separation of M12's deferred real populated-archive check.
- The exact final candidate source SHA, workflow/job evidence, production artifact/provenance identity and hashes are recorded in the matching WP6 `CODEX_DONE` comment rather than embedded self-referentially in this source document.

Scope review: documentation and worklog only. No production source, tests, schema, XAML, dependencies, release configuration or workflow behavior is changed. No owner manual acceptance, interactive Windows/UI test, device operation or M12 real populated-archive check is claimed here. Exact-head automated verification and CI/artifact evidence are delivered in the matching completion comment. PR #26 remains unmerged; WP6 does not claim M13 Passed, owner acceptance or release.
