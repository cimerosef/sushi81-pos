# M07 final Windows/WPF manual acceptance

**Status:** Remediation-21 candidate prepared — M20 owner D/E/H passed; Scenario G now executable; owner retest required — NOT EXECUTED / NOT PASSED
**Prepared:** 2026-09-11
**Milestone:** M07 — Pairing, target-directed formal handoff and disaster recovery  
**Execution gate:** CLOSED at preparation time  
**Implementation contract:** `milestone-07-pairing-handoff-disaster-recovery.md`

## Automated closure candidate (Codex evidence; not owner acceptance)

- Accepted production-code candidate: `79db4bb8a92f401fbc40d464a2f7792d269cdb83`.
- WP7, WP8 and WP9 including AL/AM: accepted by ChatGPT review `5147243664`.
- Final local Release evidence: `531/531` Passed, `0` failed, `0` skipped; build `0` warnings / `0` errors.
- Focused final M07 safety sweep: infrastructure `163/163`, application guard `2/2`, Architecture/WPF `37/37`, all `0` failed / `0` skipped.
- Fresh self-contained `win-x64` publish candidate: `artifacts/m07-wp10-final-publish`; primary executable `Sushi81.Pos.Desktop.exe`, `162816` bytes, SHA-256 `D9310D527BF2D64D60B3CEA65EDA04274366289576B485F08C7B02C78E826F0D`.
- Prior accepted production-head CI: #623 / run `34280672910`, Restore/Build/Test successful. The final WP10 documentation head requires its own exact-head CI before delivery.
- Project-owner Windows/WPF multi-device acceptance: **NOT EXECUTED / NOT PASSED**.
- Replacement remediation production-code head: `b58c264` (`b58c26473e3f4158bcafb16be07a8f8c7ea40c8f`, pending exact-head CI verification).
- Replacement self-contained `win-x64` artifact: `artifacts/m07-manual-remediation-18-publish`; primary executable `Sushi81.Pos.Desktop.exe`, `162816` bytes, SHA-256 `C8C6B7F2420B016FEA9AE079E6B1E4D7FCB981313B2F213D9FDDD2AAED90A45C`.
- Replacement local Release evidence: `532/532` Passed, `0` failed, `0` skipped; Release build `0` warnings / `0` errors. Exact-head CI and owner retest remain pending.

### Latest remediation candidate — M07-MANUAL-ACCEPTANCE-REMEDIATION-20

- The prior R19 owner review (`e2fdcacdc2d1837ebe27478569ce3d66209e57b6`) found Scenario H retention blocked by non-converging legacy/incompatible grant evidence and found successful target acquisition could leave running WPF business views stale until restart.
- Production/evidence implementation head: `c74693c5f4a79c69878b59a9df88231ea083835c`.
- Full local Release evidence: `539/539` Passed, `0` failed, `0` skipped; Release build `0` warnings / `0` errors.
- Focused remediation evidence: `NormalHandoffTests` `4/4` and `M07PresentationRefreshTests` `2/2`, all passed.
- Fresh self-contained `win-x64` artifact: `artifacts/m07-manual-remediation-20-publish`; primary executable `Sushi81.Pos.Desktop.exe`, `162816` bytes, SHA-256 `DF4C312903B35B856830D4218A776E421AE7EE617BA717AFFA81779ED64DBE86`.
- Exact-head CI status for the final pushed docs/evidence head is recorded in the matching `CODEX_DONE` delivery comment.
- Project-owner Windows/WPF multi-device acceptance: **NOT EXECUTED / NOT PASSED**.
- Detailed finding and owner rerun scope: [`milestone-07-manual-acceptance-findings-02.md`](milestone-07-manual-acceptance-findings-02.md).

The M20 owner rerun must cover Scenario H retention convergence, Scenario D/E target acquisition without process restart, and the successful DR/stale-generation database-replacement paths where applicable. Scenario A remains a required retest because it was blocked on the earlier candidate. No scenario or overall M07 acceptance is Passed by M20 automated evidence.

### Latest remediation candidate — M07-MANUAL-ACCEPTANCE-REMEDIATION-21

R20 owner retest evidence is preserved: Scenario D A→B same-process refresh passed, Scenario E B→A
same-process refresh passed, and Scenario H newest-three retention convergence passed by direct
inspection of the operational handoff Release. Scenario G remained blocked on R20 solely because
the owner had no deterministic way to stop the source after durable relinquishment and before
grant creation. R21 adds that manual-acceptance-only seam without changing the authority protocol.

- Production implementation head: `ae4e9b38ebd66e8b898fb0e4ad569bab024f0be4` (exact final docs/evidence head follows).
- Full local Release evidence: `540/540` Passed, `0` failed, `0` skipped; build `0` warnings / `0` errors.
- Focused normal-handoff evidence: `NormalHandoffTests` `5/5` Passed; existing exact-target and
  non-target acquisition coverage remains in `TargetAcquisitionTests`.
- Fresh self-contained `win-x64` artifact: `artifacts/m07-manual-remediation-21-publish`;
  `Sushi81.Pos.Desktop.exe`, `143364771` bytes, SHA-256
  `52619B649E4651C59E6F2B73051191DD6B0433B656B3EFD9369DB8463B866472`.
- Exact-head CI and project-owner Windows/WPF retest remain pending; these automated results do
  not claim Scenario G or overall M07 acceptance.

#### Scenario G owner procedure on R21

1. On authoritative A, launch the R21 artifact with a process-scoped environment variable
   `SUSHI81_M07_MANUAL_FAULT_AFTER_RELINQUISH=1`. Do not set it permanently at user/system scope.
2. Start the normal exact A→B target-directed handoff. The source must first persist
   `RelinquishedPendingGrant`; the probe then fails before any target-releasing grant upload.
3. Confirm A shows the existing pending-transfer/read-only state, cannot perform business writes,
   and identifies the immutable target B. Confirm B has not acquired authority.
4. Clear the variable before restarting/retrying A. Restarting while it remains set is safe because
   the probe is crossed only while entering the durable pending phase, but clearing it makes the
   operator intent explicit.
5. Use the existing pending-transfer resume action on A. It must reuse the same transfer ID,
   target B, handoff version, business revision and snapshot receipt; no retarget/cancel/rollback
   action is allowed.
6. Confirm the retry creates exactly the B-bound grant, A becomes released/read-only, and B can
   acquire and become writable. Any non-target device remains read-only.

R21 does not mark Scenario G, Scenario A, Scenario F, Scenario I, Scenario J, Scenario K,
Scenario L, Scenario M, Scenario N or overall M07 Passed. The project owner must record the
manual result on the same accepted artifact/head.

## Remediation finding carried into owner retest

The first owner review of Scenario A found a blocking defect on the prior WP10 candidate
(`79db4bb8a92f401fbc40d464a2f7792d269cdb83`): after a genuinely fresh first launch created and
migrated `Data\live.db`, a later restart could mistake `schema_migrations` for pre-M07 legacy
evidence and create a new local Authoritative lineage. The UI ultimately remained Recovery
Required because shared metadata contradicted that local lineage, but the local authority
creation itself was unsafe and Scenario A is **Blocked / failed on the old candidate**.

The remediation adds paired non-authority fresh-install provenance under Config and Data,
captured before migrations with create-new/write-through/flush semantics. A valid pair permanently
disqualifies legacy bootstrap; a missing or corrupt half fails closed. The automated regression
also covers repeated fresh restarts, fresh setup against existing shared lineage, explicit
self-join, stable identity/generation and read-only restart reconstruction.

This finding is recorded in [`milestone-07-manual-acceptance-findings-01.md`](milestone-07-manual-acceptance-findings-01.md).
The replacement production artifact and exact head must be filled below after delivery, and
Scenario A plus any dependent owner checks must be rerun. No scenario or overall M07 acceptance
is Passed by this remediation.

This checklist is executed by the project owner only after ChatGPT review/remediation is complete and an exact candidate production head/artifact is identified. Codex must not mark these scenarios Passed on the owner's behalf.

## 1. Evidence identity

Fill before testing:

- exact production-code head SHA: `ae4e9b38ebd66e8b898fb0e4ad569bab024f0be4`
- exact docs/evidence head SHA: see the final evidence-bookkeeping head on PR #13
- PR number: `#13`
- artifact/publish location: `artifacts/m07-manual-remediation-21-publish`
- Release test result: `540/540` Passed, `0` failed, `0` skipped
- Release build warnings/errors: `0 / 0`
- exact-head CI run: pending delivery verification
- Windows PC A identity/display name:
- Windows PC B identity/display name:
- device C (physical/VM/Sandbox) identity/display name:
- test OneDrive root:
- dedicated private GitHub handoff repository/release configuration verified:
- production protected credential path used: Yes / No
- only synthetic test customer/order data used: Yes / No

If the exact head or artifact changes after a blocking remediation, restart the affected acceptance scenarios on the new exact head and record the new identity.

## 2. Test environment rules

Use at least two real Windows computers A and B. Use a third real/logical device C to prove N-device/non-target behavior; Windows Sandbox/VM is acceptable for C as a supplement.

Use the production WPF application and production configuration/credential paths. Do not substitute the M02 feasibility console tool for acceptance.

Use real OneDrive cross-device synchronization and the actual dedicated private GitHub handoff repository/release with synthetic test data. Do not expose credentials in screenshots/logs.

## 3. Scenario A — self-join does not require source approval

Starting state: A is current authoritative; B is a fresh installation with no prior Sushi81 identity.

Steps/result:

- [ ] Launch B and choose join-existing-lineage.
- [ ] B creates/shows a new stable device identity/display name.
- [ ] B joins the existing current lineage **without any approval action on A**.
- [ ] A does not need to click an “approve B” prompt for membership to become valid.
- [ ] B clearly remains non-authoritative/read-only after joining.
- [ ] B cannot create/edit an order, change payment/lifecycle, edit catalogue/settings or otherwise write business state.
- [ ] Pairing/joining did not release/change A authority.
- [ ] A can see/select B as an eligible normal current-generation target after metadata converges.
- [ ] Restart B and verify the same immutable device identity/membership persists.

Result: **Blocked / failed on prior WP10 candidate; retest required on remediation candidate**

## 4. Scenario B — third device and target identity

- [ ] Fresh C self-joins independently without A approval.
- [ ] A sees both B and C with human-readable names/short identities.
- [ ] Duplicate/similar display names remain distinguishable by stable identity.
- [ ] Neither B nor C becomes writable by being online/running/paired.
- [ ] Target selector excludes A itself and allows exactly one target selection.

Result: Pending

## 5. Scenario C — close and retain authority, including offline

On authoritative A:

- [ ] Close window; dialog clearly offers Close and retain authority / Transfer authority and close / Cancel in current language.
- [ ] Safe/default choice is Close and retain authority.
- [ ] Disconnect Internet/OneDrive/GitHub access.
- [ ] Choose Close and retain authority; application closes without a remote handoff requirement.
- [ ] Relaunch A still offline; A remains authoritative and can perform ordinary local business writes.
- [ ] Verify ordinary synthetic order/catalogue/payment write succeeds and local recovery behavior remains normal.
- [ ] B/C remain read-only and cannot infer authority merely because A was closed/offline.
- [ ] Network-dependent handoff/DR actions report unavailability clearly without corrupting authority.

Result: Pending

## 6. Scenario D — normal A → B transfer

Reconnect services and use a small synthetic dataset with an obvious final committed change.

- [ ] On A choose Transfer authority and close, target B explicitly.
- [ ] UI blocks further A business edits while transfer runs.
- [ ] Completed remote unit contains a valid snapshot and matching B-targeted grant.
- [ ] Transfer completes only after required server evidence; A closes/remains non-authoritative.
- [ ] Start/restart A: A remains read-only and cannot resume business writes.
- [ ] On B discover/acquire exact handoff.
- [ ] B validates/installs the latest committed synthetic data.
- [ ] B becomes writable only after acquisition completes.
- [ ] The exact final synthetic change from A is present.
- [ ] C sees/observes the same environment but cannot acquire the B-targeted grant and remains read-only.

Result: Pending — remediation-20 no-restart target-acquisition refresh must be rerun by the owner

## 7. Scenario E — B → A round trip

- [ ] On authoritative B transfer normally back to A.
- [ ] B becomes/remains read-only after relinquishment.
- [ ] A acquires exact A-targeted transfer and becomes writable.
- [ ] B/C cannot acquire A's grant.
- [ ] Data continuity remains intact across the round trip.

Result: Pending — remediation-20 round-trip target refresh must be rerun by the owner

## 8. Scenario F — failure before source relinquishment

Use the provided deterministic/testable failure hook or a controlled network/auth failure that is known to occur before durable relinquishment; do not rely on random unplug timing.

- [ ] Start A→B transfer and inject a pre-relinquishment GitHub failure.
- [ ] No target-releasing grant is accepted remotely.
- [ ] UI reports actionable failure.
- [ ] Safe abort returns A to valid authoritative operation.
- [ ] A can make a new local synthetic write afterwards.
- [ ] B/C remain read-only.

Result: Pending

## 9. Scenario G — failure after source relinquishment

Use the accepted deterministic failure hook to stop after durable source relinquishment but before completed grant publication/Released.

- [ ] A transitions to post-relinquishment pending state.
- [ ] A cannot perform business writes.
- [ ] Closing/restarting A preserves the same read-only pending transfer.
- [ ] UI does not offer retarget, force reclaim or “cancel back to writable”.
- [ ] Retry continues the exact same transfer/target B.
- [ ] Successful retry publishes/completes the exact B-bound transfer.
- [ ] B acquires; no third device substituted itself.

Result: Pending

## 10. Scenario H — GitHub newest-three normal handoff retention

Perform enough complete alternating transfers with synthetic data to create at least four complete historical normal-handoff units.

- [ ] After cleanup converges, exactly the newest three complete validated snapshot+grant units are retained as normal handoff history.
- [ ] A temporary fourth during completion/cleanup is acceptable.
- [ ] No retained complete unit is split by deleting only one member accidentally.
- [ ] Incomplete/starter/stray assets are not counted as complete retention units.
- [ ] Current authority remains correct even if a cleanup retry is needed.

Result: Blocked / failed on old R19 candidate; retest required on remediation-20 artifact

## 11. Scenario I — real OneDrive DR checkpoint publication

On current authoritative device make distinct durable synthetic changes and observe the checkpoint system.

- [ ] Changed data produces a validated DR checkpoint when due.
- [ ] Repeated unchanged time does not produce redundant checkpoints merely on a timer tick.
- [ ] Publication is rate-limited to maximum normal frequency one completed checkpoint per 15 minutes.
- [ ] The newest five complete valid checkpoints are retained after enough changes/time.
- [ ] Another real Windows device actually observes the completed checkpoint through OneDrive.
- [ ] On the other device verify referenced DB is readable and application/test validation confirms size/SHA-256/SQLite integrity.
- [ ] A checkpoint does not make the observing device writable and is not auto-consumed as ordinary read-only refresh.
- [ ] Temporarily unavailable OneDrive delays checkpoint but does not stop authoritative local POS writes.

Result: Pending

## 12. Scenario J — fresh replacement PC after old authority is dead

This is a mandatory owner scenario and is the reason self-join cannot require old-source approval.

Prepare current authoritative A with synthetic data and ensure at least one eligible safe recovery candidate exists. Then **fully stop/quarantine A** and treat it as unavailable.

Use a fresh B/replacement installation (or reset B identity exactly as the implementation provides).

- [ ] B self-joins the existing lineage without needing any action on stopped A.
- [ ] If no ordinary initialization seed is available, B enters a clear paired/uninitialized read-only state rather than deadlocking or inventing authority.
- [ ] B offers explicit Disaster Recovery because the normal authority path is unavailable.
- [ ] DR UI clearly says old authoritative/designated-target PC must remain stopped/quarantined until reinitialized.
- [ ] Operator must explicitly confirm that condition.
- [ ] B shows eligible validated safe candidate(s), candidate type/source/version/time and possible data-loss information.
- [ ] A completed GitHub handoff+matching grant is eligible where present.
- [ ] A OneDrive DR checkpoint is eligible where present.
- [ ] A GitHub snapshot lacking its valid matching grant is not offered/accepted as safe recovery data.
- [ ] With GitHub/Internet deliberately unavailable, DR cannot complete and B remains read-only; there is no offline force-authority button.
- [ ] Restore Internet; B completes the online next-generation activation and local restore.
- [ ] B becomes writable only after the new-generation activation/data install/local authority commit succeeds.
- [ ] Restored synthetic data matches the selected safe candidate.

Result: Pending

## 13. Scenario K — returning old-generation PC

After Scenario J, keep B authoritative in the new generation. Return A to service without allowing it to make intentional business writes before it observes the new generation.

- [ ] A is identified as old/stale generation and is read-only.
- [ ] A cannot create/edit business state or consume an old handoff grant.
- [ ] UI explains reinitialization is required.
- [ ] Reinitialize A from current-generation validated data/membership.
- [ ] A remains non-authoritative/read-only after reinitialization unless B later performs a normal exact A-targeted handoff.
- [ ] B remains authoritative throughout reinitialization.

Also confirm the warning/docs do not falsely claim that software could have remotely stopped A had the operator violated the quarantine rule and kept A running while disconnected.

Result: Pending

## 14. Scenario L — DR single-winner evidence

This is primarily a deterministic/controlled proof, not a millisecond manual race.

Review the recorded M07-WP7 evidence and, where the prepared harness exposes a safe operator-visible proof:

- [ ] Two independent recovery contenders for the same lineage/next generation were exercised against a disposable private GitHub test release using synthetic identities/data.
- [ ] Exactly one valid immutable activation exists for that generation.
- [ ] Losing contender remains non-authoritative.
- [ ] Same winner can retry/resume idempotently.
- [ ] Starter/incomplete handling grants nobody authority.
- [ ] No production token/customer data is present in recorded evidence.

If the proof is absent/ambiguous, owner acceptance is Blocked.

Result: Pending

## 15. Scenario M — localization and operational clarity

Exercise the main M07 states in French and Simplified Chinese:

- [ ] authoritative/retained;
- [ ] non-authoritative read-only;
- [ ] paired-uninitialized read-only;
- [ ] transfer target selection;
- [ ] transfer in progress;
- [ ] post-relinquishment pending/retry;
- [ ] target acquisition pending;
- [ ] DR candidate/warning/quarantine confirmation;
- [ ] DR network failure;
- [ ] stale generation/reinitialize;
- [ ] GitHub/OneDrive setup/connection-test failures.

Verify:

- [ ] no clipped/overlapped critical buttons/text at representative supported window sizes;
- [ ] language switch does not change business data/device IDs/protocol IDs;
- [ ] unrelated catalogue/order/search selections are not needlessly reset by authority status/localization refresh;
- [ ] UI remains responsive during hashing/snapshot/upload/download/checkpoint operations.

Result: Pending

## 16. Scenario N — ordinary business regression under authority guard

On current authoritative device:

- [ ] create and edit order;
- [ ] adjust payment/lifecycle;
- [ ] catalogue/settings representative mutations;
- [ ] verify M06 local recovery still produced/retained correctly.

On a non-authoritative/stale/pending device repeat representative mutations:

- [ ] every attempted authoritative business mutation is rejected before persistence;
- [ ] no partial SQL state is created;
- [ ] read-only consultation still works as designed.

Printing-specific final behavior remains owned by M08 and must not be claimed Passed here beyond existing non-authority guard boundary.

Result: Pending

## 17. Final owner result

Complete only after every blocking scenario passes on the same accepted exact head or is rerun as required after remediation.

- exact head accepted:
- date:
- owner result: **Pending**
- blocking findings:
- non-blocking M08+ carry-over:
- screenshots/log/evidence references (sanitized):

### Criterion closure to record after Passed

- AC-STO-002: Pending
- AC-STO-003: Pending
- AC-STO-004: Pending
- AC-STO-005: Pending
- AC-STO-007: Pending
- AC-STO-008: Pending
- AC-STO-009: Pending
- AC-PROD-002 M07 portion: Pending
- AC-STO-006 regression: Pending (criterion itself already Passed in M06)
- AC-STO-010 M07 transfer/stale/self-join regression: Pending (M06 foundation already Passed)

Do not mark M07 Passed, merge its PR or start M08 until the project owner explicitly records acceptance and later explicitly approves merge.
