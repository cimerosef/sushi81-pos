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
