# Post-M14 Production Maintenance Batch 01 — worklog

**Status:** ACTIVE
**Baseline:** `71024888a9cfd08109496ad076002be96e708c1a`

This is an append-only implementation/evidence ledger. Historical entries must not be rewritten to make later outcomes look earlier.

## 2026-09-29 — owner scope approval

The owner reported and approved one combined maintenance batch containing:

1. intermittent unintended duplicate product addition in Caisse — correctness bug / first priority;
2. cart auto-reveal after a new line is appended;
3. natural alphanumeric current product-code ordering;
4. applied Retrait discount amount on customer tickets.

Controller ordering is WP1 -> WP2 -> WP3 -> WP4 -> WP5 integration/candidate.

Owner acceptance is planned on computer A PREPROD only because the approved scope does not touch the M14 deferred multi-device areas. M12 populated real archive verification remains separately deferred. M14 two-PC PREPROD verification remains deferred/not Passed.
