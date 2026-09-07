# Source code

Production source code lives under this directory.

**Current status:** M01–M06 are implemented, accepted and merged to `main`; M02 transport harness work remains isolated under `tools/`. M06 — Local recovery and authoritative/read-only enforcement — passed project-owner Windows/WPF manual acceptance and was merged through PR #11 at merge commit `2c5eb52740d0c12e3e837579ecceac6d0600b59e`. Accepted M06 production repair head: `4a0c1ca9e44a6c48899e6ef8dc211172371e4d20`; final documentation/PR head: `86326d81551aa4cb5cdcbc6826b8c740317b34c4`; Release tests: 364/364 Passed. M07 is the next planned milestone and is not yet implementation-authorized.

The source tree is ready for explicitly authorized Codex implementation milestones against:

- `../docs/v1-specification-freeze.md`;
- the approved baseline/amendment documents under `../docs/`;
- `../docs/acceptance-criteria.md`;
- repository instructions in `../AGENTS.md`.

Do not implement speculative features or alter frozen/amended business/architecture semantics merely to simplify coding. If a genuine material specification ambiguity appears, surface it and resolve the specification before continuing that affected path.

Normal handoff transport is the dedicated private GitHub Release Asset API defined by `../docs/decisions/github-handoff-transport.md`; the historical OneDrive diagnostics are not a source of write authority.

The merged M03–M05 production code provides Catalogue/settings maintenance, order entry/cart/pricing/order snapshots, same-ID lifecycle/payment/search/dashboard behavior and the post-commit print-dispatch boundary. M06 adds the single production persistent authority/read-only state, centralized Application-layer business-write guard, validated latest-five local recovery snapshots and recovery scheduling/flush. M07 owns production pairing, target-directed formal handoff, target acquisition, recovery-only cloud checkpoints, Disaster Recovery and generation invalidation. Final Windows printing, imports/exports, archive and installer remain in later milestones.
