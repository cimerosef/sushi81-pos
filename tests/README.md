# Tests

Automated tests for Sushi81 POS live under this directory.

**Current status:** M01–M09 and the post-M09 Hiboutik daily CB/Espèce dashboard regression/evidence suites are part of the merged project baseline. The post-M09 enhancement was merged through PR #19 at `861cfba1dfacbb3289395c0370f6d42765b6c223`. M10 Catalogue `.xlsx` preparation is complete but implementation is **NOT AUTHORIZED**; no M10 production/test implementation handoff is active and Issue #4 remains CLOSED.

The authoritative test strategy comes from `../docs/acceptance-criteria.md`, approved acceptance amendments, the frozen/amended V1 documents and milestone-specific implementation contracts under `../docs/implementation/`.

Implementation should convert acceptance criteria into automated unit/integration/regression tests wherever practical, including business pricing, lifecycle/payment arithmetic, effective payment-date attribution, catalogue validation/import, Hiboutik paste parsing, data persistence/migrations, storage/handoff/recovery/archive behavior, deterministic print models and export contracts.

Use only synthetic or sanitized fixtures. Never commit real customer, order, payment, credential or other sensitive production data.

GitHub transport tests must validate strict Release Asset server receipts, exact snapshot/grant names, target binding, digest/size integrity, durable relinquishment ordering, restart safety and retention without live GitHub dependencies in ordinary automated suites.

Existing Catalogue coverage includes normalization/validation, opaque identity preservation, Category short-code persistence, deterministic option ordering, activation/deactivation, filtered bulk atomicity/conflict rollback, aggregate persistence, authority rejection and WPF Catalogue behavior.

Existing order/lifecycle/storage/printing/M09/post-M09 suites cover historical order snapshots, payment/date arithmetic, same-ID modification, search/dashboard queries, authority/recovery, handoff/DR, deterministic printing, Hiboutik parsing/import orchestration, source-aware reporting boundaries and the two passive Hiboutik daily payment values.

## M10 planned evidence — not yet executable

The prepared M10 contract requires future authorized tests at these levels:

- pure import-planner tests for Update/Add-only identity semantics, Category name/short-code rules, parent relationships, validation, preview and no-delete overlay;
- real temp `.xlsx` ClosedXML contract tests for three visible sheets, hidden/locked technical columns, VeryHidden metadata, round-trip, corrupt/tampered workbook rejection and no COM/Interop;
- SQLite integration tests for one-transaction whole-import commit, failure rollback, no implicit deletion, concurrency conflict, business revision/recovery notification and historical-order independence;
- Application tests for export/preview as reads and authoritative-only commit;
- STA/WPF tests for localized Export/Import/Preview workflow, Confirm gating, Cancel/close safety and Catalogue refresh/state preservation;
- full M03/M04/M06/M07 plus whole-solution Release regression.

The owner-approved M10 Category short-code acceptance clarification is `../docs/acceptance-criteria-amendment-m10-category-short-code-workbook.md`.

No M10 test implementation may be started merely because this evidence plan exists. A separate explicit project-owner M10 implementation authorization, valid executable handoff and OPEN Issue #4 gate are still required.