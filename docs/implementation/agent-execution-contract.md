# Implementation execution contract — main agent + Luna subagents

**Status:** Approved  
**Approved by:** project owner  
**Effective:** 2026-08-30  
**Scope:** all Sushi81 POS implementation handoffs issued after this approval, including remaining M03 remediation work where applicable and milestones M04–M13.

This contract is process/implementation governance only. It does not amend frozen V1 business behavior, data semantics, architecture boundaries, acceptance criteria, or milestone scope.

## 1. Objective

Use parallel subagents when doing so can reduce wall-clock implementation time without reducing reliability, reviewability, or specification fidelity.

Priority remains:

**reliability > simplicity > maintainability > operational clarity > speed > novelty.**

Parallelism is a tool, not a requirement. A narrow task that is safer or faster to execute serially must remain serial.

## 2. Required execution model

For each authorized implementation handoff, the Codex main agent must first perform a short decomposition/parallelization assessment before writing production code.

The main agent remains the single integrator and is responsible for:

- reading the complete authoritative milestone contract and current `AGENTS.md`;
- identifying shared interfaces, invariants, file ownership and dependency order;
- making only the technical decisions already delegated by approved project rules;
- freezing any shared implementation seams needed before delegation;
- deciding which work packages are genuinely independent;
- delegating suitable independent work to subagents;
- reviewing every returned change before integration;
- resolving conflicts and rejecting non-conforming subagent output;
- performing final integration, whole-solution verification and completion reporting.

Subagents are implementation workers. They do not gain independent authority to change product behavior, business rules, data meaning, architecture decisions, acceptance criteria, milestone scope or GitHub governance.

## 3. Preferred subagent model and effort

When the Codex runtime supports explicit per-subagent selection, every implementation subagent must use:

- **model:** GPT-5.6 Luna;
- **reasoning effort:** the highest available level, preferably `max`.

Do not silently substitute a different subagent model when explicit selection is available.

If the current Codex runtime cannot explicitly request, guarantee or report the per-subagent model and/or effort level:

1. do not fabricate that Luna/max was used;
2. record the limitation in the completion evidence;
3. prefer main-agent serial execution for safety when model substitution would materially reduce confidence;
4. otherwise use only a runtime-provided worker when the task is mechanical, independently reviewable and the main agent can fully validate the result before integration.

The main agent may always choose to execute a task itself when delegation would be less reliable.

## 4. Parallelization eligibility gate

A work package may run in parallel only when all of the following are true:

1. its required behavior is already fixed by the authoritative contract;
2. its input/output or shared interface boundary is sufficiently stable;
3. it can be described with an explicit deliverable and verification target;
4. its expected write set is disjoint from other concurrent work packages, or conflicts are structurally isolated by separate worktrees/branches;
5. it does not require a business/product/architecture decision from the operator;
6. it does not depend on the unfinished implementation result of another concurrent package;
7. the main agent can review and test it independently before integration.

If any of these conditions is not satisfied, execute serially.

## 5. Work that should normally be parallelized

After shared contracts/seams are frozen, good candidates include independent combinations such as:

- Domain/Application implementation versus WPF presentation work when the application contracts are already fixed;
- SQLite store/integration-test work versus localized UI resources when they do not modify the same shared contracts;
- independent adapters or export/printing components with explicit interfaces;
- isolated test-fixture or regression-test additions whose production dependencies are already stable;
- documentation/evidence updates that do not overlap with active production edits;
- independent review/audit tasks that inspect rather than modify the same code.

A milestone may use several subagents simultaneously when these boundaries are clear.

## 6. Work that should normally remain serial

Do not create artificial parallelism for:

- narrow bug fixes concentrated in one UI/control/service/file area;
- shared interface or contract design that downstream work depends on;
- architecture/specification decisions;
- schema design whose shape is still being determined;
- multiple tasks that need to edit the same hot files or the same method/class;
- final integration and conflict resolution;
- real-device/operator acceptance;
- destructive or authority-sensitive live tests;
- GitHub merge/gate transitions;
- any task where parallel execution makes causal debugging or data-safety reasoning harder.

For these cases, one main-agent path is preferred even if multiple subagents are technically available.

## 7. Isolation and file ownership

When multiple subagents write code concurrently:

- use separate worktrees/branches or an equivalent isolated workspace when the runtime supports it;
- assign each subagent an explicit work package and expected file/area ownership;
- avoid two subagents editing the same production file concurrently;
- avoid simultaneous modification of shared project files (`*.csproj`, central package files, migrations registry, composition root, shared contracts) unless the main agent explicitly serializes those edits;
- subagents must not merge directly to `main` or independently finalize the implementation PR;
- the main agent integrates returned commits/patches deliberately and reviews the resulting diff.

If overlap emerges during execution, stop the conflicting package and serialize the integration rather than letting workers race.

## 8. Dependency-first execution order

For larger milestones, use this default order when applicable:

1. main agent reads contract and maps dependencies;
2. main agent establishes/finalizes shared Domain/Application contracts and cross-cutting invariants;
3. main agent defines subagent work packages and disjoint write sets;
4. independent subagents execute in parallel;
5. main agent collects and reviews each result;
6. main agent integrates in dependency order;
7. main agent performs targeted integration tests;
8. main agent runs the full required restore/build/test/publish/CI matrix;
9. manual/operator evidence remains separate and must never be fabricated.

Do not delegate downstream work before the interface it depends on is stable merely to maximize concurrency.

## 9. Required subagent task contract

Each delegated work package must state at least:

- authoritative milestone/handoff ID;
- exact scope and explicit exclusions;
- relevant files/modules or expected write area;
- interfaces/invariants that must not change;
- required tests or verification commands;
- prohibition on starting another milestone or inventing behavior;
- requirement to return a concise summary of files changed, tests run and any blocker.

The subagent must stop and return the ambiguity to the main agent if it encounters a specification conflict or needs a non-delegated decision.

## 10. Main-agent integration review

Subagent output is not accepted merely because it compiles or because the subagent reports success.

Before integration/final completion, the main agent must verify:

- the diff matches the assigned scope;
- no frozen behavior or acceptance rule changed;
- no unauthorized dependency or architectural shortcut was introduced;
- concurrent work did not create incompatible assumptions;
- tests cover the intended behavior rather than only implementation details;
- full milestone build/test/publish requirements still pass after all pieces are combined.

The main agent owns the final implementation quality.

## 11. Safety restrictions

Parallel agents must never be used to bypass existing safety/governance rules.

In particular:

- no subagent may use real customer/order/payment/credential data;
- no two agents may perform competing live authority/handoff operations;
- destructive retention, recovery, migration or real-device tests require the same explicit operator/test controls as before;
- no subagent may merge the PR, open the next milestone, change issue #4 state, or claim manual acceptance;
- secrets/tokens must not be copied into prompts, logs, screenshots or completion evidence.

## 12. Completion evidence

Every `CODEX_DONE` for an implementation handoff after this contract takes effect must include an **execution topology** section.

At minimum report:

- whether the main agent evaluated parallelization;
- whether subagents were used;
- number of subagents used concurrently and/or serially;
- for each subagent: work package, returned result, and integrated/rejected status;
- requested model and reasoning effort (`GPT-5.6 Luna`, highest available / `max`) when explicitly controllable;
- actual model/effort if the runtime exposes them;
- if actual model/effort is not verifiable, state that explicitly rather than guessing;
- any work deliberately kept serial and the short reason (for example overlapping write set or narrow hotfix);
- final integration verification performed by the main agent.

This reporting requirement is evidence only; it does not change the existing durable mailbox or browser-notification protocol.

## 13. Future milestone contracts

Every new milestone implementation contract (M04–M13) must treat this file as inherited execution governance and should include a short **Parallel execution plan** section identifying likely parallel workstreams and the dependency seams that must be frozen first.

The plan is provisional: the Codex main agent must still re-evaluate actual parallelization at runtime based on the current repository diff and dependency state.

If a milestone-specific contract conflicts with this execution contract on purely execution mechanics, the newer explicitly approved milestone contract governs. Neither document can override frozen product/business/architecture specifications without the normal specification-amendment process.
