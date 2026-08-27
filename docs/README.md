# Project documentation

This directory contains the approved product, business, architecture, and operational specifications for Sushi81 POS.

Documents are written progressively by design phase. A document should not be treated as implementation-authoritative until it has been reviewed and its status is clear.

## Planned documentation sequence

### Phase 1 — Current system and product scope

- `current-system.md`
- `product-requirements.md`

### Phase 2 — Business model

- `order-lifecycle.md`
- `business-rules.md`
- `catalogue-management.md`

### Phase 3 — Core technical architecture

- `architecture.md`
- `data-model.md`
- `storage-strategy.md`

Phase 3 is complete when all three documents are approved baselines and their remaining purely technical choices are frozen consistently.

### Phase 4 — Input, printing and export specifications

- `paste-order-import.md`
- `printing.md`
- `export.md`

A separate `sync-and-backup.md` is not planned for V1 because live storage, local recovery, OneDrive handoff, disaster recovery and annual archive behavior are already specified comprehensively in the approved `storage-strategy.md`. A separate document should be added only if a future requirement cannot be represented cleanly there.

### Phase 5 — V1 specification freeze

- `acceptance-criteria.md`
- final cross-document consistency review;
- approved decisions under `decisions/` where a materially constraining decision merits a separate record;
- V1 Specification freeze.

Production implementation must not begin until the required V1 design documents have been reviewed, the acceptance criteria are approved and the V1 Specification freeze has been completed.