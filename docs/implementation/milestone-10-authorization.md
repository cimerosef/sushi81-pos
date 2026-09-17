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
**Execution gate:** CLOSED until a new valid executable handoff is published and Issue #4 is updated/reopened

## Durable project-owner authorization

On 2026-09-17 the project owner separately and explicitly stated:

> 批准 M10 implementation

That statement is durably recorded in PR #21 top-level Conversation comment `5716521030` (`OWNER_AUTHORIZATION_RECORDED: M10-IMPLEMENTATION`).

This is the required separate M10 implementation authorization. It is distinct from the earlier approval of the Category `short_code` workbook semantics.

This authorization permits **M10 implementation only** under the controlling package below. It does not authorize merge, M11, M12 or M13.

## Control-history clarification

Before the explicit implementation-authorization statement above had been durably recorded in GitHub, an attempted WP1 handoff was correctly invalidated/tombstoned and the authorization record was returned to NOT AUTHORIZED.

That temporary correction is preserved as historical governance evidence. It is now superseded by the explicit owner authorization recorded in PR #21 comment `5716521030`.

The tombstoned handoff must not be revived or reused. A new handoff ID is required after this authorization record is in place.

## Authorized baseline

The exact finalized preparation state selected for implementation is:

`7d42092b2e93bad52b05b7a6628eecf54b74233c`

The preparation baseline was `main` at the already-merged post-M09 commit:

`861cfba1dfacbb3289395c0370f6d42765b6c223`

Historical preparation PR #20 is CLOSED / unmerged and superseded by the dedicated implementation line. Its evidence remains historical and must not be rewritten.

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

- complete Catalogue `.xlsx` export with operator-facing logical sheets `Products`, `OptionGroups`, `Options`;
- approved visible Product/Category business fields including Category `short_code` semantics;
- hidden/locked Product/OptionGroup/Option technical identity and relationship data plus protected VeryHidden technical metadata;
- safe ID-preserving Update mode;
- explicit Add-only mode including first initialization;
- complete validation, immutable preview, blocking Errors and non-blocking Warnings;
- deterministic fail-safe identity/relationship handling with no guessing;
- row-absence-means-no-change semantics and no import Delete operation;
- one atomic whole-import SQLite commit;
- current centralized authority guard and one post-commit durable-change notification;
- FR / zh-CN workflow;
- one pinned ClosedXML dependency behind the Infrastructure boundary;
- required automated evidence and real Windows/Excel owner acceptance preparation.

## Non-negotiable boundaries

M10 must not:

- add an operator-facing `Categories` worksheet;
- expose `category_id` as operator-maintained data;
- match an existing Product/OptionGroup/Option by code/name as a substitute for required protected identity in Update mode;
- let Add-only silently update existing records;
- guess, fuzzy-match or silently repair corrupt IDs/relationships;
- treat workbook row absence as deletion;
- add an Excel permanent-delete mechanism;
- globally change or clear an existing Category short code through Product rows;
- rewrite historical order snapshots;
- bypass authority/recovery boundaries;
- add a SQLite schema migration unless a genuine new blocker is returned to the controller first;
- implement M11/M12/M13 behavior.

## Work-package authorization model

The milestone implementation contract defines WP1 through WP5.

The owner authorization permits the controller to issue M10 work-package handoffs, but only the single handoff explicitly named by OPEN Issue #4 may execute at any moment.

A `CODEX_DONE` does not authorize the next package. Controller acceptance does not automatically authorize the next package. Each later package requires a new unique controller handoff and matching Issue #4 pointer.

The intended first executable package is WP1 only:

- Application-owned workbook contracts/DTOs;
- one pinned ClosedXML dependency;
- Infrastructure deterministic read-only export writer;
- `Products` / `OptionGroups` / `Options` export;
- protected technical identity + VeryHidden metadata;
- empty-Catalogue template;
- focused workbook/protection/round-trip/export-read-only evidence;
- no import commit.

## Gate transition

At the time of this record, Issue #4 must remain CLOSED until the controller completes all of the following:

1. publish a **new** complete top-level `CODEX_HANDOFF_READY` on PR #21 using a fresh handoff ID;
2. scope it to M10 WP1 only;
3. ensure there is no conflicting active handoff;
4. update Issue #4 to point only to that exact branch / PR / new handoff;
5. only then reopen Issue #4.

The earlier tombstoned handoff is invalid forever and cannot satisfy this gate.

## Merge and later milestones

PR #21 merge requires separate explicit project-owner approval after controller review and owner/manual acceptance.

M11, M12 and M13 remain unauthorized regardless of M10 progress or completion.
