# Source code

Production source code lives under this directory.

**Current status:** M01 foundation and the M03 current-catalogue/settings subsystem are implemented on the active M03 branch; M02 transport harness work remains isolated under `tools/`.

The source tree is ready for explicit Codex implementation milestones against:

- `../docs/v1-specification-freeze.md`;
- the approved baseline documents under `../docs/`;
- `../docs/acceptance-criteria.md`;
- repository instructions in `../AGENTS.md`.

Do not implement speculative features or alter frozen business/architecture semantics merely to simplify coding. If a genuine material specification ambiguity appears, surface it and resolve the specification before continuing that affected path.

Normal handoff transport is the dedicated private GitHub Release Asset API defined by `../docs/decisions/github-handoff-transport.md`; the historical OneDrive diagnostics are not a source of write authority.

M03 production code is intentionally limited to Categories, Products, structured OptionGroups/Options, the singleton
BusinessSettings contract, SQLite migration 2, and the localized two-destination WPF maintenance shell. Order entry,
pricing, payments, printing, imports/exports, handoff UI and recovery workflows remain in their authorized milestones.
