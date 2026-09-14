# Sushi81 POS

Repository for the design and implementation of a Windows local point-of-sale and order-management application for Sushi 81.

## Project status

**V1 Specification frozen — Phase 5 complete (2026-08-27), with approved post-freeze amendments. Phase 6 implementation is active.**

The approved V1 product, business, architecture, data, storage, paste-import, printing, export and acceptance specifications are frozen/amended in `docs/`.

Formal freeze record: `docs/v1-specification-freeze.md`  
Implementation acceptance contract: `docs/acceptance-criteria.md` plus approved acceptance amendments.

M01 through M08 are **Passed and merged**.

Recent merge baselines:

- M05 — Lifecycle, payments, search and operational dashboard — merged through PR #10 at `79499d7c6ed65a74f524097c1507ca648dc151c3`;
- M06 — Local recovery and authoritative/read-only enforcement — merged through PR #11 at `2c5eb52740d0c12e3e837579ecceac6d0600b59e`;
- M07 — Pairing, target-directed handoff and disaster recovery — merged through PR #13 at `9ea7d5e15bceba6932cb2caba50d0afb64ca1ff9`;
- M08 — Printing and reprinting — Passed owner/manual acceptance and merged through PR #14 at `8f246ce7fb32baa33e1dfe1d334175bf2df60c1f`.

M09 — Hiboutik paste-order fallback — has completed **preparation/readiness** only. The owner approved the 2026-09-14 M09 specification amendment covering product-detail-block paste input, fail-safe unresolved-line operator handling, passive Hiboutik source identification and nullable read-only `source_total_ttc` reference amount.

M09 is **ready for separate project-owner implementation authorization**, but implementation is not yet authorized. There is no active M09 branch/PR/executable handoff and Codex execution gate issue #4 remains CLOSED. M10 and later milestones remain unauthorized.

Controlling M09 preparation records:

- `docs/decisions/m09-hiboutik-paste-operator-workflow-and-source-reference.md`;
- `docs/acceptance-criteria-amendment-m09-hiboutik-paste-fallback.md`;
- `docs/paste-order-import.md`;
- `docs/implementation/milestone-09-preparation-readiness.md`;
- `docs/implementation/milestone-09-hiboutik-paste-fallback.md`;
- `docs/implementation/milestone-09-final-manual-acceptance.md`.

Normal target-directed authority handoff uses a dedicated private GitHub Release Asset repository and strict server receipts. OneDrive remains recovery/archive storage and historical diagnostic transport rather than the normal authority gate.

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
│   ├── implementation/
│   ├── acceptance-criteria.md
│   ├── v1-specification-freeze.md
│   └── ...
├── samples/
│   └── pasted-orders/
├── src/
└── tests/
```

See `docs/README.md` for the frozen/amended V1 documentation baseline and authority rules.
