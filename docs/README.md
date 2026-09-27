# Project documentation

This directory contains the authoritative product, business, architecture, operational and implementation-control specifications for Sushi81 POS.

## Current status

**Phase 6 implementation active: M01–M13 are Passed/merged. Current authoritative `main` is M13 merge commit `6ff2e04ce17e34addf58cf6dcfa756d4b7fae8aa`. M12 real populated-archive operational verification remains DEFERRED-NOT-M13. M14 PreProd Foundation and immutable release promotion is owner-approved/frozen on 2026-09-27 and implementation is package-gated on `codex/m14-preprod-foundation-authorized`.**

M12 final pre-merge head `49a0fe69e23e68c5591ef36ba5c56ef09a9d88b3` passed exact-head CI #862 / run `36021889765` with 881/881 tests and Release build 0 warnings/errors. Post-merge CI #863 / run `36023054757` build-and-test also succeeded.

M13 includes the Approved Gestion export ledger retention/compaction amendment, full FR/zh-CN localization completion, self-contained win-x64 packaging, per-user Inno Setup installer, upgrade/reinstall data preservation, diagnostics/performance/repository-security hardening, final production-target regression, release provenance and the user operating guide.

Owner acceptance covers A–G, the protected in-app reset, guide-led B setup, normal A→B production cutover and B catalogue VAT repair. B is authoritative/writable; A is read-only. Genuine Excel-percent formatting and only the approved plain-number compatibility values `0.055→5.5`, `0.1→10`, `0.2→20` normalize to canonical VAT points; other plain fractions remain literal. The owner committed and visually checked B's 5.5/10/20 values, then saw zero changes on a second export/reimport preview. M12's real populated-archive verification remains deferred under its existing owner waiver. A docs-only CI artifact after this reconciliation does not replace the accepted production candidate.

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
- `storage-strategy.md` — Approved — Phase 3 baseline, including later approved GitHub handoff, M07 self-join/DR decisions and the M12 local-archive amendment.

A separate `sync-and-backup.md` is not part of V1 because live storage, local recovery, GitHub target-directed normal handoff, OneDrive Disaster Recovery behavior and local annual archive behavior are already authoritative in the applicable baseline/decision documents.

### Phase 4 — Input, printing and export specifications

- `paste-order-import.md` — Approved — Phase 4 baseline, amended 2026-09-14 for M09 product-block/operator-resolution/source-reference semantics.
- `printing.md` — Approved — Phase 4 baseline.
- `export.md` — Approved — Phase 4 baseline, amended 2026-09-20 for M11 lifecycle/SettlementDate/correction clarification.

M08 owner-selected visual/identity detail is frozen in `decisions/m08-print-layout-and-receipt-identity.md` and the matching implementation-contract addendum.

### Phase 5 — V1 specification freeze and acceptance amendments

- `acceptance-criteria.md` — Approved — Phase 5 baseline (V1 Specification).
- `acceptance-criteria-amendment-filtered-catalogue-bulk-activation.md` — Approved amendment adding AC-CAT-013.
- `acceptance-criteria-amendment-m07-self-join-disaster-recovery.md` — Approved M07 amendment.
- `acceptance-criteria-amendment-m09-hiboutik-paste-fallback.md` — Approved 2026-09-14 M09 acceptance amendment.
- `acceptance-criteria-amendment-post-m09-hiboutik-daily-payment-dashboard.md` — Approved post-M09 dashboard amendment.
- `acceptance-criteria-amendment-m10-category-short-code-workbook.md` — Approved 2026-09-17 clarification of AC-CAT-008 through AC-CAT-011 for Category short-code workbook behavior.
- `acceptance-criteria-amendment-m11-gestion-export.md` — Approved 2026-09-20 M11 export clarification.
- `acceptance-criteria-amendment-m12-local-archive.md` — Approved 2026-09-21 M12 local archive/user-selected export amendment.
- `acceptance-criteria-amendment-m14-preprod-foundation.md` — Approved 2026-09-27 M14 PreProd environment/release acceptance amendment.
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
- post-M09 dashboard — Passed/merged through PR #19;
- M10 — Passed/merged through PR #22;
- M11 — Passed/merged through PR #24 at `1a94f3400e0aa9fe9f878bbe98a8285112206ba9`.

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

M10 completed implementation package:

- `decisions/m10-category-short-code-workbook-semantics.md` — Approved owner decision;
- `acceptance-criteria-amendment-m10-category-short-code-workbook.md` — Approved acceptance clarification;
- `implementation/milestone-10-preparation-readiness.md` — readiness complete / ready for separate authorization;
- `implementation/milestone-10-catalogue-xlsx.md` — frozen implementation contract with WP5 evidence/candidate regeneration pending controller review;
- `implementation/milestone-10-final-manual-acceptance.md` — CANDIDATE PREPARED / OWNER NOT YET EXECUTED;
- `implementation/milestone-10-worklog.md` — implementation/evidence ledger;
- `implementation/milestone-10-authorization.md` — AUTHORIZED; the current executable scope is controlled by Issue #4.

M11 completed implementation package:

- `decisions/m11-export-lifecycle-and-settlement-clarifications.md` — Approved owner decision;
- `acceptance-criteria-amendment-m11-gestion-export.md` — Approved acceptance clarification;
- amended `export.md`;
- `implementation/milestone-11-preparation-readiness.md` — PASS / ready for separate authorization;
- `implementation/milestone-11-gestion-export.md` — prepared implementation contract;
- `implementation/milestone-11-final-manual-acceptance.md` — prepared owner checklist;
- `implementation/milestone-11-worklog.md` — preparation/evidence ledger;
- `implementation/milestone-11-authorization.md` — AUTHORIZED; executable scope is controlled by Issue #4;
- draft preparation PR #23 / `prep/m11-gestion-export` — historical preparation line;
- implementation PR #24 / `codex/m11-gestion-export-authorized` — historical implementation mailbox, CLOSED/MERGED.

M12 completed implementation package:

- `decisions/m12-local-archive-and-user-selected-export.md` — Approved M12 archive storage/export decision;
- `acceptance-criteria-amendment-m12-local-archive.md` — matching Approved acceptance amendment;
- `implementation/milestone-12-preparation-readiness.md`;
- `implementation/milestone-12-annual-archive-historical-access.md`;
- `implementation/milestone-12-authorization.md`;
- `implementation/milestone-12-worklog.md`;
- `implementation/milestone-12-final-manual-acceptance.md`;
- PR #25 / `codex/m12-annual-archive-authorized` — historical mailbox, CLOSED/MERGED;
- deferred real populated-archive operational verification remains explicit under owner waiver and is not claimed Passed.

M13 completed control package:

- `decisions/m13-gestion-export-ledger-retention-compaction.md` — Approved M13 retention/compaction decision;
- `acceptance-criteria-amendment-m13-gestion-export-retention.md` — matching Approved acceptance amendment;
- `implementation/milestone-13-preparation-readiness.md`;
- `implementation/milestone-13-installer-localization-final-acceptance.md`;
- `implementation/milestone-13-authorization.md`;
- `implementation/milestone-13-worklog.md`;
- `implementation/milestone-13-final-manual-acceptance.md`;
- [Simplified Chinese operator guide](operating-guide.zh-CN.md) — primary practical manual for Chinese-speaking staff; start with the new-PC setup.
- [English operating guide](operating-guide.md) — equivalent English reference.
- branch `codex/m13-installer-final-acceptance-authorized`;
- Draft PR #26 — active M13 durable mailbox;
- Issue #4 — sole execution gate and active-PR pointer; it was OPEN only for this final documentation handoff. Normal post-package expectation is no active implementation gate until a new authorized task.

## Decision records

Approved materially constraining decisions live under `decisions/` and supplement/amend the baseline documents.

Relevant later decisions include:

- `decisions/filtered-catalogue-bulk-activation.md`;
- M04/M05 implementation clarification decisions;
- `decisions/target-directed-authority-handoff.md` and `decisions/github-handoff-transport.md`;
- M07 self-join/DR/recovery-ordering decisions;
- `decisions/m08-print-layout-and-receipt-identity.md`;
- `decisions/m09-hiboutik-paste-operator-workflow-and-source-reference.md`;
- `decisions/m10-category-short-code-workbook-semantics.md`;
- `decisions/m11-export-lifecycle-and-settlement-clarifications.md`;
- `decisions/m12-local-archive-and-user-selected-export.md`;
- `decisions/m13-gestion-export-ledger-retention-compaction.md`.

Where a later Approved decision explicitly supersedes a narrow older clause, the later decision controls until the next complete consolidation pass. A genuine unresolved contradiction still requires implementation to stop rather than guess.

## M14 current control package

- `decisions/m14-preprod-environment-isolation-and-promotion.md` — Approved deployment/storage/release decision;
- `acceptance-criteria-amendment-m14-preprod-foundation.md` — Approved acceptance amendment;
- `implementation/milestone-14-preparation-readiness.md` — readiness frozen;
- `implementation/milestone-14-preprod-foundation.md` — owner-authorized implementation contract;
- `implementation/milestone-14-final-manual-acceptance.md` — prepared owner checklist;
- `implementation/milestone-14-worklog.md` — package/evidence ledger;
- `implementation/milestone-14-authorization.md` — owner authorization.

M14 does not include post-launch business bug fixes. Those begin only after the isolated PreProd foundation is accepted.

## Implementation authority rule

Codex and other implementation agents must use current GitHub specification/control records as the source of truth rather than prior chat memory or former Excel/VBA behavior.

Before an implementation task, read `implementation-plan.md`, `implementation-status.md`, the detailed explicitly assigned milestone files, `AGENTS.md`, and all frozen/amended specification/decision sources referenced by that milestone.

If a genuine product/business/architecture/data contradiction or missing material decision appears during implementation, stop the affected path for specification resolution rather than silently guessing.

Pure implementation details that preserve the frozen behavior may be selected autonomously according to:

**reliability > simplicity > maintainability > operational clarity > novelty.**

Phase 6 approval does not authorize all milestones at once. Issue #4 is the master execution gate; while CLOSED, Codex makes no project changes.
