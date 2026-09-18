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
- Full Release tests: **720/720 passed** across all six Release test assemblies; Release build: **0 warnings / 0 errors**. Exact pushed head, CI run and browser notification result remain pending final repair push and durable `CODEX_DONE` delivery.
- `git diff --check`: passed (only expected CRLF normalization warnings for edited text files). Dependency audit: ClosedXML **0.105.1** remains the sole XLSX library; no COM/Interop/macros. Schema audit: no migration, durable business field or import-history table. Authority/recovery audit: one centralized write scope, one import transaction and one successful post-commit notification; no-op/authority/validation/concurrency/rollback failures notify zero times. Delete audit: import path has no Delete operation or SQL. Privacy audit: synthetic/generated fixtures only.
- Remaining manual evidence: controller review and owner Windows/Excel acceptance remain unexecuted; WP4/WP5 remain unauthorized.
- Blockers/unresolved items: none identified within this authorized repair scope.

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
