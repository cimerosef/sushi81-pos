# M06 final Windows/WPF manual acceptance — local recovery and read-only enforcement

**Status:** Passed — project-owner Windows/WPF acceptance completed  
**Milestone:** M06 — Local recovery and authoritative/read-only enforcement  
**Implementation branch:** `codex/m06-local-recovery-read-only`  
**Contract:** `milestone-06-local-recovery-read-only-enforcement.md`  
**Project-owner gate:** Passed on 2026-09-07  
**POST_TASK_POWER_ACTION:** `NONE`

## Evidence rule

This document is the durable project-owner manual acceptance record. Codex prepared the reviewed self-contained `win-x64` build and automated evidence; the project owner performed the Windows/WPF checks recorded below using synthetic test records and the deliberately protected normal local installation.

No real customer/order/payment data is reproduced in this evidence.

## Acceptance build / environment

- **Reviewed implementation SHA:** `4a0c1ca9e44a6c48899e6ef8dc211172371e4d20`
- **Reviewed fix:** `M06-MANUAL-ACCEPTANCE-STARTUP-FIX-06`
- **Release tests:** 364/364 Passed, 0 failed, 0 skipped
- **Release build:** Passed, 0 warnings / 0 errors
- **Exact-head CI:** Continuous integration #517, run `34091370109`, Passed for source head `4a0c1ca9e44a6c48899e6ef8dc211172371e4d20`
- **Acceptance artifact:** `C:\Users\zshu\Documents\Projects\Sushi81POS\artifacts\m06-startup-fix-06-owner-acceptance\Sushi81.Pos.Desktop.exe`
- **EXE SHA-256:** `314B3E5C000BFDEA0C264835F6C0FFA5703A61EA07B829B6B587E5143CF4861B`
- **Environment:** normal Windows desktop installation using the application's ordinary `%LOCALAPPDATA%\Sushi81 POS` local-data root; the root was protected by an owner-created backup before acceptance.
- **Data discipline:** synthetic test records only; no destructive `live.db` deletion, no authority-marker/anchor deletion, and no manual Recovery failure injection.

### Startup-remediation note

The first owner launch of the earlier reviewed artifact began from the supported pre-M06 local installation and created the M06 bootstrap authority artifacts, but remained windowless because startup deadlocked while synchronously waiting for asynchronous validated-Recovery discovery. The owner stopped testing, the defect was remediated, and the repaired artifact above was reviewed before testing resumed. The bootstrap artifacts were deliberately preserved rather than deleted to manufacture a second first-run condition. On the repaired build the same protected installation opened normally, writable, with existing data intact; restart persistence then passed. The production-composition WPF regression with existing validated Recovery also passed on the reviewed head.

## A. Authoritative startup and ordinary operation

- [x] A1 — Supported legacy bootstrap / repaired startup path completed safely and the application opened authoritative/writable with existing data intact. See startup-remediation note above.
- [x] A2 — Close and relaunch restored the durable authoritative state; startup did not fall back through a generic “missing state = writable” path.
- [x] A3 — Main Caisse/order-entry operation remained usable and responsive.
- [x] A4 — Created and confirmed a synthetic new order successfully.
- [x] A5 — Opened and saved a same-ID modification successfully.
- [x] A6 — Changed cumulative CB/Espèce values successfully and verified persisted values after reopening.
- [x] A7 — Exercised Close on an exactly settled synthetic order successfully.
- [x] A8 — Exercised Cancel on a synthetic order successfully.
- [x] A9 — Performed and verified a representative reversible catalogue mutation successfully.
- [x] A10 — Saved and verified a representative BusinessSettings change and restored the original value.

## B. Local recovery generation and retention

- [x] B1 — UI remained responsive during ordinary durable saves and Recovery debounce/snapshot work; no visible multi-second freeze was observed.
- [x] B2 — After the debounce window, a new complete Recovery unit appeared under the application-managed local Recovery area.
- [x] B3 — The newest Recovery unit contained `snapshot.db` and `metadata.json` as expected.
- [x] B4 — Nearby durable saves/coalescing behavior was acceptable and a final explicit save was confirmed protected by a subsequent Recovery unit. A Recovery generated during the normal duration of one order modification was accepted as valid debounce timing rather than a defect.
- [x] B5 — After generating more than five successful recovery points, exactly the latest five valid Recovery units remained.
- [x] B6 — Accepted from the reviewed automated failure-injection evidence: older valid Recovery cleanup occurs only after a newer valid replacement exists. The owner did not inject a destructive local snapshot failure into the protected installation.
- [x] B7 — Closing immediately after a durable save completed normally and generated a newer Recovery unit through orderly shutdown flush.

## C. Ordinary non-authoritative/read-only mode

A persistent synthetic/local authority state was used; no real M07 handoff was performed.

- [x] C1 — Relaunched in ordinary non-authoritative/read-only state.
- [x] C2 — Persistent, clearly visible read-only/non-authoritative indication was shown on the main shell.
- [x] C3 — The presentation made clear that local live data may be stale where applicable.
- [x] C4 — Live order search remained usable.
- [x] C5 — Date browsing/order-detail viewing remained usable.
- [x] C6 — Dashboard/operational read-only views remained usable from the local copy.
- [x] C7 — New-order authoritative confirmation was disabled/blocked.
- [x] C8 — Existing-order modification save was disabled/blocked.
- [x] C9 — CB/Espèce payment changes were disabled/blocked.
- [x] C10 — Close and Cancel were disabled/blocked.
- [x] C11 — Catalogue/category/product/option mutations were disabled/blocked.
- [x] C12 — Filtered bulk activation/deactivation was disabled/blocked.
- [x] C13 — BusinessSettings save was disabled/blocked.
- [x] C14 — Reachable mutation paths remained safely blocked with no business change; no bypass was observed.
- [x] C15 — Close and relaunch preserved read-only state; the device did not silently become writable.
- [x] C16 — No force-takeover, “continue anyway”, target substitution or other M07 authority-acquisition escape was exposed.

## D. Pending/transitioning read-only mode

A persistent local `Transitioning` authority state was used; no GitHub handoff was published.

- [x] D1 — Established and relaunched a persistent pending/transitioning state.
- [x] D2 — The main shell clearly identified the transitioning condition and did not present ordinary writable operation.
- [x] D3 — All authoritative business writes remained blocked.
- [x] D4 — Read-only consultation remained available where safe.
- [x] D5 — Close and relaunch preserved transitioning state and read-only enforcement.
- [x] D6 — No retarget, target-selection, acquisition or Disaster Recovery workflow was exposed by M06.

## E. Recovery-required/blocked mode

- [x] E1 — Established and relaunched the supported local recovery-required/blocked state.
- [x] E2 — The main shell showed a clear localized blocked/recovery-required state.
- [x] E3 — Authoritative business writes were blocked.
- [x] E4 — Restart preserved the blocked state; it did not revert silently to authoritative.
- [x] E5 — Existing orders/catalogue/settings remained present and readable; no automatic destructive restore/reset of `live.db` occurred merely because the state existed. The owner did not delete `live.db`; the missing-database fail-closed path remains covered by deterministic automation.

## F. Localization and control-state preservation

- [x] F1 — In read-only state, switching `fr-FR` → `zh-CN` rerendered the safety indicator immediately with correct semantics.
- [x] F2 — Switching `zh-CN` → `fr-FR` preserved authority state.
- [x] F3 — User-entered catalogue/order/contact/comment data was not translated or modified by the language switch.
- [x] F4 — Search/date/filter/selected read-only detail state remained acceptably preserved through language switching.
- [x] F5 — Safety warnings remained readable without material clipping at normal, reduced and enlarged window sizes exercised by the owner.

## G. Offline/local-first check

- [x] G1 — With network access disabled, an authoritative M06 device could still create/edit/pay/Close/Cancel synthetic local orders and perform local catalogue/settings work.
- [x] G2 — Local Recovery generation continued while offline and retained the expected latest-five set.
- [x] G3 — Read-only enforcement and restart reconstruction also worked while offline and remained independent of GitHub/OneDrive/Internet availability.
- [x] G4 — No M07 handoff capability was expected or exposed. Network access was restored after the check and the owner restored `Authoritative` state successfully.

## H. Scope regression

- [x] H1 — No M07 pairing/device-management UI appeared.
- [x] H2 — No real Transfer authority / target selection / GitHub grant-acquisition workflow appeared.
- [x] H3 — No Disaster Recovery/generation workflow appeared.
- [x] H4 — No M08 final printer/reprint implementation was introduced solely for M06.
- [x] H5 — No M10 catalogue workbook import, M11 export or M12 annual archive workflow was implemented early.
- [x] H6 — Existing M03–M05 accepted operator workflows remained normal in authoritative mode, including Caisse, Commandes, Catalogue, Settings, payments, Close and Cancel.

## Final project-owner result

**Reviewed implementation SHA:** `4a0c1ca9e44a6c48899e6ef8dc211172371e4d20`  
**Acceptance environment:** Windows desktop, self-contained `win-x64` reviewed artifact, protected normal `%LOCALAPPDATA%\Sushi81 POS` installation, synthetic test operations  
**Date executed:** 2026-09-07  
**Result:** `PASSED`

### Findings / remediation required

No remaining blocking manual-acceptance finding.

The initial owner launch found the existing-Recovery WPF dispatcher deadlock. That defect was remediated by `M06-MANUAL-ACCEPTANCE-STARTUP-FIX-06`, reviewed at `4a0c1ca9e44a6c48899e6ef8dc211172371e4d20`, and the repaired artifact then passed the complete owner checklist above.

### Acceptance closure

M06 project-owner Windows/WPF manual acceptance is **Passed** on the reviewed implementation SHA and artifact recorded above. Required automated/build/publish/CI evidence also passes. This acceptance does **not** authorize merging PR #11; merge remains a separate explicit project-owner decision. M07 remains unauthorized/not started until that separate transition is approved.
