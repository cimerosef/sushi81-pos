# Source code

Production source code lives under this directory.

**Current status:** M01 foundation is implemented; M02 transport harness work is isolated under `tools/`; no M03 business-feature code has been started.

The source tree is ready for explicit Codex implementation milestones against:

- `../docs/v1-specification-freeze.md`;
- the approved baseline documents under `../docs/`;
- `../docs/acceptance-criteria.md`;
- repository instructions in `../AGENTS.md`.

Do not implement speculative features or alter frozen business/architecture semantics merely to simplify coding. If a genuine material specification ambiguity appears, surface it and resolve the specification before continuing that affected path.

Normal handoff transport is the dedicated private GitHub Release Asset API defined by `../docs/decisions/github-handoff-transport.md`; the historical OneDrive diagnostics are not a source of write authority.
