# AGENTS.md

## Purpose

This repository is design-led. Implementation agents must execute the frozen approved specifications rather than invent product or business decisions.

## Current phase

**V1 Specification is frozen — Phase 5 complete (2026-08-27).**

The repository is ready for implementation, but production application code should be created only when an explicit implementation/Codex task authorizes the relevant milestone.

A specification freeze is not itself an instruction to implement everything at once. Implement the assigned milestone only.

## Authoritative sources

Use the approved documents under `docs/`, the approved records under `docs/decisions/` and `docs/acceptance-criteria.md` as the source of truth.

The formal V1 freeze record is `docs/v1-specification-freeze.md`.

`docs/current-system.md` describes the former/current Excel/VBA operation and is contextual/historical. It must not override later approved V1 target behavior.

If code, assumptions, issue text, prior chat discussion or legacy behavior conflicts with the frozen GitHub specification, follow the approved GitHub specification. If two approved specification sources genuinely contradict one another, stop the affected implementation path and surface the conflict instead of silently choosing a behavior.

## Changes that require an explicit approved specification amendment

Do not independently change or invent any of the following:

- core technology stack;
- database engine or core data model semantics;
- order lifecycle or finalization semantics;
- payment model or received-payment date attribution;
- discount, VAT or commercial business rules;
- synchronization/handoff/backup/disaster-recovery strategy;
- data retention/archive/deletion behavior;
- printing architecture or business-facing print semantics;
- paste-import business behavior;
- export eligibility, contract or correction semantics;
- introduction of paid, proprietary, hosted or subscription dependencies that alter the approved cost/architecture boundary;
- any behavior required by `docs/acceptance-criteria.md`.

When such a question appears during implementation:

1. stop the affected decision path;
2. describe the ambiguity/conflict precisely;
3. obtain an approved specification decision/amendment;
4. update the affected GitHub specification before implementing the changed behavior.

## Safe implementation autonomy

Within the frozen specification, implementation agents may normally:

- implement assigned functionality;
- choose physical SQL table/column/index names consistent with the logical data model;
- choose internal class/module/file names and ordinary code organization consistent with the approved architecture;
- select minor UI layout/typography details that do not change approved workflow or required visibility;
- choose low-level serialization/coordination details where the storage specification explicitly delegates them and all invariants remain preserved;
- refactor local code without changing behavior;
- add/improve tests;
- improve error handling, diagnostics and logging;
- fix clear defects;
- improve internal naming and maintainability.

Use this priority order when choosing between equally conformant implementation options:

**reliability > simplicity > maintainability > operational clarity > novelty.**

## Acceptance contract

Implementation work is not complete merely because the application builds or appears to work manually.

The V1 implementation must satisfy `docs/acceptance-criteria.md`.

Where practical, acceptance criteria should be converted into automated unit/integration/regression tests during implementation rather than postponed to the end.

## Data safety

Business data is durable and non-disposable.

- Never delete, overwrite, reset or migrate real business data outside an explicitly approved operation with a recovery path.
- Schema migrations must be versioned and testable.
- Migration failure must not silently reset production data.
- Backward compatibility and recovery implications must be considered for database changes.
- Never commit real customer, order, payment, credential or other sensitive production data to Git.
- Parser/test fixtures must use synthetic or sanitized data.

## Dependencies

Prefer free and open-source dependencies compatible with the approved architecture.

Use the frozen technology/dependency boundaries in `docs/architecture.md`, including the approved .NET/WPF/SQLite, ClosedXML and Windows-printing directions.

Do not add a paid service or dependency that creates recurring operational cost without explicit approval.

## Scope discipline

Implement the requested milestone only. Do not add speculative product features merely because they seem useful.

Do not reintroduce superseded complexity, especially the former Hiboutik emergency-order UI/original-total/discrepancy/reconciliation model. Hiboutik paste-created orders use the normal order model plus only the hidden anti-double-counting source discriminator defined by the frozen specification.
