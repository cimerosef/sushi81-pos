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

## Completed M05 contracts

M05 — Lifecycle, payments, search and operational dashboard — is Passed and merged through PR #10 at merge commit `79499d7c6ed65a74f524097c1507ca648dc151c3`.

Accepted production-code head: `84c1c534c1df105ccb1839cbc6dfc9e0e055bb70`. Final documentation/status head: `217d187dd3ef5498c11f21bc516eccc6737fa952`.

Historical M05 records remain under this directory, including:

- `milestone-05-lifecycle-payments-search-dashboard.md`;
- `milestone-05-authorization.md`;
- `milestone-05-worklog.md`;
- `milestone-05-final-manual-acceptance.md`;
- related manual-acceptance findings/remediation records.

M05 is no longer active work and does not itself authorize M06.

## Completed M06 contracts

M06 — Local recovery and authoritative/read-only enforcement — is Passed and merged through PR #11 at merge commit `2c5eb52740d0c12e3e837579ecceac6d0600b59e` after complete project-owner Windows/WPF manual acceptance.

Accepted production repair head: `4a0c1ca9e44a6c48899e6ef8dc211172371e4d20`. Final documentation/PR head: `86326d81551aa4cb5cdcbc6826b8c740317b34c4`. Release tests: 364/364 Passed.

Historical M06 records remain under this directory:

- `milestone-06-local-recovery-read-only-enforcement.md` — detailed historical implementation contract;
- `milestone-06-authorization.md` — historical durable project-owner authorization;
- `milestone-06-worklog.md` — historical implementation/evidence record;
- `milestone-06-final-manual-acceptance.md` — Passed project-owner Windows/WPF acceptance record.

M06 is no longer active work. Its completion does not authorize M07.

## Next milestone

M07 — Pairing, target-directed formal handoff and disaster recovery — is the next planned milestone but is not yet implementation-authorized. No M07 implementation branch, PR, durable authorization or active handoff exists; Codex execution gate issue #4 is CLOSED. Any pre-authorization M07 design-review material under this directory is non-executable until the project owner explicitly approves the M07 implementation and the normal closed-gate preparation sequence is completed.

M07 inherits every cross-cutting governance file listed above. It owns the production connection of the M02 target-directed GitHub transport/revalidation semantics to the M06 production authority/read-only/recovery seams, while preserving the single Application-layer write guard as the business-write safety boundary.

Future milestone contracts M07–M13 must include a short **Parallel execution plan** section that identifies likely independent workstreams and the dependency seams that must be stabilized before concurrent delegation, and must treat `interactive-quality-gate.md` as inherited whenever the milestone includes user-visible UI/workflow behavior.
