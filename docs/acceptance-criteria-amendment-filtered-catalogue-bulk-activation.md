# V1 acceptance amendment — filtered catalogue bulk activation/deactivation

**Status:** Approved — V1 Specification amendment  
**Date:** 2026-08-30  
**Product:** Sushi81 POS  
**Amends:** `acceptance-criteria.md`, Catalogue management section
**Decision record:** `decisions/filtered-catalogue-bulk-activation.md`

## Purpose

This document adds one approved V1 acceptance criterion discovered during M03 interactive catalogue acceptance. It is part of the frozen-and-amended V1 acceptance contract and must be read together with `acceptance-criteria.md` until the next consolidated rewrite of that file.

## AC-CAT-013 — Filtered bulk activation/deactivation

**Given** the operator is in the in-application Catalogue maintenance area and has any combination of:

- code/name keyword search;
- category filter;
- Active/Inactive/All status filter;

**when** the operator invokes bulk Activate or bulk Deactivate,

**then** all of the following must hold:

1. the action targets the complete current filtered Product result, not only currently rendered/visible rows;
2. the action captures the matching Product IDs and requested target state as one immutable operation snapshot before confirmation;
3. confirmation clearly states the target action, the number of matched products, and the number that would actually change state;
4. products already in the target state are skipped rather than rewritten;
5. zero effective changes produce no business write and clear operator behavior (disabled action or explicit no-change result);
6. explicit confirmation is required before mutation;
7. every required state change commits atomically in one business transaction, or none commits;
8. a missing/stale/conflicting captured Product causes complete failure rather than partial success;
9. only Product active/inactive state changes; all other catalogue attributes, groups/options and ordering remain unchanged;
10. historical order snapshots are not rewritten;
11. bulk permanent deletion is not exposed by this workflow;
12. after success the catalogue refreshes automatically while preserving the active search/category/status filter values;
13. French and Simplified Chinese labels/confirmation/result messages are available without translating catalogue business data.

**Evidence:**

- Application/use-case tests for immutable target snapshot/count calculation/no-op behavior;
- SQLite integration tests proving all-or-nothing mutation and stale/missing-ID rollback;
- regression tests proving unrelated Product/OptionGroup/Option data remains unchanged;
- WPF/manual tests covering composed filters, confirmation counts, Activate, Deactivate, zero-change behavior, post-success live refresh, FR/zh-CN localization and absence of bulk Delete.

## Ownership

**Milestone owner:** M03 — Catalogue and business settings.

M03 cannot be marked Passed until this criterion and the remaining M03 manual checklist are accepted. This amendment does not authorize M04 or any later milestone.
