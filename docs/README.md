# Project documentation

This directory contains the authoritative product, business, architecture, operational and implementation-control specifications for Sushi81 POS.

## Current status

**Phase 6 implementation plan Approved (2026-08-27); production implementation not yet started.**

The V1 Specification remains frozen. The formal freeze record is `v1-specification-freeze.md`; the implementation acceptance contract is `acceptance-criteria.md`; the approved implementation sequence is `implementation-plan.md`.

## V1 documentation baseline

### Phase 1 — Current system and product scope

- `current-system.md` — Approved — Phase 1 baseline; historical/current-system reference, not a target-behavior override.
- `product-requirements.md` — Approved — Phase 1 baseline.

### Phase 2 — Business model

- `order-lifecycle.md` — Approved — Phase 2 baseline.
- `business-rules.md` — Approved — Phase 2 baseline.
- `catalogue-management.md` — Approved — Phase 2 baseline.

### Phase 3 — Core technical architecture

- `architecture.md` — Approved — Phase 3 baseline.
- `data-model.md` — Approved — Phase 3 baseline.
- `storage-strategy.md` — Approved — Phase 3 baseline.

A separate `sync-and-backup.md` is not part of V1 because live storage, local recovery, OneDrive handoff, disaster recovery and annual archive behavior are already authoritative in `storage-strategy.md`.

### Phase 4 — Input, printing and export specifications

- `paste-order-import.md` — Approved — Phase 4 baseline.
- `printing.md` — Approved — Phase 4 baseline.
- `export.md` — Approved — Phase 4 baseline.

### Phase 5 — V1 specification freeze

- `acceptance-criteria.md` — Approved — Phase 5 baseline (V1 Specification).
- `v1-specification-freeze.md` — Approved — Phase 5 baseline.
- final repo-wide consistency review — complete.

### Phase 6 — Implementation planning and controlled execution

- `implementation-plan.md` — Approved — Phase 6 baseline; ordered implementation milestones and gates.
- `implementation-status.md` — living acceptance/milestone traceability record.
- `implementation/milestone-01-foundation.md` — detailed M01 task contract; implementation not yet started.

Phase 6 approval does not authorize all milestones at once. Codex must implement only the milestone explicitly assigned in the current task.

## Decision records

Approved materially constraining decisions live under `decisions/`.

Those records supplement the baseline documents. Phase 5 folded superseded behavior into the affected baselines so implementation should not need to choose between contradictory historical requirements.

## Implementation authority rule

Codex and other implementation agents must use the approved GitHub specification as the source of truth rather than prior chat memory or former Excel/VBA behavior.

Before an implementation task, read `implementation-plan.md`, `implementation-status.md` and the detailed file for the explicitly assigned milestone under `implementation/`, in addition to `AGENTS.md` and the frozen specification sources referenced by that milestone.

If a genuine product/business/architecture/data contradiction or missing material decision appears during implementation, the affected path must be surfaced for specification resolution rather than silently guessed.

Pure implementation details that preserve the frozen behavior may be selected autonomously according to:

**reliability > simplicity > maintainability > operational clarity > novelty.**

