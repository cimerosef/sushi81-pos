# Tests

Automated tests for Sushi81 POS live under this directory once implementation begins.

**Current status:** M01–M06 regression/evidence suites are green. M06 final Release verification is 364/364 Passed with project-owner Windows/WPF manual acceptance Passed; M06 was merged through PR #11 at merge commit `2c5eb52740d0c12e3e837579ecceac6d0600b59e`. M07 is next but is not yet implementation-authorized. M02 GitHub transport tests use deterministic fake HTTP/synthetic state only, with separate sanitized historical live evidence recorded in the M02 report.

The test strategy is authoritative from `../docs/acceptance-criteria.md` and the approved V1 documents under `../docs/`.

Implementation should convert acceptance criteria into automated unit/integration/regression tests wherever practical, including business pricing, lifecycle/payment arithmetic, effective payment-date attribution, catalogue validation/import, Hiboutik paste parsing, data persistence/migrations, storage/handoff/recovery/archive behavior, deterministic print models and export contracts.

Use only synthetic or sanitized fixtures. Never commit real customer, order, payment, credential or other sensitive production data.

GitHub transport tests must validate strict Release Asset server receipts, exact snapshot/grant names, target binding, digest/size integrity, durable relinquishment ordering, restart safety and newest-three retention without live GitHub dependencies.

M03 domain and infrastructure tests use isolated temporary SQLite databases and synthetic catalogue/settings values. They
cover normalization and validation boundaries, migration 2/default singleton idempotence, aggregate transactionality,
opaque identity preservation, deterministic option ordering, activation/deactivation, atomic filtered bulk activation/deactivation
(including stale/missing rollback, injected failure rollback, timestamp and aggregate preservation), cascade deletion, code reuse
and settings round-trip persistence. Desktop tests cover composed-filter capture/latest-debounce synchronization, immutable
bulk snapshots, localized actions/confirmation resources, filter-state preservation and the absence of bulk deletion.

M04/M05 infrastructure tests also use isolated temporary SQLite databases and synthetic orders. They cover persisted order
snapshots, migration 5 reference backfill/allocation, signed CB/Espèce deltas, same-ID modification and automatic reopen,
Close/Cancel financial exclusions, live reference/telephone/comment search, and operational summary queries.

M06 adds automated coverage for centralized authoritative-write rejection before business mutation, durable authority/read-only restart reconstruction, fail-closed missing/corrupt authority state, validated latest-five local-recovery retention, post-commit recovery scheduling/debounce/single-flight/shutdown flush, and real STA/WPF read-only/transition/recovery-required presentation. The separate final operator acceptance is recorded in `../docs/implementation/milestone-06-final-manual-acceptance.md`.
