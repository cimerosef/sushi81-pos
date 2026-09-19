# Project documentation

This directory contains the authoritative product, business, architecture, operational and implementation-control specifications for Sushi81 POS.

## Current status

**Phase 6 implementation active: M01–M09 Passed/merged; the independent post-M09 Hiboutik daily CB/Espèce dashboard enhancement is Passed/merged; M10 WP1–WP5 are controller-accepted, owner Scenarios A/B passed, and Scenario C's narrow existing-Category short-code guidance remediation is active.**

M08 — Printing and reprinting — Passed project-owner Windows/WPF/physical-print acceptance and was merged through PR #14 to `main` at merge commit `8f246ce7fb32baa33e1dfe1d334175bf2df60c1f`. Accepted production candidate: `86d19cbc3aa127c836b1b13f91292ddcb54d08bd`; final pass-record head: `5f8c92c29116e17a3365ecd8801f7e13f107269c`; final exact-head CI #675 succeeded.

M09 implementation, automated evidence and owner Windows/WPF manual acceptance are complete and merged through PR #17 at `d840066d8d2ffa1856c4fcd88dbfdd3c8f2a1be5`. The accepted production candidate remains `7d0144d452231fe92cf7c31e027e9b1bb6f5a43d`, with accepted EXE/ZIP hashes recorded in `implementation/milestone-09-final-manual-acceptance.md`.

The independent post-M09 Hiboutik daily payment dashboard enhancement is Passed and merged through PR #19 at `861cfba1dfacbb3289395c0370f6d42765b6c223`. It adds exactly two passive read-only values to the existing top Caisse dashboard and does not alter ordinary POS-originated summaries. Automated evidence and owner Windows/WPF A–F acceptance are recorded in PR #19 comments `5705723428` and `5715414940`; final controller closure was accepted on head `d55e36a244327c55b81f6fb1040c0ef46dbe154a` before merge.

M10 — Catalogue `.xlsx` import/export — is on PR #22 (`codex/m10-catalogue-xlsx-authorized`). WP1–WP5 are controller-accepted; owner Scenarios A/B passed, while Scenario C correctly blocked an existing-Category short-code change but failed the frozen actionable preview-message requirement. The narrow diagnostic remediation is active; Scenario C must be owner-retested on its next candidate, D–L remain not run, PR #22 remains unmerged, and M10 is not marked Passed. Live Issue #4 controls later execution; M11+ remain unauthorized.

The V1 Specification remains frozen-and-amended. The formal freeze record is `v1-specification-freeze.md`; the primary implementation acceptance contract is `acceptance-criteria.md` together with approved acceptance amendments; the approved implementation sequence is `implementation-plan.md`; the living current control state is `implementation-status.md`.

## V1 documentation baseline

### Phase 1 — Current system and product scope

- `current-system.md` — Approved — Phase 1 baseline; historical/current-system reference, not a target-behavior override.
- `product-requirements.md` — Approved — Phase 1 baseline.

### Phase 2 — Business model

- `order-lifecycle.md` — Approved — Phase 2 baseline.
- `business-rules.md` — Approved — Phase 2 baseline.
- `catalogue-management.md` — Approved — Phase 2 baseline, amended for filtered bulk activation/deactivation, Category short-code business data and the 2026-09-17 M10 short-code workbook contract.

### Phase 3 — Core technical architecture

- `architecture.md` — Approved — Phase 3 baseline, including later approved handoff amendments.
- `data-model.md` — Approved — Phase 3 baseline, subject to later approved decision amendments where explicitly stated.
- `storage-strategy.md` — Approved — Phase 3 baseline, including later approved GitHub handoff and M07 self-join/DR decisions.

A separate `sync-and-backup.md` is not part of V1 because live storage, local recovery, GitHub target-directed normal handoff, OneDrive disaster-recovery/archive behavior and annual archive behavior are already authoritative in the applicable baseline/decision documents.

### Phase 4 — Input, printing and export specifications

- `paste-order-import.md` — Approved — Phase 4 baseline, amended 2026-09-14 for M09 product-block/operator-resolution/source-reference semantics.
- `printing.md` — Approved — Phase 4 baseline.
- `export.md` — Approved — Phase 4 baseline.

M08 owner-selected visual/identity detail is frozen in `decisions/m08-print-layout-and-receipt-identity.md` and the matching implementation-contract addendum.

### Phase 5 — V1 specification freeze and acceptance amendments

- `acceptance-criteria.md` — Approved — Phase 5 baseline (V1 Specification).
- `acceptance-criteria-amendment-filtered-catalogue-bulk-activation.md` — Approved amendment adding AC-CAT-013.
- `acceptance-criteria-amendment-m07-self-join-disaster-recovery.md` — Approved M07 amendment.
- `acceptance-criteria-amendment-m09-hiboutik-paste-fallback.md` — Approved 2026-09-14 M09 acceptance amendment.
- `acceptance-criteria-amendment-post-m09-hiboutik-daily-payment-dashboard.md` — Approved post-M09 dashboard amendment.
- `acceptance-criteria-amendment-m10-category-short-code-workbook.md` — Approved 2026-09-17 clarification of AC-CAT-008 through AC-CAT-011 for Category short-code workbook behavior.
- `v1-specification-freeze.md` — Approved — Phase 5 baseline, amended through approved post-freeze decisions.

### Phase 6 — Implementation planning and controlled execution

- `implementation-plan.md` — Approved — Phase 6 baseline; ordered milestones and gates.
- `implementation-status.md` — living current acceptance/milestone control record.
- `implementation/agent-execution-contract.md` — main-agent/subagent execution governance.
- `implementation/interactive-quality-gate.md` — mandatory WPF/user-visible quality gate.
- `implementation/control-state-preservation.md` — interactive-state invariant.
- `implementation/post-task-power-policy.md` — explicit one-shot host power policy.

Historical completed milestone records remain under `implementation/` and in their original PRs:

- M01 — Passed/merged through PR #1;
- M02 — Passed/merged after target-directed GitHub transport revalidation;
- M03 — Passed/merged through PR #5;
- M04 — Passed/merged through PR #6;
- M05 — Passed/merged through PR #10;
- M06 — Passed/merged through PR #11;
- M07 — Passed/merged through PR #13;
- M08 — Passed/merged through PR #14;
- M09 — Passed/merged through PR #17;
- post-M09 dashboard — Passed/merged through PR #19.

Current M09 control package:

- `decisions/m09-hiboutik-paste-operator-workflow-and-source-reference.md`;
- `acceptance-criteria-amendment-m09-hiboutik-paste-fallback.md`;
- `paste-order-import.md`;
- `implementation/milestone-09-preparation-readiness.md`;
- `implementation/milestone-09-hiboutik-paste-fallback.md`;
- `implementation/milestone-09-final-manual-acceptance.md`;
- `implementation/milestone-09-authorization.md`;
- `samples/pasted-orders/hiboutik-product-block-synthetic.txt`.

Post-M09 dashboard control package:

- `decisions/post-m09-hiboutik-daily-payment-dashboard.md`;
- `acceptance-criteria-amendment-post-m09-hiboutik-daily-payment-dashboard.md`;
- `implementation/post-m09-hiboutik-daily-payment-dashboard-authorization.md`;
- `implementation/post-m09-hiboutik-daily-payment-dashboard.md`;
- `implementation/post-m09-hiboutik-daily-payment-dashboard-manual-acceptance.md`;
- `implementation/post-m09-hiboutik-daily-payment-dashboard-worklog.md`.

Current M10 implementation package:

- `decisions/m10-category-short-code-workbook-semantics.md` — Approved owner decision;
- `acceptance-criteria-amendment-m10-category-short-code-workbook.md` — Approved acceptance clarification;
- `implementation/milestone-10-preparation-readiness.md` — readiness complete / ready for separate authorization;
- `implementation/milestone-10-catalogue-xlsx.md` — frozen implementation contract with WP5 evidence/candidate regeneration pending controller review;
- `implementation/milestone-10-final-manual-acceptance.md` — CANDIDATE PREPARED / OWNER NOT YET EXECUTED;
- `implementation/milestone-10-worklog.md` — implementation/evidence ledger;
- `implementation/milestone-10-authorization.md` — AUTHORIZED; the current executable scope is controlled by Issue #4.

## Decision records

Approved materially constraining decisions live under `decisions/` and supplement/amend the baseline documents.

Relevant later decisions include:

- `decisions/filtered-catalogue-bulk-activation.md`;
- M04/M05 implementation clarification decisions;
- `decisions/target-directed-authority-handoff.md` and `decisions/github-handoff-transport.md`;
- M07 self-join/DR/recovery-ordering decisions;
- `decisions/m08-print-layout-and-receipt-identity.md`;
- `decisions/m09-hiboutik-paste-operator-workflow-and-source-reference.md`;
- `decisions/m10-category-short-code-workbook-semantics.md`.

Where a later Approved decision explicitly supersedes a narrow older clause, the later decision controls until the next complete consolidation pass. A genuine unresolved contradiction still requires implementation to stop rather than guess.

## Implementation authority rule

Codex and other implementation agents must use current GitHub specification/control records as the source of truth rather than prior chat memory or former Excel/VBA behavior.

Before an implementation task, read `implementation-plan.md`, `implementation-status.md`, the detailed explicitly assigned milestone files, `AGENTS.md`, and all frozen/amended specification/decision sources referenced by that milestone.

If a genuine product/business/architecture/data contradiction or missing material decision appears during implementation, stop the affected path for specification resolution rather than silently guessing.

Pure implementation details that preserve the frozen behavior may be selected autonomously according to:

**reliability > simplicity > maintainability > operational clarity > novelty.**

Phase 6 approval does not authorize all milestones at once. Issue #4 is the master execution gate; while CLOSED, Codex makes no project changes.
