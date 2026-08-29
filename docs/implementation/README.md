# Implementation contracts

Milestone implementation contracts under this directory define the executable scope for Phase 6 delivery.

All implementation handoffs issued on or after 2026-08-30 inherit the cross-cutting execution governance in:

- `agent-execution-contract.md`

That contract requires the Codex main agent to assess safe parallelization before implementation, delegate only independent work packages, prefer GPT-5.6 Luna subagents at the highest available reasoning effort (`max` when explicitly controllable), retain main-agent integration/review responsibility, and report execution topology in `CODEX_DONE` evidence.

Milestone-specific contracts remain authoritative for milestone scope and acceptance requirements. The execution contract does not change frozen V1 product, business, data or architecture semantics.

Future milestone contracts M04–M13 must include a short **Parallel execution plan** section that identifies likely independent workstreams and the dependency seams that must be stabilized before concurrent delegation.
