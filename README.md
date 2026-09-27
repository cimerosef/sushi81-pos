# Sushi81 POS

Repository for the design and implementation of a Windows local point-of-sale and order-management application for Sushi 81.

## Project status

**V1 Specification frozen — Phase 5 complete (2026-08-27), with approved post-freeze amendments. Phase 6 implementation is active.**

The approved V1 product, business, architecture, data, storage, paste-import, printing, export and acceptance specifications are frozen/amended in `docs/`.

Formal freeze record: `docs/v1-specification-freeze.md`  
Implementation acceptance contract: `docs/acceptance-criteria.md` plus approved acceptance amendments.

M01 through M13 are **Passed and merged**.

Recent final baselines:

- M10 — Catalogue `.xlsx` import/export — Passed/merged through PR #22 at `299df8b44a1959497ad46f861e44db32913b4d11`;
- M11 — Gestion intermediate export — Passed/merged through PR #24 at `1a94f3400e0aa9fe9f878bbe98a8285112206ba9`;
- M12 — Annual archive and historical access — controller-accepted and merged through PR #25 at `f59663c6b47ab21114c24360544e4e25094f4722` under the explicit owner waiver. Final pre-merge head `49a0fe69e23e68c5591ef36ba5c56ef09a9d88b3` passed CI #862 / run `36021889765` with 881/881 tests and Release build 0 warnings/errors; post-merge CI #863 / run `36023054757` build-and-test succeeded.

M12's remaining real populated-archive operational verification is **deferred, not Passed**. The first safe real verification point remains an authoritative startup on or after 2027-02-01 with real 2026 data; archive failure must continue to leave eligible live rows safe/retryable.

M13 — Installer, localization completion and final V1 acceptance — is **Passed and merged** through PR #26. Current authoritative `main` is merge commit `6ff2e04ce17e34addf58cf6dcfa756d4b7fae8aa`. The accepted production application candidate remains source `469c8761061b0ecf488e06386a97b9e10652a916`, exact-head CI #912 / run `36253271385` (932/932 tests), installer artifact `10909188508` / `Sushi81-POS-production-installer-win-x64-1.0.0-469c876`. B is authoritative/writable and A is paired read-only.

M14 — PreProd Foundation and immutable release promotion — was owner-approved and frozen on 2026-09-27. It establishes permanent Prod/PreProd isolation, a one-time safe production-data seed into a pristine PreProd profile, independent PreProd GitHub/OneDrive/credential infrastructure, coexisting installers, immutable GitHub candidates, and production promotion from the exact owner-accepted application payload without rebuilding it. M14 implementation is package-gated; post-launch business bug fixes are explicitly outside M14.

Accepted M13 package heads and exact-head CI:

| Package | Accepted head | Exact-head CI |
|---|---|---|
| WP1 — Gestion export retention/compaction | `a3d3110b59f436bc0a9107777ea97f73df85119d` | #872 / `36057354475` — success |
| WP2 — retention runtime/history | `97d0a6e048aedca66b1f93e9768285f49f04ac63` | #873 / `36063106606` — success |
| WP3 — localization completion | `e40a1f882d4c0557fc0fe4e30cb88395be290c89` | #874 / `36106655435` — success |
| WP4 — production publish/installer | `0f73cee0311a1c76e8b4cafd7413720c252be27c` | #880 / `36114601424` — success; artifact `10854348679` |
| WP5 — diagnostics/performance/security hardening | `aa734cc94bb6500be42cbaa00407e8fc8ec7a1fd` | #882 / `36124050769` — success; artifact `10859146322` |

M12's populated real-archive operational verification remains **DEFERRED-NOT-M13, not Passed**; its first safe real verification point remains an authoritative startup on or after 2027-02-01 with real 2026 data. M13's accepted VAT import semantics normalize genuine Excel percentage-formatted cells and only the approved plain-numeric legacy values `0.055→5.5`, `0.1→10`, `0.2→20`; unrelated plain fractions remain literal. Owner evidence on B confirms committed 5.5/10/20 values and a second export/reimport preview with zero changes. A docs-only CI artifact after this reconciliation does not replace the accepted production application candidate above.

Controlling M09 records:

- `docs/decisions/m09-hiboutik-paste-operator-workflow-and-source-reference.md`;
- `docs/acceptance-criteria-amendment-m09-hiboutik-paste-fallback.md`;
- `docs/paste-order-import.md`;
- `docs/implementation/milestone-09-preparation-readiness.md`;
- `docs/implementation/milestone-09-hiboutik-paste-fallback.md`;
- `docs/implementation/milestone-09-final-manual-acceptance.md`;
- `docs/implementation/milestone-09-authorization.md`.

Controlling post-M09 dashboard records:

- `docs/decisions/post-m09-hiboutik-daily-payment-dashboard.md`;
- `docs/acceptance-criteria-amendment-post-m09-hiboutik-daily-payment-dashboard.md`;
- `docs/implementation/post-m09-hiboutik-daily-payment-dashboard-authorization.md`;
- `docs/implementation/post-m09-hiboutik-daily-payment-dashboard.md`;
- `docs/implementation/post-m09-hiboutik-daily-payment-dashboard-worklog.md`.

M10 completed control package:

- `docs/decisions/m10-category-short-code-workbook-semantics.md`;
- `docs/acceptance-criteria-amendment-m10-category-short-code-workbook.md`;
- `docs/implementation/milestone-10-preparation-readiness.md`;
- `docs/implementation/milestone-10-catalogue-xlsx.md`;
- `docs/implementation/milestone-10-final-manual-acceptance.md`;
- `docs/implementation/milestone-10-worklog.md`;
- `docs/implementation/milestone-10-authorization.md` — AUTHORIZED; execution remains limited to the exact Issue #4 handoff.

M11 completed control package:

- `docs/decisions/m11-export-lifecycle-and-settlement-clarifications.md`;
- `docs/acceptance-criteria-amendment-m11-gestion-export.md`;
- amended `docs/export.md`;
- `docs/implementation/milestone-11-preparation-readiness.md`;
- `docs/implementation/milestone-11-gestion-export.md`;
- `docs/implementation/milestone-11-final-manual-acceptance.md`;
- `docs/implementation/milestone-11-worklog.md`;
- `docs/implementation/milestone-11-authorization.md` — AUTHORIZED; execution remains limited to the exact Issue #4 handoff.

M12 completed control package:

- `docs/decisions/m12-local-archive-and-user-selected-export.md`;
- `docs/acceptance-criteria-amendment-m12-local-archive.md`;
- `docs/implementation/milestone-12-preparation-readiness.md`;
- `docs/implementation/milestone-12-annual-archive-historical-access.md`;
- `docs/implementation/milestone-12-final-manual-acceptance.md`;
- `docs/implementation/milestone-12-worklog.md`;
- `docs/implementation/milestone-12-authorization.md`;
- PR #25 — historical mailbox, CLOSED/MERGED.

M13 completed control package:

- `docs/decisions/m13-gestion-export-ledger-retention-compaction.md`;
- `docs/acceptance-criteria-amendment-m13-gestion-export-retention.md`;
- `docs/implementation/milestone-13-preparation-readiness.md`;
- `docs/implementation/milestone-13-installer-localization-final-acceptance.md`;
- `docs/implementation/milestone-13-final-manual-acceptance.md`;
- `docs/operating-guide.md` — final-candidate day-to-day operator guide;
- `docs/implementation/milestone-13-worklog.md`;
- `docs/implementation/milestone-13-authorization.md`.

M14 current control package:

- `docs/decisions/m14-preprod-environment-isolation-and-promotion.md` — Approved owner decision;
- `docs/acceptance-criteria-amendment-m14-preprod-foundation.md` — Approved acceptance amendment;
- `docs/implementation/milestone-14-preparation-readiness.md` — READY after execution-gate reopening;
- `docs/implementation/milestone-14-preprod-foundation.md` — owner-authorized implementation contract;
- `docs/implementation/milestone-14-final-manual-acceptance.md` — owner acceptance checklist.

Normal target-directed authority handoff uses a dedicated private GitHub Release Asset repository and strict server receipts. OneDrive remains the approved Disaster Recovery/historical diagnostic boundary, not the normal authority gate; canonical annual archives are permanent application-managed local data after the M12 amendment.

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
│   ├── implementation/
│   ├── acceptance-criteria.md
│   ├── v1-specification-freeze.md
│   └── ...
├── samples/
│   └── pasted-orders/
├── src/
└── tests/
```

See `docs/README.md` for the frozen/amended V1 documentation baseline and authority rules.
