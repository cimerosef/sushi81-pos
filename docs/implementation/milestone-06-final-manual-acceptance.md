# M06 final Windows/WPF manual acceptance — local recovery and read-only enforcement

**Status:** Prepared checklist — not yet executed  
**Milestone:** M06 — Local recovery and authoritative/read-only enforcement  
**Implementation branch:** `codex/m06-local-recovery-read-only`  
**Contract:** `milestone-06-local-recovery-read-only-enforcement.md`  
**Project-owner gate:** Required before M06 may be accepted as fully Passed  
**POST_TASK_POWER_ACTION:** `NONE`

## Evidence rule

This document is a durable project-owner manual acceptance record. Codex may prepare the exact reviewed self-contained `win-x64` build and may add factual implementation/automation evidence, but must not mark the manual checks Passed unless the project owner actually performs them.

Use synthetic test records only. Do not expose or copy real customer/order/payment data into GitHub evidence.

Before the manual run, record:

- reviewed implementation SHA;
- exact publish/build SHA;
- CI run/check result for that SHA;
- Release test count/result;
- Windows version and .NET self-contained artifact path used for acceptance;
- whether the test uses an isolated synthetic local-data root or a deliberately protected normal test installation. Do not invent profile/`LOCALAPPDATA` redirection unless it is genuinely needed and already proven safe.

## A. Authoritative startup and ordinary operation

- [ ] A1 — Start the M06 build from a supported existing M05-format local installation with no prior M06 authority-state file. The one-time legacy single-device bootstrap completes safely and the application opens authoritative/writable.
- [ ] A2 — Close and relaunch. The durable authoritative state is restored; startup does not rerun a generic “missing state = writable” fallback.
- [ ] A3 — Main Caisse/order-entry operation remains usable and responsive.
- [ ] A4 — Create and confirm a synthetic new order successfully.
- [ ] A5 — Open and save a same-ID modification successfully.
- [ ] A6 — Change cumulative CB/Espèce values successfully.
- [ ] A7 — Exercise Close on an exactly settled synthetic order successfully.
- [ ] A8 — Exercise Cancel on a synthetic order successfully.
- [ ] A9 — Perform a representative catalogue mutation successfully.
- [ ] A10 — Save a representative BusinessSettings change successfully, then restore the original synthetic setting value if appropriate.

## B. Local recovery generation and retention

- [ ] B1 — After a successful durable mutation, the UI remains responsive during the recovery debounce/snapshot work; no visible multi-second freeze is introduced by ordinary recovery generation.
- [ ] B2 — After the debounce window, a new complete validated recovery unit exists under the application-managed local Recovery area.
- [ ] B3 — The recovery unit has both database and metadata components expected by the implementation and is recognized as valid by the application/test evidence.
- [ ] B4 — Perform several nearby durable saves. Coalescing is acceptable; the resulting recovery coverage reflects the latest committed state rather than losing the last change.
- [ ] B5 — Generate enough successful recovery points to exceed retention. The application retains exactly the latest five valid recovery units after cleanup.
- [ ] B6 — Older cleanup happens only after a newer valid replacement exists; no valid set is reduced because a newer attempt failed.
- [ ] B7 — Close the application while a recovery request is still pending. Orderly shutdown flushes/protects the pending latest durable change when safely possible.

## C. Ordinary non-authoritative/read-only mode

Use the implementation's safe test/diagnostic seam to establish a persistent synthetic non-authoritative state. Do not perform a real M07 handoff.

- [ ] C1 — Relaunch in ordinary non-authoritative/read-only state.
- [ ] C2 — A persistent, clearly visible read-only/non-authoritative indicator is shown on the main shell.
- [ ] C3 — The message makes clear that the local live data may be stale where applicable.
- [ ] C4 — Live order search remains usable.
- [ ] C5 — Date browsing/order-detail viewing remains usable.
- [ ] C6 — Dashboard/operational read-only views remain usable from the local copy.
- [ ] C7 — New-order authoritative confirmation is disabled/blocked.
- [ ] C8 — Existing-order modification save is disabled/blocked.
- [ ] C9 — CB/Espèce payment changes are disabled/blocked.
- [ ] C10 — Close and Cancel are disabled/blocked.
- [ ] C11 — Catalogue/category/product/option mutations are disabled/blocked.
- [ ] C12 — Filtered bulk activation/deactivation is disabled/blocked.
- [ ] C13 — BusinessSettings save is disabled/blocked.
- [ ] C14 — Attempting a blocked operation through any still-reachable command path produces a safe authority error and no business change.
- [ ] C15 — Close and relaunch. The device remains read-only; it does not silently become writable.
- [ ] C16 — No force-takeover, “continue anyway”, target substitution or other M07 authority-acquisition escape is exposed.

## D. Pending/transitioning read-only mode

Use only a safe synthetic/local-state test seam; do not publish a GitHub handoff.

- [ ] D1 — Establish and relaunch a persistent pending/transitioning state.
- [ ] D2 — The main shell clearly identifies the pending/transitioning condition and does not present it as ordinary writable operation.
- [ ] D3 — All authoritative business writes remain blocked.
- [ ] D4 — Read-only consultation remains available where safe.
- [ ] D5 — Close and relaunch. Pending/transitioning state persists and remains read-only.
- [ ] D6 — No retarget, target-selection, acquisition or Disaster Recovery workflow is exposed by M06.

## E. Recovery-required/blocked mode

- [ ] E1 — Establish the supported synthetic recovery-required/blocked state.
- [ ] E2 — The main shell shows an actionable localized blocked/recovery-required state.
- [ ] E3 — Authoritative business writes are blocked.
- [ ] E4 — Restart preserves the blocked state; it does not revert silently to authoritative.
- [ ] E5 — No automatic destructive restore/reset of `live.db` occurs merely because this state exists.

## F. Localization and control-state preservation

Repeat the safety-state presentation in French and Simplified Chinese.

- [ ] F1 — In read-only state, switch `fr-FR` → `zh-CN`; the safety indicator rerenders immediately and remains semantically correct.
- [ ] F2 — Switch `zh-CN` → `fr-FR`; authority state is unchanged.
- [ ] F3 — User-entered catalogue/order/contact/comment data is not translated or modified by the language switch.
- [ ] F4 — Search text, selected date/filter and selected read-only order/detail remain preserved where the current frozen workflow does not require reset.
- [ ] F5 — Safety warnings are readable and not clipped at ordinary minimum/normal/larger window sizes used by the current application.

## G. Offline/local-first check

- [ ] G1 — With Internet/GitHub/OneDrive unavailable, an already-authoritative M06 device can still create/edit/pay/Close/Cancel synthetic local orders and perform local catalogue/settings work.
- [ ] G2 — Local recovery generation continues without Internet/GitHub/OneDrive.
- [ ] G3 — Read-only enforcement/restart reconstruction also does not depend on Internet/GitHub/OneDrive.
- [ ] G4 — No M07 handoff capability is expected or claimed by this check.

## H. Scope regression

- [ ] H1 — No M07 pairing/device-management UI has appeared.
- [ ] H2 — No real Transfer authority / target selection / GitHub grant-acquisition workflow has appeared.
- [ ] H3 — No Disaster Recovery/generation workflow has appeared.
- [ ] H4 — No M08 final printer/reprint implementation has been introduced solely for M06.
- [ ] H5 — No M10 catalogue workbook import, M11 export or M12 annual archive workflow has been implemented early.
- [ ] H6 — Existing M03–M05 accepted operator workflows not related to authority state still behave normally in authoritative mode.

## Final project-owner result

**Reviewed implementation SHA:** Pending  
**Acceptance environment:** Pending  
**Date executed:** Pending  
**Result:** `PENDING`

### Findings / remediation required

Pending.

### Acceptance closure

Do not populate this section as Passed until all blocking findings are resolved and the project owner explicitly accepts the M06 Windows/WPF behavior.

A Passed manual checklist plus required automated/build/publish/CI evidence may support marking M06/owned criteria Passed. It does not authorize merging the M06 PR; merge still requires a separate explicit project-owner approval.
