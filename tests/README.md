# Tests

Automated tests for Sushi81 POS live under this directory.

**Current status:** M01–M09 and the post-M09 Hiboutik daily CB/Espèce dashboard regression/evidence suites are part of the merged project baseline. The post-M09 enhancement was merged through PR #19 at `861cfba1dfacbb3289395c0370f6d42765b6c223`. M10 WP1–WP4 are controller-accepted; WP5 final production-path hardening and exact owner-candidate preparation is active on PR #22 under OPEN Issue #4 from `24da074827610325c0a27292d5a9856f91b7c666`. Owner Windows/Excel acceptance remains unexecuted.

The authoritative test strategy comes from `../docs/acceptance-criteria.md`, approved acceptance amendments, the frozen/amended V1 documents and milestone-specific implementation contracts under `../docs/implementation/`.

Implementation should convert acceptance criteria into automated unit/integration/regression tests wherever practical, including business pricing, lifecycle/payment arithmetic, effective payment-date attribution, catalogue validation/import, Hiboutik paste parsing, data persistence/migrations, storage/handoff/recovery/archive behavior, deterministic print models and export contracts.

Use only synthetic or sanitized fixtures. Never commit real customer, order, payment, credential or other sensitive production data.

GitHub transport tests must validate strict Release Asset server receipts, exact snapshot/grant names, target binding, digest/size integrity, durable relinquishment ordering, restart safety and retention without live GitHub dependencies in ordinary automated suites.

Existing Catalogue coverage includes normalization/validation, opaque identity preservation, Category short-code persistence, deterministic option ordering, activation/deactivation, filtered bulk atomicity/conflict rollback, aggregate persistence, authority rejection and WPF Catalogue behavior.

Existing order/lifecycle/storage/printing/M09/post-M09 suites cover historical order snapshots, payment/date arithmetic, same-ID modification, search/dashboard queries, authority/recovery, handoff/DR, deterministic printing, Hiboutik parsing/import orchestration, source-aware reporting boundaries and the two passive Hiboutik daily payment values.

## M10 WP5 evidence — active authorized handoff

The prepared M10 contract requires future authorized tests at these levels:

- pure import-planner tests for Update/Add-only identity semantics, Category name/short-code rules, parent relationships, validation, preview and no-delete overlay;
- real temp `.xlsx` ClosedXML contract tests for three visible sheets, hidden/locked technical columns, VeryHidden metadata, round-trip, corrupt/tampered workbook rejection and no COM/Interop;
- SQLite integration tests for one-transaction whole-import commit, failure rollback, no implicit deletion, concurrency conflict, business revision/recovery notification and historical-order independence;
- Application tests for export/preview as reads and authoritative-only commit;
- STA/WPF tests for localized Export/Import/Preview workflow, Confirm gating, Cancel/close safety and Catalogue refresh/state preservation;
- full M03/M04/M06/M07 plus whole-solution Release regression.

The owner-approved M10 Category short-code acceptance clarification is `../docs/acceptance-criteria-amendment-m10-category-short-code-workbook.md`. WP5 adds real temporary-file production-path round-trip/edit/tamper evidence and the final automated regression/audit matrix; it does not execute owner Windows/Excel acceptance.

The current executable handoff is `M10-WP5-HARDENING-FINAL-CANDIDATE-17`; no other M10 package, owner manual checklist or later milestone is implied.
