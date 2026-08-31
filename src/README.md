# Source code

Production source code lives under this directory.

**Current status:** M01 foundation plus the complete M03 current-catalogue/settings subsystem are implemented and merged to `main`; M02 transport harness work remains isolated under `tools/`. M04 — First complete order-entry vertical slice — is the current authorized implementation milestone under `../docs/implementation/milestone-04-order-entry.md`.

The source tree is ready for explicit Codex implementation milestones against:

- `../docs/v1-specification-freeze.md`;
- the approved baseline/amendment documents under `../docs/`;
- `../docs/acceptance-criteria.md`;
- repository instructions in `../AGENTS.md`.

Do not implement speculative features or alter frozen/amended business/architecture semantics merely to simplify coding. If a genuine material specification ambiguity appears, surface it and resolve the specification before continuing that affected path.

Normal handoff transport is the dedicated private GitHub Release Asset API defined by `../docs/decisions/github-handoff-transport.md`; the historical OneDrive diagnostics are not a source of write authority.

The merged M03 production code provides Categories, Products, structured OptionGroups/Options, the singleton BusinessSettings contract, atomic filtered bulk Product activation/deactivation, SQLite migration 2, and the localized Catalogue/Settings WPF maintenance shell. M04 may now add only its authorized order-entry/cart/pricing/order-snapshot/test-dispatch slice. Payments/lifecycle dashboard, final Windows printing, imports/exports, handoff UI, recovery enforcement, archive and installer remain in later milestones.
