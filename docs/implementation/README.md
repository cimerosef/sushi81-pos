# Implementation contracts

Milestone implementation contracts under this directory define the executable scope for Phase 6 delivery.

All implementation handoffs issued on or after 2026-08-30 inherit the cross-cutting execution governance in:

- `agent-execution-contract.md`
- `interactive-quality-gate.md`
- `post-task-power-policy.md`

`agent-execution-contract.md` requires the Codex main agent to assess safe parallelization before implementation, delegate only independent work packages, prefer GPT-5.6 Luna subagents at the highest available reasoning effort (`max` when explicitly controllable), retain main-agent integration/review responsibility, and report execution topology in `CODEX_DONE` evidence.

`interactive-quality-gate.md` is the mandatory defect-prevention/verification layer for user-visible implementation, especially WPF. It requires operator-journey analysis, framework-lifecycle reasoning, realistic STA/WPF regression coverage for critical dynamic/event paths, explicit localization/DataContext handling, layout checks, defect-escape retrospectives, and focused adjacent-pattern audits after operator-found defects. For a user-visible crash, repeated defect in the same UI area, or lifecycle defect that escaped tests, the main agent should normally delegate one independent read-only Luna audit/test-design package when the runtime supports it, while keeping the production hotfix single-writer.

`post-task-power-policy.md` makes host sleep/hibernate/shutdown strictly explicit and one-shot. The default for every handoff/run is `POST_TASK_POWER_ACTION: NONE`; a previous request to sleep or shut down expires with that task and must never become a persistent preference. Future handoffs should carry an explicit per-handoff power-action directive, using a non-NONE value only when the operator asks for that specific run.

Milestone-specific contracts remain authoritative for milestone scope and acceptance requirements. These cross-cutting contracts do not change frozen V1 product, business, data or architecture semantics.

Future milestone contracts M04–M13 must include a short **Parallel execution plan** section that identifies likely independent workstreams and the dependency seams that must be stabilized before concurrent delegation, and must treat `interactive-quality-gate.md` as inherited whenever the milestone includes user-visible UI/workflow behavior.
