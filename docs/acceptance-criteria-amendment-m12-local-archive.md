# Acceptance criteria amendment — M12 local annual archive and user-selected export

**Status:** Approved — V1 specification amendment
**Decision date:** 2026-09-21
**Controls:** `docs/decisions/m12-local-archive-and-user-selected-export.md`

This amendment supersedes only the OneDrive-specific annual-archive location/publication portions of the baseline acceptance criteria. Other M12 acceptance semantics remain unchanged.

## AC-STO-013 — Local archive publication safety

Eligible records are removed from `live.db` only after a complete target-year archive database is staged locally, validated, durably promoted to the application-managed local annual-archive area, reopened, and validated there as the completed canonical archive.

Any staging, validation, promotion or completed-file validation failure leaves eligible records in `live.db` and the archive operation retryable.

No OneDrive publication/synchronization acknowledgement is part of annual archive completion after this amendment.

**Evidence:** real-SQLite failure-injection tests across staging, promotion, reopen/validation and live-removal boundaries.

## AC-STO-014 — Permanent local read-only annual archives and explicit export

Annual archives remain independent read-only SQLite historical databases in application-managed local business-data storage.

They:

- survive ordinary application update/reinstall;
- are retained permanently by normal POS workflow;
- remain queryable/reprintable through explicit archive-year selection;
- are never part of normal handoff lineage;
- are not automatically synchronized/shared through OneDrive.

The operator can explicitly export/copy a completed validated archive to a destination they choose. Export never moves/deletes the canonical local archive and export failure does not mutate the canonical archive or `live.db`.

**Evidence:** reinstall/local-archive access test plus user-selected export/failure test.

## AC-LIFE-015 archive-access clarification

The operator explicitly selects locally available annual archive years for historical search/inspection. Normal live lookup still does not automatically open archives.

## AC-PRINT-009

Unchanged. Archived reprint uses historical snapshots without current Catalogue/current VAT dependency and without writing the archive database.
