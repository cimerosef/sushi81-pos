# M10 implementation authorization — Catalogue `.xlsx` import/export

**Status:** **AUTHORIZED**  
**Prepared:** 2026-09-17  
**Readiness finalized:** 2026-09-17  
**Authorized:** 2026-09-17 by explicit project-owner statement `批准 M10 implementation`  
**Milestone:** M10 — Catalogue `.xlsx` import/export  
**Finalized preparation head approved by owner:** `6fda83115ccde97e8d0538205eff2b769b353f71`  
**Implementation branch:** `codex/m10-catalogue-xlsx-authorized`  
**Implementation PR/mailbox:** #22 — `M10: Catalogue .xlsx import/export`  
**Execution gate:** Issue #4 is the sole execution switch and may be OPEN only while it points to exactly one complete executable handoff  
**First authorized handoff ID:** `M10-WP1-CONTRACTS-CLOSEDXML-EXPORT-01`

## Current control state

M10 specification/readiness preparation is complete and the project owner has now separately and explicitly authorized **M10 implementation**.

The owner authorization applies to the finalized preparation state at:

`6fda83115ccde97e8d0538205eff2b769b353f71`

That preparation was based on current `main` at:

`861cfba1dfacbb3289395c0370f6d42765b6c223`

which is the merge commit of PR #19 — `Post-M09: Hiboutik daily CB/Espèce dashboard`.

Current governance facts:

- PR #19 is CLOSED / MERGED;
- Issue #18 is CLOSED / completed;
- Category `short_code` M10 workbook semantics are Approved;
- M10 readiness has no material owner-decision blocker;
- M10 implementation is AUTHORIZED at milestone level;
- dedicated implementation branch is `codex/m10-catalogue-xlsx-authorized`;
- dedicated implementation PR/mailbox is #22;
- PR #21 remains historical VOID/CLOSED and must never be used as a mailbox;
- M11, M12 and M13 remain unauthorized;
- M10 merge remains separately unauthorized until explicit owner approval.

## Scope of owner implementation authorization

The owner authorizes implementation of the Approved M10 Catalogue `.xlsx` milestone under the frozen specification/readiness package, including the technical implementation choices already accepted as non-business choices in that package.

This milestone-level authorization permits the controller to establish work-package handoffs under Issue #4. It does **not** allow Codex to execute arbitrary M10 work. Codex may execute only the single exact handoff named by the OPEN Issue #4 pointer.

A `CODEX_DONE`, controller acceptance, green CI or owner manual acceptance does not auto-authorize the next work package.

## Controlling preparation package

The implementation must remain within:

- `docs/decisions/m10-category-short-code-workbook-semantics.md`;
- `docs/acceptance-criteria-amendment-m10-category-short-code-workbook.md`;
- `docs/catalogue-management.md`;
- `docs/acceptance-criteria.md`;
- `docs/implementation/milestone-10-preparation-readiness.md`;
- `docs/implementation/milestone-10-catalogue-xlsx.md`;
- `docs/implementation/milestone-10-final-manual-acceptance.md`;
- this authorization record and the milestone worklog.

## First executable work package

The first work package is:

`CODEX_HANDOFF_READY: M10-WP1-CONTRACTS-CLOSEDXML-EXPORT-01`

WP1 is intentionally narrow. It authorizes only:

1. application-owned M10 workbook/export contracts and DTO boundaries needed for read-only export;
2. one pinned/tested ClosedXML dependency isolated behind the Infrastructure workbook gateway, with no Excel COM/Interop and no second XLSX library;
3. deterministic read-only Catalogue `.xlsx` export foundation using exactly three operator-facing logical sheets: `Products`, `OptionGroups`, `Options`;
4. protected/hidden technical identity plus non-operator VeryHidden metadata/manifest needed for safe later import;
5. Approved Category name + Category short-code export semantics;
6. automated Domain/Application/Infrastructure evidence for the WP1 scope, including real temporary `.xlsx` round-trip/reopen assertions where applicable;
7. docs/worklog evidence for the exact WP1 result.

WP1 explicitly does **not** authorize:

- workbook import parsing/planning;
- Update/Add-only import behavior;
- preview UI;
- atomic import commit/store;
- WPF Import/Export operator workflow beyond any minimal non-production seam strictly required by the export foundation;
- SQLite/schema migration;
- deletion behavior changes;
- M11 Gestion export;
- M12/M13;
- merge.

## Issue #4 transition rule

Execution is allowed only after all of the following are simultaneously true:

1. PR #22 is OPEN on `codex/m10-catalogue-xlsx-authorized`;
2. one complete top-level PR #22 Conversation comment publishes the exact WP1 `CODEX_HANDOFF_READY`;
3. Issue #4 body points to PR #22, the branch and that exact handoff only;
4. Issue #4 is OPEN.

If any pointer differs, Codex must stop.

After matching `CODEX_DONE`, the controller must close Issue #4 before review/acceptance unless a specifically documented repair handoff is issued. No follow-on WP is automatic.

## Merge and later milestones

This owner statement authorizes implementation only.

It does **not** authorize:

- merging PR #22;
- starting M11;
- starting M12;
- starting M13;
- any unrelated enhancement.

Merge requires a later separate explicit project-owner approval after required automated and owner manual acceptance evidence is complete.