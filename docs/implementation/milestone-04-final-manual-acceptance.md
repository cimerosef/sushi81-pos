# M04 final Windows/WPF manual acceptance

**Status:** Passed  
**Milestone:** M04 — Order-entry vertical slice  
**PR:** #6 — `M04: first complete order-entry vertical slice`  
**Accepted branch:** `codex/m04-order-entry`  
**Operator acceptance date:** 2026-09-02  
**Project owner:** accepted manually  

## 1. Final operator result

The project owner reran the Windows/WPF operator acceptance after the M04 remediation sequence and confirmed that the implemented changes behave as expected in the local published application.

The accepted operator flow includes the previously blocked/remediated areas:

- category-first Caisse navigation;
- concise Category short-code navigation after codes are assigned;
- direct add/double-click for simple Products and option-dialog routing for option-enabled Products;
- stable cart action alignment;
- practical address/order-entry layout;
- mandatory structured planned time using the approved hours and five-minute slots;
- prevention of past planned dates for new orders;
- 24-hour planned-time display, including evening values such as `18:10`/`18:25` without 12-hour ambiguity;
- immediate committed-order visibility;
- persisted read-only order browsing by `planned_fulfilment_date`, including multiple orders for one date and retrieval after restart;
- exact committed snapshot viewing/reload;
- FR/zh-CN presentation behavior covered by the preceding manual/automated remediation checks.

The project owner explicitly reported that the manual acceptance passed and that all implemented M04 modifications matched expectations.

## 2. Acceptance meaning

This manual acceptance closes the outstanding M04 Windows/WPF operator gate for the accepted implementation behavior. It does not itself authorize merging PR #6; merge still requires the project owner's explicit merge approval under the execution contract.

M05 remains unauthorized until M04 closure/merge and a separate M05 authorization/handoff are prepared.

## 3. Non-blocking M05 carry-over from final operator review

The following items were raised after M04 acceptance and are intentionally **not M04 acceptance blockers**. They must be carried into M05 design/implementation planning.

### 3.1 Human-friendly operator order number

The current stable internal order ID is a GUID and is too long/machine-oriented for ordinary operator use.

M05 must introduce a short, stable, human-friendly operator-facing order reference while preserving the existing opaque GUID as the technical/internal identity and relational key. The operator-facing reference should be suitable for reading aloud, visual scanning and practical search. A date-plus-daily-sequence form such as `YYYYMMDD-NNN` is a preferred design candidate, but the exact format is to be finalized in the M05 contract rather than silently changing the M04 internal ID semantics.

Existing internal GUID identity, historical snapshot integrity and foreign-key/technical relationships must not be replaced merely for display convenience.

### 3.2 More readable order-detail presentation

The final order-detail presentation should provide stronger visual grouping. At minimum, the information hierarchy should visually separate:

- header / fulfilment information;
- order lines;
- authoritative Total TTC / discount information;
- VAT information;
- later M05 payment/lifecycle information.

The operator specifically requested more visual separation around the current `Heure prévue` → `Lignes`, end-of-lines → `Total TTC`, and pre-`TVA enregistrée` boundaries. Because M05 will introduce the dedicated existing-order workspace described below, this should preferably be implemented as structured layout/sections rather than merely adding blank lines to the temporary monospaced M04 snapshot text.

### 3.3 Dedicated existing-order page/workspace

In the final application, viewing and operating on existing orders should live on a dedicated page/workspace rather than being embedded as the lower portion of the new-order Caisse composition screen.

M05 should use this dedicated existing-order workspace for the lifecycle/search functionality already owned by M05, while keeping new-order composition focused on fast order entry. The workspace should build on the persisted date browser and exact snapshot identity delivered by M04 rather than duplicating or weakening those persistence guarantees.

This carry-over does not authorize M05 implementation from PR #6 and does not change the existing M04 scope retroactively.
