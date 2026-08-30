# Post-task host power policy

**Status:** Approved  
**Approved by:** project owner  
**Effective:** 2026-08-30  
**Scope:** all Codex implementation/review handoffs and recurring mailbox runs for Sushi81 POS.

This policy controls only host-computer power actions after a Codex task. It does not change product behavior, business rules, architecture, milestone scope, acceptance criteria, or GitHub governance.

## 1. Core rule — power actions are explicit, one-shot, and default to NONE

A request to sleep, hibernate, shut down, restart, log off, or otherwise change the host computer power/session state is **never persistent project intent**.

The default for every Codex run and every handoff is:

`POST_TASK_POWER_ACTION: NONE`

Codex must not infer a power action from:

- a prior handoff;
- an earlier message in the Codex conversation;
- a previous operator request;
- an old PR comment;
- an old `CODEX_DONE` record;
- previous success performing sleep/shutdown;
- project memory, habit, or convenience.

A previous request to sleep after one task expires when that task ends.

## 2. Per-handoff directive

Every new ChatGPT → Codex implementation handoff should include exactly one explicit directive near the top:

- `POST_TASK_POWER_ACTION: NONE` — normal/default; do not change host power state.
- `POST_TASK_POWER_ACTION: SLEEP` — one-time sleep after this handoff only.
- `POST_TASK_POWER_ACTION: HIBERNATE` — one-time hibernate after this handoff only, when supported.
- `POST_TASK_POWER_ACTION: SHUTDOWN` — one-time shutdown after this handoff only.

Absence of the directive means `NONE`.

A non-NONE directive applies only to the handoff whose `CODEX_HANDOFF_READY: <id>` record contains it. It must never be carried forward to a later handoff.

## 3. Direct operator request while a task is already running

If the operator explicitly tells the currently running Codex session, in clear words, to sleep/hibernate/shut down **after the current task**, that is a one-time override for the current run only.

Codex must interpret phrases such as “这次做完后休眠”, “for this task only, sleep when finished”, or equivalent explicit current-run wording as ephemeral instructions.

After the requested power action is performed or the run ends, the effective value automatically returns to `NONE` for all later runs.

An old direct instruction must not be treated as a standing preference.

## 4. Required completion order

When a non-NONE power action is explicitly authorized for the current handoff, perform it only after all normal completion duties that can reasonably finish first:

1. finish the authorized implementation/review work;
2. run required verification;
3. push required commits/evidence;
4. publish the matching durable `CODEX_DONE: <id>` record;
5. attempt the required ChatGPT browser notification;
6. perform the explicitly authorized one-time host power action.

Do not power down early in a way that can lose unpushed work, omit the durable completion record, or skip required verification.

## 5. Safety and ambiguity

If the requested power action is ambiguous, unsupported, unsafe at that point, or cannot be confirmed as applying to the current handoff, do not guess. Use `NONE` and report the ambiguity when appropriate.

A power-action request does not authorize any project change, merge, milestone transition, or destructive data operation.

## 6. Completion evidence

When the current handoff explicitly uses a non-NONE power action, `CODEX_DONE` should record the intended one-time action before it is executed, for example:

`postTaskPowerAction: SLEEP (one-shot; not persistent)`

For the normal default, no extra completion field is required unless the handoff specifically asks for it.

## 7. Recurring automation

Recurring mailbox automation must treat host power action as handoff-local data. On every wake-up, compute it only from the current unprocessed handoff plus an explicit current-run operator override, if any.

Never cache a previous non-NONE power action as automation state or a user preference.
