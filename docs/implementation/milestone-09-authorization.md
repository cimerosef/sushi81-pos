# M09 implementation authorization — Hiboutik paste-order fallback

**Status:** **AUTHORIZED**  
**Prepared:** 2026-09-14  
**Authorized:** 2026-09-14 by explicit project-owner approval  
**Milestone:** M09 — Hiboutik paste-order fallback  
**Exact authorized preparation head:** `aa1c5a58025f96fef3a4505721186eb1ad05552b`  
**Execution gate at authorization record time:** CLOSED — must remain closed until branch/PR/mailbox/handoff pointers are cross-verified  
**Active M09 implementation PR at authorization record time:** none  
**Active handoff at authorization record time:** none

## Project-owner authorization

On 2026-09-14 the project owner explicitly stated:

> 批准 M09 implementation

This statement authorizes **M09 implementation only** under the controlling scope below. It does not authorize merge, M10 or any later milestone, and it does not permit Codex execution until the normal branch/PR/mailbox/gate prerequisites are completed.

## Authorized controlling package

Implementation is authorized only within the behavior and boundaries frozen in:

- `docs/implementation/milestone-09-hiboutik-paste-fallback.md`;
- `docs/implementation/milestone-09-preparation-readiness.md`;
- `docs/implementation/milestone-09-final-manual-acceptance.md`;
- `docs/decisions/m09-hiboutik-paste-operator-workflow-and-source-reference.md`;
- `docs/acceptance-criteria-amendment-m09-hiboutik-paste-fallback.md`;
- `docs/paste-order-import.md`;
- current Approved V1 baseline/acceptance criteria;
- inherited execution/interactive-quality/control-state/power governance under `docs/implementation/`.

## Authorized scope summary

M09 is authorized to implement:

- deterministic parsing of the Hiboutik product-detail plain-text block;
- tolerated recognition of per-line `Total : ...`, final `TOTAL ...`, and the known `Livraison (0)` source technical line;
- exact active catalogue product-code resolution only;
- fail-safe unresolved-line operator handling with explicit product selection or explicit ignore/not-a-product disposition;
- transient imported-line option-confirmation state reusing the ordinary option workflow;
- ordinary order-entry draft population and ordinary pricing/validation/confirmation;
- durable `source_type = HIBOUTIK_PASTE` provenance through the ordinary order model;
- nullable, read-only, non-authoritative `source_total_ttc` source-reference persistence and passive ordinary list/detail display;
- preservation of ordinary lifecycle, payment, authority/recovery and M08 printing behavior;
- regression evidence for POS-originated reporting exclusions and ordinary operational inclusion;
- French/Simplified Chinese UI/messages required by the M09 contract;
- synthetic/sanitized parser fixtures and the full M09 automated/manual acceptance evidence.

M09 does **not** authorize:

- Gmail/Outlook integration, mailbox monitoring, Hiboutik API or scraping;
- background clipboard monitoring;
- fuzzy automatic product-name matching;
- raw pasted production-text retention;
- dedicated Hiboutik reference field;
- emergency-order subtype/screen/status/counter;
- discrepancy/reconciliation or duplicate-management subsystem;
- Hiboutik-specific payment workflow;
- M10 catalogue `.xlsx`;
- M11 export implementation beyond the M09 regression boundary already specified;
- M12 archive work;
- M13 installer/final-acceptance work.

## Execution setup required after this authorization record

Before Issue #4 may be opened, the governance controller must complete and cross-check all of the following:

1. create dedicated branch `codex/m09-hiboutik-paste-fallback` from the latest governance-only `main` baseline descending from the exact authorized preparation head;
2. create dedicated PR `M09: Hiboutik paste-order fallback` targeting `main`;
3. update Issue #4 to name exactly that branch/PR/milestone while keeping the issue CLOSED;
4. publish exactly one valid unprocessed top-level `CODEX_HANDOFF_READY: <id>` on that PR;
5. include `POST_TASK_POWER_ACTION: NONE` unless the owner explicitly requests another one-shot action;
6. make the first handoff execute only WP1 from `milestone-09-hiboutik-paste-fallback.md` unless the handoff explicitly states another prepared work package;
7. forbid M10+ and any business/spec invention;
8. verify no conflicting active implementation PR/handoff exists;
9. only then ask the owner to reopen Issue #4.

Once Issue #4 is OPEN with all pointers consistent, Codex may execute the single active M09 handoff. A `CODEX_DONE` never authorizes merge or the next work package automatically.