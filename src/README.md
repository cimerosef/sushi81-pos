# Source code

Production source code lives under this directory.

**Current status:** M01–M09 are implemented, accepted and merged to `main`; the independent post-M09 Hiboutik daily CB/Espèce dashboard enhancement is also Passed/merged through PR #19 at `861cfba1dfacbb3289395c0370f6d42765b6c223`. M10 — Catalogue `.xlsx` import/export — has completed specification/readiness preparation on draft PR #20 but is **NOT implementation-authorized**. Issue #4 is CLOSED with no executable handoff. M11–M13 remain unauthorized.

The source tree is ready only for explicitly authorized Codex implementation work against:

- `../docs/v1-specification-freeze.md`;
- the approved baseline/amendment documents under `../docs/`;
- `../docs/acceptance-criteria.md` plus approved acceptance amendments;
- `../docs/implementation-status.md`;
- repository instructions in `../AGENTS.md`.

Do not implement speculative features or alter frozen/amended business/architecture/data semantics merely to simplify coding. If a genuine material specification ambiguity appears, surface it and resolve the specification before continuing that affected path.

Normal authority handoff transport is the dedicated private GitHub Release Asset API defined by `../docs/decisions/github-handoff-transport.md`; OneDrive remains recovery/archive storage rather than the normal authority gate.

Current merged production capabilities include:

- M03 Catalogue/settings maintenance;
- M04 order entry/cart/pricing/snapshots;
- M05 lifecycle/payments/search/dashboard;
- M06 persistent authority/read-only enforcement and local recovery;
- M07 pairing, target-directed handoff and disaster recovery;
- M08 printing/reprinting;
- M09 Hiboutik paste-order fallback;
- the post-M09 passive Hiboutik CB/Espèce daily dashboard values.

M10 preparation has approved/frozen the Catalogue workbook contract, including Category short-code semantics in `../docs/decisions/m10-category-short-code-workbook-semantics.md`, but **no M10 production code, ClosedXML production package reference, schema migration or WPF import/export implementation is authorized yet**.

A separate explicit project-owner M10 implementation authorization plus the dedicated branch/PR/mailbox and OPEN Issue #4 gate are required before Codex may modify production source for M10.