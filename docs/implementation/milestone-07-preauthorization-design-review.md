# M07 pre-authorization design review — pairing, target-directed formal handoff and disaster recovery

**Status:** Pre-authorization design review — NOT an implementation contract and NOT Codex authorization  
**Prepared:** 2026-09-07  
**Milestone:** M07 — Pairing, target-directed formal handoff and disaster recovery  
**Execution gate:** CLOSED; no implementation branch/PR/handoff is authorized by this document  
**Authority:** Approved GitHub V1 baseline/decisions remain authoritative; this record identifies proposed implementation choices and material decisions that require project-owner approval before implementation preparation.

## 1. Verified entry baseline

M06 — Local recovery and authoritative/read-only enforcement — is Passed and merged through PR #11 at merge commit `2c5eb52740d0c12e3e837579ecceac6d0600b59e`.

Accepted M06 production repair head: `4a0c1ca9e44a6c48899e6ef8dc211172371e4d20`. Final M06 documentation/PR head: `86326d81551aa4cb5cdcbc6826b8c740317b34c4`. Release tests: 364/364 Passed. Project-owner Windows/WPF manual acceptance: Passed.

At preparation start GitHub issue #4 was CLOSED with no active implementation PR/branch/handoff; no M07 implementation PR or branch existed; M07 was not implementation-authorized and M08+ were not started.

M06 already supplies the production safety seam M07 must extend rather than duplicate:

- one persistent production authority state path and independent bootstrap marker/anchor;
- `Authoritative`, `NonAuthoritativeReadOnly`, `Transitioning`, `RecoveryRequired` presentation/write states;
- centralized Application-layer `IWriteAuthorityGuard` as the final business-write boundary;
- fail-closed startup rather than `missing state => writable`;
- validated latest-five local Recovery snapshots;
- post-commit recovery scheduling/debounce/single-flight/shutdown flush;
- persistent localized read-only/transition/recovery-required UI.

M02 already proved the normal target-directed transfer protocol and GitHub Release Asset transport semantics. Those feasibility implementations remain isolated under `tools/`; M07 must port/consolidate their proven semantics into the M06 production authority model rather than reference or create a second production authority architecture.

## 2. Already-decided M07 semantics

The following are fixed by Approved baseline/decision records and are not open implementation choices:

1. Normal authority movement is source-directed to exactly one explicit paired target. Generic claims/election/competitive acquisition are forbidden.
2. Normal close distinguishes **Close and retain authority** from **Transfer authority and close**. Retain is the safe/default close meaning.
3. Normal handoff snapshot transport is the configured dedicated private GitHub Release Asset repository, not the source-code repository and not OneDrive synchronization state.
4. Snapshot publication requires strict GitHub server receipt: HTTP 201, uploaded state, exact name/size/asset ID and matching SHA-256 digest.
5. Source must durably relinquish business-write authority before a target-releasing grant can exist.
6. After durable relinquishment the source cannot resume ordinary writes or retarget; it may only retry the same immutable transfer.
7. Target must validate exact target binding, paired membership, lineage, generation, version, transfer identity, asset identity, size/hash and SQLite integrity and must durably acquire before writes are enabled.
8. Non-target devices remain read-only and never compete.
9. Normal GitHub handoff retains the newest three complete validated snapshot+grant units; cleanup is post-completion and non-authority-critical.
10. Recovery-only cloud checkpoints are change-triggered, no more than once per 15 minutes in normal publication, retain the newest five validated units and never release authority or act as ordinary handoffs.
11. Disaster Recovery is explicit, exceptional and generation-advancing; it is not ordinary target substitution.
12. M07 must preserve local-first authoritative operation while ordinary network-dependent handoff/recovery publication may be unavailable offline.

## 3. Consistency findings requiring baseline cleanup

These are not new product decisions; later Approved decisions already supersede them:

- `storage-strategy.md` §7 still describes OneDrive `Handoff/` as if it stored normal handoff snapshot/marker units, and §9.1 still says subsequent normal authority transfer uses that root. The document header, GitHub transport decision and acceptance criteria already make GitHub Release Assets the normal handoff transport. `System/`, `DisasterRecovery/` and `Archive/` remain valid OneDrive purposes.
- `product-requirements.md` NFR-004 still contains the older phrase “OneDrive handoff/disaster-recovery”; normal handoff must be described as GitHub target-directed handoff, with OneDrive retained for recovery/archive.
- living implementation-status wording must record M06 Passed/merged and M07 next/not authorized while preserving its historical evidence sections unchanged or archived exactly.

These wording residues must be aligned before M07 implementation authorization is published.

## 4. Material decision gap A — pairing authorization

The baseline defines immutable `device_id`, human-readable display name, lineage/generation, N-device membership and the rule that pairing itself never grants authority. It says a new device joins “using the approved pairing flow”, but no Approved record defines that flow.

Because paired membership controls target eligibility, the missing flow is material and must not be invented by Codex.

### Recommended pairing model P1

Project-owner approval is requested for this model:

- A new installation creates its immutable random `device_id` and editable display name locally.
- “Join existing Sushi81 POS” creates a generation-bound pairing request in the selected lineage `System/Pairing/Requests` area. A random request identity/nonce is immutable; the UI displays a short human comparison code and the device display name/short ID.
- Only the **current authoritative device** may approve a pairing request. Approval is explicit and shows the exact requesting device. Non-authoritative/read-only devices cannot approve pairing.
- Approval is immutable and bound to lineage, current generation, request identity and exact new `device_id`. Pairing never changes authority.
- Current-generation paired membership is reconstructed from validated immutable approvals rather than relying on a single conflict-prone mutable “paired-set” file.
- The authoritative device creates a fresh SQLite-safe **pairing seed snapshot** plus checksum/integrity metadata for read-only initialization. This is not a normal handoff and carries no authority grant.
- The new device waits until the approval/seed are locally available through the shared OneDrive System area, validates lineage/generation/device binding/hash/SQLite integrity, installs the seed as its local `live.db`, and persists `NonAuthoritativeReadOnly`.
- Incomplete/corrupt/stale-generation pairing artifacts fail closed.
- A Windows reinstall that loses `device_id` is a new device and must pair again.
- After a Disaster Recovery generation advance, an old-generation device must be reinitialized into the current generation before it is again an eligible target.

This preserves N-device behavior without introducing competitive authority acquisition.

## 5. Material decision gap B — Disaster Recovery fencing

The Approved baseline says Disaster Recovery advances generation and old-generation devices cannot resume writing. However, it also permits an authoritative local device to continue ordinary business writes during temporary network loss.

Those two requirements create a real distributed-systems boundary: if an old authoritative device is still running while disconnected, another device cannot remotely and instantaneously make the old process stop writing merely by publishing `generation + 1`. Local-only software on the disconnected old device cannot observe that remote event.

The implementation therefore needs an explicit approved safety model rather than pretending generation metadata alone is a physical remote kill switch.

### DR-A — operationally fenced exceptional recovery — RECOMMENDED

- Preserve normal local-first offline writes on the current authoritative device.
- Disaster Recovery is allowed only after explicit operator confirmation that the previously authoritative/designated-target device is genuinely unavailable and **must remain stopped/quarantined and must not be used for business writes again until it has been reinitialized**.
- Disaster Recovery itself requires online coordination and may not be completed offline.
- Before the recovery device becomes locally authoritative, it must obtain a server-acknowledged, immutable, deterministic next-generation recovery activation that has a proven single-winner/create-once property. M07 must prove the chosen GitHub primitive with deterministic tests and a disposable real private-repository race/retry drill before broad DR implementation; if the primitive cannot prove one winner, M07 stops for architecture amendment.
- Crash after remote activation but before local activation yields zero writable recovery devices; only the same recovery identity/device may resume that activation.
- Returning old-generation devices become stale/read-only when current generation is observed and require reinitialization.
- The product documentation must state truthfully that software cannot revoke an already-running, disconnected old writer if the DR quarantine precondition is violated.

This option preserves AC-PROD-002 local-first availability and keeps DR rare/explicit, at the cost of an operational safety precondition during genuine disaster recovery.

### DR-B — strict renewable software lease

- Introduce a remotely renewed authority lease/fencing token.
- Business-write eligibility expires when the lease cannot be renewed; Disaster Recovery waits for/establishes a new fencing epoch before another device writes.
- This gives stronger software-only fencing against a disconnected stale writer after lease expiry, but authoritative POS operation eventually becomes read-only during Internet/GitHub loss.
- Lease duration becomes a material availability parameter and the design adds a continuing network dependency/coordinator semantic that weakens the approved local-first boundary.

This option is not recommended under the project priority order unless the project owner prefers stronger automatic fencing over local offline availability.

## 6. Material decision gap C — Disaster Recovery data source

The baseline talks about selecting a recovery checkpoint, but a fully completed GitHub handoff unit can be newer than the latest recovery-only OneDrive checkpoint. If the designated target is lost after the source completed that handoff, ignoring the completed handoff could discard otherwise validated newer business data.

### Recommended recovery-source model R1

Disaster Recovery selects the **freshest validated safe recovery candidate** for the current generation:

1. a complete GitHub handoff snapshot+grant unit is eligible because the matching grant proves the source crossed durable relinquishment for that exact snapshot; or
2. a validated OneDrive recovery-only checkpoint is eligible;
3. a GitHub snapshot without a valid matching grant is never eligible for DR, because it may have been uploaded before the source relinquished authority;
4. the UI shows candidate type/source/time/version and warns explicitly about the possible data-loss window before confirmation.

Whichever candidate is selected, DR creates a new generation and never treats the old target binding as a normal transfer acquisition.

R1 minimizes avoidable data loss but requires an explicit acceptance/spec clarification because AC-STO-009 currently uses “checkpoint” terminology.

## 7. Proposed production durable authority model

M07 should evolve the existing M06 `Config/authority-state.json` to one new versioned canonical document. It must not add a second independent production cursor that can disagree with M06 state.

The detailed document should durably carry at least:

- schema/revision;
- immutable local `device_id`;
- `lineage_id`;
- current generation;
- high-water handoff version;
- detailed authority phase;
- current immutable transfer identity/evidence when applicable;
- current immutable Disaster Recovery identity/evidence when applicable;
- update timestamp.

Detailed phases should distinguish at least:

- initial/uninitialized;
- authoritative;
- closed with retained authority;
- transfer preparing;
- relinquished/pending grant — irreversible;
- released/non-authoritative;
- target acquisition pending;
- non-authoritative read-only;
- stale generation;
- Disaster Recovery pending;
- recovery required.

The existing coarse `WriteAuthorityState` used by WPF and `IWriteAuthorityGuard` is **derived** from the canonical detailed phase. It is not a second independent source of authority truth.

Every established-state ambiguity, missing required identity/artifact, unsupported schema, contradictory generation/version or missing pre-existing `live.db` remains fail-closed.

### M06-to-M07 migration

A valid accepted M06 installation may migrate its existing authority document only through an explicit version migration that preserves its current coarse state and existing marker/anchor evidence.

- A valid M06 `Authoritative` installation may initialize `device_id`/`lineage_id`/initial generation/high-water state once.
- Existing M06 non-authoritative/transitioning/recovery-required state must never be promoted merely because M07 is installed.
- Missing established M06 state/marker/anchor/database remains fail-closed; M07 must not add a new `missing => authoritative` path.

Safety-critical JSON replacement should use the proven M02 durability shape: temporary file, write-through/flush-to-disk, atomic replace/move, then reopen/revalidate before a later authority-sensitive step proceeds.

## 8. Proposed normal transfer state machine

### Authoritative → preparing

1. Operator selects **Transfer authority and close** and exactly one current-generation eligible paired target.
2. Application switches the centralized write guard to transitioning before starting transfer preparation; new business writes are rejected at the Application boundary.
3. Accepted writes/local recovery flush complete.
4. Persist an immutable transfer identity including transfer ID, lineage, generation, new monotonic handoff version, source and target.
5. Create a SQLite-safe complete snapshot, validate integrity, compute byte length/SHA-256 and reserve a non-overwriting timestamp filename.

### Preparing → durable relinquishment

6. Upload snapshot to the configured private GitHub Release Asset container.
7. Require/revalidate strict server receipt: 201, uploaded state, exact name/size/asset ID/digest.
8. **Exact source relinquishment durability point:** atomically persist/reopen/revalidate the source state as `RelinquishedPendingGrant` with the exact immutable transfer and snapshot receipt. From this completed durable commit onward the source can never return to ordinary business writes for this transfer.
9. Reconstruct the source guard from the durable state and prove it is non-writable before grant creation is allowed.

### Relinquished → released

10. Create the matching immutable target-bound grant only from the persisted relinquished state.
11. Upload/validate the grant strict GitHub receipt; retry/idempotency is restricted to the same immutable transfer and exact artifact identity.
12. Persist `ReleasedNonAuthoritative` with grant receipt.
13. Run newest-three retention cleanup after completion. Cleanup failure is logged/retryable and never rolls authority back.
14. Complete transfer-and-close.

### Failure rules

- Before step 8, a safe abort may return the source to Authoritative only when no target-releasing grant exists.
- After step 8, no Cancel-to-writable, retarget, replacement transfer version or new business write exists. Only same-transfer technical retry or explicit DR is available.
- Monotonic handoff versions need not be contiguous; implementation should prefer never reusing an allocated version/transfer identity after an abandoned preparation.

## 9. Proposed target acquisition

A non-authoritative device may acquire only a grant targeted to its immutable device ID and current paired lineage/generation.

1. Discover exact eligible grant; reject non-target/stale/old-generation/replayed units.
2. Validate grant protocol fields and exact GitHub snapshot asset identity/name/size/hash.
3. Download snapshot to staging.
4. Verify bytes/SHA-256/SQLite integrity/schema.
5. Persist `TargetAcquisitionPending` while the write guard remains false.
6. Atomically replace local `live.db` with the validated staged database.
7. Reopen and revalidate the installed database.
8. Durably persist local Authoritative acquisition with exact transfer evidence.
9. Only after step 8 may the centralized write guard become Authoritative.

Crash after database replacement but before authority-state commit remains read-only on restart and must idempotently finish/revalidate the same acquisition; authority is never enabled first and data installed second.

A third/non-target device seeing the same assets remains read-only. No claim/election path exists.

## 10. Close-and-retain and WPF behavior

For an authoritative device, window close presents an explicit close decision rather than silently releasing authority:

- **Close and retain authority** — default/safe action; no network required, flush local recovery, preserve authority and close.
- **Transfer authority and close** — explicit target selector; with one eligible other target it may be preselected but confirmation remains explicit; with several, show human-readable name plus short device identity.
- **Cancel** — leave application running without authority change.

During pre-relinquishment transfer preparation, Cancel may abort safely. After durable relinquishment, retarget/cancel-to-writable must disappear; the UI shows that authority has already been relinquished and offers only same-transfer Retry/status/close behavior.

Non-authoritative devices close normally and never show an authority-transfer action they are not allowed to perform.

All new safety/status UI must be complete in French and Simplified Chinese and inherit the project's interactive-quality and control-state-preservation contracts.

## 11. Recovery-only OneDrive checkpoints

M07 should subscribe to the same post-commit change seam already used by M06 local recovery, but use an independent asynchronous scheduler/watermark:

- publish only when authoritative durable business data changed since the last completed checkpoint;
- enforce at most one normal checkpoint publication per 15-minute interval with an injectable clock;
- SQLite-safe snapshot + metadata including lineage/generation/source/checkpoint identity/change watermark/time/size/hash;
- finalize into the configured OneDrive `DisasterRecovery` area only after local validation;
- retain the latest five complete validated units; incomplete/corrupt units never count;
- a recovery checkpoint has no authority-release/acquisition semantics and is never consumed as ordinary read-only refresh/handoff;
- network/OneDrive unavailability delays checkpoints but does not change local authority or block ordinary authoritative POS operation;
- the application must not falsely present ordinary OneDrive desktop state as the strict server acknowledgement used by GitHub handoff. Real cross-device acceptance must prove that finalized checkpoints become visible/readable/valid on the other Windows device.

## 12. Proposed Disaster Recovery state machine — contingent on owner choice

For recommended DR-A + R1:

1. Enter a separately named Disaster Recovery workflow; ordinary handoff/acquisition stays blocked.
2. Show known normal-authority/target facts plus available validated recovery candidates, source/version/time and explicit possible-data-loss warning.
3. Require explicit confirmation that normal authority/target recovery is genuinely unavailable and the old authority/target device is quarantined and will not be used until reinitialized.
4. Validate the selected current-generation recovery candidate locally.
5. Determine next generation and immutable recovery identity.
6. Obtain the proven single-winner server-side recovery activation for that lineage/next generation. No remote activation = no new writer.
7. Persist `DisasterRecoveryPending` with activation receipt; guard remains false.
8. Restore candidate to staged/local `live.db`, validate hash/integrity/schema.
9. Durably persist the new-generation authoritative state/device and current-generation pairing/system metadata.
10. Only then set centralized guard Authoritative.
11. Any old-generation local state/grant remains ineligible; returning devices must reinitialize into the current generation.

Remote activation records are safety history and should be immutable/non-retained rather than deleted with normal handoff retention.

## 13. Offline behavior matrix

- Authoritative ordinary POS/catalogue/payment work: **available offline**.
- Close and retain authority: **available offline**.
- Normal transfer before durable relinquishment: requires GitHub; failure may abort/retain safely.
- Source after relinquishment: remains read-only offline and can only retry the same transfer after connectivity returns.
- Target acquisition: remains read-only unless the exact immutable target package has already been fully obtained/validated; unknown/missing remote evidence never becomes writable.
- Pairing/new-device hydration: requires the configured shared metadata/seed to become available; no offline self-pairing.
- Recovery checkpoint publication: may be delayed offline; no authority impact.
- Disaster Recovery: must not establish new authority offline under recommended DR-A; it requires the remote recovery activation.
- Returning old-generation device: behavior follows the owner-approved DR fencing model; it never silently consumes an old grant as current authority.

## 14. Credential/configuration boundary

Pure technical recommendation:

- GitHub owner/repository/release configuration is ordinary non-secret local configuration.
- Production fine-grained token is restricted to the dedicated handoff repository and stored using Windows protected credential storage (prefer Windows Credential Manager behind an application-owned credential interface).
- Environment-variable token injection remains test/tool-only.
- Token/Authorization headers never enter SQLite, handoff/recovery metadata, logs, screenshots/evidence or exception text.
- M07 setup includes a non-authority-changing connection/configuration validation path before the operator relies on transfer.

## 15. Automated evidence required before M07 can pass

At minimum:

### State/durability
- exhaustive valid/invalid state transitions and restart reconstruction;
- M06 authority-state migration including fail-closed missing/corrupt/partial upgrade paths;
- safety-critical atomic durable-write failure injection at every boundary;
- every M03–M05 business mutation still rejected by `IWriteAuthorityGuard` before SQL when not authoritative.

### Pairing
- N-device request/approval/seed initialization;
- only current authoritative source can approve;
- incomplete/corrupt/delayed/out-of-order/stale-generation pairing artifacts fail closed;
- pairing never grants write authority;
- reinstallation/new device ID and post-DR reinitialization.

### Normal handoff
- deterministic three-or-more-device interleaving proves at most one protocol-eligible writer and valid-path liveness;
- close-retain restart;
- wrong target/non-target/replay/stale generation/version;
- snapshot/grant metadata mismatch, truncation, SHA/size/SQLite corruption;
- crash/failure before/after snapshot receipt, before/after durable relinquishment, before/after grant receipt, before Released and during retention;
- source restart after relinquishment remains non-writable and only same-target/same-transfer retry is possible;
- target crash before/after local DB replacement and before durable acquisition;
- fake GitHub 401/403/404/422/502/timeouts/starter/contradictory receipt and exact idempotent retry;
- release-wide newest-three retention, temporary fourth unit, cleanup failure and reconvergence.

### Recovery checkpoints
- changed vs unchanged scheduling;
- fake-clock 15-minute frequency;
- latest-five completed retention;
- offline/OneDrive-unavailable delay without authority change;
- incomplete/corrupt checkpoint rejection.

### Disaster Recovery
- owner-selected fencing model;
- single-winner activation proof for DR-A, concurrent contenders, duplicate/retry and crash-after-activation recovery;
- candidate selection/freshness rules;
- generation advance and old-generation rejection/reinitialization;
- no normal handoff target substitution disguised as DR.

### WPF/localization
- real STA/WPF regressions for close modal/default retain, target list/selection, pre/post-relinquishment action states, pending-source restart, target acquisition/read-only status, pairing, DR warnings and FR↔zh-CN behavior;
- reduced/normal/enlarged window layout checks and preserved unrelated operator state.

All tests use synthetic/sanitized data; no real business snapshot/token may enter the source repository.

## 16. Project-owner Windows/WPF manual acceptance required

The final owner gate should use at least two real Windows PCs A/B with the production WPF artifact, the real configured OneDrive shared root and the dedicated private GitHub handoff repository, using synthetic orders only. A third logical/real Windows device C is required for non-target/N-device evidence; deterministic automated/VM/Sandbox evidence may supplement the two physical PCs.

Required owner/operator scenarios include:

1. initialize/pair B from authoritative A; pairing leaves B read-only;
2. optionally pair C and verify N-device target list/non-target behavior;
3. A close-and-retain, restart, and offline retain-authority behavior;
4. A → B normal transfer, B exact-target acquisition, A remains read-only;
5. B → A round-trip with exact version/generation progression;
6. C can observe artifacts but cannot acquire a transfer targeted to A/B;
7. pre-relinquishment network failure safely aborts/retains authority;
8. post-relinquishment failure/restart remains pending/read-only and recovers only the same target-bound transfer;
9. real newest-three handoff retention evidence using synthetic/disposable operational data;
10. recovery checkpoint creation on authoritative device and cross-device OneDrive visibility/hash/SQLite validation;
11. owner-approved DR flow with the old authority device deliberately stopped/quarantined, data-loss warning, generation advance, then old-device return showing stale/read-only and required reinitialization;
12. FR/zh-CN safety/status presentation and ordinary offline authoritative POS regression.

Timing-sensitive crash windows should primarily be exercised by deterministic failure injection against production services rather than relying on the owner to interrupt at millisecond boundaries.

## 17. Acceptance ownership and later-scope boundary

M07 is expected to close the full M07 storage set after approved material decisions and evidence:

- `AC-STO-002`;
- `AC-STO-003`;
- `AC-STO-004`;
- `AC-STO-005`;
- `AC-STO-007`;
- `AC-STO-008`;
- `AC-STO-009`.

`AC-STO-006` and the M06-owned centralized/read-only portion of `AC-STO-010` are already M06 acceptance and must receive M07 regression/cross-check rather than a replacement authority architecture. Real M07 transitions complete the operational cross-check of `AC-STO-010`.

M07 supplies the local-first/network-failure evidence for `AC-PROD-002`; final printer-adapter-specific evidence remains naturally cross-checked in M08 rather than being implemented early in M07.

M07 must not implement M08 final printing/reprint, M09 Hiboutik paste import, M10 catalogue workbook, M11 Gestion export, M12 annual archive/historical hydration or M13 installer/final acceptance merely to close its authority work.

## 18. Proposed implementation work packages after owner approval

Only after the material decisions and M07 implementation are explicitly approved:

1. align Approved baseline/decision records and clear the known wording residues;
2. create the authoritative M07 implementation contract, durable authorization, worklog and final owner checklist;
3. create the dedicated M07 branch and PR;
4. publish the active mailbox pointer and one complete `CODEX_HANDOFF_READY` record;
5. open issue #4 only after all preparation is complete;
6. Codex executes in dependency order:
   - canonical production identity/authority-state v2 + M06 migration;
   - pairing/current-generation membership/seed hydration;
   - production GitHub transport and credential seam;
   - source normal-transfer state machine;
   - target acquisition;
   - retention/retry/restart hardening;
   - OneDrive recovery checkpoint scheduler/retention;
   - owner-approved DR fencing/generation/reinitialization;
   - WPF/localization/observability;
   - exhaustive automated + real multi-device evidence preparation.

Parallel work is allowed only after shared authority/identity/state contracts are stabilized; the main integration agent remains responsible for proving there is one authority model and one final write guard.

## 19. Owner gate

M07 implementation remains **NOT AUTHORIZED** until the project owner explicitly resolves/approves:

- pairing model P1 (or an approved alternative);
- Disaster Recovery fencing model DR-A vs DR-B;
- recovery-source model R1 (or strict checkpoint-only behavior);
- the resulting M07 implementation plan.

No issue opening, implementation PR/branch, executable handoff or M08 work is authorized by this design-review record.
