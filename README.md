# Sushi81 POS

Private repository for the design and implementation of a Windows local point-of-sale and order-management application for Sushi 81.

## Project status

**V1 Specification frozen — Phase 5 complete (2026-08-27).**

The approved V1 product, business, architecture, data, storage, paste-import, printing, export and acceptance specifications are frozen in `docs/`.

Formal freeze record: `docs/v1-specification-freeze.md`  
Implementation acceptance contract: `docs/acceptance-criteria.md`

M01 is complete. M02 GitHub transport revalidation is the only active implementation scope on `codex/m02-directed-handoff-revalidation`; M03 remains unauthorized. Normal target-directed handoff uses a dedicated private GitHub Release Asset repository and strict server receipts. Historical OneDrive feasibility evidence is retained and is not the normal authority gate.

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

See `docs/README.md` for the frozen V1 documentation baseline and authority rules.
