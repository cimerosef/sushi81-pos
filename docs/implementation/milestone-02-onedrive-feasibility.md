# M02 — OneDrive single-writer feasibility gate

**Status:** Approved task definition — implementation not started  
**Phase:** 6 — Implementation  
**Milestone:** M02  
**Scope type:** Technical feasibility proof and evidence; no business-feature implementation

## 1. Codex mission

Prove or disprove whether the frozen V1 Windows/OneDrive architecture can implement the required fail-closed N-device single-writer handoff/acquisition protocol without weakening the Approved specification.

M02 is a feasibility gate. It is deliberately before Catalogue/Order implementation because a false assumption here would make later business work unsafe or disposable.

GitHub `main` is authoritative. Read the complete repository instruction/specification set before editing, especially:

- `AGENTS.md`;
- `docs/v1-specification-freeze.md`;
- `docs/implementation-plan.md`;
- `docs/implementation-status.md`;
- `docs/acceptance-criteria.md`;
- `docs/architecture.md`;
- `docs/storage-strategy.md`;
- all Approved records under `docs/decisions/` that touch storage, authority, read-only mode or printing;
- `src/README.md`;
- `tests/README.md`;
- M01 implementation currently on `main`.

Do not use prior chat history, legacy Excel/VBA behavior or assumptions about two specific PCs as specification.

## 2. Frozen invariants that M02 must not weaken

M02 must preserve these V1 invariants exactly:

1. Every device keeps its active `live.db` under local `%LOCALAPPDATA%\Sushi81 POS\Data\live.db`.
2. `live.db` is never the active database inside OneDrive.
3. The architecture supports N paired devices; exactly-two-device SHOP/HOME assumptions are forbidden.
4. At most one device may be authoritative/writable at any moment.
5. Every other device is non-authoritative/read-only until valid exclusive acquisition completes.
6. Normal authority transfer requires a complete validated formal handoff.
7. Formal handoff publishes an immutable SQLite-safe snapshot first, confirms required synchronization, then publishes a matching ready marker and confirms marker synchronization.
8. A snapshot without its matching ready marker is not a released handoff.
9. Snapshot/marker lineage, generation, version and checksum must match.
10. SQLite integrity must pass before a snapshot can be accepted.
11. Unknown, partial, corrupt, stale, offline, synchronization-error or unresolved contention state must never default to writable.
12. No ordinary silent force takeover from a stale copy is permitted.
13. Explicit Disaster Recovery is separate from normal acquisition and advances lineage generation.
14. Old-generation devices cannot later resume writes silently.
15. OneDrive is transport/recovery/archive storage, not a live database server and not a row-level merge system.
16. Non-authoritative printing semantics are not changed by M02.

If implementation evidence shows that one of these guarantees cannot be achieved with the frozen architecture, stop and conclude `BLOCKED — specification/architecture amendment required`. Do not silently reduce the guarantee.

## 3. M02 is not M07

Do not implement the full production pairing/handoff/disaster-recovery workflow in M02.

M02 may implement:

- narrow reusable Windows/OneDrive observation primitives where justified;
- pure protocol models/state machines needed to test safety properties;
- deterministic simulation/failure tests;
- a non-shipping feasibility harness under `tools/` or an equivalently isolated test/harness location;
- synthetic SQLite snapshot publication/validation experiments;
- evidence/reporting code.

M02 must not implement:

- Catalogue, BusinessSettings, Orders, Payments, pricing, VAT or POS business UI;
- final pairing/settings UI;
- final handoff-on-application-exit wiring;
- final acquisition UI;
- Disaster Recovery UI;
- final M06/M07 write-authority integration across business use cases;
- full cloud checkpoint scheduler;
- archive/printing/export behavior;
- any workaround that changes frozen user workflow or storage semantics.

If a small low-level component is clearly reusable for M07, it may live in an appropriate existing production technical layer, but it must remain unconnected to real write-authority activation in M02.

## 4. Primary questions M02 must answer

M02 must answer each question with executable evidence rather than assumption.

### 4.1 Can the application reliably identify the selected OneDrive sync root?

Prove that a proposed shared root can be recognized as lying under a registered Windows cloud-storage sync root and, where practical, that the provider/root metadata identifies OneDrive rather than an arbitrary local directory.

Preferred investigation order:

1. first-party Windows Storage Provider / Cloud Files information;
2. documented file-system/cloud placeholder metadata;
3. small documented interop/PInvoke where required.

Do not depend on Explorer icon scraping, OCR, GUI automation or undocumented OneDrive internal databases.

A plain local folder that merely happens to be named `OneDrive` must not pass the proof.

### 4.2 Can the application observe publication state for an exact file?

For a synthetic file created beneath the selected OneDrive root, determine whether the application can distinguish at least:

- not a recognized cloud placeholder/sync item;
- pending/not in sync;
- in sync with cloud;
- partial/not locally ready where applicable;
- invalid/unknown/error.

Investigate documented Windows Cloud Files state such as `CF_PLACEHOLDER_STATE_IN_SYNC` and registered sync-root information.

Do not equate file existence with successful cloud publication.

Unknown/unsupported state must be fail-closed.

### 4.3 Can formal publication ordering be proven?

Using synthetic SQLite data only, exercise this order:

1. create a complete SQLite-safe snapshot locally using M01-safe primitives or an equivalent isolated test source;
2. validate SQLite integrity;
3. compute checksum;
4. assign synthetic lineage/generation/handoff version/source-device metadata;
5. publish immutable snapshot into a synthetic `Handoff` test area;
6. wait for a documented, observable confirmation that the snapshot is synchronized;
7. only then create/publish the matching ready marker;
8. wait for marker synchronization;
9. on a receiving device, require both locally available files and revalidate metadata/checksum/integrity.

The experiment must prove that marker publication cannot race ahead of snapshot publication in the implementation under test.

### 4.4 Is per-file `IN_SYNC` sufficient for the required meaning?

Do not assume that a local `IN_SYNC` indication automatically proves every property required by the frozen protocol.

Explicitly determine what it proves and what it does not prove, including:

- whether the local change has reached the cloud;
- whether a second client can subsequently observe/download the same immutable file;
- whether the receiving client can independently confirm its local copy is synchronized/current;
- whether it says anything about global absence of competing acquisition claims.

Record the distinction in the M02 feasibility report.

### 4.5 Can competing acquisition attempts be made safely exclusive?

This is the highest-risk M02 question.

The frozen specification requires unresolved competing acquisition never to activate multiple writers. M02 must therefore test or formally reason about two or more devices attempting to acquire the same released handoff nearly simultaneously.

Do not rely on local NTFS file locks as cross-device locks. Do not assume creation of the same local OneDrive filename is a globally atomic compare-and-swap. Do not assume deterministic conflict renaming creates a safe lease.

Investigate whether the approved local OneDrive synchronization model exposes a documented primitive sufficient to establish exclusive authority across clients.

If proposing a claim/election protocol over immutable files, prove the safety property under delayed/reordered synchronization and near-simultaneous claims. A fixed sleep/quiet period is not sufficient evidence unless a documented upper-bound/guarantee exists that makes it safe.

Safety requirement:

> For the same lineage/generation/released handoff, no execution allowed by the tested transport model may allow two distinct devices to both conclude `authoritative/writable`.

Liveness is secondary to safety. It is acceptable for uncertain contention to stay blocked; it is not acceptable for uncertainty to produce two writers.

### 4.6 Does the protocol remain N-device rather than two-device?

At minimum, deterministic tests/simulation must include three distinct device IDs.

Prove there are no fixed `shop/home`, A/B or exactly-two-device slots in protocol state or arbitration logic.

### 4.7 Do failure states remain fail-closed?

Exercise or simulate at least:

- OneDrive unavailable/not running where observable;
- network offline;
- sync paused where reproducible;
- item still syncing/pending;
- item state unknown/invalid;
- snapshot synchronized but ready marker absent;
- ready marker present locally but snapshot missing;
- checksum mismatch;
- lineage mismatch;
- generation mismatch;
- stale handoff version;
- malformed metadata;
- SQLite integrity failure;
- competing claims unresolved;
- one acquisition participant disappears mid-protocol.

None may yield writable authority.

## 5. Technical research boundaries

Use documented Windows capabilities first.

Relevant API families to investigate include:

- Windows Cloud Files API (`CfGetPlaceholderStateFromAttributeTag`, `CfGetPlaceholderStateFromFileInfo` or equivalent documented state query);
- `CF_PLACEHOLDER_STATE`, especially `IN_SYNC`, `PARTIAL`, `PARTIALLY_ON_DISK`, `INVALID`;
- `Windows.Storage.Provider.StorageProviderSyncRootManager.GetCurrentSyncRoots()` and `StorageProviderSyncRootInfo` metadata such as Path/Id/ProviderId where usable from the .NET desktop target;
- ordinary supported Windows file APIs for atomic local staging/promotion;
- existing M01 SQLite-safe snapshot/checksum/integrity primitives.

A small pinned Windows SDK/interoperability dependency may be added only if genuinely needed and compatible with the approved .NET 10/WPF architecture. Prefer a small documented P/Invoke over a large framework when both are equally reliable.

Do not use as a production proof:

- Explorer overlays/icons/status text scraping;
- OCR/screenshots;
- undocumented OneDrive SQLite/databases/internal cache formats;
- automating the OneDrive GUI;
- registry heuristics as the sole proof of synchronization state;
- filesystem modified timestamps as synchronization proof;
- raw copy of an open SQLite live database;
- polling a web page;
- reverse-engineered private OneDrive network protocols.

## 6. No silent architecture expansion

Do not introduce Microsoft Graph, OAuth login, a hosted coordination service, Azure, a remote database, Redis, SMB server locking or another cloud/backend merely to make M02 pass.

If a globally atomic server-side primitive appears necessary for exclusive acquisition, document that finding and stop with `BLOCKED — specification/architecture amendment required`.

A Graph/backend design may be mentioned only as an amendment option after the blocker is proven; it must not be implemented under M02 authorization.

## 7. Required feasibility harness

Create one small non-shipping feasibility harness, preferably:

`tools/Sushi81.Pos.OneDriveFeasibility/`

unless the existing project structure makes a different isolated location materially cleaner.

The harness must use synthetic data only and must not read real Sushi81 business databases.

It should provide clear commands/subcommands or equivalent operations to perform reproducible experiments. Exact naming is technical, but capabilities must include:

1. enumerate registered sync roots and show non-sensitive technical identity/path information;
2. validate whether a supplied test root is beneath an acceptable registered sync root;
3. create a synthetic immutable payload and report state transitions until synchronized or timed out;
4. create/publish a synthetic matching ready marker only after payload synchronization;
5. inspect/validate an existing synthetic snapshot+marker unit;
6. create/observe synthetic acquisition claims needed by the candidate protocol;
7. print machine-readable and human-readable experiment results without customer/business data.

The harness must never make the actual POS writable or alter production `live.db`.

If Windows SDK/OneDrive behavior cannot be exercised in CI, isolate the platform dependency behind a small interface and test pure interpretation/protocol logic deterministically.

## 8. Synthetic handoff metadata

For the feasibility proof, define a minimal stable technical metadata record carrying at least:

- format/protocol version;
- lineage ID;
- generation;
- handoff version;
- source device ID;
- snapshot checksum algorithm and checksum;
- snapshot byte length if useful;
- application-generated creation timestamp;
- marker type/state.

Exact filenames/JSON field names are technical details, but parsing must be strict and deterministic. Unknown required values, malformed metadata or unsupported protocol version must fail closed.

Do not turn feasibility metadata into a new business data model.

## 9. Automated test inventory

Add deterministic automated tests covering at least:

### 9.1 Sync-state interpretation

- recognized in-sync state accepted only for the narrow publication-state decision it actually proves;
- pending/partial/invalid/unknown state rejected;
- non-cloud/local path rejected when cloud synchronization proof is required;
- platform/API error returns a blocked/unknown result rather than writable success.

### 9.2 Handoff unit validation

- valid snapshot+marker pair;
- missing marker;
- missing snapshot;
- malformed metadata;
- unsupported metadata version;
- checksum mismatch;
- size mismatch if modeled;
- lineage mismatch;
- generation mismatch;
- handoff-version mismatch;
- stale version;
- wrong source metadata where relevant;
- SQLite integrity failure;
- deterministic repeated validation.

### 9.3 Publication ordering

- ready marker cannot be published before snapshot synchronization success;
- timeout/error prevents marker publication;
- failed marker synchronization never reports successful formal release;
- retry does not mutate an already completed immutable handoff unit.

### 9.4 Authority/acquisition safety

Using an in-memory/fake delayed synchronization transport capable of reordering visibility between devices, test at least:

- one device acquires an uncontested released handoff;
- two devices attempt acquisition nearly simultaneously;
- three devices attempt/observe claims;
- different visibility orders on different devices;
- delayed claim propagation;
- duplicate/replayed claim observation;
- participant disappears during acquisition;
- stale generation claimant;
- stale handoff-version claimant;
- unknown transport state;
- repeated evaluation is deterministic;
- unresolved contention never becomes writable;
- no simulated execution produces two simultaneous authoritative results from one released handoff.

If the candidate protocol cannot satisfy the final safety property in the simulator, stop; do not hide the failing scenario.

## 10. Real Windows/OneDrive experiment matrix

Automated simulation is necessary but not sufficient for the transport assumptions.

Use real OneDrive with synthetic M02 files. Record Windows build, OneDrive environment/version when discoverable without fragile scraping, selected registered sync-root identity, device IDs and timestamps. Do not record account email or personal file names.

### 10.1 Minimum one-device experiments

- selected test root recognized beneath a registered sync root;
- synthetic payload created;
- initial/pending state observed when possible;
- transition to confirmed in-sync observed;
- marker publication occurs only after payload confirmation;
- marker reaches confirmed in-sync state;
- offline/paused/error/unknown cases exercised where practical and shown fail-closed.

### 10.2 Minimum two-device experiments

Using two real Windows devices synchronized to the same synthetic M02 test root:

1. Device A publishes a synthetic handoff snapshot and marker in the required order.
2. Device B observes both, confirms local availability/sync state and validates metadata/checksum/integrity.
3. Repeat several times with different versions to show monotonic handling.
4. Start near-simultaneous acquisition attempts from A and B against the same released version.
5. Repeat with synchronization delayed/paused on one side and then resumed where feasible.
6. Record whether the candidate protocol can prove exclusive authority without relying on timing luck.

### 10.3 Three-device evidence

At least three-device behavior must be covered by deterministic automated protocol tests.

If a third real OneDrive-synchronized Windows device is readily available, repeat a contention experiment with three real devices. If not, do not fabricate evidence; explicitly record that real transport evidence is two-device while three-device protocol safety is simulation/design evidence pending M07 production acceptance.

## 11. Manual evidence workflow if Codex cannot access multiple real PCs

Codex must not claim real multi-device success from a single machine.

If its environment cannot run the required two-device OneDrive experiments:

1. finish the harness and all deterministic automated tests;
2. create concise exact commands for Device A and Device B;
3. make outputs easy for the operator to copy back without sensitive information;
4. update the feasibility report as `PARTIAL — real multi-device evidence still required`;
5. do not mark M02 Passed;
6. do not start M03.

The operator's later returned results may be appended to the same M02 evidence/report and used to close the gate if sufficient.

## 12. Required feasibility report

Create:

`docs/implementation/milestone-02-feasibility-report.md`

The report is evidence, not a specification amendment.

It must include:

- exact M02 branch and commit(s);
- Windows/.NET environment;
- OneDrive/sync-root environment without personal account data;
- documented APIs/primitives actually tested;
- what `IN_SYNC` or equivalent was observed to prove and not prove;
- harness commands used;
- automated test inventory/results;
- real one-device experiment results;
- real two-device experiment results or explicit reason not available;
- three-device simulation/results;
- contention scenarios and outcome;
- failure/offline/paused/unknown-state results;
- any transport limitation found;
- acceptance-criteria preparation status for AC-STO-002 through AC-STO-005 and AC-STO-007 through AC-STO-010;
- one final gate conclusion using exactly one of the strings in section 13.

Do not include OneDrive account email, customer/order data, credentials, access tokens or personal file listings.

## 13. Required final gate conclusion

M02 must end with exactly one of:

### `FEASIBLE — evidence sufficient`

Use only if evidence demonstrates that the frozen architecture can implement the required fail-closed publication and exclusive acquisition semantics. Real Windows/OneDrive multi-device evidence must be sufficient for the transport assumptions; pure in-memory simulation alone is not enough.

This conclusion authorizes preparation of M03 but does not mark final M07-owned acceptance criteria Passed.

### `PARTIAL — real multi-device evidence still required`

Use when deterministic code/tests are complete and no blocker has been proven, but required real multi-device OneDrive evidence is still missing or inconclusive.

M03 must not start until the missing M02 gate evidence is completed or the project explicitly decides otherwise through an approved plan/specification change.

### `BLOCKED — specification/architecture amendment required`

Use when evidence shows that the frozen guarantees cannot be achieved with the approved OneDrive/local-filesystem model without a material architecture or workflow change.

Describe the minimal blocker precisely. Do not implement the amendment. M03 does not start until the specification is amended and the implementation plan is re-authorized.

## 14. Acceptance-criteria handling

M02 prepares evidence for:

- AC-STO-002 — N-device single writer;
- AC-STO-003 — formal handoff;
- AC-STO-004 — handoff validation/exclusive acquisition;
- AC-STO-005 — no silent force takeover;
- AC-STO-007 — handoff retention semantics;
- AC-STO-008 — cloud recovery-checkpoint transport assumptions;
- AC-STO-009 — generation invalidation assumptions;
- AC-STO-010 — read-only authority boundary assumptions.

Do not mark these final criteria `Passed` merely because M02 proves feasibility. Their owner milestones remain M06/M07/M08 as recorded in `implementation-status.md`.

M02 should record them as feasibility evidence / Partial preparation only where appropriate.

## 15. Repository/status discipline

Start M02 only from the latest `main` containing the merged M01 result and this task definition.

Use a dedicated branch, recommended:

`codex/m02-onedrive-feasibility`

Do not continue work on `codex/m01-foundation`.

Before coding:

- fetch/pull latest `main`;
- confirm M01 merge is present;
- confirm working tree is clean;
- create/switch to the M02 branch.

At completion:

- update `docs/implementation-status.md` with concrete M02 evidence and final gate conclusion;
- add `docs/implementation/milestone-02-feasibility-report.md`;
- keep M03 `Not started` unless the gate conclusion is `FEASIBLE — evidence sufficient` and the repository is subsequently explicitly authorized for M03;
- open a separate PR to `main`;
- do not merge it yourself unless explicitly instructed;
- do not start M03.

If M02 is `PARTIAL` or `BLOCKED`, the PR may still contain the harness/tests/evidence needed to preserve the finding, but it must not include a speculative architecture workaround.

## 16. Build and verification

Run at least:

```powershell
dotnet --info
dotnet restore Sushi81.Pos.sln
dotnet build Sushi81.Pos.sln -c Release --no-restore
dotnet test Sushi81.Pos.sln -c Release --no-build
```

If the feasibility harness is intentionally outside the main solution, explicitly restore/build/test it as well and document why. Prefer keeping it in normal repository verification unless doing so would contaminate shipping output.

Existing M01 tests must remain green.

Any new Windows-specific dependency must be exact-version pinned and explained.

## 17. Completion report to the operator

At the end, report:

1. final gate conclusion string;
2. branch and latest commit SHA;
3. PR number/URL;
4. changed files/projects;
5. documented Windows APIs actually used;
6. what sync state can and cannot prove;
7. candidate acquisition protocol tested, if any;
8. all automated test results;
9. all real OneDrive experiment results available;
10. any manual two-device commands still required;
11. all failure/unknown cases tested;
12. acceptance-criteria preparation status;
13. any blocker requiring specification amendment;
14. confirmation that no M03/business features were started and no real business/sensitive data was added.

M02 is not complete merely because the harness can see OneDrive files. The gate exists to establish whether the frozen safety guarantees are implementable.