# Implementation contracts

Milestone implementation contracts under this directory define controlled Phase 6 delivery scope.

All implementation handoffs inherit:

- `agent-execution-contract.md`;
- `interactive-quality-gate.md`;
- `control-state-preservation.md`;
- `post-task-power-policy.md`.

These files govern execution mechanics/quality and do not themselves authorize implementation or change frozen business/data semantics.

## Current milestone state

M01 through M09 are Passed and merged. The independent post-M09 Hiboutik daily CB/Espèce dashboard enhancement is also Passed and merged through PR #19 at `861cfba1dfacbb3289395c0370f6d42765b6c223`.

Key recent baselines:

- M07 — PR #13 merged at `9ea7d5e15bceba6932cb2caba50d0afb64ca1ff9`;
- M08 — PR #14 merged at `8f246ce7fb32baa33e1dfe1d334175bf2df60c1f`;
- M09 — PR #17 merged at `d840066d8d2ffa1856c4fcd88dbfdd3c8f2a1be5`;
- post-M09 dashboard — PR #19 merged at `861cfba1dfacbb3289395c0370f6d42765b6c223`.

Historical contracts/worklogs/manual-acceptance records remain authoritative in their existing files and PRs. Do not rewrite historical evidence merely to match later project state.

## Current M11/M12 state

M11 Gestion intermediate export is Passed/merged through PR #24 at `1a94f3400e0aa9fe9f878bbe98a8285112206ba9`. Accepted runtime candidate `77ccecf9d947462e96e74b8aa1d99ced30e3788e`; controller closure comment `5762784303`; post-merge CI #821 / run `35617254203` succeeded with 831/831 passed, 0 failed, 0 skipped and Release build 0 warnings / 0 errors.

M12 Annual archive and historical access is owner-authorized on `codex/m12-annual-archive-authorized` / Draft PR #25. Current control files are `milestone-12-preparation-readiness.md`, `milestone-12-annual-archive-historical-access.md` and `milestone-12-authorization.md`. The owner-approved 2026-09-21 amendment makes canonical annual archives permanent local application-managed databases, removes OneDrive from annual archive publication/access, and adds explicit operator-selected archive export. WP1 remains the first package. M13 remains unauthorized.

## Historical M10 preparation — Catalogue `.xlsx` import/export

M10 preparation is complete and is **READY FOR PROJECT-OWNER IMPLEMENTATION AUTHORIZATION**, but implementation remains **NOT AUTHORIZED**.

Prepared control package:

- `milestone-10-preparation-readiness.md` — final readiness audit;
- `milestone-10-catalogue-xlsx.md` — prepared non-executable implementation contract;
- `milestone-10-final-manual-acceptance.md` — prepared owner Windows/WPF + real Excel acceptance checklist;
- `milestone-10-worklog.md` — preparation/evidence ledger;
- `milestone-10-authorization.md` — **NOT AUTHORIZED**;
- `../decisions/m10-category-short-code-workbook-semantics.md` — owner-approved M10 Category short-code workbook decision;
- `../acceptance-criteria-amendment-m10-category-short-code-workbook.md` — matching acceptance clarification.

The project owner approved the Category short-code workbook semantics on 2026-09-17. That specification decision closes the last material M10 readiness gap but **does not authorize implementation**.

Historical execution state at that preparation point:

- Issue #4: CLOSED;
- executable M10 `CODEX_HANDOFF_READY`: none;
- production M10 implementation: not started;
- ClosedXML production dependency: not yet added;
- M11/M12/M13: unauthorized.

A separate explicit project-owner statement such as **“批准 M10 implementation”** is still required before the controller may mark M10 Authorized. Only after that may the dedicated implementation branch/PR/mailbox be established, one complete handoff be published, Issue #4 be updated to that unique pointer, and the gate be opened.

## M12 current package

- `milestone-12-preparation-readiness.md` — code/spec readiness review and remote-publication blocker record;
- `milestone-12-annual-archive-historical-access.md` — work-package contract and WP1 scope;
- `milestone-12-authorization.md` — durable owner authorization;
- `milestone-12-worklog.md` — active package/evidence ledger;
- Draft PR #25 — active durable mailbox;
- Issue #4 — sole execution switch/status pointer.

## Governance reminder

Issue #4 is the master execution switch and active-mailbox pointer.

- OPEN = Codex may execute only the single matching valid handoff.
- CLOSED = Codex makes no project changes.

Preparation/readiness, a Passed previous milestone, controller acceptance, `CODEX_DONE`, owner manual acceptance or appearance in the implementation plan never authorize implementation, the next work package or merge by themselves.

Merge always requires separate explicit project-owner approval.

`POST_TASK_POWER_ACTION` defaults to `NONE` unless a particular authorized handoff explicitly states otherwise.
