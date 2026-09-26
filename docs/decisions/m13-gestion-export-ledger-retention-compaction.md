# M13 Gestion export ledger retention and compaction

**Status:** Approved — V1 specification amendment  
**Decision date:** 2026-09-23  
**Owner evidence:** PR #25 comment `5792796519`  
**Milestone:** M13 — Installer, localization completion and final V1 acceptance

## Context

M11 intentionally persists immutable successful Gestion export batches and per-order successful emission state so duplicate CREATE protection and later UPDATE/CANCEL behavior remain correct, and so a successful batch can be regenerated exactly if its generated workbook is lost.

M12 can later remove completed historical orders from `live.db` after a validated annual archive has been durably completed. Without a retention rule, obsolete M11 payload/history could remain in `live.db` indefinitely after the related orders have left the live order set.

## Approved product/data-retention rules

1. **Unresolved export work is never pruned.**
   - Every `PREPARED`, pending or otherwise unemitted CREATE / UPDATE / CANCEL remains durable until successfully resolved.
   - Cleanup must never make a valid pending export action disappear.

2. **Live-order correction state remains durable.**
   - While an order remains in the live order set, retain whatever last-successful export state is required for duplicate CREATE protection and later UPDATE/CANCEL semantics.
   - M11 frozen correction semantics are unchanged.

3. **Archived-order export state may become obsolete.**
   An order's long-lived Gestion export state is eligible for cleanup only when:
   - the order has left the live order set through completed M12 annual archival;
   - no unresolved `PREPARED` / pending export action remains for that order; and
   - cleanup cannot affect any still-live order's CREATE / UPDATE / CANCEL decision.

4. **Successful-batch exact-regeneration retention is 30 days.**
   - Keep the complete immutable successful-batch payload/history for at least 30 days after successful completion.
   - Exact regeneration remains available throughout that retention window.
   - After the 30-day window, an otherwise unneeded successful batch may be pruned once all referential and business dependencies are safely satisfied.

5. **History UI reflects retained/rebuildable history only.**
   - Successful-batch history shows only batches whose complete retained payload remains available for exact regeneration.
   - A legitimately pruned batch is no longer presented as regenerable.
   - V1 does not require a manual destructive cleanup button.

6. **Cleanup is a safety-critical database operation.**
   - It must be transactional, idempotent and failure-safe.
   - Foreign-key integrity must be preserved.
   - Authority, recovery and target-directed handoff guarantees must not be weakened.
   - A versioned migration may be used if the current physical schema cannot implement the approved behavior safely.

## Conservative V1 implementation allowance

The implementation may retain data longer than the minimum 30-day window when a live-order, unresolved-work, foreign-key or other proven business dependency still exists. It must never prune earlier than permitted merely to reduce database size.

V1 does not require the most aggressive possible compaction algorithm. A simpler all-or-nothing successful-batch cleanup rule is acceptable if it demonstrably preserves all dependencies and prevents obsolete payload/history from growing indefinitely after annual archival.

## Non-goals

This amendment does not change:

- the Gestion workbook schema or action meanings;
- M11 CREATE / UPDATE / CANCEL selection semantics;
- M12 archive-year or archive safety behavior;
- authority acquisition, recovery or handoff semantics;
- the existing user-selected export destination behavior.

This decision is controlling for M13 together with the corresponding acceptance amendment.
