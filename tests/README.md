# Tests

Automated tests for Sushi81 POS live under this directory once implementation begins.

**Current status:** V1 Specification frozen; implementation/test code has not yet been started by the freeze work itself.

The test strategy is authoritative from `../docs/acceptance-criteria.md` and the approved V1 documents under `../docs/`.

Implementation should convert acceptance criteria into automated unit/integration/regression tests wherever practical, including business pricing, lifecycle/payment arithmetic, effective payment-date attribution, catalogue validation/import, Hiboutik paste parsing, data persistence/migrations, storage/handoff/recovery/archive behavior, deterministic print models and export contracts.

Use only synthetic or sanitized fixtures. Never commit real customer, order, payment, credential or other sensitive production data.
