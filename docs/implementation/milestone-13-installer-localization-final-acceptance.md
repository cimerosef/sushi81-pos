# M13 — Installer, localization completion and final V1 acceptance — implementation contract

**Status:** OWNER-AUTHORIZED; execution package-gated; current gate blocked pending repository-visibility resolution  
**Milestone:** M13  
**Implementation branch:** `codex/m13-installer-final-acceptance-authorized`  
**Start baseline:** `f59663c6b47ab21114c24360544e4e25094f4722`

## 1. Mission

Complete the final V1 milestone without reopening already-accepted M01–M12 behavior except where the Approved M13 retention/compaction amendment explicitly requires new behavior.

M13 owns:

- Gestion export ledger retention/compaction;
- complete FR/zh-CN UI parity;
- self-contained Windows x64 production publish;
- per-user Inno Setup installer and upgrade/reinstall preservation;
- diagnostics/performance/repository-security hardening;
- final production-target regression, release provenance, operating guide and owner acceptance.

## 2. Non-negotiable invariants

1. No ordinary install/upgrade/reinstall/uninstall-reinstall may silently destroy durable Sushi81 business/configuration/authority state.
2. `live.db`, canonical annual archives, recovery material and durable device/authority/config state remain outside the installer-managed binary tree.
3. Application migrations remain versioned and fail-safe; installer logic never "fixes" a migration problem by deleting/recreating the database.
4. M11 CREATE/UPDATE/CANCEL semantics remain frozen.
5. Unresolved PREPARED/pending export work is never pruned.
6. Live-order export decision state is retained.
7. Complete successful-batch payload is retained for at least 30 days after success and remains exactly regenerable during that period.
8. Any compaction is transactional, idempotent, failure-safe and authority-safe.
9. M12 archive fail-safe remains: archive failure cannot silently delete eligible live orders.
10. M12 populated real-archive verification remains deferred to the first safe real prior-year cycle; M13 does not restart synthetic archive forensics unless a new installer/regression fact proves direct blockage.
11. French and Simplified Chinese localize software UI, not operator-entered catalogue/customer/order data or fixed export schema identifiers.
12. Production packages contain binaries/resources only; they must not contain development/test databases, production credentials, logs, customer/order fixtures or temporary business artifacts.
13. Final production provenance must identify exact source head, application version, CI run, publish target/options, installer artifact and SHA-256.
14. Codex never merges the M13 PR.

## 3. WP1 — Gestion export retention/compaction core

### Scope

Implement a focused application/infrastructure compaction boundary against the existing M11/M12 schema.

Required behavior:

- define a deterministic 30-day successful-batch retention deadline from successful completion time;
- never select `PREPARED` batches for pruning;
- refuse/fail closed on malformed SUCCESS state that lacks trustworthy completion/dependency facts;
- retain successful history/state when any associated order is still live or unresolved PREPARED/pending work depends on it;
- permit cleanup only when related orders have left the live set through completed archival semantics and no unresolved work remains;
- perform dependent-row deletion in one SQLite transaction while preserving foreign-key integrity;
- repeated compaction is idempotent;
- injected failures roll back completely;
- non-authoritative execution is blocked.

A conservative all-or-nothing successful-batch prune is preferred over a more complex partial-payload rewrite if it satisfies the Approved retention rules. Do not introduce a separate per-order summary table unless the existing model demonstrably cannot preserve M11 selection semantics safely without it.

### Required evidence

Use real SQLite, not mock-only tests. Cover at least:

- 29d23h59m59s / exact 30-day boundary / older-than-window behavior;
- old PREPARED remains untouched;
- old SUCCESS with any live-order dependency remains;
- old SUCCESS with any unresolved PREPARED/pending dependency remains;
- archived-settled dependency-free SUCCESS can be removed;
- mixed dependency batch is conservatively retained;
- latest successful emission needed by a live order is unchanged by compaction;
- M11 selection before/after compaction is equivalent for all live orders;
- FK check remains clean;
- failure injection leaves byte/logical state unchanged;
- second run is a no-op;
- non-authoritative guard blocks mutation.

### WP1 exclusions

No installer, publish pipeline, localization sweep, operating guide, archive forensic work, manual cleanup UI, or final owner candidate.

## 4. WP2 — retention runtime integration/history

Use one simple deterministic authoritative lifecycle point, preferably startup after migrations/authority resolution and after any M12 annual-archive attempt for that startup, so an upgraded installation can clean already-eligible history and newly archived history without a background service.

Failure to compact must be non-destructive, diagnosable and retryable on a later safe authoritative startup. It must not convert an otherwise safe installation into silent data loss.

Successful-history UI naturally reflects only retained/rebuildable rows. Do not invent tombstone rows for pruned batches.

## 5. WP3 — localization completion

Required closure:

- French and zh-CN resource-key sets match for every final user-facing resource;
- no new user-facing hard-coded strings bypass the localization seam;
- major screens/dialogs are reviewed in both languages;
- formatting placeholders and dynamic messages remain valid in both languages;
- language switch does not mutate business data;
- obvious clipping/truncation/garbling is repaired without redesigning business workflows.

Fixed technical schema identifiers such as CREATE/UPDATE/CANCEL and workbook field names remain contract identifiers and need not be translated.

## 6. WP4 — production publish and installer

Production target:

- `win-x64`;
- self-contained .NET 10 WPF publish;
- per-user Inno Setup 6 installer;
- stable installer application identity across upgrades;
- binaries installed to a per-user program location separate from `%LOCALAPPDATA%\Sushi81 POS`;
- no background self-updater.

Installer acceptance must prove:

- clean install/launch;
- upgrade from a prior accepted V1 candidate;
- same-version repair/reinstall;
- uninstall followed by reinstall with durable data preserved;
- no re-pair/re-authority reset merely because binaries changed;
- existing `live.db`, Archive, Recovery, Config, printer/settings survive;
- first-launch migration is application-controlled and failure-safe;
- no forbidden business/test/credential/log fixture is present in publish/installer contents;
- a clear installed version/provenance can be inspected.

Downgrade to an older binary against a newer incompatible schema must fail clearly or otherwise remain safe; it must never silently downgrade/reset data.

## 7. WP5 — diagnostics, performance, repository/security hardening

Review:

- redacted logs and actionable failure messages;
- no customer/order secrets in repository fixtures or build artifacts;
- dependencies and generated artifacts;
- common workflow responsiveness;
- SQLite/index/query behavior under realistic retained history;
- startup behavior with compaction/archive/recovery seams;
- repository visibility and final security posture;
- final V1 exclusions.

Performance work must be evidence-driven; no speculative VACUUM/background maintenance subsystem is required.

## 8. WP6 — final V1 release/acceptance

Closure requires:

- full Release test suite: 0 failed, 0 unexpected skipped;
- Release build: 0 warnings / 0 errors;
- exact-head CI success;
- final production installer artifact from that exact head;
- artifact SHA-256 + source head + version + CI/run provenance;
- Windows owner install/upgrade/reinstall/smoke acceptance;
- FR <-> zh-CN acceptance;
- operating guide delivered and consistent with final UI;
- README/docs/status reconciled;
- all explicit V1 exclusions rechecked;
- no unresolved material defect, contradiction or security blocker.

M12's deferred real populated-archive operational check remains explicitly deferred rather than being falsely marked Passed.
