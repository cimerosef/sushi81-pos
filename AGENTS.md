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

## ChatGPT ↔ Codex collaboration handoff protocol v2.1

This section is the durable collaboration protocol for implementation/review relay between the ChatGPT project lead and Codex. It **supersedes the earlier browser-polling rule** in which one long-running Codex task repeatedly opened the ChatGPT conversation and waited for a new marker.

### Durable mailbox

The active GitHub implementation pull request is the durable inter-agent mailbox.

- GitHub PR comments are the authoritative relay channel between ChatGPT and Codex.
- ChatGPT browser access may be used as a convenience/fast notification path, but it is not the durable source of relay state.
- A Codex run must not remain alive merely to poll a ChatGPT browser tab.
- Ending one Codex run must not be interpreted as cancelling the recurring relay automation.

### Codex Execution Gate — daily master switch

GitHub issue **#4 — `Codex execution gate — Sushi81 POS`** is the single daily execution switch for the recurring Codex mailbox automation.

Its issue state has exactly this meaning:

- **OPEN = Codex execution ACTIVE.**
- **CLOSED = Codex execution PAUSED.**

This gate controls **Codex execution only**. It does not restrict ChatGPT discussion, specification work, code review, GitHub documentation updates, or publication of future handoff tasks.

Therefore ChatGPT may continue to publish new `CODEX_HANDOFF_READY: <id>` tasks while issue #4 is closed. Those tasks remain queued in GitHub and must not be executed until the gate is reopened.

Closing issue #4 is not an interrupt mechanism. If Codex is already executing an authorized handoff when the gate is closed, that already-running task should finish safely, push its work and publish its matching `CODEX_DONE`; no additional queued handoff may start afterward. Emergency cancellation of an already-running task is a separate explicit manual action.

Reopening issue #4 resumes queued work. Queued handoffs must be processed **serially in publication order, oldest unprocessed first**, never concurrently and never newest-first.

### ChatGPT → Codex handoff

When ChatGPT has completed review/design work, no user decision is required, and Codex should execute the next task, ChatGPT must:

1. leave a top-level comment on the active implementation PR containing a unique marker:

   `CODEX_HANDOFF_READY: <unique-id>`

2. include the complete executable Codex instruction in that same PR comment, or an unambiguous pointer to an authoritative committed implementation contract;
3. optionally repeat the same marker at the end of the ChatGPT conversation response for human visibility.

Every handoff ID is single-use. Codex must never process the same `CODEX_HANDOFF_READY` ID twice.

The Execution Gate does not prevent ChatGPT from publishing handoffs. A handoff published while issue #4 is closed is queued, not cancelled.

### Codex recurring wake-up behavior

Codex should use a recurring Thread Automation / Scheduled Task, when available, to wake periodically and inspect GitHub rather than keeping one task alive in a browser-polling loop.

Recommended default cadence is approximately 5 minutes unless the operator chooses another cadence.

On each automation wake-up, Codex must perform these checks in order:

1. read GitHub issue #4;
2. if issue #4 is **CLOSED**, make no repository/project changes and end that automation run immediately;
3. if issue #4 is **OPEN**, inspect the active Sushi81 POS implementation PR comments;
4. find the **oldest** `CODEX_HANDOFF_READY: <id>` that has not already been completed by a matching `CODEX_DONE: <id>`;
5. if none exists, make no repository changes and end that automation run normally;
6. if a handoff exists, execute only that one associated authorized task;
7. never infer a new milestone or continue work merely because the automation woke up.

The recurring automation itself remains enabled after an individual run ends. A no-work or gate-closed run should end quickly rather than sleeping/polling inside the same run.

### Codex → ChatGPT completion

After completing an authorized handoff, Codex must:

1. push the implementation/evidence to the authorized branch/PR;
2. leave a top-level comment on the same PR beginning with:

   `CODEX_DONE: <same-id>`

3. include at least the implementation/pushed SHA, tests/build/CI status, gate status, and any blocker or unresolved finding;
4. stop implementation and wait for the next distinct handoff unless the current task contract explicitly authorizes another step.

After the durable GitHub `CODEX_DONE` comment exists, Codex should make a **best-effort** notification through the already-open Sushi81 POS ChatGPT browser conversation when that browser session is available. The convenience message should contain only the same handoff ID plus a short request to review the corresponding PR/evidence. It must not include secrets, tokens, large logs or code dumps.

Failure of the browser notification does not make the implementation fail and does not authorize re-execution of the handoff. GitHub remains the durable completion record. A later recurring wake-up may retry the convenience notification if the automation can distinguish that the same handoff has already been implemented; it must never implement the handoff twice.

### User-decision stop state

When ChatGPT requires a business/product/architecture decision or other explicit user intervention, ChatGPT will begin its user-facing response with:

`🔔 USER_ACTION_REQUIRED: <short description>`

During that stop state:

- ChatGPT must not issue a new Codex implementation handoff that depends on the unresolved user decision;
- unrelated already-approved handoffs may still exist in the mailbox and are governed by issue #4;
- Codex must not guess the user's decision, start another milestone, or continue implementation speculatively;
- after the user decides, ChatGPT updates authoritative documentation if needed and only then issues a new unique handoff marker when Codex work is appropriate.

### Long human delays

For known multi-hour/manual waits (for example real-device testing at another location), there is no requirement to keep a Codex browser session or long-running task alive.

The operator may simply close issue #4 to pause new Codex execution and reopen it later. ChatGPT may continue discussion and may queue future handoffs while the gate is closed. The durable PR mailbox preserves all relay state.

### Safety and governance

- A relay marker authorizes only the task attached to that marker; it never authorizes PR merge by itself.
- Existing milestone merge gates and explicit user-approval requirements remain unchanged.
- Codex must not merge an implementation PR unless the user has explicitly approved the merge under the project process.
- Codex must not start the next milestone merely because the current implementation completed.
- If a PR comment conflicts with approved GitHub specification, approved specification wins and the conflict must be surfaced.
