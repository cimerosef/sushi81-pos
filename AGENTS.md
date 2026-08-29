# AGENTS.md

## Purpose

This repository is design-led. Implementation agents must execute the frozen approved specifications rather than invent product or business decisions.

## Current phase

**V1 Specification is frozen — Phase 5 complete (2026-08-27).**

The repository is ready for implementation, but production application code should be created only when an explicit implementation/Codex task authorizes the relevant milestone.

A specification freeze is not itself an instruction to implement everything at once. Implement the assigned milestone only.

## Authoritative sources

Use the approved documents under `docs/`, the approved records under `docs/decisions/` and `docs/acceptance-criteria.md` as the source of truth.

The formal V1 freeze record is `docs/v1-specification-freeze.md`.

`docs/current-system.md` describes the former/current Excel/VBA operation and is contextual/historical. It must not override later approved V1 target behavior.

If code, assumptions, issue text, prior chat discussion or legacy behavior conflicts with the frozen GitHub specification, follow the approved GitHub specification. If two approved specification sources genuinely contradict one another, stop the affected implementation path and surface the conflict instead of silently choosing a behavior.

## Changes that require an explicit approved specification amendment

Do not independently change or invent any of the following:

- core technology stack;
- database engine or core data model semantics;
- order lifecycle or finalization semantics;
- payment model or received-payment date attribution;
- discount, VAT or commercial business rules;
- synchronization/handoff/backup/disaster-recovery strategy;
- data retention/archive/deletion behavior;
- printing architecture or business-facing print semantics;
- paste-import business behavior;
- export eligibility, contract or correction semantics;
- introduction of paid, proprietary, hosted or subscription dependencies that alter the approved cost/architecture boundary;
- any behavior required by `docs/acceptance-criteria.md`.

When such a question appears during implementation:

1. stop the affected decision path;
2. describe the ambiguity/conflict precisely;
3. obtain an approved specification decision/amendment;
4. update the affected GitHub specification before implementing the changed behavior.

## Safe implementation autonomy

Within the frozen specification, implementation agents may normally:

- implement assigned functionality;
- choose physical SQL table/column/index names consistent with the logical data model;
- choose internal class/module/file names and ordinary code organization consistent with the approved architecture;
- select minor UI layout/typography details that do not change approved workflow or required visibility;
- choose low-level serialization/coordination details where the storage specification explicitly delegates them and all invariants remain preserved;
- refactor local code without changing behavior;
- add/improve tests;
- improve error handling, diagnostics and logging;
- fix clear defects;
- improve internal naming and maintainability.

Use this priority order when choosing between equally conformant implementation options:

**reliability > simplicity > maintainability > operational clarity > novelty.**

## Acceptance contract

Implementation work is not complete merely because the application builds or appears to work manually.

The V1 implementation must satisfy `docs/acceptance-criteria.md`.

Where practical, acceptance criteria should be converted into automated unit/integration/regression tests during implementation rather than postponed to the end.

## Data safety

Business data is durable and non-disposable.

- Never delete, overwrite, reset or migrate real business data outside an explicitly approved operation with a recovery path.
- Schema migrations must be versioned and testable.
- Migration failure must not silently reset production data.
- Backward compatibility and recovery implications must be considered for database changes.
- Never commit real customer, order, payment, credential or other sensitive production data to Git.
- Parser/test fixtures must use synthetic or sanitized data.

## Dependencies

Prefer free and open-source dependencies compatible with the approved architecture.

Use the frozen technology/dependency boundaries in `docs/architecture.md`, including the approved .NET/WPF/SQLite, ClosedXML and Windows-printing directions.

Do not add a paid service or dependency that creates recurring operational cost without explicit approval.

## Scope discipline

Implement the requested milestone only. Do not add speculative product features merely because they seem useful.

Do not reintroduce superseded complexity, especially the former Hiboutik emergency-order UI/original-total/discrepancy/reconciliation model. Hiboutik paste-created orders use the normal order model plus only the hidden anti-double-counting source discriminator defined by the frozen specification.

## ChatGPT ↔ Codex collaboration handoff protocol v2

This section is the durable collaboration protocol for implementation/review relay between the ChatGPT project lead and Codex. It **supersedes the earlier browser-polling rule** in which one long-running Codex task repeatedly opened the ChatGPT conversation and waited for a new marker.

### Durable mailbox

The active GitHub implementation pull request is the durable inter-agent mailbox.

- GitHub PR comments are the authoritative relay channel between ChatGPT and Codex.
- ChatGPT browser access may be used as a convenience/fast notification path, but it is not the durable source of relay state.
- A Codex run must not remain alive merely to poll a ChatGPT browser tab.
- Ending one Codex run must not be interpreted as cancelling the recurring relay automation.

### ChatGPT → Codex handoff

When ChatGPT has completed review/design work, no user decision is required, and Codex should execute the next task, ChatGPT must:

1. leave a top-level comment on the active implementation PR containing a unique marker:

   `CODEX_HANDOFF_READY: <unique-id>`

2. include the complete executable Codex instruction in that same PR comment, or an unambiguous pointer to an authoritative committed implementation contract;
3. optionally repeat the same marker at the end of the ChatGPT conversation response for human visibility.

Every handoff ID is single-use. Codex must never process the same `CODEX_HANDOFF_READY` ID twice.

### Codex recurring wake-up behavior

Codex should use a recurring Thread Automation / Scheduled Task, when available, to wake periodically and inspect the active PR rather than keeping one task alive in a browser-polling loop.

Recommended default cadence during active implementation is approximately 5 minutes unless the operator chooses another cadence.

On each automation wake-up:

1. inspect the active Sushi81 POS implementation PR comments;
2. find the newest `CODEX_HANDOFF_READY: <id>` that has not already been completed;
3. if none exists, make no repository changes and end that automation run normally;
4. if a new handoff exists, execute only the associated authorized task;
5. never infer a new milestone or continue work merely because the automation woke up.

The recurring automation itself may continue to exist after an individual run ends. A no-work run should end quickly rather than sleeping/polling inside the same run.

### Codex → ChatGPT completion

After completing an authorized handoff, Codex must:

1. push the implementation/evidence to the authorized branch/PR;
2. leave a top-level comment on the same PR beginning with:

   `CODEX_DONE: <same-id>`

3. include at least the implementation/pushed SHA, tests/build/CI status, gate status, and any blocker or unresolved finding;
4. stop implementation and wait for the next distinct `CODEX_HANDOFF_READY` ID unless the current task contract explicitly authorizes another step.

Codex may additionally send the same `CODEX_DONE` message through the ChatGPT browser as a convenience notification, but the PR comment is the durable completion record.

### User-decision stop state

When ChatGPT requires a business/product/architecture decision or other explicit user intervention, ChatGPT will begin its user-facing response with:

`🔔 USER_ACTION_REQUIRED: <short description>`

During that stop state:

- ChatGPT must not issue a new `CODEX_HANDOFF_READY` marker;
- Codex automation wake-ups must make no changes and end normally if there is no new handoff marker;
- Codex must not guess the user's decision, start another milestone, or continue implementation speculatively;
- after the user decides, ChatGPT updates authoritative documentation if needed and only then issues a new unique handoff marker when Codex work is appropriate.

### Long human delays

For known multi-hour/manual waits (for example real-device testing at another location), there is no requirement to keep a Codex browser session or long-running task alive. The recurring automation may be paused by the operator or left at a low-cost cadence. The durable PR mailbox preserves the relay state.

### Safety and governance

- A relay marker authorizes only the task attached to that marker; it never authorizes PR merge by itself.
- Existing milestone merge gates and explicit user-approval requirements remain unchanged.
- Codex must not merge an implementation PR unless the user has explicitly approved the merge under the project process.
- Codex must not start the next milestone merely because the current implementation completed.
- If a PR comment conflicts with approved GitHub specification, approved specification wins and the conflict must be surfaced.
