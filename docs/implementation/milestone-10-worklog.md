# M10 — Catalogue `.xlsx` import/export — worklog / evidence ledger

**Status:** Implementation authorized — WP3 active under Issue #4
**Created:** 2026-09-17  
**Milestone:** M10  
**Issue #4:** must be OPEN only for the sole exact active handoff  
**Implementation branch:** `codex/m10-catalogue-xlsx-authorized`  
**Implementation PR/mailbox:** #22

This file is the append-only milestone worklog/evidence ledger. Historical entries must not be rewritten merely because later work changes current state.

## Entry 2026-09-17 — preparation baseline audit

Baseline re-established from GitHub:

- `main`: `861cfba1dfacbb3289395c0370f6d42765b6c223`;
- PR #19: CLOSED / MERGED at the same merge commit;
- Issue #18: CLOSED / completed;
- Issue #4: CLOSED, no active Codex handoff;
- M10/M11/M12/M13: not implementation-authorized.

Reviewed specifications and current code seams include:

- V1 freeze/acceptance/implementation plan/status;
- catalogue/product/business/data/architecture/storage specifications;
- relevant Approved Catalogue/Category decisions;
- Domain catalogue model;
- Application `CatalogueService`/authority/recovery seams;
- SQLite Catalogue persistence/migrations/transaction runner;
- WPF Catalogue presentation/composition;
- dependency/package state;
- current Catalogue/domain/application/infrastructure/desktop test suites.

Key technical findings:

1. current SQLite schema already contains all M10 business fields; no migration is expected;
2. ClosedXML is not currently referenced but is explicitly selected/allowed by Approved architecture for M10;
3. current `UpdateProductAsync` replaces child aggregate contents and deletes omitted groups/options, so M10 must use complete-current-state overlay + a dedicated no-delete atomic import plan rather than naïve partial aggregate updates;
4. whole-workbook atomicity requires one dedicated import-store transaction boundary rather than looping existing per-command CatalogueService mutations;
5. existing centralized authority guard + durable-change notifier can be reused for commit;
6. export/parse/preview are non-writing operations;
7. historical order snapshots are structurally independent and require regression proof only, not schema redesign.

Material specification finding at audit time:

- one owner decision remained: Category `short_code` workbook semantics. The later Approved M04 decision requires future `.xlsx` support to preserve Category short code, while the older M10 catalogue workbook section defined only Category-name representation and no Categories sheet.

No production implementation was performed.

## Entry 2026-09-17 — project-owner Category short-code decision

The project owner explicitly approved the proposed M10 Category `short_code` workbook semantics.

Committed controlling records/alignment on the preparation branch:

- `docs/decisions/m10-category-short-code-workbook-semantics.md`;
- `docs/acceptance-criteria-amendment-m10-category-short-code-workbook.md`;
- amended `docs/catalogue-management.md`;
- finalized `milestone-10-preparation-readiness.md`;
- finalized prepared `milestone-10-catalogue-xlsx.md`;
- updated `milestone-10-authorization.md`, which at that historical point deliberately remained **NOT AUTHORIZED**.

Approved behavior summary:

- visible Category name + Category short code on Product rows;
- no operator-facing Categories worksheet;
- new Category may receive one optional consistent short code;
- existing Category short code is preserve/consistency data only;
- workbook cannot clear or globally change an existing Category short code;
- existing Category changes remain in the app Category manager;
- conflicts block the whole import.

Readiness result after this decision:

- material M10 owner decisions: none open;
- specification readiness: PASS;
- code/architecture seams: PASS;
- M10 became ready for separate project-owner implementation authorization;
- Issue #4 remained CLOSED;
- no executable M10 handoff existed;
- ClosedXML had not been added to production;
- no production code/schema/migration changes were made;
- M11+ remained unauthorized.

## Entry 2026-09-17 — project-owner M10 implementation authorization

The project owner explicitly stated:

`批准 M10 implementation`

Authorization was granted against the exact finalized preparation head:

`6fda83115ccde97e8d0538205eff2b769b353f71`

Controller setup actions:

- created dedicated implementation branch `codex/m10-catalogue-xlsx-authorized` from that exact preparation head;
- opened implementation PR/mailbox #22 — `M10: Catalogue .xlsx import/export` against `main`;
- changed `docs/implementation/milestone-10-authorization.md` to **AUTHORIZED** and recorded the exact preparation head;
- selected the first intentionally narrow work package ID `M10-WP1-CONTRACTS-CLOSEDXML-EXPORT-01`;
- retained PR #21 as historical VOID/CLOSED and prohibited its reuse;
- retained merge as a separate owner gate;
- retained M11/M12/M13 as unauthorized.

The implementation authorization is milestone-level only. Codex execution remains work-package gated: Issue #4 must point to exactly one complete top-level PR #22 handoff and be OPEN before execution.

## Prepared control package

The implementation line contains:

- `milestone-10-preparation-readiness.md`;
- `milestone-10-catalogue-xlsx.md`;
- `milestone-10-final-manual-acceptance.md`;
- `milestone-10-worklog.md`;
- `milestone-10-authorization.md`;
- M10 Category-short-code decision/acceptance records.

## Future implementation evidence template

Append one immutable section per authorized work package.

### WPx — `<handoff id>`

- Authorization/gate state observed:
- Starting PR head:
- Ending pushed PR head:
- Files changed:
- Production behavior added:
- Test files/cases added:
- Focused tests:
- Full Release tests:
- Release build:
- `git diff --check`:
- Dependency changes:
- Schema/migration changes:
- Authority/recovery impact:
- Privacy/synthetic-data audit:
- CI run / exact head:
- Remaining manual evidence:
- Controller findings:
- Blockers/unresolved items:
- Execution topology:
- Browser notification result:

### Owner acceptance evidence

Append only after owner execution:

- exact candidate source head;
- EXE/ZIP path/size/SHA-256;
- manual checklist result by section A–L;
- screenshots/notes where appropriate;
- defects and repair-cycle references;
- final owner PASSED/FAILED statement.

### Merge closure evidence

Append only after separate explicit merge approval:

- owner merge approval statement/reference;
- PR merged state;
- merge commit;
- resulting `main` head;
- Issue #4 closed/no active handoff;
- next milestone remains unauthorized unless separately approved.

## Governance reminder

A worklog entry, `CODEX_DONE`, controller acceptance, owner manual PASSED or green CI does not authorize the next work package, merge, M11 or any other project change. Issue #4 and explicit owner approvals remain controlling.

## Entry 2026-09-17 — `M10-WP1-CONTRACTS-CLOSEDXML-EXPORT-01`

- Authorization/gate state observed: Issue #4 OPEN; PR #22 OPEN; exact top-level handoff matched with no prior `CODEX_DONE`.
- Starting PR head: `65e1dac84d87b918abb709412e15dc2b99f8ca12`.
- Ending pushed PR head: recorded in the matching durable `CODEX_DONE` comment after push.
- Files changed: application workbook contracts/service; infrastructure ClosedXML gateway; package management; Catalogue short-code query projection; focused application/infrastructure tests; this contract status line and worklog entry.
- Production behavior added: deterministic read-only Products / OptionGroups / Options workbook export with a VeryHidden binding manifest, protected technical IDs/relationships, visible Category name and short code, inactive-row preservation, and no import/write path.
- Test files/cases added: `CatalogueWorkbookApplicationTests`; `CatalogueWorkbookIntegrationTests`.
- Focused tests: application workbook tests 2/2 passed; infrastructure workbook tests 2/2 passed.
- Full Release tests: passed 660/660 across all six Release test assemblies.
- Release build: passed with zero warnings and zero errors after restore.
- `git diff --check`: passed (only normal Git LF/CRLF conversion warnings were emitted).
- Dependency changes: pinned `ClosedXML` `0.105.1` in `Directory.Packages.props`; package is consumed only by Infrastructure and its integration tests; no COM/Interop or macro-writing dependency.
- Schema/migration changes: none.
- Authority/recovery impact: export is read-only and does not acquire the existing write-authority guard or emit durable-change notifications; no recovery/schema path changed.
- Privacy/synthetic-data audit: tests use generated IDs and synthetic catalogue values only.
- CI run / exact head: pending push and CI observation.
- Remaining manual evidence: Windows/Excel owner inspection of the exported workbook remains required; import/update/add-only/preview/commit work is excluded from WP1.
- Controller findings: no material specification conflict identified.
- Blockers/unresolved items: none for WP1 implementation; later import workflow remains unimplemented by explicit scope.
- Execution topology: isolated worktree `m10-authorized`; root worktree preserved.
- Browser notification result: pending durable `CODEX_DONE` delivery.

## Entry 2026-09-17 — `M10-WP1-REPAIR-WORKBOOK-SAFETY-02`

- Authorization/gate state observed: Issue #4 OPEN and pointing to PR #22; repair handoff comment `5717108498` matched the controller findings in `5717100264`.
- Starting PR head: `3d6403a977ac8836a43cb05ab393456aa3a4683c`.
- Ending pushed PR head: recorded in the matching durable repair `CODEX_DONE` after push.
- Exact technical repairs: business-column and blank-next-row unlock under worksheet protection; protected technical columns with InsertRows permission; full business-plus-technical filter range for binding-preserving sort; one-connection read transaction snapshot seam; length/type-prefixed canonical SHA-256 baseline encoding; invariant `CatalogueWorkbookSchema` descriptors and metadata rows.
- Files changed: `CatalogueWorkbookSchema.cs`; `CatalogueWorkbookContracts.cs`; `SqliteCatalogueStore.cs`; `ClosedXmlCatalogueWorkbookGateway.cs`; focused Application/Infrastructure workbook tests; M10 contract technical wording; this appended worklog entry.
- Focused tests: Application workbook tests **3/3 passed** and Infrastructure workbook tests **4/4 passed**, including snapshot-seam use, empty/new-row protection, sort/insertion binding, metadata descriptors and delimiter/Unicode fingerprint distinction.
- Full Release tests: **663/663 passed** across all six Release test assemblies.
- Release build: **passed with zero warnings and zero errors** for the full solution.
- `git diff --check`: **passed**.
- Dependency/schema impact: ClosedXML remains the single pinned `0.105.1` XLSX dependency; no COM/Interop and no SQLite migration/new durable business field.
- Authority/recovery impact: export remains read-only; the snapshot transaction is read-only and acquires no write authority or durable-change notification.
- Privacy/synthetic-data audit: generated IDs and synthetic catalogue values only.
- CI run / exact head: pending repair push.
- Remaining manual evidence: owner Windows/Excel inspection remains required; import parser/planner, preview, atomic commit and WPF workflow remain excluded.
- Blockers/unresolved items: none identified within the narrow repair scope.
- Browser notification result: pending durable repair `CODEX_DONE` delivery.

## Entry 2026-09-18 — `M10-WP3-ATOMIC-COMMIT-AUTHORITY-RECOVERY-07`

- Authorization/gate state observed: Issue #4 OPEN and pointing to PR #22; exact WP3 handoff comment `5726485115` matched with required starting head `5da47fe5b0f409e310555e8d9dd5fe80fa4db9cd`.
- Scope: atomic Catalogue import persistence, centralized authority/recovery integration, complete-Catalogue optimistic baseline validation, durable ID/reference resolution, rollback/concurrency and historical-order independence evidence. WP4/WPF workflow, WP5 hardening, schema changes, permanent deletion, merge and later milestones remain excluded.
- Implementation: added Application-owned commit request/result contracts and service commit orchestration; added canonical complete-Catalogue baseline fingerprint; added one-transaction SQLite commit path with commit-time validation, local/entity reference maps, delayed ID allocation, explicit INSERT/UPDATE only and no-delete omitted-row behavior; added test-only mid-batch failure hook.
- Authority/recovery: changed commits use one `IWriteAuthorityGuard.EnterWriteScopeAsync` and one non-cancellable post-commit `IDurableChangeNotifier.NotifyCommittedAsync`; no-op, authority, validation, concurrency and rollback failures notify zero times.
- Evidence added: exact-entity update/notifier, mid-batch rollback/revision, transaction-runner rollback, Add-only first-initialization, and real SQLite historical-order/reprint-independence regressions, all using synthetic catalogue/order data. The import path touches catalogue tables only.
- Files changed: `CatalogueImportContracts.cs`; `CatalogueImportPlanner.cs`; `SqliteCatalogueStore.cs`; `milestone-10-catalogue-xlsx.md`; this worklog; `M10Wp3CatalogueImportApplicationTests.cs`; `M10Wp3CatalogueImportCommitTests.cs`.
- Focused tests: WP3 Application authority/commit suite **4/4 passed**; WP3 Infrastructure commit/rollback/initialization/historical-independence suite **5/5 passed**.
- Full Release tests: **711/711 passed** across all six Release test assemblies; Release build: **0 warnings / 0 errors**.
- `git diff --check`: passed (only expected CRLF normalization warnings for edited text files). Dependency audit: ClosedXML **0.105.1** remains the sole XLSX library; no COM/Interop/macros. Schema audit: no migration, durable business field or import-history table. Authority/recovery audit: one centralized write scope, one import transaction and one successful post-commit notification; no-op/authority/validation/concurrency/rollback failures notify zero times. Delete audit: no import Delete SQL or operation. Privacy audit: synthetic/generated fixtures only.
- Remaining manual evidence: controller review and owner Windows/Excel acceptance remain unexecuted; WP4/WP5 remain unauthorized.
- Blockers/unresolved items: none identified within the authorized WP3 scope at implementation start.

## Entry 2026-09-17 — `M10-WP2-PARSER-PLANNER-PREVIEW-04`

- Authorization/gate state observed: Issue #4 OPEN and pointing to PR #22; WP1 controller accepted at `4edc540b69704194cf40fad9cef3ea03f54afd68`; exact WP2 handoff comment `5720707849` matched with no prior `CODEX_DONE`.
- Starting PR head: `4edc540b69704194cf40fad9cef3ea03f54afd68`.
- Scope: read-only untrusted workbook parser, coherent SQLite import baseline, pure deterministic Update/Add-only planner, immutable Errors/Warnings/preview/plan candidate. No business write, durable ID allocation, authority acquisition, notifier or WPF workflow.
- Technical contract: parser validates the three logical sheets, VeryHidden metadata descriptors, manifest identities/parents, row-local helpers, formulas, scalar types and corrupt input; omitted rows produce no operation. New Products/Groups/Options use deterministic preview-only local keys and exact normalized parent resolution.
- Planner contract: current baseline is overlaid only by explicit rows; Category short-code rules, domain aggregate validation, stale-workbook truth table and Add-only create-only semantics are enforced; plan model has Create/Modify/Activate/Deactivate only and no Delete operation.
- Files changed: Application import contracts/planner/service/fingerprint; Infrastructure ClosedXML import gateway and SQLite import-baseline seam; export fingerprint delegation; focused parser/planner/baseline tests; current-state M10 contract/checklist wording.
- Focused tests: WP2 Application planner tests and Infrastructure parser/baseline tests recorded with the completion evidence; existing WP1 workbook/snapshot tests retained.
- Full Release tests/build, diff check, dependency/schema/authority/recovery/privacy audits and exact-head CI are recorded in the matching durable `CODEX_DONE` comment.
- Schema/migrations: none. ClosedXML remains the sole XLSX dependency; no COM/Interop or macro behavior.
- Historical independence: WP2 is read-only and cannot rewrite historical order snapshots.
- Remaining manual evidence: owner Windows/Excel inspection and later WP3 atomic commit/WPF workflow remain outside this handoff.
- Blockers/unresolved items: none identified within WP2 scope at implementation start; any material specification conflict would stop the affected path.

## Entry 2026-09-17 — `M10-WP1-REPAIR-EXCEL-SORT-SNAPSHOT-03`

- Authorization/gate state observed: Issue #4 OPEN and pointing to PR #22; handoff comment `5717457956` matched controller review `5717448826`.
- Starting PR head: `78442bd2baf6ce75fdfe479a1be776920374503d`.
- Ending pushed PR head: recorded in the matching durable repair `CODEX_DONE` after push.
- Exact technical repairs: hidden row-local Product/OptionGroup/Option helpers are unlocked for protected Excel sorting while remaining hidden and excluded from FormatColumns/unhide permission; sort ranges include all row-local helpers; mandatory `ICatalogueWorkbookSnapshotQueries` removed the mixed-revision fallback; production SQLite snapshot uses one read connection/read transaction; internal synchronization seam and deterministic concurrent-commit integration evidence added; export contract version now has one schema source.
- Files changed: `CatalogueWorkbookContracts.cs`; `ClosedXmlCatalogueWorkbookGateway.cs`; `SqliteCatalogueStore.cs`; focused Application/Infrastructure workbook tests; M10 technical contract/worklog documentation.
- Focused tests: Application workbook tests **3/3 passed**; Infrastructure workbook/snapshot tests **6/6 passed**, including Product/OptionGroup/Option sort and insertion bindings plus deterministic concurrent SQLite snapshot coherence.
- Full Release tests: **665/665 passed** across all six Release test assemblies.
- Release build: **passed with zero warnings and zero errors** for the full solution.
- `git diff --check`: **passed**.
- Dependency/schema impact: ClosedXML remains the single pinned `0.105.1` XLSX dependency; no COM/Interop, second XLSX library, SQLite migration or new durable business field.
- Authority/recovery impact: export remains read-only; the snapshot transaction acquires no write authority and emits no durable-change notification.
- Privacy/synthetic-data audit: generated IDs and synthetic catalogue values only.
- CI run / exact head: pending repair push.
- Remaining manual evidence: owner Windows/Excel inspection of protected sorting remains required; import parser/planner, preview, atomic commit and WPF workflow remain excluded.
- Controller findings: the two findings in `5717448826` are addressed within this narrow repair scope.
- Blockers/unresolved items: none identified within this repair scope.
- Execution topology: isolated worktree `m10-authorized`; root worktree preserved.
- Browser notification result: pending durable repair `CODEX_DONE` delivery.

## Entry 2026-09-18 — `M10-WP3-REPAIR-COMMIT-SAFETY-EVIDENCE-08`

- Authorization/gate state observed: Issue #4 OPEN and pointing to PR #22; controller review `5727045011` and exact handoff `5727059641` authorize this repair from starting head `318154edfbe6d48b1f6f2ac7474ec897fc585c05`.
- Scope: strict Application commit-boundary validation, production preview-baseline binding, affected-row staging safety, stable SQLite concurrency mapping and focused Application/Infrastructure evidence. WP4/WPF workflow, WP5 hardening, schema changes, permanent deletion, merge and later milestones remain excluded.
- Repairs: added an Application-owned validator for entity/action/reference/scalar/category/parent/resulting-state invariants; removed the production plan-plus-baseline convenience bypass; staged only changed Product codes and changed Group/Option orders with collision-safe temporary values; mapped SQLite BUSY/LOCKED/BUSY_SNAPSHOT to blocking `concurrent-write-conflict`.
- Evidence added: malformed-plan/action-matrix tests, authority-scope wait, baseline-token and stale-baseline rejection, first-initialization/rollback/no-delete/revision coverage, safe Product-code and Group/Option-order swaps, and historical-order/reprint independence. All fixtures are synthetic.
- Files changed: `CatalogueImportCommitValidator.cs`; `CatalogueImportPlanner.cs`; `SqliteCatalogueStore.cs`; `M10Wp3CatalogueImportApplicationTests.cs`; `M10Wp3CatalogueImportCommitValidatorTests.cs`; `M10Wp3CatalogueImportCommitTests.cs`; this contract and worklog.
- Prior-entry correction: the earlier WP3 entry recorded **711/711** before exact-head CI; its exact-head CI result was **712/712**. This repair entry records the later final Release count after the new evidence tests.
- Focused tests before final full run: Application WP3 suite **9/9 passed** plus validator matrix **4/4 passed**; Infrastructure WP3 suite **8/8 passed**.
- Ending pushed PR head: `a6a05d6df66bdb917113694ad77f58c44e2aa9a1`.
- Full Release tests: **720/720 passed** across all six Release test assemblies; Release build: **0 warnings / 0 errors**. Exact-head CI **#766 / run `35324532363`** completed **SUCCESS** for the pushed head.
- `git diff --check`: passed (only expected CRLF normalization warnings for edited text files). Dependency audit: ClosedXML **0.105.1** remains the sole XLSX library; no COM/Interop/macros. Schema audit: no migration, durable business field or import-history table. Authority/recovery audit: one centralized write scope, one import transaction and one successful post-commit notification; no-op/authority/validation/concurrency/rollback failures notify zero times. Delete audit: import path has no Delete operation or SQL. Privacy audit: synthetic/generated fixtures only.
- Remaining manual evidence: controller review and owner Windows/Excel acceptance remain unexecuted; WP4/WP5 remain unauthorized.
- Blockers/unresolved items: none identified within this authorized repair scope.
- Browser notification result: pending durable `CODEX_DONE` delivery.

## Entry 2026-09-18 — `M10-WP2-REPAIR-NORMALIZATION-EVIDENCE-06`

- Authorization/gate state observed: Issue #4 OPEN and pointing to PR #22; controller review `5721769594` and exact handoff comment `5721775503` authorize this repair from `e48ffc79154b8b791dde89b68c824122a0c68626`.
- Scope: WP2 read-side normalization/fail-closed repair and direct Application/Infrastructure evidence only. No SQLite business write, durable ID allocation, authority acquisition, notifier, WPF workflow, WP3 commit, merge or later milestone work.
- Repairs: existing/new Category short-code collision checks now use normalized keys end-to-end; duplicate current normalized Category names never select an arbitrary row or throw; malformed identity inputs fail closed without nullable dereference.
- Evidence added: direct Category matrix, stale-workbook five-outcome table, product/group/option edit and state cases, Add-only/no-delete/identity/parent-binding cases, domain validation cases, deterministic plan-shape checks, metadata/manifest/formula/visibility workbook cases, and live-parent-rename/fingerprint integration evidence.
- Files changed: `CatalogueImportPlanner.cs`; Application planner evidence tests; Infrastructure workbook integration tests; this worklog.
- Dependency/schema/authority impact: ClosedXML remains the sole pinned `0.105.1` XLSX dependency; no COM/Interop/macros, migration or durable field; planner/parser remain read-only with no guard/notifier.
- Privacy/synthetic-data audit: generated IDs and synthetic catalogue/workbook fixtures only.
- Focused tests: Application planner/evidence suite **26/26 passed**; Infrastructure workbook/parser/baseline suite **18/18 passed**.
- Full Release tests: **703/703 passed** across all six Release test assemblies; Release build **passed with zero warnings and zero errors**.
- `git diff --check`: passed; exact-head CI status is recorded in the matching durable `CODEX_DONE` comment after the repair push.
- Remaining manual evidence: owner Windows/Excel acceptance remains unexecuted; WP3 atomic persistence, WP4 WPF workflow and WP5 hardening remain outside this handoff.
- Blockers/unresolved items: none identified within the authorized repair scope at implementation start.

## Entry 2026-09-17 — `M10-WP2-REPAIR-PLANNER-PARSER-CONFORMANCE-05`

- Authorization/gate state observed: Issue #4 OPEN and pointing to PR #22; exact repair handoff comment `5721408633` matched controller findings `5721396562` at starting head `af2c6d0ddf56e0a2581b8d3677f2cc4297955a3c`.
- Scope: read-only WP2 conformance repair only. No SQLite business write, write-authority acquisition, durable ID allocation, notifier, WPF workflow, WP3 commit, merge or later milestone work.
- Repairs: Product/Option Modify now excludes pure Active transitions; Category short-code collision/proposal handling is normalized with `CatalogueNormalization.Key`; immutable plan operations now carry explicit existing/new entity, Category and parent references with null Create IDs; planned-new Categories use deterministic local keys; Option parent helper validation follows OptionGroup → Product manifest bindings; manifest parent-display fingerprints preserve legitimate original descriptors after live parent rename/recode; localized visible headers are accepted through invariant metadata; business-sheet visibility and formula-in-blank-row fail-closed checks are enforced; SelectionMode accepts only SINGLE/MULTI.
- Technical contract: manifest `ParentDisplayFingerprint` uses the Application typed length/type-prefixed SHA-256 helpers (`ParentProduct` and `ParentOptionGroup`). Existing references carry opaque Guid + local key; new references carry local key only; WP3 receives no preview-generated durable Guid and no name/code re-resolution requirement.
- Files changed: Application import contracts/planner; Infrastructure ClosedXML import/export gateways; Application planner tests; Infrastructure workbook/parser tests; current-state M10 contract and this worklog.
- Focused tests: Application planner suite and Infrastructure Catalogue workbook/parser/baseline suite recorded in the matching durable `CODEX_DONE` comment.
- Full Release tests/build, diff check, dependency/schema/authority/recovery/privacy audits and exact-head CI are recorded in the matching durable `CODEX_DONE` comment.
- Remaining manual evidence: owner Windows/Excel acceptance remains unexecuted; WP3 atomic persistence, WP4 WPF workflow and WP5 hardening remain outside this handoff.
- Blockers/unresolved items: none identified within the authorized repair scope.
- Execution topology: isolated worktree `m10-authorized`; root worktree preserved.
- Browser notification result: pending durable repair `CODEX_DONE` delivery.

## Entry 2026-09-18 — `M10-WP3-REPAIR-CONTRADICTIONS-EVIDENCE-09`

- Authorization/gate state observed: Issue #4 OPEN and pointing to PR #22; controller review `5727488193` and exact handoff `5727495209` authorize this repair from starting head `8ea404465b07620762c2b297ad1ba07c450386ad8`.
- Scope: close the remaining WP3 contradictory-combined-operation and typed durable-ID-resolution findings; keep semantic validation in the Application boundary; add direct Application/production-SQLite evidence. WP4/WPF workflow, WP5 hardening, schema changes, permanent deletion, merge and later milestones remain excluded.
- Repairs: combined operations are canonicalized by entity type/local key with deterministic payload/reference equality independent of list order; only operation kind may differ. Final-state/action validation uses that canonical payload. Infrastructure no longer contains the removed duplicate semantic validation helpers. New durable IDs use typed Category/Product/OptionGroup/Option namespaces; untyped alias resolution is rejected and allocation remains after validation inside the single transaction.
- Evidence added: order-independent contradictory Product operations, defined Modify plus state final payload, invalid mode and planned Category collisions/orphans, baseline fingerprint order independence, key/reference/parent payload mismatch, production `WriteAuthorityGuard` barrier, exact revision/no-op/concurrency/rollback/history cases, and empty/colliding generated-ID fail-closed evidence. Combined operation semantics are covered for both list orders.
- Files changed: `CatalogueImportCommitValidator.cs`; `SqliteCatalogueStore.cs`; `M10Wp3CatalogueImportApplicationTests.cs`; `M10Wp3CatalogueImportCommitValidatorTests.cs`; `M10Wp3CatalogueImportCommitTests.cs`; this worklog.
- Focused tests before final full run: Application validator suite **10/10 passed**, Application authority/commit suite **5/5 passed**, Infrastructure WP3 commit suite **12/12 passed**.
- Starting-head correction: prior docs-only follow-up head `8ea404465b07620762c2b297ad1ba07c450386ad8` follows the green implementation/evidence head `a6a05d6df66bdb917113694ad77f58c44e2aa9a1`; this repair must finish with CI green on its own final pushed head.
- Full Release tests: **730/730 passed** across all six Release test assemblies; Release build: **0 warnings / 0 errors**. `git diff --check`: passed (only expected CRLF normalization warnings for edited text files). Exact-head CI: pending final push and observation. Dependency audit: ClosedXML **0.105.1** remains the sole XLSX library; no COM/Interop/macros. Schema audit: no migration, durable business field or import-history table. Authority/recovery audit: one production `WriteAuthorityGuard` scope, one SQLite import transaction and one successful post-commit notification; no-op/authority/validation/concurrency/rollback failures notify zero times. Delete audit: import path has no Delete operation or SQL. Privacy audit: synthetic/generated fixtures only.
- Remaining manual evidence: controller review and owner Windows/Excel acceptance remain unexecuted; WP4/WP5 remain unauthorized.
- Blockers/unresolved items: none identified within this authorized repair scope at implementation start.
- Execution topology: isolated worktree `m10-authorized`; root worktree preserved.
- Browser notification result: pending durable `CODEX_DONE` delivery.

## Entry 2026-09-18 — `M10-WP3-EVIDENCE-CLOSURE-10`

- Authorization/gate state observed: Issue #4 OPEN and pointing to PR #22; controller handoff `5728000721` and review `5727993457` authorize this evidence closure from starting head `9f7e7d52902067d8caa3838ce74b6587f92e7b34`.
- Scope: close the remaining WP3 evidence matrix only: real production WriteAuthorityGuard transition barrier, explicit SQLite BUSY race outcome, Application validator action/reference/parent/category/combined-operation matrix, complete hierarchy allocation/rollback/no-delete/reparent/order-edge and unrelated-order token evidence. WP4/WPF workflow, WP5 hardening, schema changes, permanent deletion, merge and later milestones remain excluded.
- Evidence added: production `WriteAuthorityGuard` transition now waits through both the import transaction and non-cancellable post-commit notifier; race evidence records the actual writer result (`blocked:5`, SQLite BUSY) and confirms `concurrent-write-conflict` with no partial import; validator matrix covers duplicate Create/Modify/Activate/Deactivate, state/payload mismatch, state-only edits, typed/missing references and parents, orphan/planned Category and Category name/short-code conflicts, order-independent contradictory Option payload/references, and nested fingerprint relationship changes; SQLite evidence covers blank-ID Product/Group/Option allocation with exact parents, complete hierarchy rollback, omitted-row no-delete and Category preservation, reparent rejection, display-order `1000000` staging, and an unrelated order write that leaves the Catalogue baseline token valid.
- Production defect found and fixed by the new evidence: `CatalogueImportCommitValidator.ValidateState` now rejects an Activate payload with `isActive=false` or a Deactivate payload with `isActive=true` even when the requested final state would otherwise look redundant; this is fail-closed frozen-contract validation, with no schema or persistence-model change.
- Files changed: `CatalogueImportCommitValidator.cs`; `M10Wp3CatalogueImportCommitValidatorTests.cs`; `M10Wp3CatalogueImportCommitTests.cs`; this worklog.
- Focused tests: Application validator matrix **13/13 passed**; Application authority/commit suite **5/5 passed**; Infrastructure WP3 production commit/evidence suite **18/18 passed**. The strengthened race test records the writer as blocked with provider error code 5; no writer commit is claimed.
- Full Release tests: **739/739 passed** across all six Release test assemblies. Release build: **0 warnings / 0 errors**. Exact-head CI is recorded in the matching durable `CODEX_DONE` after the final push; no post-CI docs-only commit is permitted.
- `git diff --check`: passed (only expected CRLF normalization warnings for edited text files). Dependency audit: ClosedXML **0.105.1** remains the sole XLSX library; no COM/Interop/macros. Schema audit: no migration, durable business field or import-history table. Authority/recovery audit: one production write-authority scope, one import transaction and one successful post-commit notification; no-op/authority/validation/concurrency/rollback failures notify zero times. Delete audit: import path has no Delete operation or SQL. Privacy audit: synthetic/generated fixtures only.
- Remaining manual evidence: controller review and owner Windows/Excel acceptance remain unexecuted; WP4/WP5 remain unauthorized.
- Blockers/unresolved items: none identified within this authorized evidence handoff.
- Ending pushed PR head: to be recorded in the matching durable `CODEX_DONE` after final push; browser notification result pending durable delivery.

## Entry 2026-09-18 — `M10-WP3-FINAL-EVIDENCE-CLOSURE-11`

- Authorization/gate state observed: Issue #4 OPEN and pointing to PR #22; controller handoff `5728314096` and review `5728308380` authorize this final evidence-only closure from starting head `f3318d135f455b4a9aae9bfb71907ae5474f54c5`.
- Scope: close only the remaining direct WP3 persistence/evidence gaps. WP4/WPF, WP5, schema changes, permanent Delete semantics, merge and M11+ remain excluded.
- Evidence added: existing Product → planned-new Category atomic reassignment with one revision advance; new Group → existing Product and new Option → existing Group exact-parent allocation; existing Option re-parent rejection before mutation; complete omitted Product/Group/Option no-delete and exact Category preservation; full Category/Product/Group/Option rollback after an Option write and after transaction-runner pre-commit failure with unchanged revision; real SQLite UNIQUE constraint conflict mapped to `persistence-conflict` with atomic rollback; temp-code namespace collision safety; unaffected Option display order `1000000` staging edge; and final Activate+Deactivate plus contradictory Option-reference order-independence validator cases.
- Production code: no business behavior or persistence semantics changed. Added only an internal test-only SQLite constraint fault hook, null in production, so the evidence test can execute a genuine constraint statement inside the real import transaction.
- Files changed: `SqliteCatalogueStore.cs` (test-only internal hook); `M10Wp3CatalogueImportCommitValidatorTests.cs`; `M10Wp3CatalogueImportCommitTests.cs`; this worklog.
- Focused tests: Application validator/authority WP3 suites **14/14 + 5/5 passed**; Infrastructure WP3 production SQLite suite **23/23 passed**. The real constraint test observed SQLite UNIQUE failure and confirmed `persistence-conflict`, no partial rows and unchanged revision.
- Full Release tests: **745/745 passed** across all six Release test assemblies. Release build: **0 warnings / 0 errors**. `git diff --check`: passed (only expected CRLF normalization warnings).
- Audits: ClosedXML **0.105.1** remains the sole XLSX dependency; no COM/Interop. No schema migration, durable field or import-history table. Import remains one transaction with no Delete operation or SQL. Authority/recovery behavior is unchanged. Fixtures use synthetic/generated data only.
- Exact final pushed head and CI: to be recorded in the matching durable `CODEX_DONE` after the final push; no post-CI docs-only commit is permitted.
- Remaining manual evidence: controller review and owner Windows/Excel acceptance remain pending; WP4/WP5/M11+ remain unauthorized. Blockers: none within this handoff. Browser notification result pending durable delivery.

## Entry 2026-09-18 — `M10-WP4-WPF-OPERATOR-WORKFLOW-12`

- Authorization/gate state observed: Issue #4 OPEN and pointing to PR #22; controller handoff `5729812048` authorizes the Windows/WPF operator workflow from starting head `bfa5f5c5bc3ee844b560cc7d23caa4157fbbadb3`.
- Scope: Desktop-owned localized Export/Import actions, explicit Update/Add-only selection, native file dialogs, read-only immutable preview, authority-aware Confirm, atomic commit invocation, refresh barrier, re-entrancy/Cancel safety and FR/zh-CN workflow strings. WP5 hardening, owner manual acceptance, schema/import-history changes, permanent deletion, merge and M11+ remain excluded.
- Implementation: `CatalogueWorkbookWorkflowViewModel` owns file/preview/commit state and preserves the selected target on failed export through sibling-temp replacement; `CatalogueImportModeDialog` requires an explicit choice; `CatalogueImportPreviewDialog` presents source/mode/authority, counts, localized Errors/Warnings, affected rows, safety notices and new Categories; `CompositionRoot` wires one ClosedXML gateway and the existing Application services; `ShellViewModel` refreshes Admin Catalogue and Caisse/order-entry presentation only after a changed successful commit.
- Localization: French and Simplified Chinese resource entries cover action labels, file filter, mode descriptions, preview notices, commit outcomes and stable issue-code messages. Existing Catalogue maintenance actions and authority guard remain unchanged.
- Focused validation: Desktop Release build **0 warnings / 0 errors**; WP4 automated/STA/layout tests and full Release matrix remain to be run before final delivery. Manual Windows/Excel acceptance remains owner-owned and unexecuted.
- Blockers at implementation point: none identified within this handoff; exact-head CI and completion evidence remain pending final push.

## Entry 2026-09-18 — `M10-WP4-REPAIR-LOCALIZATION-OUTCOME-WPF-EVIDENCE-13`

- Authorization/gate state observed: Issue #4 OPEN and pointing to PR #22; controller review `5730301398` and exact repair handoff `5730312614` authorize this narrow Desktop/WPF repair from starting head `7753e55781af6cc39d35fcc23e3f51ac26d56cfb`.
- Scope: repair only the WP4 presentation boundary and its evidence. Accepted WP1/WP2/WP3 workbook, parser, planner, commit and persistence semantics are unchanged; WP5, owner Windows/Excel acceptance, merge and M11+ remain excluded.
- Outcome separation: workflow state now distinguishes pre-result commit failure, blocking commit-result failure, successful no-op, changed success and durable changed success followed by presentation-refresh failure. Commit-stage exceptions return a localized blocking failure without raw exception text, never refresh, and permanently disable retry. Durable success remains success when the post-commit refresh fails; the existing shell barrier remains fail-closed.
- Commit issue presentation: structured result issues replace the preview issue collection after a failed Confirm, preserving severity, worksheet, row, field, stable code and localized message in the bound DataGrid.
- Localization/lifecycle: all current import issue families have deliberate FR/zh-CN mapping with safe stable-code-only fallback; entity/action and issue-grid headers are localized; Preview Escape/Cancel/close are fail-closed while busy; no-op and refresh-failure results remain visibly displayed with Close, and a durable attempt cannot be retried from the old preview.
- Evidence added: outcome separation/exception/no-op/refresh-failure tests, complete stable issue-code coverage/fallback test, production composition single-store/single-service wiring smoke assertions, FR/zh-CN resource checks and existing WPF architecture/lifecycle matrix. All fixtures are synthetic/generated.
- Files changed: `CatalogueWorkbookWorkflow.cs`, `CatalogueImportDialogs.cs`, `MainWindow.xaml.cs`, `Localization.cs`, FR/zh-CN `Resources.resx`, `M10Wp4DesktopTests.cs`, and this worklog/current-state reconciliation.
- Local validation before final push: focused M10 WP4 Architecture tests **8/8 passed**; relevant Architecture/WPF/localization/regression filter **102/102 passed**; Desktop Release build **0 warnings / 0 errors**; `git diff --check` passed (only expected CRLF normalization warnings). Exact-head CI remains pending the final repair push.
- Audits: ClosedXML `0.105.1` remains the sole XLSX dependency; no COM/Interop/macros, schema migration, durable field, import-history or Delete operation; authority guard and existing refresh barrier remain in force; privacy fixtures are synthetic/generated only.
- Remaining evidence: exact-final-head CI and controller review; owner Windows/Excel manual acceptance remains unexecuted. Blockers: none within this repair scope.

## Entry 2026-09-18 — `M10-WP4-FINAL-WPF-EVIDENCE-CLOSURE-14`

- Authorization/gate state observed: Issue #4 OPEN and pointing to PR #22; controller review `5730800374` and exact handoff `5730810263` authorize this final WP4 evidence closure from starting head `2101c2a6455bb542fdb916126357576bc1bda1ac`.
- Scope: tests/evidence plus the single authorized Desktop presentation correction only. WP1/WP2/WP3 workbook, parser, planner, commit and persistence semantics remain unchanged; WP5, owner Windows/Excel acceptance, merge and M11+ remain excluded.
- Presentation correction: a pre-result `transaction-failed` exception now has `Worksheet`, `ExcelRow` and `FieldKey` all null; the source filename remains in the preview Context section and is never misrepresented as a worksheet.
- Evidence added: direct workflow authority/barrier tests; real STA mode-dialog explicit-choice/cancel/Escape/close coverage; real STA preview failure/no-op/refresh-failure lifecycle coverage; structured issue context and retry barrier checks; FR/zh-CN stable-code/fallback coverage; real rendered action/scroll-surface checks at 640x480, 940x700 and 1280x900.
- Lineage/evidence correction: repository compare/merge-base confirms valid lineage from the authorized previous starting head `7753e55781af6cc39d35fcc23e3f51ac26d56cfb3`; the prior `CODEX_DONE` `5730734974` contained only a textual starting-head typo and is intentionally not edited or deleted.
- Files changed: `CatalogueWorkbookWorkflow.cs`; `M10Wp4DesktopTests.cs`; living M10 status/contract/manual-acceptance docs; this worklog.
- Focused tests: M10 WP4 Architecture suite **15/15 passed**; full Release solution matrix **760/760 passed** across all test assemblies. Release build completed with **0 warnings / 0 errors** and `git diff --check` passed (only expected line-ending notices).
- Audits: ClosedXML `0.105.1` remains the sole XLSX dependency; no COM/Interop/macros, schema migration, durable field, import-history or Delete operation; authority guard/refresh barrier remain fail-closed; fixtures are synthetic/generated only.
- Remaining manual evidence: owner Windows/Excel acceptance remains unexecuted. Blockers: none within this authorized evidence closure.
- Exact-final-head CI remains required after the final push; no post-CI docs-only follow-up is permitted.

## Entry 2026-09-18 — `M10-WP4-EVIDENCE-INTEGRITY-CLOSURE-15`

- Authorization/gate state observed: Issue #4 OPEN and pointing to PR #22; controller review `5731229688` and exact handoff `5731236782` authorize this tests/docs-only evidence-integrity closure from starting head `f9369b993ebcd816692058b73961649ab3e35c36`.
- Scope: close the remaining WP4 evidence-integrity gaps only. Production code, WP5 hardening, owner Windows/Excel acceptance, merge and M11+ remain excluded.
- Evidence added: real-STA mode-dialog rendering for Update/Add-only choices and close safety; preview Cancel/Escape/X no-commit paths; commit-in-progress close/Escape/re-entrancy blocking; pre-result exception context and no-retry behavior; structured issue worksheet/row/field DataGrid presentation; authoritative Error versus non-blocking Warning confirmation state; FR/zh-CN render/layout and relocalization invariants; Product/OptionGroup/Option Create/Modify/Activate/Deactivate affected-row matrix; and production-composition Admin/Entry refresh-barrier success/failure coverage.
- Files changed: `M10Wp4DesktopTests.cs`; living `implementation-status.md`, `implementation/milestone-10-catalogue-xlsx.md`, `implementation/milestone-10-final-manual-acceptance.md`; this worklog.
- Lineage correction: `7753e55781af6cc39d35fcc23e3f51ac26d56cfb` is the prior authorized WP4 implementation head and is an ancestor of the current `f9369b993ebcd816692058b73961649ab3e35c36`; repository compare/merge-base confirms valid lineage. Earlier `...56cfb3` occurrences were textual head typos only and are not alternate repository history.
- Focused M10 WP4 Architecture tests: **25/25 passed**. Full Architecture Release suite: **170/170 passed**. Full Release solution test matrix: **770/770 passed** across all test assemblies. Release build: **0 warnings / 0 errors**. `git diff --check` passed (only expected line-ending notices).
- Audits: ClosedXML `0.105.1` remains the sole XLSX dependency; no COM/Interop/macros, schema migration, durable field, import-history or M10 Delete operation; existing authority/refresh barrier remains fail-closed; fixtures are synthetic/generated only; historical order independence remains covered by accepted WP3 evidence.
- Remaining manual evidence: owner Windows/Excel acceptance remains unexecuted. Blockers: none within this authorized evidence closure. Exact-final-head CI and durable completion comment are required after the final push; no post-CI docs-only follow-up is permitted.
