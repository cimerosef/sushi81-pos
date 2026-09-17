# M10 implementation authorization — Catalogue `.xlsx` import/export

**Status:** **AUTHORIZED**  
**Prepared:** 2026-09-17  
**Readiness finalized:** 2026-09-17  
**Authorized:** 2026-09-17 by explicit project-owner approval  
**Milestone:** M10 — Catalogue `.xlsx` import/export  
**Preparation branch:** `prep/m10-catalogue-xlsx`  
**Exact authorized preparation head:** `7d42092b2e93bad52b05b7a6628eecf54b74233c`  
**Active implementation branch:** `codex/m10-catalogue-xlsx`  
**Active implementation PR/mailbox:** #21 — `M10: Catalogue .xlsx import/export`  
**Execution gate:** CLOSED until the complete first executable handoff is published and Issue #4 is updated to point to it

## Project-owner authorization

On 2026-09-17 the project owner explicitly stated:

> 批准 M10 implementation

This authorizes **M10 implementation only** under the controlling package below.

It does not authorize:

- merge of PR #21;
- M11 — Gestion export;
- M12 — annual archive/historical access;
- M13 — installer/final acceptance;
- any business/specification change outside the approved M10 contract.

## Authorized baseline

The exact finalized preparation head authorized for implementation is:

`7d42092b2e93bad52b05b7a6628eecf54b74233c`

That preparation was based on `main` at the already-merged post-M09 baseline:

`861cfba1dfacbb3289395c0370f6d42765b6c223`

PR #19 and Issue #18 are CLOSED / completed. Historical post-M09 evidence remains unchanged.

## Authorized controlling package

M10 implementation must remain within:

- `docs/catalogue-management.md`;
- `docs/decisions/m10-category-short-code-workbook-semantics.md`;
- `docs/acceptance-criteria.md`;
- `docs/acceptance-criteria-amendment-m10-category-short-code-workbook.md`;
- `docs/implementation/milestone-10-preparation-readiness.md`;
- `docs/implementation/milestone-10-catalogue-xlsx.md`;
- `docs/implementation/milestone-10-final-manual-acceptance.md`;
- `docs/implementation/milestone-10-worklog.md`;
- current Approved V1 baseline documents and decision records referenced there;
- `docs/implementation/agent-execution-contract.md`;
- `docs/implementation/interactive-quality-gate.md`;
- `docs/implementation/control-state-preservation.md`;
- `docs/implementation/post-task-power-policy.md`.

## Authorized scope summary

M10 may implement:

- complete Catalogue `.xlsx` export using the three operator-facing logical sheets `Products`, `OptionGroups`, `Options`;
- visible Product/Category business fields including approved Category `short_code` semantics;
- hidden/locked Product/OptionGroup/Option technical identity and relationship data plus a protected VeryHidden technical manifest;
- deterministic update-mode re-import by exact protected existing identity;
- explicit Add-only mode including empty-catalogue first initialization;
- complete workbook/identity/parent/category/business validation;
- immutable preview with blocking Errors and non-blocking Warnings;
- row-addressable localized issues;
- overlay planning in which omitted rows never imply deletion;
- one atomic whole-import SQLite commit;
- existing centralized authority guard and one post-commit durable-change notification;
- FR / zh-CN WPF workflow;
- ClosedXML as the single pinned XLSX library behind the Infrastructure boundary;
- automated evidence and real Windows/Excel owner-acceptance candidate required by the M10 contract.

## Non-negotiable boundaries

M10 must not:

- introduce an operator-facing `Categories` worksheet;
- expose `category_id` as operator-maintained data;
- use Product code/name as a substitute for missing existing technical identity in Update mode;
- let Add-only silently update existing Product/OptionGroup/Option records;
- silently repair, guess or fuzzy-match corrupt IDs or parent relationships;
- treat workbook row absence as deletion;
- add an Excel permanent-delete mechanism;
- globally change or clear an existing Category short code through Product rows;
- rewrite historical order snapshots;
- bypass current authority/recovery boundaries;
- add a SQLite schema migration unless a new material blocker is surfaced to the controller first;
- implement M11/M12/M13 behavior.

## Work-package authorization model

The milestone contract defines WP1 through WP5. This owner authorization allows the controller to publish executable handoffs for M10, but **only the single handoff currently named by Issue #4 is executable at any moment**.

A `CODEX_DONE` does not authorize the next package. Controller acceptance does not authorize the next package automatically. Each later package requires a new explicit controller handoff while the Issue #4 execution gate remains the controlling pointer.

The first executable package is WP1 only:

- application-owned workbook DTO/interfaces;
- one pinned ClosedXML dependency;
- Infrastructure export writer;
- deterministic `Products` / `OptionGroups` / `Options` export;
- protected technical identity/VeryHidden metadata;
- empty-Catalogue template;
- export read-only behavior and focused workbook/protection/round-trip tests;
- **no import commit**.

## Gate transition

Before Codex may execute WP1, the controller must complete all of the following in order:

1. keep Issue #4 CLOSED while preparing the executable handoff;
2. publish exactly one complete top-level `CODEX_HANDOFF_READY` on PR #21;
3. ensure that handoff names branch `codex/m10-catalogue-xlsx`, PR #21 and WP1 only;
4. ensure no conflicting active handoff exists;
5. update Issue #4 so its sole active pointer names that exact handoff/branch/PR;
6. only then reopen Issue #4.

After those prerequisites, Codex may execute only that single WP1 handoff.

## Merge and later milestones

PR #21 merge always requires separate explicit project-owner approval after controller and owner acceptance.

M11, M12 and M13 remain unauthorized regardless of M10 progress or completion.
