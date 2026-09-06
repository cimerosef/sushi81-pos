# Source code

Production source code lives under this directory.

**Current status:** M01–M04 are implemented and merged to `main`; M02 transport harness work remains isolated under `tools/`. M05 — Lifecycle, payments, search and operational dashboard — is implemented and has Passed project-owner Windows/WPF acceptance on `codex/m05-lifecycle-payments-search-dashboard`; PR #10 remains open/unmerged pending explicit merge approval. Its contract and final acceptance record are under `../docs/implementation/`.

The source tree is ready for explicit Codex implementation milestones against:

- `../docs/v1-specification-freeze.md`;
- the approved baseline/amendment documents under `../docs/`;
- `../docs/acceptance-criteria.md`;
- repository instructions in `../AGENTS.md`.

Do not implement speculative features or alter frozen/amended business/architecture semantics merely to simplify coding. If a genuine material specification ambiguity appears, surface it and resolve the specification before continuing that affected path.

Normal handoff transport is the dedicated private GitHub Release Asset API defined by `../docs/decisions/github-handoff-transport.md`; the historical OneDrive diagnostics are not a source of write authority.

The merged M03/M04 production code provides current Catalogue and settings maintenance, the order-entry/cart/pricing/order-snapshot slice, and the post-commit print-dispatch boundary. M05 adds the same-ID Commandes lifecycle/payment/search/dashboard workflow and SQLite migration 5. Final Windows printing, imports/exports, handoff UI, recovery enforcement, archive and installer remain in later milestones.
