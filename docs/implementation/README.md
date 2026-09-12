# Implementation contracts

Milestone implementation contracts under this directory define the executable scope for Phase 6 delivery.

All implementation handoffs inherit the cross-cutting governance in:

- `agent-execution-contract.md`;
- `interactive-quality-gate.md`;
- `control-state-preservation.md`;
- `post-task-power-policy.md`.

These files govern implementation mechanics/quality and do not themselves change frozen product/business/data behavior.

## Completed milestones

M01 through M07 are Passed and merged.

Key recent closure state:

- M04 — Passed/merged through PR #6 at `ab218263bd4eee9c1be203d36acc552988cef43a`.
- M05 — Passed/merged through PR #10 at `79499d7c6ed65a74f524097c1507ca648dc151c3`.
- M06 — Passed/merged through PR #11 at `2c5eb52740d0c12e3e837579ecceac6d0600b59e`.
- M07 — Passed/merged through PR #13 at `9ea7d5e15bceba6932cb2caba50d0afb64ca1ff9`; accepted production implementation head `e971580ef43d3b50366d51733ca9431ca0997e8d`; final closure docs/evidence head `d586c847f2dd541815b8c00565c58b3685a3e4be`; exact-head CI #631 / run `34698627867` succeeded; 541/541 tests Passed; project-owner Windows/WPF multi-device acceptance Passed.

Historical milestone contracts/worklogs/manual-acceptance records remain authoritative in their existing files and PRs. Do not rewrite historical evidence to make later project state appear historical.

## M08 preparation — printing and reprinting

M08 is the current next milestone but remains **Preparation / NOT AUTHORIZED**.

Controlling preparation package:

- `milestone-08-printing-reprinting.md` — detailed base implementation contract;
- `milestone-08-contract-addendum-print-layout-identity.md` — controlling addendum resolving owner print-layout/receipt-identity semantics;
- `milestone-08-preparation-readiness.md` — preparation/readiness audit;
- `milestone-08-authorization.md` — currently `NOT AUTHORIZED`;
- `milestone-08-worklog.md` — preparation/evidence record;
- `milestone-08-final-manual-acceptance.md` — prepared Windows/WPF + real printer acceptance checklist;
- `../decisions/m08-print-layout-and-receipt-identity.md` — owner-approved page-1 kitchen / page-2 customer visual target and exact Sushi 81 receipt identity.

The former M08-D1 receipt-identity gap is resolved. The customer identity is frozen as authoritative SQLite business configuration; printer queue selection remains local technical configuration.

This preparation does **not** authorize Codex.

Current execution state:

- Issue #4: CLOSED;
- M08 implementation branch: none;
- M08 implementation PR: none;
- executable `CODEX_HANDOFF_READY`: none;
- M09+: unauthorized.

A separate explicit project-owner approval of **M08 implementation** is required. Only after that approval may the governance controller update `milestone-08-authorization.md` to Authorized, record the exact preparation head, create the dedicated M08 branch/PR/mailbox, publish one valid handoff, and finally open Issue #4.

## Execution model for a future authorized M08 run

The M08 contract inherits `agent-execution-contract.md` and must use dependency-first execution:

1. main agent re-reads current GitHub authority and freezes shared print contracts/seams;
2. main agent evaluates safe parallel work packages;
3. likely parallel candidates after seams are stable include deterministic print-model/layout work, Windows queue/config adapter work, and independent WPF/localization/test work where write sets do not overlap;
4. final integration, CompositionRoot wiring, whole-solution verification and GitHub gate transitions remain main-agent/serial responsibilities;
5. real printer/project-owner acceptance is never delegated or fabricated.

The exact runtime topology remains a Codex responsibility under the execution contract once M08 is actually authorized.

## Governance reminder

Issue #4 is the master execution switch and active-mailbox pointer.

- OPEN = Codex may execute only the single matching valid handoff.
- CLOSED = Codex makes no project changes.

Preparation documents, a Passed prior milestone, or a future milestone appearing in the implementation plan never authorize execution by themselves.

`POST_TASK_POWER_ACTION` defaults to `NONE` unless a particular future handoff explicitly states otherwise.
