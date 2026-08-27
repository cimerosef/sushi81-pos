# Sushi81 POS

Private repository for the design and implementation of a Windows local point-of-sale and order-management application for Sushi 81.

## Project status

**Phase 0 — Repository initialized. Product and architecture design in progress.**

No production implementation should begin until the v1 product requirements, business rules, order lifecycle, data model, storage/sync strategy, and acceptance criteria have been reviewed and frozen.

## Working model

- Business needs and product decisions are discussed and decided before implementation.
- GitHub is the authoritative project record for approved specifications and decisions.
- Codex is used primarily for implementation, testing, refactoring, build and other execution work after specifications are approved.
- Core business or architecture decisions must not be invented during implementation.

## Final delivery requirement

The completed project must include a concise **user manual / operating guide** as part of the final deliverables.

The manual must summarize the application's main user-facing functions and normal operating workflows in practical language. It should be written for day-to-day Sushi 81 use rather than as developer documentation and should cover at least the major areas implemented in the final product, such as order entry and modification, payment entry/reconciliation, printing/reprinting, future orders and reminders, catalogue maintenance, Excel catalogue import/export, Hiboutik emergency import, archive/restore operations, settings and other essential operator actions.

The exact table of contents should reflect the final implemented application. The project is not considered fully handed over until this operating guide has been delivered together with the application and the other approved project artifacts.

## Planned repository structure

```text
sushi81-pos/
├── README.md
├── AGENTS.md
├── .gitignore
├── docs/
│   ├── decisions/
│   └── ...
├── samples/
│   └── pasted-orders/
├── src/
└── tests/
```

Detailed product documents will be added progressively during the design phases.
