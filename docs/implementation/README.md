# Implementation contracts

Milestone implementation contracts under this directory define the executable scope for Phase 6 delivery.

All implementation handoffs issued on or after 2026-08-30 inherit the cross-cutting execution governance in:

- `agent-execution-contract.md`
- `interactive-quality-gate.md`
- `control-state-preservation.md`
- `post-task-power-policy.md`

`agent-execution-contract.md` requires the Codex main agent to assess safe parallelization before implementation, delegate only independent work packages, prefer GPT-5.6 Luna subagents at the highest available reasoning effort (`max` when explicitly controllable), retain main-agent integration/review responsibility, and report execution topology in `CODEX_DONE` evidence.

`interactive-quality-gate.md` is the mandatory defect-prevention/verification layer for user-visible implementation, especially WPF. It requires operator-journey analysis, framework-lifecycle reasoning, realistic STA/WPF regression coverage for critical dynamic/event paths, explicit localization/DataContext handling, layout checks, defect-escape retrospectives, and focused adjacent-pattern audits after operator-found defects.

`control-state-preservation.md` requires interactive actions to preserve unrelated editor/control state unless the frozen workflow explicitly couples those values.

`post-task-power-policy.md` makes host sleep/hibernate/shutdown strictly explicit and one-shot. The default for every handoff/run is `POST_TASK_POWER_ACTION: NONE`.

Milestone-specific contracts remain authoritative for milestone scope and acceptance requirements. These cross-cutting contracts do not change frozen V1 product, business, data or architecture semantics.

## Completed M03 contracts

M03 is Passed and merged through PR #5 at merge commit `57f89cac0672d6d98dda7dc3c9a8ba7b3434e292`.

Historical M03 contracts remain under this directory as implementation/evidence records.

## Completed M04 contracts

M04 — First complete order-entry vertical slice — is Passed and merged through PR #6 at merge commit `ab218263bd4eee9c1be203d36acc552988cef43a` after complete Windows/WPF manual acceptance.

Historical M04 records remain under this directory, including:

- `milestone-04-order-entry.md`;
- `milestone-04-authorization.md`;
- `milestone-04-worklog.md`;
- `milestone-04-final-manual-acceptance.md`.

## Current M05 contract

M05 — Lifecycle, payments, search and operational dashboard — is the current controlled implementation milestone under PR #10 / branch `codex/m05-lifecycle-payments-search-dashboard`.

Authoritative M05 preparation:

- `milestone-05-lifecycle-payments-search-dashboard.md` — detailed implementation contract;
- `milestone-05-authorization.md` — durable project-owner authorization;
- `../decisions/m05-lifecycle-payment-modification-clarifications.md` — approved D1/D2/D3 decision record.

M05 inherits every cross-cutting governance file listed above. Its implementation PR must remain open/unmerged until explicit project-owner merge approval. M05 completion does not authorize M06.

Future milestone contracts M06–M13 must include a short **Parallel execution plan** section that identifies likely independent workstreams and the dependency seams that must be stabilized before concurrent delegation, and must treat `interactive-quality-gate.md` as inherited whenever the milestone includes user-visible UI/workflow behavior.
