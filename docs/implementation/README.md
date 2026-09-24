# Implementation contracts

Milestone implementation contracts under this directory define controlled Phase 6 delivery scope.

All implementation handoffs inherit:

- `agent-execution-contract.md`;
- `interactive-quality-gate.md`;
- `control-state-preservation.md`;
- `post-task-power-policy.md`.

These files govern execution mechanics/quality and do not themselves authorize implementation or change frozen business/data semantics.

## Current milestone state

M01 through M09 are Passed and merged. The independent post-M09 Hiboutik daily CB/Espèce dashboard enhancement is also Passed and merged through PR #19 at `861cfba1dfacbb3289395c0370f6d42765b6c223`.

Key recent baselines:

- M07 — PR #13 merged at `9ea7d5e15bceba6932cb2caba50d0afb64ca1ff9`;
- M08 — PR #14 merged at `8f246ce7fb32baa33e1dfe1d334175bf2df60c1f`;
- M09 — PR #17 merged at `d840066d8d2ffa1856c4fcd88dbfdd3c8f2a1be5`;
- post-M09 dashboard — PR #19 merged at `861cfba1dfacbb3289395c0370f6d42765b6c223`.

Historical contracts/worklogs/manual-acceptance records remain authoritative in their existing files and PRs. Do not rewrite historical evidence merely to match later project state.

## Current M11/M12/M13 state

M11 Gestion intermediate export is Passed/merged through PR #24 at `1a94f3400e0aa9fe9f878bbe98a8285112206ba9`. Accepted runtime candidate `77ccecf9d947462e96e74b8aa1d99ced30e3788e`; controller closure comment `5762784303`; post-merge CI #821 / run `35617254203` succeeded with 831/831 passed, 0 failed, 0 skipped and Release build 0 warnings / 0 errors.

M12 Annual archive and historical access is controller-accepted and merged through PR #25 at `f59663c6b47ab21114c24360544e4e25094f4722`. Final pre-merge head `49a0fe69e23e68c5591ef36ba5c56ef09a9d88b3`; exact-head CI #862 / run `36021889765` succeeded with 881/881, 0 failed, 0 skipped and Release build 0 warnings / 0 errors; post-merge CI #863 / run `36023054757` build-and-test succeeded. Remaining real populated historical-archive checks are explicitly deferred under the owner waiver and are not claimed Passed. The first safe real verification point remains on or after 2027-02-01 for real 2026 rows.

M13 is owner-authorized on `codex/m13-installer-final-acceptance-authorized` / Draft PR #26. The owner explicitly confirmed the source repository's public visibility is intentional and not a blocker. WP1 Gestion export retention/compaction core is the first executable package; execution still requires the exact READY and OPEN Issue #4.

## Historical M10 preparation — Catalogue `.xlsx` import/export

M10 preparation is complete and is **READY FOR PROJECT-OWNER IMPLEMENTATION AUTHORIZATION**, but implementation remains **NOT AUTHORIZED**.

Prepared control package:

- `milestone-10-preparation-readiness.md` — final readiness audit;
- `milestone-10-catalogue-xlsx.md` — prepared non-executable implementation contract;
- `milestone-10-final-manual-acceptance.md` — prepared owner Windows/WPF + real Excel acceptance checklist;
- `milestone-10-worklog.md` — preparation/evidence ledger;
- `milestone-10-authorization.md` — **NOT AUTHORIZED**;
- `../decisions/m10-category-short-code-workbook-semantics.md` — owner-approved M10 Category short-code workbook decision;
- `../acceptance-criteria-amendment-m10-category-short-code-workbook.md` — matching acceptance clarification.

The project owner approved the Category short-code workbook semantics on 2026-09-17. That specification decision closes the last material M10 readiness gap but **does not authorize implementation**.

Historical execution state at that preparation point:

- Issue #4: CLOSED;
- executable M10 `CODEX_HANDOFF_READY`: none;
- production M10 implementation: not started;
- ClosedXML production dependency: not yet added;
- M11/M12/M13: unauthorized.

A separate explicit project-owner statement such as **“批准 M10 implementation”** is still required before the controller may mark M10 Authorized. Only after that may the dedicated implementation branch/PR/mailbox be established, one complete handoff be published, Issue #4 be updated to that unique pointer, and the gate be opened.

## M12 completed package

- `milestone-12-preparation-readiness.md`;
- `milestone-12-annual-archive-historical-access.md`;
- `milestone-12-authorization.md`;
- `milestone-12-worklog.md`;
- `milestone-12-final-manual-acceptance.md`;
- PR #25 — historical durable mailbox, CLOSED/MERGED.

M12 is merged under the explicit owner waiver. The deferred real populated-archive operational verification remains visible and must not be represented as Passed.

## M13 current package

- `../decisions/m13-gestion-export-ledger-retention-compaction.md` — Approved owner decision;
- `../acceptance-criteria-amendment-m13-gestion-export-retention.md` — matching Approved acceptance amendment;
- `milestone-13-preparation-readiness.md` — controller readiness;
- `milestone-13-installer-localization-final-acceptance.md` — WP1–WP6 implementation contract;
- `milestone-13-authorization.md` — OWNER-AUTHORIZED; execution not enabled;
- `milestone-13-worklog.md` — append-only evidence ledger;
- `milestone-13-final-manual-acceptance.md` — prepared final V1 owner checklist;
- branch `codex/m13-installer-final-acceptance-authorized`;
- Draft PR #26 — active M13 durable mailbox;
- Issue #4 — sole execution switch/status pointer, currently CLOSED.

First intended executable scope after repository privacy is resolved: WP1 Gestion export retention/compaction core only.

## Governance reminder

Issue #4 is the master execution switch and active-mailbox pointer.

- OPEN = Codex may execute only the single matching valid handoff.
- CLOSED = Codex makes no project changes.

Preparation/readiness, a Passed previous milestone, controller acceptance, `CODEX_DONE`, owner manual acceptance or appearance in the implementation plan never authorize implementation, the next work package or merge by themselves.

Merge always requires separate explicit project-owner approval.

`POST_TASK_POWER_ACTION` defaults to `NONE` unless a particular authorized handoff explicitly states otherwise.
