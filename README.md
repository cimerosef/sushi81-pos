# Sushi81 POS

Repository for the design and implementation of a Windows local point-of-sale and order-management application for Sushi 81.

## Project status

**V1 Specification frozen — Phase 5 complete (2026-08-27), with approved post-freeze amendments. Phase 6 implementation is active.**

The approved V1 product, business, architecture, data, storage, paste-import, printing, export and acceptance specifications are frozen/amended in `docs/`.

Formal freeze record: `docs/v1-specification-freeze.md`  
Implementation acceptance contract: `docs/acceptance-criteria.md` plus approved acceptance amendments.

M01 through M09 are **Passed and merged**.

Recent merge baselines:

- M05 — Lifecycle, payments, search and operational dashboard — merged through PR #10 at `79499d7c6ed65a74f524097c1507ca648dc151c3`;
- M06 — Local recovery and authoritative/read-only enforcement — merged through PR #11 at `2c5eb52740d0c12e3e837579ecceac6d0600b59e`;
- M07 — Pairing, target-directed handoff and disaster recovery — merged through PR #13 at `9ea7d5e15bceba6932cb2caba50d0afb64ca1ff9`;
- M08 — Printing and reprinting — merged through PR #14 at `8f246ce7fb32baa33e1dfe1d334175bf2df60c1f`;
- M09 — Hiboutik paste-order fallback — merged through PR #17 at `d840066d8d2ffa1856c4fcd88dbfdd3c8f2a1be5`;
- Post-M09 — Hiboutik daily CB/Espèce dashboard — Passed/merged through PR #19 at `861cfba1dfacbb3289395c0370f6d42765b6c223`.

The accepted M09 production candidate remains `7d0144d452231fe92cf7c31e027e9b1bb6f5a43d`. The independent post-M09 dashboard accepted executable candidate remains sourced from checkout `dbab706a3f535b521a4a0fc67c318bf0c14b60bb`; PR #19 final closure head was `d55e36a244327c55b81f6fb1040c0ef46dbe154a` before merge.

M10 — Catalogue `.xlsx` import/export — is in active implementation on PR #22 (`codex/m10-catalogue-xlsx-authorized`). WP1–WP4 are controller-accepted; WP5 final automated hardening and exact owner-candidate preparation is owner-authorized under OPEN Issue #4, starting from `24da074827610325c0a27292d5a9856f91b7c666`. Owner Windows/Excel acceptance remains pending and owner-owned; M10 is not marked Passed. M11, M12 and M13 remain unauthorized.

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

Current M10 implementation package:

- `docs/decisions/m10-category-short-code-workbook-semantics.md`;
- `docs/acceptance-criteria-amendment-m10-category-short-code-workbook.md`;
- `docs/implementation/milestone-10-preparation-readiness.md`;
- `docs/implementation/milestone-10-catalogue-xlsx.md`;
- `docs/implementation/milestone-10-final-manual-acceptance.md`;
- `docs/implementation/milestone-10-worklog.md`;
- `docs/implementation/milestone-10-authorization.md` — AUTHORIZED; execution remains limited to the exact Issue #4 handoff.

Normal target-directed authority handoff uses a dedicated private GitHub Release Asset repository and strict server receipts. OneDrive remains recovery/archive storage and historical diagnostic transport rather than the normal authority gate.

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
