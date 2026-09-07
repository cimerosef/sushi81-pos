# Sushi81 POS

Private repository for the design and implementation of a Windows local point-of-sale and order-management application for Sushi 81.

## Project status

**V1 Specification frozen — Phase 5 complete (2026-08-27), with approved post-freeze amendments. Phase 6 implementation is active.**

The approved V1 product, business, architecture, data, storage, paste-import, printing, export and acceptance specifications are frozen/amended in `docs/`.

Formal freeze record: `docs/v1-specification-freeze.md`  
Implementation acceptance contract: `docs/acceptance-criteria.md`

M01 through M06 are Passed and merged. M04 merged through PR #6 at merge commit `ab218263bd4eee9c1be203d36acc552988cef43a` after complete Windows/WPF manual acceptance.

M05 — Lifecycle, payments, search and operational dashboard — Passed implementation and project-owner Windows/WPF acceptance and was merged through PR #10 to `main` at merge commit `79499d7c6ed65a74f524097c1507ca648dc151c3`. Accepted M05 production-code head: `84c1c534c1df105ccb1839cbc6dfc9e0e055bb70`; final M05 documentation head: `217d187dd3ef5498c11f21bc516eccc6737fa952`. M05 is no longer open work.

M06 — Local recovery and authoritative/read-only enforcement — Passed implementation and project-owner Windows/WPF acceptance and was merged through PR #11 to `main` at merge commit `2c5eb52740d0c12e3e837579ecceac6d0600b59e`. Accepted M06 production repair head: `4a0c1ca9e44a6c48899e6ef8dc211172371e4d20`; final M06 documentation/PR head: `86326d81551aa4cb5cdcbc6826b8c740317b34c4`; Release tests: 364/364 Passed.

M07 — Pairing, target-directed formal handoff and disaster recovery — is the next planned milestone and is **not yet implementation-authorized**. There is no active implementation PR/branch/handoff, and Codex execution gate issue #4 is CLOSED. M08 and later milestones are not started.

Normal target-directed authority handoff uses a dedicated private GitHub Release Asset repository and strict server receipts. Historical OneDrive feasibility evidence is retained and is not the normal authority gate. Real pairing/handoff/target acquisition/disaster recovery belong to M07.

## Working model

- Business needs and product decisions are discussed and decided before implementation.
- GitHub is the authoritative project record for approved specifications and decisions.
- Codex is used primarily for implementation, testing, refactoring, build and other execution work after specifications are approved.
- Core business or architecture decisions must not be invented during implementation.
- If a genuine specification ambiguity affecting business behavior, architecture, data safety, printing/export contracts or acceptance criteria appears, the affected implementation path must stop for specification resolution rather than silently guessing.
- Pure implementation details that preserve approved behavior may be selected according to the project priority order: reliability > simplicity > maintainability > operational clarity > novelty.

## Final delivery requirement

The completed project must include a concise **user manual / operating guide** as part of the final deliverables.

The manual must summarize the application's main user-facing functions and normal operating workflows in practical language. It should be written for day-to-day Sushi 81 use rather than as developer documentation and should cover at least the major areas implemented in the final product, such as order entry and modification, payment entry/reconciliation, printing/reprinting, future orders and reminders, catalogue maintenance, Excel catalogue import/export, Hiboutik paste-order fallback, archive/recovery operations, settings and other essential operator actions.

The exact table of contents should reflect the final implemented application. The project is not considered fully handed over until this operating guide has been delivered together with the application and the other approved project artifacts.

## Repository structure

```text
sushi81-pos/
├── README.md
├── AGENTS.md
├── .gitignore
├── docs/
│   ├── decisions/
│   ├── acceptance-criteria.md
│   ├── v1-specification-freeze.md
│   └── ...
├── samples/
│   └── pasted-orders/
├── src/
└── tests/
```

See `docs/README.md` for the frozen/amended V1 documentation baseline and authority rules.
