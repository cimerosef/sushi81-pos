# AGENTS.md

## Purpose

This repository is design-led. Implementation agents must execute approved specifications rather than invent product or business decisions.

## Current phase

The project is currently in product and architecture design. Unless an approved implementation task explicitly says otherwise, do not create production application code.

## Authoritative sources

When implementation begins, use the approved documents under `docs/` and recorded architecture decisions as the source of truth.

If code, assumptions, issue text, or prior discussion conflicts with an approved specification, stop and surface the conflict instead of silently choosing a behavior.

## Changes that require an explicit approved decision

Do not independently change or invent any of the following:

- core technology stack;
- database engine or core data model;
- order lifecycle or finalization semantics;
- payment model;
- discount and commercial business rules;
- synchronization or backup strategy;
- data retention or deletion behavior;
- printing architecture;
- import/export contracts;
- repository architecture;
- introduction of paid, proprietary, hosted, or subscription dependencies.

When such a question appears during implementation, document it as an open question and stop that affected decision path until it is resolved.

## Safe implementation autonomy

Within approved specifications, implementation agents may normally:

- implement assigned functionality;
- refactor local code without changing behavior;
- add or improve tests;
- improve error handling, diagnostics, and logging;
- fix clear defects;
- improve internal naming and maintainability;
- make minor UI layout adjustments that do not change the approved workflow.

## Data safety

Business data must be treated as durable and non-disposable.

- Never delete, overwrite, reset, or migrate real business data without an explicit approved operation and a recovery path.
- Schema migrations must be versioned and testable.
- Backward compatibility and backup/restore implications must be considered for database changes.
- Never commit real customer, order, payment, credential, or other sensitive production data to Git.

## Dependencies

Prefer free and open-source dependencies compatible with the approved architecture. Do not add a paid service or dependency that creates a recurring operational cost unless explicitly approved.

## Scope discipline

Implement the requested milestone only. Do not add speculative product features merely because they seem useful.

If a requested change exposes a product, business, or architecture ambiguity, surface the ambiguity rather than filling it with an assumption.
