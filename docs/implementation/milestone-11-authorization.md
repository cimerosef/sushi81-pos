# M11 — Gestion intermediate export — authorization

**Status:** NOT AUTHORIZED  
**Date:** 2026-09-20

## Current state

The project owner authorized entry into M11 preparation, specification audit and implementation planning.

The owner also approved the M11 material specification clarifications recorded in:

- `docs/decisions/m11-export-lifecycle-and-settlement-clarifications.md`;
- `docs/acceptance-criteria-amendment-m11-gestion-export.md`.

Those approvals do **not** by themselves authorize production implementation.

## Required authorization transition

Before Codex may modify production/test implementation for M11, the project owner must give a separate explicit implementation authorization after reviewing the finalized preparation package and work-package split.

After that explicit authorization, the controller may:

1. create the dedicated M11 implementation branch from the exact finalized preparation head;
2. create the dedicated implementation PR/mailbox;
3. update this record to AUTHORIZED on that implementation line;
4. publish the first exact `CODEX_HANDOFF_READY`;
5. update/open Issue #4 only after branch/PR/handoff pointers match.

Until all execution prerequisites match, execution fails closed.

## Current gate

- Issue #4: CLOSED;
- active Codex handoff: none;
- preparation PR is documentation/control work only;
- M12/M13: unauthorized;
- merge: not authorized.
