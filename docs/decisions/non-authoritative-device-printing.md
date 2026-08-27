# Printing from a non-authoritative device

**Status:** Approved — Phase 4 decision  
**Date:** 2026-08-27  
**Applies to:** live-order printing from a paired device that is read-only/non-authoritative and may contain stale data

## Decision

A non-authoritative paired device is allowed to print and reprint both kitchen and customer tickets from the live-data copy currently available on that device.

The application must make the stale/non-authoritative state clear before or at the print action, but it must not hard-block printing.

The operator is responsible for deciding whether the locally available order state is sufficiently current for the intended print.

Completed annual archive orders remain printable as ordinary immutable historical records.

## Required safety behavior

When printing from a non-authoritative live-data copy:

- the UI must clearly indicate that the device is not authoritative and that the displayed order may not be the latest version;
- the print action remains available;
- no write, synchronization claim or authority transfer is implied by printing;
- the printed document is generated strictly from the committed order state actually available on that device;
- the application must not silently pretend that freshness has been verified when it has not.

## Rationale

V1 deliberately leaves the final operational risk judgment to the user rather than removing printing capability from a fallback device. This preserves emergency usefulness while keeping the stale-data risk explicit.
