# M10 — Catalogue `.xlsx` import/export — worklog / evidence ledger

**Status:** Implementation authorized — WP1 mailbox setup in progress  
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
