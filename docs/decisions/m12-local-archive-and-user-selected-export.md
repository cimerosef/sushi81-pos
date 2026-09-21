# M12 local annual archive and user-selected export

**Status:** Approved — V1 specification amendment  
**Decision date:** 2026-09-21  
**Owner evidence:** PR #25 comment `5765067664`

## Context

The frozen M12 baseline stored permanent annual archives under OneDrive `Archive` and required confirmed OneDrive publication/synchronization before eligible rows could leave `live.db`.

During M12 readiness review, the project re-encountered the M02-proven limitation that the OneDrive desktop/local-filesystem boundary does not expose a documented per-artifact remote-upload acknowledgement for a newly-created regular file. The project owner then chose a simpler product model: annual archive databases should remain local, and any archive export should let the operator choose the destination.

## Decision

### 1. Canonical annual archive is local

The authoritative completed annual archive database is stored in application-managed local data, not in OneDrive.

The production logical location is:

```text
%LOCALAPPDATA%\Sushi81 POS\Archive\
    sushi81-archive-YYYY.db
```

Exact filename formatting may be refined technically, but the archive year must remain unambiguous.

The canonical local archive is application business data and must survive ordinary application update/reinstall. It is not disposable cache content.

### 2. OneDrive is removed from the annual-archive boundary

After this amendment, OneDrive is not used for:

- annual archive publication;
- annual archive retention;
- annual archive completion acknowledgement;
- annual archive discovery;
- ordinary archive historical access.

Existing OneDrive Disaster Recovery behavior is unchanged. GitHub normal handoff behavior is unchanged.

### 3. Failure-safe local publication

Annual archive execution remains authoritative-device-only and keeps the same staged safety model:

1. identify the target-year eligible rows;
2. build a complete archive in local safe staging;
3. validate archive schema/integrity/expected content;
4. durably promote/replace the canonical local archive file in the application-managed `Archive\` area;
5. reopen and validate that canonical local archive;
6. only then remove exactly the eligible rows from `live.db`;
7. create the required post-archive local recovery point.

If staging, validation, local promotion or post-promotion validation fails, eligible live rows remain in `live.db` and the archive attempt is retryable.

No cloud synchronization receipt is required because cloud publication is no longer part of annual archiving.

### 4. Explicit user-selected export is a separate action

The operator may explicitly export/copy a completed annual archive database.

For this action:

- the operator chooses the destination path/location;
- export copies the validated completed archive; it never moves/deletes the canonical local archive;
- export failure cannot invalidate the canonical archive or change `live.db`;
- destination selection is operator-controlled and is not a background automatic archive requirement;
- the automatic February/late-start archive flow remains non-interactive and must not stop startup waiting for a file picker.

Standard safe file-finalization/overwrite handling is a technical implementation detail provided no existing destination is silently destroyed on failure.

### 5. Historical access

Normal live search remains live-only.

Historical access remains explicit by archive year and uses locally available completed annual archives. Archive databases are opened read-only in ordinary POS use. Archive viewing/reprinting does not grant authority and does not mutate the archive.

A normal handoff transfers live authority/data only; it does not automatically copy annual archive files between devices.

### 6. Retention

Completed canonical local annual archives are retained permanently by the application with no rolling deletion.

The operator may deliberately manage exported copies outside normal POS workflow. Normal POS workflow does not auto-delete completed canonical archives.

### 7. Printing/export/history semantics preserved

This amendment does not change:

- archive-year assignment;
- February/late-start timing;
- Open-order exclusion;
- POS/HIBOUTIK_PASTE archive-year parity;
- historical snapshot fidelity;
- archived reprint markings/content;
- M11 export-preservation requirement before live removal;
- M07 authority/handoff/DR semantics.

## Consequence for M12 implementation

The prior OneDrive remote-publication blocker is removed from M12 because annual archive publication is now a local durable promotion/validation boundary.

WP1 remains archive core/eligibility/staging.

WP2 becomes: pending-export preservation + local canonical publication + validated live removal.

WP4 historical discovery/search reads the application-managed local Archive area; OneDrive hydration/cache is no longer part of M12.

M13 remains unauthorized.
