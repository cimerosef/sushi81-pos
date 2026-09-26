# M13 — Installer, localization completion and final V1 acceptance — preparation/readiness

**Status:** PREPARATION COMPLETE / OWNER-AUTHORIZED / WP1 READY  
**Milestone:** M13  
**Reviewed baseline:** `f59663c6b47ab21114c24360544e4e25094f4722`  
**Review date:** 2026-09-24
**Durable mailbox:** Draft PR #26

## 1. Re-established GitHub baseline

The controller re-established the M13 baseline from current GitHub state rather than prior chat memory.

Verified:

- M12 PR #25 is closed after merge;
- `main` is `f59663c6b47ab21114c24360544e4e25094f4722`, the PR #25 merge commit;
- final pre-merge M12 head is `49a0fe69e23e68c5591ef36ba5c56ef09a9d88b3`;
- exact-head pre-merge CI #862 / run `36021889765` completed successfully;
- post-merge CI #863 / run `36023054757` build-and-test job completed successfully;
- Issue #4 is closed and has no active executable handoff;
- M12's populated real-archive operational verification remains explicitly deferred under the owner waiver; it is not represented as fully Passed;
- the approved M13 Gestion export retention/compaction decision is PR #25 comment `5792796519`.

## 2. Repository visibility disposition

GitHub currently reports `cimerosef/sushi81-pos` as public.

On 2026-09-24 the project owner explicitly instructed the controller not to treat that visibility as an M13 blocker and stated that the public state is intentional. Repository visibility therefore does not block M13 execution or final V1 acceptance unless later owner instructions change.

No source-repository visibility change is required by M13.

## 3. No unresolved M13 business/product ambiguity

Current Approved docs contain enough product/data semantics to implement M13 without a new owner business decision.

Pure technical decisions are delegated to the controller/Codex within the existing priority:

**reliability > data safety > simplicity > maintainability > operational clarity > performance > novelty**

## 4. Existing technical seams relevant to M13

- `WindowsAppPaths` roots durable application data under `%LOCALAPPDATA%\Sushi81 POS` and separates `Data`, `Recovery`, `Archive`, `Config`, `Logs`, `Cache` and `Temp`.
- `AC-ARCH-007` and `architecture.md` already freeze a self-contained Windows x64 WPF publish and per-user Inno Setup 6 installer.
- The current Desktop project targets `net10.0-windows` / WPF, but final production publish/installer/provenance is not yet the normal V1 release pipeline.
- CI currently performs Release build/test and retains an older M11-specific owner-candidate artifact job; M13 must replace milestone-specific packaging with the final production artifact path.
- French and Simplified Chinese resource files and runtime language switching already exist, but no whole-V1 final parity/hard-coded-string audit has yet closed M13.
- M11 migration 8 stores `export_batches`, `export_batch_orders` and `export_emissions`; M12 migration 9 stores annual-archive completion state. M13 must add safe retention/compaction behavior without weakening M11 selection semantics.
- M12 annual archives are permanent local business data and must survive ordinary update/reinstall.

## 5. Installer/data-preservation contract

Ordinary install, in-place upgrade, repair/reinstall and uninstall/reinstall must treat installer-managed binaries separately from durable Sushi81 data.

The installer must not delete, overwrite, replace with fixtures, or silently reinitialize:

- `%LOCALAPPDATA%\Sushi81 POS\Data\live.db`;
- `Archive\` canonical annual archives;
- `Recovery\` retained recovery snapshots/metadata;
- `Config\` including device identity, paired-lineage and durable authority/transfer state;
- configured printer/settings and other durable application configuration;
- protected handoff credential material outside the binary install tree.

`Cache\` and `Temp\` are disposable technical areas, but installer behavior still must not confuse them with durable business data. Logs are technical diagnostics and must not be packaged as production input or used to restore business state.

Database schema migration remains application-controlled on first launch of the new version. A failed migration must fail safely without silently deleting/recreating `live.db`. Downgrade/rollback must never silently apply an incompatible older schema.

V1 does not add a destructive "remove all Sushi81 data" uninstall option.

## 6. Work-package plan

### WP1 — Gestion export retention/compaction core

Implement the approved M13 retention policy in application/infrastructure code with deterministic time policy, transaction-safe pruning primitives and real-SQLite evidence. First package remains narrow: no installer, no localization sweep, no final release artifact and no broad UI refactor.

### WP2 — retention runtime integration and history behavior

Wire safe authoritative execution at a simple deterministic lifecycle point, preserve failure-safe startup/archive behavior, and prove successful history exposes only retained/rebuildable batches.

### WP3 — localization completion

Audit French/zh-CN resource-key parity, hard-coded user-facing strings, dynamic messages, dialogs, major views, truncation/readability and language-switch refresh behavior. Do not change frozen business semantics.

### WP4 — final Windows x64 publish + per-user Inno Setup installer

Create the production publish/installer pipeline, stable upgrade identity, version/provenance metadata, safety scan and install/upgrade/reinstall/uninstall-reinstall preservation evidence.

### WP5 — diagnostics, performance, repository/security and production hardening

Review redaction/logging, failure diagnostics, normal-workflow responsiveness, long-lived SQLite behavior, dependencies, repository fixtures/secrets and final production-target regression.

### WP6 — final V1 candidate, operating guide and owner acceptance

Produce the exact-head production installer/release artifacts with hashes/provenance, reconcile all docs/status/exclusions, deliver the practical operating guide and execute the shortest high-value Windows owner acceptance needed to close V1.

## 7. Dependency order

WP1 -> WP2 must complete before final installer regression because schema/data-retention behavior is part of upgrade safety.

WP3 may use the same branch after WP2 and must be complete before the final owner candidate.

WP4 depends on the then-current production schema and resources.

WP5 validates the integrated product and release pipeline.

WP6 is the release/acceptance closure package and cannot precede exact-head green CI/artifact provenance from the final integrated head.

## 8. First executable package decision

Issue #4 may now be deliberately opened for the first executable handoff, which is **WP1 only**.

WP1 must not start installer work.
