# Project documentation

This directory contains the authoritative product, business, architecture, operational and implementation-control specifications for Sushi81 POS.

## Current status

**Phase 6 implementation active: M01–M06 Passed/merged; M07 — Pairing, target-directed formal handoff and disaster recovery — is in implementation preparation after project-owner approval of its material product/safety decisions, but production implementation is NOT yet authorized.**

M04 merged through PR #6 at merge commit `ab218263bd4eee9c1be203d36acc552988cef43a` after complete Windows/WPF manual acceptance.

M05 — Lifecycle, payments, search and operational dashboard — Passed implementation and project-owner Windows/WPF acceptance and was merged through PR #10 to `main` at merge commit `79499d7c6ed65a74f524097c1507ca648dc151c3`. Accepted production-code head: `84c1c534c1df105ccb1839cbc6dfc9e0e055bb70`; final docs head: `217d187dd3ef5498c11f21bc516eccc6737fa952`. M05 is no longer open work.

M06 — Local recovery and authoritative/read-only enforcement — Passed implementation and project-owner Windows/WPF acceptance and was merged through PR #11 to `main` at merge commit `2c5eb52740d0c12e3e837579ecceac6d0600b59e`. Accepted M06 production repair head: `4a0c1ca9e44a6c48899e6ef8dc211172371e4d20`; final M06 documentation/PR head: `86326d81551aa4cb5cdcbc6826b8c740317b34c4`; Release tests: 364/364 Passed.

M07 material decisions were approved by the project owner on 2026-09-07 and are recorded in `decisions/m07-self-join-disaster-recovery.md` plus `acceptance-criteria-amendment-m07-self-join-disaster-recovery.md`. The prepared implementation contract/worklog/manual checklist remain explicitly non-authorizing.

M07 has no active implementation PR, branch, durable implementation authorization or executable handoff. Codex execution gate issue #4 remains CLOSED. M08 and later milestones are not started.

The V1 Specification remains frozen-and-amended. The formal freeze record is `v1-specification-freeze.md`; the primary implementation acceptance contract is `acceptance-criteria.md` together with approved acceptance amendments; the approved implementation sequence is `implementation-plan.md`.

## V1 documentation baseline

### Phase 1 — Current system and product scope

- `current-system.md` — Approved — Phase 1 baseline; historical/current-system reference, not a target-behavior override.
- `product-requirements.md` — Approved — Phase 1 baseline. NFR-004 is clarified by the Approved M07 decision: production recoverability means local recovery + GitHub target-directed normal handoff + OneDrive disaster recovery + annual archive.

### Phase 2 — Business model

- `order-lifecycle.md` — Approved — Phase 2 baseline.
- `business-rules.md` — Approved — Phase 2 baseline.
- `catalogue-management.md` — Approved — Phase 2 baseline, amended 2026-08-30 for filtered bulk activation/deactivation.

### Phase 3 — Core technical architecture

- `architecture.md` — Approved — Phase 3 baseline.
- `data-model.md` — Approved — Phase 3 baseline.
- `storage-strategy.md` — Approved — Phase 3 baseline, amended by later Approved GitHub handoff and M07 self-join/DR decisions. Older OneDrive `Handoff` wording has no normal-authority semantics.

A separate `sync-and-backup.md` is not part of V1 because live storage, local recovery, GitHub target-directed normal handoff, OneDrive disaster-recovery/archive behavior and annual archive behavior are already authoritative in the applicable baseline/decision documents.

### Phase 4 — Input, printing and export specifications

- `paste-order-import.md` — Approved — Phase 4 baseline.
- `printing.md` — Approved — Phase 4 baseline.
- `export.md` — Approved — Phase 4 baseline.

### Phase 5 — V1 specification freeze

- `acceptance-criteria.md` — Approved — Phase 5 baseline (V1 Specification).
- `acceptance-criteria-amendment-filtered-catalogue-bulk-activation.md` — Approved 2026-08-30 V1 acceptance amendment adding AC-CAT-013 until the next consolidated acceptance rewrite.
- `acceptance-criteria-amendment-m07-self-join-disaster-recovery.md` — Approved 2026-09-07 amendment clarifying self-join, local-first/online-only coordination, operational DR fencing and safe recovery candidates.
- `v1-specification-freeze.md` — Approved — Phase 5 baseline, amended through approved post-freeze decisions.
- final repo-wide consistency review — complete for the frozen baseline; later Approved amendment records control where they explicitly supersede older wording.

### Phase 6 — Implementation planning and controlled execution

- `implementation-plan.md` — Approved — Phase 6 baseline; ordered implementation milestones and gates.
- `implementation-status.md` — living current acceptance/milestone control record. Historical M01–M06 implementation-status content is preserved byte-for-byte under `implementation/archive/implementation-status-through-m06-2026-09-07.md`.
- `implementation/milestone-01-foundation.md` — M01 contract; Passed/merged.
- `implementation/milestone-02-github-transport-revalidation.md` — M02 contract; Passed/merged.
- `implementation/milestone-03-catalogue-settings.md` and related extension/worklog — M03 historical implementation records; Passed/merged through PR #5.
- `implementation/milestone-04-order-entry.md`, authorization/worklog/final manual acceptance — M04 historical implementation records; Passed/merged through PR #6.
- `implementation/milestone-05-lifecycle-payments-search-dashboard.md`, authorization/worklog/final manual acceptance — M05 historical implementation/evidence records; Passed/merged through PR #10.
- `implementation/milestone-06-local-recovery-read-only-enforcement.md` — historical approved M06 implementation contract; Passed/merged through PR #11.
- `implementation/milestone-06-authorization.md` — historical durable M06 authorization.
- `implementation/milestone-06-worklog.md` — historical M06 execution/evidence record.
- `implementation/milestone-06-final-manual-acceptance.md` — Passed project-owner Windows/WPF acceptance record.
- `implementation/milestone-07-preauthorization-design-review.md` — historical M07 design analysis; its pairing-approval proposal is superseded by the Approved 2026-09-07 owner decision.
- `implementation/milestone-07-pairing-handoff-disaster-recovery.md` — prepared detailed M07 implementation contract; **NOT YET AUTHORIZED**.
- `implementation/milestone-07-worklog.md` — prepared M07 execution/evidence log; no implementation entries yet.
- `implementation/milestone-07-final-manual-acceptance.md` — prepared project-owner Windows/WPF acceptance checklist; not executed/not Passed.

Normal target-directed handoff uses the configured dedicated private GitHub repository (`sushi81-pos-handoff` conceptually), one long-lived Release and immutable snapshot/grant assets. OneDrive is used for non-authority System/device metadata, recovery-only checkpoints and later annual archives. Real self-join/pairing, normal handoff/target acquisition and disaster recovery belong to M07.

Phase 6 approval does not authorize all milestones at once. Codex must implement only the milestone/task explicitly assigned in the current durable handoff.

## Decision records

Approved materially constraining decisions live under `decisions/`.

Those records supplement the baseline documents. Post-freeze amendments are folded into affected baselines or accompanied by explicit approved decision/acceptance records so implementation does not need to choose between contradictory requirements.

Relevant recent amendments include:

- `decisions/filtered-catalogue-bulk-activation.md` — 2026-08-30 M03 catalogue amendment;
- `decisions/m04-order-entry-pricing-clarifications.md` and `decisions/m04-order-entry-operator-ergonomics-amendment.md` — approved M04 amendments;
- `decisions/m05-lifecycle-payment-modification-clarifications.md` and later approved M05 manual-acceptance clarifications — M05 lifecycle/search/layout/payment-date/UX amendments;
- `decisions/m07-self-join-disaster-recovery.md` — Approved 2026-09-07 M07 material decision: new devices may self-join without old-source approval but self-join never grants authority; genuine recovery uses operationally fenced, online, single-winner generation advancement and the freshest validated safe candidate.

## Implementation authority rule

Codex and other implementation agents must use the approved GitHub specification as the source of truth rather than prior chat memory or former Excel/VBA behavior.

Before an implementation task, read `implementation-plan.md`, `implementation-status.md` and the detailed file for the explicitly assigned milestone/task under `implementation/`, in addition to `AGENTS.md` and the frozen/amended specification sources referenced by that task.

If a genuine product/business/architecture/data contradiction or missing material decision appears during implementation, the affected path must be surfaced for specification resolution rather than silently guessed.

Pure implementation details that preserve the frozen behavior may be selected autonomously according to:

**reliability > simplicity > maintainability > operational clarity > novelty.**
