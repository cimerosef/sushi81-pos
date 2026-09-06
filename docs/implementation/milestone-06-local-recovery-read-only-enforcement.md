# M06 — Local recovery and authoritative/read-only enforcement

**Status:** Approved implementation contract  
**Phase:** 6 — Implementation  
**Milestone:** M06 — Local recovery and authoritative/read-only enforcement  
**Implementation branch:** `codex/m06-local-recovery-read-only`  
**Base:** `main` at M05 merge commit `79499d7c6ed65a74f524097c1507ca648dc151c3`  
**Primary acceptance ownership:** `AC-STO-006`, `AC-STO-010`  
**Supporting acceptance:** partial `AC-PROD-002`, failure-path support for `AC-NFR-004`  
**POST_TASK_POWER_ACTION:** `NONE`

## 1. Mission

Implement only M06.

Connect the M01 local-recovery and write-authority primitives to every durable business mutation that exists through M05, make local recovery generation/retention production-real, persist and restore local authority/read-only state safely, and ensure every authoritative business write is blocked centrally whenever the local device is not writable.

Add a clear persistent French/Simplified-Chinese WPF presentation for non-authoritative, stale/blocked and pending-transfer-style local states while preserving read-only consultation of the live copy actually present on the device.

Do **not** implement M07 pairing, formal GitHub authority handoff, target acquisition, generation advancement, cloud disaster-recovery checkpoints or Disaster Recovery. Do **not** implement M08 printing, M10 catalogue workbook import, M12 archive execution or any later milestone.

GitHub frozen/amended specifications are authoritative. If an implementation detail can be chosen without changing business behavior, authority/recovery semantics, data meaning or safety boundaries, choose according to:

**reliability > simplicity > maintainability > operational clarity > novelty.**

If a genuine conflict in those frozen semantics is found, stop the affected path and report it rather than inventing a new rule.

## 2. Required reading before editing

Read the current branch versions of at least:

- `AGENTS.md`;
- `README.md`;
- `docs/README.md`;
- `docs/v1-specification-freeze.md`;
- `docs/acceptance-criteria.md`, especially `AC-STO-006`, `AC-STO-010`, `AC-PROD-002`, `AC-PRINT-010`, `AC-STO-002` through `AC-STO-014`;
- `docs/implementation-plan.md`;
- `docs/implementation-status.md`;
- `docs/architecture.md`;
- `docs/storage-strategy.md`;
- `docs/data-model.md`;
- `docs/order-lifecycle.md`;
- `docs/business-rules.md`;
- all Approved records under `docs/decisions/`, with particular attention to `target-directed-authority-handoff.md` and `github-handoff-transport.md`;
- `docs/implementation/milestone-01-foundation.md`;
- the M02 directed/GitHub handoff revalidation records as feasibility/semantic evidence only;
- the current M03–M05 implementation contracts/worklogs/final acceptance records;
- `docs/implementation/agent-execution-contract.md`;
- `docs/implementation/interactive-quality-gate.md`;
- `docs/implementation/control-state-preservation.md`;
- `docs/implementation/post-task-power-policy.md`;
- `src/README.md`;
- `tests/README.md`.

Do not use prior ChatGPT/Codex conversation memory or the legacy Excel workbook to override these sources.

## 3. Frozen M06 boundary

### 3.1 M06 owns

M06 owns production completion of:

1. **AC-STO-006 — Local recovery generation and retention** for all currently implemented durable business mutations, while establishing the mandatory reusable mutation/recovery seam later milestones must use.
2. **AC-STO-010 — Non-authoritative and pending-transfer read-only mode**, including persistent startup reconstruction, centralized write blocking and user-visible read-only/pending state.
3. The M06 portion of **AC-PROD-002**: normal local POS work remains local-first on the authoritative device and authority/read-only enforcement itself does not depend on Internet/GitHub/OneDrive availability. Full multi-device/handoff completion remains M07.
4. Recovery/authority failure-path evidence contributing to **AC-NFR-004**, whose final owner remains M13.

### 3.2 M06 must not implement

Do not implement or expose production workflows for:

- device pairing or paired-device management;
- `device_id`, `lineage_id`, generation or handoff-version user workflows beyond the minimum future-compatible local technical seam strictly needed by this milestone;
- normal Close-and-retain / Transfer-authority-and-close UI;
- selecting a handoff target;
- GitHub Release Asset snapshot/grant publication;
- durable source relinquishment as part of a real remote transfer;
- target handoff acquisition;
- remote server receipt validation;
- handoff retention (`AC-STO-007`);
- recovery-only cloud checkpoints (`AC-STO-008`);
- explicit Disaster Recovery / generation advancement (`AC-STO-009`);
- final printing/reprinting (`M08`), including the actual `AC-PRINT-010` printer path;
- Hiboutik paste import (`M09`);
- catalogue `.xlsx` import/export (`M10`);
- Gestion export (`M11`);
- annual archive creation/removal/hydration (`M12`);
- installer/final V1 work (`M13`).

Do not create fake future UI merely to make M07/M12 criteria appear implemented.

## 4. Existing M01 primitives are mandatory foundations

M01 already provides:

- `IWriteAuthorityGuard` and `WriteAuthorityState`;
- Infrastructure `WriteAuthorityGuard`;
- `ILocalRecoverySnapshotService`;
- `IRecoveryScheduler`;
- `DurableChange`;
- `DebouncedRecoveryScheduler`;
- SQLite-safe recovery snapshot staging, integrity validation, SHA-256 metadata and atomic promotion;
- five-unit retention behavior;
- application data paths under `%LOCALAPPDATA%\Sushi81 POS\`;
- atomic local JSON configuration patterns;
- startup/migration and failure-safe foundation seams.

Reuse/refine these primitives. Do not create a second unrelated recovery stack or a second independent authority concept.

Refactoring is allowed when needed to satisfy M06 cleanly, but old and new authority/recovery code must converge on one understandable production path.

## 5. Durable local authority state

### 5.1 Requirement

Authority/read-only state must survive application restart. An in-memory `WriteAuthorityGuard` alone is insufficient for M06.

Implement an application-owned durable local authority-state model persisted under the existing application-managed `Config` area using a crash-safe temp-file + atomic replace/move strategy consistent with M01 configuration safety.

The persistent representation is technical local state, not business data and not ordinary operator-editable configuration.

### 5.2 Required semantics

The runtime must be able to distinguish at least these effective conditions:

- authoritative/writable;
- ordinary non-authoritative/read-only;
- pending/transitioning read-only;
- recovery-required/blocked read-only;
- uninitialized/invalid state that must not silently become writable.

A more expressive internal reason model may be added if useful (for example ordinary non-authoritative vs pending transfer vs stale/old-generation placeholder), but every non-authoritative/transitioning/recovery-required/unknown condition must map to `RequireWriteAuthority()` failure.

Do not invent M07 pairing/generation data just to populate M06 state. Future-compatible optional metadata fields are acceptable only if they remain technical, versioned, safely nullable and do not claim a real M07 transfer occurred.

### 5.3 Existing pre-M06 single-device upgrade

A normal existing Sushi81 POS installation created by the accepted M01–M05 application must remain usable after upgrading to M06.

Implement one explicit, testable **legacy single-device bootstrap** for the narrow case where:

- the local database is an existing supported Sushi81 POS live database;
- no durable M06 authority-state record exists yet;
- no contradictory local authority/recovery evidence exists;
- startup/migrations are otherwise valid.

That one-time bootstrap may establish the installation as authoritative so the accepted single-device M01–M05 workflow continues normally.

It must not be a generic rule that “missing/unknown state = writable”. Once a durable M06 authority record exists, deletion/corruption/future-version/malformed/contradictory state must fail closed or enter a clearly blocked recovery-required state rather than silently restoring authority.

Tests must prove both the safe legacy bootstrap and the fail-closed post-bootstrap paths.

## 6. Startup ordering and safety

Startup must resolve local durable authority state before normal business mutation controls can become usable.

Required ordering/invariants:

1. initialize application paths/config/logging through the existing safe startup path;
2. open/validate/migrate the supported local database using existing migration rules;
3. load/validate or perform the one-time legacy authority bootstrap;
4. construct/update the single production write guard from that resolved durable state;
5. create WPF business surfaces with the correct write-enabled/read-only presentation;
6. never briefly expose enabled authoritative mutation controls while authority is unknown.

Malformed authority state, unsupported future authority-state schema/version, contradictory state or authority-state persistence failure must not delete/recreate `live.db` and must not default to writable.

Startup errors must be actionable, localized where user-facing, and technically logged without customer/order/payment payloads.

## 7. Centralized authoritative-write guard

### 7.1 Security/safety boundary

WPF disabled buttons are **not** the authority boundary.

Every production Application-level authoritative business mutation must call the same central authority guard (directly or through one small common application-owned mutation boundary) before the first business write can occur.

An accidentally invoked command, stale enabled control, test direct call or future presentation bug must still fail before durable business mutation when authority is not writable.

Do not make individual repositories the only authority boundary because that makes use-case side effects easy to bypass and spreads policy across persistence code.

### 7.2 Current mutation inventory that must be covered

Audit the branch and enumerate every production write path. At minimum cover all currently implemented paths corresponding to:

**Orders / lifecycle**

- confirming a new order;
- saving a same-ID order modification;
- any existing-order price/fulfilment/contact/cart save that persists business state;
- cumulative CB/Espèce changes and signed dated payment adjustments;
- explicit Close;
- Cancel;
- any save that automatically reopens an incompatible Closed order to Open as part of the same persisted modification.

**Catalogue**

- category create/rename/edit/delete;
- product create/edit/delete;
- product activate/deactivate;
- filtered bulk activation/deactivation;
- option-group create/edit/reorder/delete;
- option create/edit/reorder/activate/deactivate/delete;
- any aggregate save that durably changes current catalogue state.

**Business settings**

- BusinessSettings save.

If another production durable business mutation exists on the M05 merged baseline, it is in scope even if omitted from this list. Record the final audited inventory in the M06 worklog.

Read/query/search/dashboard/reload operations must not require write authority merely because they use SQLite.

### 7.3 No partial side effects

A write-authority rejection must occur before the transaction/business mutation. It must produce:

- zero business-row changes;
- zero payment-ledger changes;
- zero lifecycle changes;
- zero catalogue/settings changes;
- no durable-change notification;
- no local recovery snapshot falsely attributed to that blocked attempt.

## 8. Durable business-change notification and recovery scheduling

### 8.1 Trigger rule

Local recovery is triggered by **successful durable business commits**, not by UI clicks or attempted saves.

The implementation must provide one clear application-owned seam so that current and future business mutation use cases can report successful durable change consistently.

Prefer the smallest design that prevents omissions. It is acceptable to refine the existing transaction/use-case boundary, add a shared committed-mutation wrapper, or use explicit post-commit notification in each application service if that is safer with the current architecture.

Do not blindly trigger recovery for every SQLite technical write if doing so would include migrations/technical bookkeeping unrelated to business mutation.

### 8.2 Required current triggers

The accepted M03–M05 mutations listed in section 7 must schedule local recovery after successful durable change.

The implementation must also leave an explicit mandatory seam for later M10 catalogue batch import and M12 archive execution. Do not implement those future workflows now. Their later owner milestone must cross-check that the established M06 guard/recovery boundary is used.

### 8.3 No-op and failed writes

Do not advance the durable-change sequence or schedule a recovery snapshot when:

- validation fails before commit;
- the authority guard rejects the action;
- a transaction rolls back;
- persistence throws and nothing commits;
- the application intentionally performs a true zero-effective-change no-op, including filtered bulk activation/deactivation with zero effective changes;
- the user cancels/abandons an uncommitted edit.

If a use case commits a real business change even when some derived value is unchanged, it is a durable change and must notify normally.

### 8.4 Debounce, coalescing and single-flight

Preserve the approved M01 default three-second debounce unless a concrete correctness blocker requires a purely technical adjustment.

Nearby successful durable business commits may coalesce into one snapshot.

The scheduler must remain single-flight. A durable business change arriving while a snapshot is being generated must not be lost: after the active snapshot finishes, the latest durable-change sequence must still be protected by an additional snapshot when necessary.

Orderly application shutdown must flush a pending local recovery request when snapshot creation remains safely possible.

## 9. Snapshot correctness and retention

Reuse the SQLite-safe M01 snapshot service. A recovery unit is successful only after:

1. SQLite-safe backup/staging completes;
2. the staged database opens independently;
3. `PRAGMA integrity_check` returns `ok`;
4. SHA-256 is computed over the completed database;
5. metadata records the required timestamp/schema/durable-change sequence and checksum;
6. database + metadata are promoted atomically/as one valid application-recognized unit;
7. only then may retention cleanup occur.

Retain the latest **five** successfully generated and validated recovery units.

An older valid unit may be removed only after a newer valid replacement exists. A failed sixth attempt must leave the previous five valid units intact.

Invalid, corrupt, incomplete, staging or metadata-mismatched artifacts must not count toward the five.

Do not expose these snapshots as an ordinary operator order-history UI in M06.

## 10. Recovery snapshot failure behavior

A business transaction that already committed successfully must never be rolled back, deleted or rewritten merely because subsequent recovery snapshot creation fails.

On recovery creation/validation/promotion/cleanup failure:

- keep the committed live business data;
- never claim a valid new recovery snapshot exists;
- preserve prior valid recovery units;
- emit bounded/redacted technical diagnostics;
- expose a concise actionable user warning when appropriate without blocking ordinary authoritative operation solely because one post-commit recovery attempt failed, unless the existing/frozen safety state actually requires `RecoveryRequired`;
- allow a later successful durable change or orderly shutdown flush to protect the latest committed sequence.

Do not invent a silent destructive “repair” that replaces the live database from an old snapshot.

## 11. Read-only and pending-state presentation

### 11.1 Persistent top-level status

When the device is not authoritative/writable, the main WPF shell must show a clear persistent status/banner rather than only displaying an error after a forbidden click.

At minimum provide localized presentation for:

- ordinary non-authoritative/read-only;
- pending/transitioning read-only;
- recovery-required/blocked read-only.

The text must make stale-data risk clear where applicable. If locally available, show the effective timestamp/version of the local copy or last protected/acquired state in a compact way; do not fabricate freshness data that the current M06 installation does not actually know.

Pending-target/source details may be shown only when real local metadata exists. M06 must not fake a target selection workflow.

### 11.2 Permitted read-only operations

A read-only device may continue to use non-mutating local functions that are already implemented, including practical consultation such as:

- live/current order search and date browse;
- viewing order snapshots/details;
- dashboard/operational views based on the local copy;
- catalogue consultation needed for viewing/inspection;
- other truly read-only current features.

The UI must state that live data may be stale where applicable.

### 11.3 Blocked operations

Disable/hide/replace mutation controls as appropriate while also retaining the central Application guard. At minimum block currently implemented UI paths for:

- new order confirmation;
- existing-order modification save;
- payment changes;
- Close/Cancel;
- catalogue/category/product/group/option edits/deletes/activation operations;
- filtered bulk activation/deactivation;
- BusinessSettings save.

Future catalogue import and archive execution must use the same guard when implemented later.

### 11.4 Printing boundary

Do not implement printing in M06. Preserve the frozen exception that a non-authoritative/read-only paired device may later print/reprint from the committed local copy it actually holds, with stale warning, under `AC-PRINT-010` in M08.

Therefore do not design the M06 write guard in a way that treats all output actions as authoritative writes.

## 12. Persistent restart behavior

Automated tests must prove:

- authoritative state persists and restores correctly;
- ordinary read-only state persists and remains read-only after restart;
- pending/transitioning state persists and remains read-only after restart;
- recovery-required/blocked state persists and remains read-only after restart;
- a malformed/corrupt/unsupported future authority-state record fails closed;
- deleting/corrupting an already-established authority record does not rerun the legacy bootstrap silently;
- no read-only/pending/recovery-required restart can transiently enable authoritative mutation controls.

The future M07 transport will drive these states through real protocol events; M06 must make the local semantics reliable first.

## 13. Failure-injection matrix — mandatory

Add deterministic automated failure-path tests for at least:

1. business transaction rollback → no durable-change increment/no snapshot;
2. authority rejection → zero business writes/no snapshot;
3. snapshot staging/backup failure;
4. staged SQLite integrity failure;
5. checksum/metadata validation mismatch;
6. final recovery-unit promotion failure;
7. retention cleanup failure;
8. failed sixth recovery snapshot preserves prior five;
9. incomplete/staging artifacts never count as valid recovery units;
10. two or more nearby durable commits coalesce according to debounce;
11. durable change arriving during an active snapshot is not lost;
12. shutdown with pending durable change flushes when safely possible;
13. snapshot failure after committed business write does not roll back business data;
14. persisted read-only state survives restart;
15. persisted pending/transitioning state survives restart;
16. malformed/unsupported authority state fails closed;
17. direct Application invocation bypassing WPF still hits the central guard;
18. accidentally/stale enabled WPF command still cannot mutate because the Application guard rejects it;
19. read-only search/view/dashboard paths remain usable;
20. localization switch in read-only mode preserves authority state and user-entered business data.

Use synthetic data only.

## 14. Automated test inventory

Add focused tests in the existing project structure rather than a new test framework.

### Application tests

Cover:

- guard invocation for every current mutation service;
- no notifier on validation/no-op/authority rejection;
- notifier after successful commit;
- user-facing authority/recovery result semantics where owned by Application;
- read-only mutation inventory completeness through stable service-level tests.

### Infrastructure integration tests

Cover:

- durable authority-state atomic persistence/reload;
- safe legacy bootstrap;
- malformed/future/corrupt state fail-closed behavior;
- real SQLite current-mutation commit → scheduler → recovery snapshot paths;
- rollback/no-op exclusion;
- five-unit validated retention;
- failure injection listed above;
- latest committed business rows present in the recovery snapshot;
- rolled-back/blocked rows absent;
- no destructive reset of `live.db` on authority/recovery failure.

### WPF/architecture tests

Use real STA/WPF presentation tests for the actual main shell and relevant mutation controls. Cover:

- authoritative controls enabled normally;
- persistent read-only banner/state;
- persistent pending/transitioning banner/state;
- recovery-required presentation;
- mutation controls disabled/unavailable while query/view routes remain usable;
- French → zh-CN → French rerendering without changing authority state or business data;
- realistic window sizes and no clipped unreadable safety warning;
- no M07 target-selection/transfer controls exposed;
- Application guard remains present even if a presentation command is invoked directly.

Extend dependency/architecture checks if needed so production mutation services cannot silently bypass the chosen central guard/recovery notification boundary.

## 15. Windows/WPF manual acceptance — project-owner gate

Codex must prepare/publish the exact reviewed self-contained `win-x64` build evidence. Codex must **not** claim the project-owner manual gate itself passed unless the project owner actually performs it.

The durable checklist is in `milestone-06-final-manual-acceptance.md`.

The checklist must verify at least:

### Authoritative mode

- application starts writable from a normal upgraded M05 data state;
- create/save order works;
- existing-order modification works;
- CB/Espèce change works;
- Close/Cancel work;
- catalogue mutation works;
- settings save works;
- recovery scheduling does not produce noticeable UI stalls;
- after debounce, a valid local recovery unit is produced;
- repeated changes retain only latest five valid units;
- restart preserves authoritative operation.

### Read-only mode

- persistent localized read-only warning is visible;
- stale-data warning is understandable;
- order search/date browse/detail/dashboard remain usable;
- order create/edit/payment/Close/Cancel are blocked;
- catalogue/settings mutations are blocked;
- restart preserves read-only state;
- no force-takeover/continue-writing escape exists.

### Pending/transitioning simulation

- persistent pending/transitioning warning differs clearly from ordinary read-only where practical;
- business writes are blocked;
- restart preserves pending state;
- no M07 retarget/force-acquire functionality appears.

Verify French and Simplified Chinese presentation.

## 16. Acceptance mapping

### AC-STO-006 — M06 owner

M06 must provide implementation and evidence for the complete local-recovery mechanism and all durable business mutation classes available through M05. Record the future M10 catalogue-import cross-check as a later regression obligation without implementing M10 now.

Do not mark Passed merely because the M01 primitive exists. Required evidence includes real current business mutation triggers, debounce/single-flight/flush and validated five-unit retention/failure behavior.

### AC-STO-010 — M06 owner

M06 must provide persistent authority/read-only state, centralized current write blocking and WPF presentation. Record future cross-checks for M08 printing exception and M12 archive execution without implementing those milestones.

### AC-PROD-002 — Partial in M06

Prove that local authoritative business operation and local recovery/authority enforcement do not require Internet/GitHub/OneDrive. Full multi-device handoff/offline boundary closes in M07.

### AC-NFR-004 — supporting evidence only

Record recovery/authority failure injection as supporting evidence. M13 remains the criterion owner.

## 17. M05 merge-state documentation cleanup

The M05 final status documentation was written before PR #10 merged. M05 is now merged to `main` at
`79499d7c6ed65a74f524097c1507ca648dc151c3`; this M06 contract is the current authorized implementation scope on PR #11.

Do not rewrite historical time-stamped evidence sections merely because they truthfully describe the state when recorded.

Update current-state sections in the applicable repository status/navigation documents so they state:

- M05 Passed;
- PR #10 merged to `main` at `79499d7c6ed65a74f524097c1507ca648dc151c3`;
- accepted production-code head `84c1c534c1df105ccb1839cbc6dfc9e0e055bb70`;
- final docs head `217d187dd3ef5498c11f21bc516eccc6737fa952`;
- M05 no longer open work;
- M06 is the active implementation milestone only after the M06 mailbox/handoff/gate sequence is established.

At minimum inspect/update the current-state portions of:

- root `README.md`;
- `docs/README.md`;
- `docs/implementation-plan.md`;
- `docs/implementation-status.md`;
- `docs/implementation/README.md`.

Historical M05 worklog/final-acceptance passages may remain unchanged when clearly historical.

## 18. Parallel execution plan

Before coding, the Codex main agent must assess the following workstreams for safe parallel delegation under `agent-execution-contract.md`:

A. **Authority persistence/startup/guard** — durable local state, fail-closed startup, central guard wiring.

B. **Recovery mutation wiring/scheduler/failure paths** — current mutation inventory, committed-change notification, debounce/single-flight/flush, snapshot integration.

C. **WPF read-only presentation/localization** — persistent banner/status, control-state adaptation, read-only consultation routes.

D. **Independent test/audit work** — mutation inventory audit, failure-injection/STA-WPF regression design, adjacent bypass search.

The main agent must first stabilize shared Application interfaces/state semantics before delegating code that depends on them. Do not let parallel agents independently invent competing authority/recovery abstractions. Main-agent integration/review responsibility is retained.

Record actual execution topology in the worklog and `CODEX_DONE`.

## 19. Interactive quality requirements

M06 inherits `interactive-quality-gate.md` and `control-state-preservation.md`.

Specifically:

- safety banners/status must rerender immediately on FR/zh-CN change;
- localization refresh must not mutate authority state;
- read-only transitions must not erase search/date/filter/selection state unless the frozen workflow requires it;
- disabling writes must not unnecessarily disable read-only inspection;
- asynchronous recovery work must not block the WPF UI thread;
- any operator-found defect requires a defect-escape retrospective and adjacent-pattern audit before the next remediation is considered complete.

## 20. Data safety and logging

- Never use real customer/order/payment/credential data in tests.
- Never commit live/recovery SQLite files, authority-state files, logs, tokens or generated publish output.
- Do not log telephone, address, comments, payment payloads or database contents.
- Authority/recovery diagnostics should use technical state names, sequence numbers, hashes where safe, counts and exception types/messages only.
- No automatic delete/recreate/reset of `live.db` is permitted as authority/recovery error handling.

## 21. Verification commands

Run and report at minimum:

```powershell
dotnet --info
dotnet restore Sushi81.Pos.sln --locked-mode
dotnet build Sushi81.Pos.sln --configuration Release --no-restore
dotnet test Sushi81.Pos.sln --configuration Release --no-build --logger "console;verbosity=minimal"
dotnet publish src/Sushi81.Pos.Desktop/Sushi81.Pos.Desktop.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishSingleFile=false `
  -o artifacts/m06-publish
git diff --check
```

If the standard publish path is occupied by a running accepted application, use an ignored milestone-specific evidence directory rather than terminating the operator's application solely for verification.

All existing tests plus all M06 tests must pass. Release build must have 0 warnings and 0 errors.

Use GitHub Actions CI on the pushed exact implementation/evidence head and record run/check results.

## 22. Worklog/evidence updates

Maintain `docs/implementation/milestone-06-worklog.md` with:

- final mutation inventory;
- implementation topology;
- authority-state persistence/startup design actually chosen;
- recovery notification/scheduler design actually chosen;
- exact automated test names/paths for critical invariants;
- failure-injection results;
- Release build/test/publish results;
- CI exact-head evidence;
- acceptance mapping;
- known later-milestone cross-checks;
- confirmation no sensitive/production data was committed.

Update `docs/implementation-status.md` honestly. Do not mark the project-owner Windows/WPF checklist Passed before the owner executes it.

## 23. Stop conditions

Stop the affected implementation path and report if any of the following occurs:

- frozen documents genuinely contradict on whether a device may write;
- satisfying M06 appears to require implementing real M07 handoff/pairing/generation behavior;
- a durable authority-state migration cannot preserve a normal accepted M05 installation safely;
- current production write paths cannot be centrally guarded without changing business semantics;
- recovery correctness appears to require deleting/resetting committed business data;
- a proposed implementation would allow unknown/corrupt authority state to become writable silently.

Do not solve such a blocker by weakening authority or recovery safety.

## 24. Completion conditions

Codex may report `CODEX_DONE` for the initial M06 implementation only when:

1. the complete current mutation inventory is centrally guarded;
2. successful current durable business mutations trigger recovery protection correctly;
3. rollback/no-op/blocked attempts do not trigger false recovery success;
4. debounce/single-flight/shutdown-flush behavior is tested;
5. latest-five validated retention/failure behavior is tested;
6. durable authority/read-only states survive restart and fail closed on invalid state;
7. the real WPF shell clearly represents read-only/pending/recovery-required states in FR/zh-CN;
8. read-only consultation remains usable while writes are blocked;
9. mandatory failure-injection tests pass;
10. full Release test suite, 0-warning/0-error build and self-contained win-x64 publish pass;
11. exact-head CI evidence is recorded;
12. current-state M05 merge documentation cleanup is included;
13. M06 worklog/status evidence is updated;
14. no M07+ production behavior has been implemented;
15. the PR remains open/unmerged.

The project-owner Windows/WPF manual acceptance is a subsequent owner gate if it has not yet been executed. A passing `CODEX_DONE` never authorizes merge.
