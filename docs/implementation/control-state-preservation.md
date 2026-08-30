# Interactive control-state preservation rule

**Status:** Approved implementation-quality governance  
**Effective:** 2026-08-30  
**Scope:** Sushi81 POS WPF/user-visible implementation from M03 remediation onward.

This rule supplements `interactive-quality-gate.md`. It does not change frozen business behavior or data semantics.

## Core invariant

A user action must mutate only the state that action is intended to mutate. Unrelated toggles, selections, text values, filters, activation flags, language state and unsaved editor values must remain unchanged unless the approved workflow explicitly couples them.

For every changed interactive journey, identify the important pre-action state and assert the relevant post-action state, including both the field being changed and nearby fields that must remain stable.

## Boolean/toggle controls

For CheckBox/ToggleButton-like controls:

- do not infer coupling merely because two controls/configurations are related;
- do not silently force a toggle on/off from another action unless the specification explicitly requires it;
- when the specification allows dormant/stored configuration behind a disabled feature flag, preserve that independence;
- test both directions where practical: `false -> unrelated action -> false` and `true -> unrelated action -> true`;
- verify persistence separately from transient editor state when Save is involved.

## Dynamic editors

For dynamic add/remove/reorder operations, regression coverage should capture and re-check nearby parent-editor state before and after the routed action. This includes feature-enable flags, selected category/status, active state, discount eligibility and other independent fields.

An operator-found unexpected state change must be treated as a lifecycle/state-management defect even if the changed value is still technically valid according to the data model.

## Completion evidence

For a remediation caused by unintended state mutation, `CODEX_DONE` should state:

- the exact before/action/after invariant;
- why previous tests missed it;
- which adjacent controls/actions were audited;
- the runtime-level regression proving unrelated state preservation.
