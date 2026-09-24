# Acceptance criteria amendment — M13 Gestion export retention and compaction

**Status:** Approved — V1 specification amendment  
**Decision source:** `docs/decisions/m13-gestion-export-ledger-retention-compaction.md`  
**Owner evidence:** PR #25 comment `5792796519`

This amendment adds M13 acceptance requirements to the existing M11 Gestion export contract. Existing `AC-EXP-001` through `AC-EXP-011` remain unchanged.

## AC-EXP-012 — Unresolved work is never pruned

Every `PREPARED`, pending or otherwise unemitted CREATE / UPDATE / CANCEL remains durable and retryable until it is successfully resolved. Cleanup cannot remove or make unreachable a valid pending action.

**Evidence:** real-SQLite retention/compaction tests covering aged PREPARED batches, pending CREATE/UPDATE/CANCEL, injected failure and retry.

## AC-EXP-013 — Live-order decision state is retained

While an order remains in the live order set, the last-successful state needed for duplicate CREATE protection and later UPDATE/CANCEL decisions remains available and produces the same M11 selection result before and after any permitted compaction attempt.

**Evidence:** selection-regression tests with live unchanged, modified, reopened/closed and cancelled orders spanning old successful batches.

## AC-EXP-014 — Archived settled state is cleanup-eligible only when safe

Export state for an order that left the live order set through completed M12 annual archival may be removed only when no unresolved PREPARED/pending action exists for that order and cleanup cannot affect any still-live order decision.

Compaction may conservatively retain additional state when dependencies remain.

**Evidence:** M12/M13 integration tests proving archived settled rows can be cleaned while live orders, other-year orders and unresolved work remain correct and untouched.

## AC-EXP-015 — Successful-batch exact regeneration is retained for 30 days

A successfully completed batch remains exactly regenerable from its immutable retained payload for at least 30 days after `completed_at_utc`.

Before the retention deadline, compaction cannot remove the complete payload. After the retention deadline, a successful batch may be removed only when all referential/business dependencies are satisfied. A retained/rebuildable batch appears in successful history; a legitimately pruned batch does not appear and cannot be offered for regeneration.

**Evidence:** deterministic clock-boundary tests around the 30-day deadline, exact-regeneration byte/content equivalence within the window, and Desktop/history tests after legal prune.

## AC-EXP-016 — Compaction is transactional, idempotent and failure-safe

Compaction preserves foreign-key integrity, authority/recovery/handoff invariants and M11/M12 business semantics. Partial deletion is not observable after failure. Repeating compaction yields the same safe result. A non-authoritative device cannot perform the live-database cleanup write.

The implementation must demonstrate that obsolete full successful-batch payload/history does not remain indefinitely solely because its related orders were archived and all dependencies resolved.

**Evidence:** real-SQLite transaction/failure-injection/idempotency/foreign-key tests, authority tests, and bounded-retention integration evidence.

## Acceptance relationship

These criteria are part of the final V1 acceptance baseline and must pass together with `AC-ARCH-007`, `AC-NFR-001` through `AC-NFR-004`, the installer/update/reinstall preservation contract, localization parity and the complete production-target regression.
