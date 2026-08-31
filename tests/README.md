# Tests

Automated tests for Sushi81 POS live under this directory once implementation begins.

**Current status:** M01 and M03 tests are green; M02 GitHub transport tests use deterministic fake HTTP/synthetic state only.

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
