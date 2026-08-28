# M02 — Target-directed handoff amendment revalidation

**Status:** Approved task definition — implementation/revalidation authorized  
**Phase:** 6 — Implementation  
**Milestone:** M02 amendment revalidation  
**Scope type:** Technical feasibility proof after approved specification amendment; no business-feature implementation

## 1. Mission

Re-run the M02 feasibility gate against the **amended V1 target-directed authority-transfer specification** approved on 2026-08-28.

The original M02 evidence proved that competitive/generic OneDrive acquisition cannot satisfy the strict single-writer contract. That evidence remains valid and is recorded in:

- `docs/implementation/milestone-02-feasibility-report.md`;
- PR #2 / merge commit `5bacafa0e4ca906d8ff058e34586dee43503bc42`.

The approved amendment is:

- `docs/decisions/target-directed-authority-handoff.md`.

The amended baseline is incorporated directly into:

- `docs/architecture.md`;
- `docs/storage-strategy.md`;
- `docs/acceptance-criteria.md`;
- `docs/v1-specification-freeze.md`.

M02 now has one job: prove or disprove that the **target-directed, source-arbitrated** handoff can satisfy the amended fail-closed safety contract using OneDrive only as immutable artifact transport.

M03 remains unauthorized until this gate is closed and M03 receives its own detailed task definition.

## 2. Repository and branch discipline

Start from latest `main` after the target-directed amendment documents above are present.

Create and use:

`codex/m02-directed-handoff-revalidation`

Do not continue development on the merged `codex/m02-onedrive-feasibility` branch.

Before editing, read the complete current authoritative repository, especially:

- `AGENTS.md`;
- `docs/v1-specification-freeze.md`;
- `docs/implementation-plan.md`;
- `docs/implementation-status.md`;
- `docs/acceptance-criteria.md`;
- `docs/architecture.md`;
- `docs/storage-strategy.md`;
- `docs/decisions/target-directed-authority-handoff.md`;
- `docs/implementation/milestone-02-feasibility-report.md`;
- this task definition;
- all other Approved decision records that touch storage/read-only/printing;
- existing M01 and M02 code/tests/harness.

If prior M02 code or prior chat conflicts with the amended GitHub specification, the amended GitHub specification wins.

## 3. Frozen amended invariants

Do not weaken or reinterpret these invariants:

1. Every active `live.db` remains local under `%LOCALAPPDATA%\Sushi81 POS\Data\live.db`.
2. OneDrive remains transport/recovery/archive storage, not a live database, distributed lock or row-level merge engine.
3. N-device support remains; fixed SHOP/HOME or exactly-two-device architecture is forbidden.
4. At most one device may be business-authoritative/writable at any time.
5. Normal close has two distinct meanings:
   - **Close and retain authority**;
   - **Transfer authority and close**.
6. Closing without explicit transfer never releases authority.
7. Only the current authoritative source may create a normal transfer.
8. A normal transfer is bound to exactly one immutable `target_device_id`.
9. Source and target IDs must be distinct valid paired devices for the current lineage/generation.
10. The source must durably relinquish business-write authority before a target-releasing ready/grant marker can exist.
11. Source relinquishment must survive restart/crash.
12. After relinquishment, the source may only retry completion of the same immutable transfer; it may not resume business writes or retarget the handoff.
13. Only the exact designated target may acquire the normal handoff.
14. Non-target devices remain read-only and do not compete through claims/election.
15. A snapshot without the matching target-bound ready/grant marker never releases authority.
16. Snapshot/marker lineage, generation, version, source, target and checksum must match; SQLite integrity must pass.
17. Unknown, partial, invalid, stale, replayed or failed state is fail-closed.
18. If the designated target cannot be recovered after source relinquishment, another device does not substitute itself through normal takeover; explicit Disaster Recovery is the exceptional path.
19. Disaster Recovery remains generation-advancing and separate from normal handoff.
20. No Graph/OAuth/Azure/hosted coordinator/server/remote DB may be added merely to pass M02.

## 4. M02 is still a feasibility gate, not M07

Do not implement the complete production pairing/handoff/Disaster-Recovery UI or wire the real POS business application into OneDrive authority transfer.

Allowed work:

- revise/reuse the non-shipping M02 feasibility harness;
- create pure authority-transfer state machines/models;
- create durable-state simulations and synthetic local persistence adapters for crash/restart proof;
- reuse narrow Windows/OneDrive observation primitives already justified in M02;
- create synthetic SQLite snapshot/marker transport tests;
- add deterministic N-device adversarial simulation;
- add exact operator commands for real OneDrive transport evidence;
- create the new revalidation evidence report.

Do not implement:

- Catalogue, Orders, Payments, pricing/VAT or M03+ business features;
- final WPF close/target-selection UI;
- production pairing/settings UI;
- production M06/M07 write-guard wiring across all use cases;
- final Disaster Recovery UI;
- archive/printing/export behavior beyond test seams required to prove authority state.

## 5. Required protocol model

Model normal authority transfer explicitly rather than as loose files/booleans.

The exact internal naming is technical, but the model must distinguish at least these source-side concepts:

- `Authoritative`;
- `PreparingTransfer` while business writes are blocked but irreversible relinquishment has not occurred;
- `RelinquishedPendingGrant` after durable local relinquishment and before completed synchronized grant;
- `TransferReleased`/equivalent after the target-bound ready/grant is completed.

And target/non-target concepts sufficient to distinguish:

- ordinary `NonAuthoritative`;
- designated target with incomplete/unvalidated handoff;
- designated target after valid acquisition;
- wrong/non-target device;
- stale/replayed/old-generation state.

There must be one centralized answer to:

`MayBusinessWrite(device, durableAuthorityState, observedHandoffState)`

or an equivalent pure decision boundary.

A state machine transition must never infer authority merely from process liveness, file presence or elapsed time.

## 6. Critical source-side safety proof

The implementation/proof must enforce this exact ordering:

1. source is authoritative;
2. operator fixes target identity;
3. block new business edits;
4. finish accepted writes;
5. create/validate immutable synthetic SQLite snapshot;
6. publish snapshot and observe required synchronization success;
7. atomically/durably persist local relinquishment record for that exact lineage/generation/version/source/target/checksum;
8. immediately after durable persistence, all source business writes evaluate blocked, including after simulated restart;
9. only then may matching target-bound ready/grant marker be created;
10. marker synchronization success may complete release.

Automated tests must prove:

> There is no execution path in which a target-releasing ready/grant marker exists while the source still evaluates business-writable.

A crash must be injected after every meaningful step.

## 7. Pre- and post-relinquishment failure semantics

### Before relinquishment

Exercise:

- snapshot creation failure;
- integrity failure;
- snapshot sync pending/timeout/error;
- operator cancellation before the irreversible point.

Required outcome:

- no target-releasing marker;
- no target acquisition;
- source may return to/retain authoritative state after safe abort.

### After relinquishment

Exercise:

- process crash immediately after durable relinquishment;
- marker creation failure;
- marker sync pending/timeout/error;
- application restart while pending;
- repeated retry;
- attempted retarget;
- attempted source write;
- attempted transfer cancellation/rollback.

Required outcome:

- source remains business-read-only;
- target remains blocked until complete valid grant is available;
- technical retry is idempotent and bound to the same immutable transfer;
- retarget and source-write attempts are rejected;
- no new business mutation is permitted to change the snapshot represented by the pending transfer.

## 8. Target acquisition proof

Target acquisition must require all of the following in the test model/harness:

- local device ID equals `target_device_id` exactly;
- source != target;
- source/target are valid paired identities for the modeled lineage/generation;
- snapshot present;
- matching target-bound ready/grant present;
- metadata format supported;
- lineage match;
- generation match;
- handoff version match;
- source ID match;
- target ID match;
- checksum/size match;
- SQLite integrity passes;
- handoff is not stale/replayed relative to durable local target state;
- durable target acquisition succeeds before business writes are enabled.

A wrong target must remain blocked even if it is the only running machine and even if it can see perfectly synchronized snapshot/marker files.

## 9. N-device adversarial simulation

Retain at least a three-device deterministic model.

The simulator must include source A and at least targets/non-targets B/C and test arbitrary delivery order of immutable artifacts.

At minimum cover:

- A retains authority and closes; B/C remain blocked;
- A transfers to B; C sees artifacts before B and remains blocked;
- A transfers to B; B sees snapshot but not marker and remains blocked;
- A has relinquished but marker is delayed; A/B/C all remain non-writable until B has valid marker;
- B acquires; A/C remain blocked;
- C cannot acquire B-targeted handoff even if B is offline;
- duplicate/replayed marker;
- stale handoff version;
- stale generation;
- malformed source/target identity;
- source==target invalid;
- retarget attempt after relinquishment;
- repeated restart/retry on source;
- repeated evaluation determinism.

Global safety assertion:

> For every modeled state/interleaving, the count of business-writable distinct devices is never greater than one.

Also assert required liveness for the valid normal path:

> With one authoritative source, one valid selected target, successful durable relinquishment, successful artifact transport and valid target acquisition, exactly that target can become writable.

A protocol that merely blocks every N-device transfer is not sufficient.

## 10. Close-and-retain proof

Add explicit deterministic tests for the newly approved user behavior:

- close-and-retain creates no authority-release marker;
- source durable authority remains authoritative across clean simulated restart;
- other paired devices remain read-only;
- mere absence of the source process never makes another device writable;
- later explicit transfer can still begin normally from the retained-authority device.

This proof is required even though final WPF UX belongs to M07.

## 11. OneDrive transport evidence boundary

The mutual-exclusion proof must **not** depend on OneDrive providing atomic file creation, bounded propagation, conflict-name semantics or a quiet period.

OneDrive only needs to transport immutable snapshot and target-bound ready/grant artifacts with the previously investigated documented per-file observation semantics.

Correct the M02 report distinction explicitly:

- **protocol safety** comes from source-directed target binding + durable source relinquishment ordering;
- **transport evidence** shows that the immutable files can be published/observed/validated through the actual OneDrive environment.

Retain the official `CF_PLACEHOLDER_STATE` values fixed during M02 and do not regress them.

## 12. Real Windows/OneDrive evidence

For final `FEASIBLE — evidence sufficient`, obtain real transport evidence rather than simulation only.

### Minimum Device A -> Device B transport test

Using synthetic M02 data only:

1. both machines run the same M02 revalidation commit;
2. both validate the same registered OneDrive root using documented Windows sync-root metadata;
3. Device A publishes one synthetic snapshot and waits for correct `IN_SYNC` observation;
4. Device A persists synthetic relinquishment state before producing the target-bound marker;
5. Device A publishes/synchronizes the B-targeted marker;
6. Device B independently observes the files;
7. Device B validates snapshot/marker/source/target/checksum/SQLite integrity;
8. Device B's model allows acquisition;
9. if practical, Device C or a second synthetic identity on B verifies wrong-target rejection; real third-PC evidence is optional in M02 because three-device protocol safety is automated.

Repeat at least two monotonically increasing handoff versions.

Real experiments verify transport and actual Windows API behavior; they do not replace the deterministic safety proof.

### If Codex cannot access two real PCs

Do not fake evidence and do not mark FEASIBLE.

Finish all code/simulation, then produce concise Device A / Device B commands for the operator. Until returned evidence is incorporated, conclude:

`PARTIAL — real multi-device evidence still required`

## 13. Required automated tests

At minimum add/adjust tests for:

### Authority model

- initial single authoritative device;
- close-and-retain restart;
- explicit target selection;
- invalid self-target;
- invalid/unpaired target;
- target immutable after transfer commitment;
- centralized write decision.

### Source ordering

- marker impossible before relinquishment;
- source write blocked immediately after durable relinquishment;
- source restart after relinquishment remains blocked;
- source retry same transfer allowed;
- source retarget blocked;
- rollback/cancel after relinquishment blocked.

### Failure injection

- failure before relinquishment safely retains authority;
- snapshot sync failure creates no release marker;
- crash after relinquishment before marker;
- marker creation failure;
- marker sync failure;
- restart/retry idempotence.

### Receiver validation

- valid exact target;
- wrong target;
- source==target;
- missing snapshot;
- missing marker;
- lineage mismatch;
- generation mismatch;
- handoff-version mismatch;
- source mismatch;
- target mismatch;
- checksum/size mismatch;
- SQLite integrity failure;
- stale/replayed handoff;
- malformed/unsupported metadata.

### N-device safety/liveness

- three-device arbitrary visibility ordering;
- non-target sees complete files first;
- target delayed/offline;
- source already relinquished;
- no execution yields >1 business writer;
- valid complete path yields exactly the selected target as writer.

### Cloud state regression

Continue tests against documented independent numeric values including:

- `0x00000009` -> `PLACEHOLDER | IN_SYNC` -> InSync;
- `0x00000011` -> Partial;
- `0x00000021` -> Partial;
- `0xffffffff` -> Invalid;
- unknown bits fail closed.

## 14. Evidence report

Create:

`docs/implementation/milestone-02-directed-handoff-revalidation-report.md`

Do not rewrite history in `milestone-02-feasibility-report.md`; that file remains the evidence for why the amendment was necessary.

The new report must include:

- branch/commits;
- exact amended specification sources;
- authority-state model;
- source relinquishment durability mechanism used in the feasibility proof;
- crash-point matrix;
- target-validation matrix;
- N-device safety/liveness results;
- Windows/OneDrive API surface used;
- automated build/test totals;
- real Device A/B evidence or explicit missing evidence;
- mapping to amended AC-STO-002 through AC-STO-005 and AC-STO-007 through AC-STO-010;
- final gate conclusion.

## 15. Gate conclusion

End M02 revalidation with exactly one of:

- `FEASIBLE — evidence sufficient`
- `PARTIAL — real multi-device evidence still required`
- `BLOCKED — specification/architecture amendment required`

Use `BLOCKED` if the amended protocol itself reveals another material architecture/specification contradiction.

Use `PARTIAL` only when the protocol/simulation is conforming but required real OneDrive transport evidence is genuinely unavailable.

Use `FEASIBLE` only when deterministic safety/liveness proof and required real transport evidence are both sufficient.

M02 becoming FEASIBLE still does not mark final M06/M07/M08-owned acceptance criteria Passed; it only closes the early feasibility gate.

## 16. Verification commands

Run at least:

```powershell
dotnet --info
dotnet restore Sushi81.Pos.sln
dotnet build Sushi81.Pos.sln -c Release --no-restore
dotnet test Sushi81.Pos.sln -c Release --no-build
dotnet publish src/Sushi81.Pos.Desktop/Sushi81.Pos.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false
```

Requirements:

- existing M01 tests remain green;
- existing corrected M02 Cloud Files tests remain green;
- all new directed-handoff tests pass;
- Release build has 0 warnings / 0 errors;
- no real customer/order/payment/credential/business data is committed.

## 17. Git and PR completion

All revalidation work belongs on:

`codex/m02-directed-handoff-revalidation`

When the executable evidence available to Codex is complete:

1. update `docs/implementation-status.md` on the branch with the actual gate result/evidence;
2. create the new revalidation report;
3. commit and push;
4. open a new independent PR to `main`;
5. do not merge it;
6. do not start M03.

If operator Device A/B testing is still required, the PR may remain `PARTIAL` while preserving all prepared code/evidence.

## 18. Required final reply from Codex

Report, in order:

1. final gate conclusion;
2. branch;
3. latest commit SHA;
4. PR number + URL;
5. files/projects changed;
6. authority state machine summary;
7. exact durable-relinquishment-before-marker enforcement;
8. crash-point/failure matrix result;
9. target-binding/wrong-target result;
10. N-device safety result;
11. normal-path liveness result;
12. total automated test results;
13. CI result;
14. real OneDrive Device A/B evidence;
15. if missing, exact Device A and Device B commands for the operator;
16. amended AC-STO preparation status;
17. blockers/limitations;
18. explicit confirmation that M03 was not started, no specification was weakened, and no sensitive/business data was committed.