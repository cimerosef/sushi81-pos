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

## M11 current closure state

M11 Gestion intermediate export implementation and owner A–E acceptance are complete on PR #24. The accepted runtime candidate is source head `77ccecf9d947462e96e74b8aa1d99ced30e3788e`; M11 is **Closure-ready**, pending final controller closure and separate explicit project-owner merge approval. PR #24 remains Draft/Open/unmerged. M12/M13 remain unauthorized. Detailed current evidence is maintained in `implementation-status.md` and `milestone-11-final-manual-acceptance.md`; historical preparation records below are not rewritten.

## M10 preparation — Catalogue `.xlsx` import/export

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

Current execution state:

- Issue #4: CLOSED;
- executable M10 `CODEX_HANDOFF_READY`: none;
- production M10 implementation: not started;
- ClosedXML production dependency: not yet added;
- M11/M12/M13: unauthorized.

A separate explicit project-owner statement such as **“批准 M10 implementation”** is still required before the controller may mark M10 Authorized. Only after that may the dedicated implementation branch/PR/mailbox be established, one complete handoff be published, Issue #4 be updated to that unique pointer, and the gate be opened.

## Governance reminder

Issue #4 is the master execution switch and active-mailbox pointer.

- OPEN = Codex may execute only the single matching valid handoff.
- CLOSED = Codex makes no project changes.

Preparation/readiness, a Passed previous milestone, controller acceptance, `CODEX_DONE`, owner manual acceptance or appearance in the implementation plan never authorize implementation, the next work package or merge by themselves.

Merge always requires separate explicit project-owner approval.

`POST_TASK_POWER_ACTION` defaults to `NONE` unless a particular authorized handoff explicitly states otherwise.
