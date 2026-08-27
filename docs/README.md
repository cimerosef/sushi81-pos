# Project documentation

This directory contains the authoritative product, business, architecture and operational specifications for Sushi81 POS.

## Current status

**V1 Specification frozen — Phase 5 complete (2026-08-27).**

The formal freeze record is `v1-specification-freeze.md`. The implementation acceptance contract is `acceptance-criteria.md`.

Production application code has not been started by the specification-freeze work itself. The repository is now ready for explicit Codex implementation tasks against the frozen V1 baseline.

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

## Decision records

Approved materially constraining decisions live under `decisions/`.

Those records supplement the baseline documents. Phase 5 folded superseded behavior into the affected baselines so implementation should not need to choose between contradictory historical requirements.

## Implementation authority rule

Codex and other implementation agents must use the approved GitHub specification as the source of truth rather than prior chat memory or former Excel/VBA behavior.

If a genuine product/business/architecture/data contradiction or missing material decision appears during implementation, the affected path must be surfaced for specification resolution rather than silently guessed.

Pure implementation details that preserve the frozen behavior may be selected autonomously according to:

**reliability > simplicity > maintainability > operational clarity > novelty.**
