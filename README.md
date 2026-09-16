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

M09 — Hiboutik paste-order fallback — implementation, automated evidence and owner Windows/WPF manual acceptance are **Passed / ready for separate merge approval**. The accepted production candidate is `7d0144d452231fe92cf7c31e027e9b1bb6f5a43d` with EXE SHA-256 `CDC1257A698FE90237316FFE92361BF20EEF97CB44065AFBF70202EE0D0A26BA` and ZIP SHA-256 `00CEFAECB56666FABB03B7D141A4B75D18CF4DAA1A270358E7D3C457D5736319`. The approved amendment covers product-detail-block paste input, fail-safe unresolved-line operator handling, passive Hiboutik source identification and nullable read-only `source_total_ttc` reference amount while keeping ordinary POS pricing authoritative.

The final documentation-only closure is being recorded on dedicated branch `codex/m09-hiboutik-paste-fallback` / PR #17. PR #17 remains OPEN / unmerged pending separate explicit project-owner merge approval; the later documentation head is not a replacement for the accepted production candidate. GitHub Issue #4 remains the sole execution gate. M10 and later milestones remain unauthorized.

Controlling M09 records:

- `docs/decisions/m09-hiboutik-paste-operator-workflow-and-source-reference.md`;
- `docs/acceptance-criteria-amendment-m09-hiboutik-paste-fallback.md`;
- `docs/paste-order-import.md`;
- `docs/implementation/milestone-09-preparation-readiness.md`;
- `docs/implementation/milestone-09-hiboutik-paste-fallback.md`;
- `docs/implementation/milestone-09-final-manual-acceptance.md`;
- `docs/implementation/milestone-09-authorization.md`.

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
