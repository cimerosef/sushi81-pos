# M10 — Catalogue `.xlsx` import/export — worklog / evidence ledger

**Status:** Preparation complete — implementation NOT AUTHORIZED  
**Created:** 2026-09-17  
**Milestone:** M10  
**Issue #4:** CLOSED  
**Executable Codex handoff:** none

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
- updated `milestone-10-authorization.md`, which deliberately remains **NOT AUTHORIZED**.

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
- M10 is ready for separate project-owner implementation authorization;
- Issue #4 remains CLOSED;
- no executable M10 handoff exists;
- ClosedXML has not been added to production;
- no production code/schema/migration changes were made;
- M11+ remain unauthorized.

## Prepared control package

Preparation branch contains:

- `milestone-10-preparation-readiness.md`;
- `milestone-10-catalogue-xlsx.md`;
- `milestone-10-final-manual-acceptance.md`;
- `milestone-10-worklog.md`;
- `milestone-10-authorization.md`;
- M10 Category-short-code decision/acceptance records.

The implementation contract is prepared but non-executable.

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