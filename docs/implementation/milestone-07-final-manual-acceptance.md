# M07 final Windows/WPF manual acceptance

**Status:** Final closure candidate prepared — Scenarios A–N supported by recorded owner/controller evidence; ready for project-owner final acceptance — NOT OWNER-DECLARED PASSED
**Prepared:** 2026-09-12
**Milestone:** M07 — Pairing, target-directed formal handoff and disaster recovery  
**Execution gate:** OPEN during closure bookkeeping
**Implementation contract:** `milestone-07-pairing-handoff-disaster-recovery.md`

## Historical automated closure candidates (retained for audit; superseded by R22/R23)

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

### Historical remediation candidate — M07-MANUAL-ACCEPTANCE-REMEDIATION-22

R21 owner evidence identified a narrow Scenario M presentation defect set: the language ComboBox
could become blank after a resource refresh, the Disaster Recovery action could disappear after a
localization/state refresh, and zh-CN/French result dialogs exposed English infrastructure
diagnostics. R22 fixes only that presentation seam and records the owner evidence ledger; it does
not redesign authority, recovery, activation, persistence or write guards.

- Production implementation head: `e971580ef43d3b50366d51733ca9431ca0997e8d`.
- Final docs/evidence head: see the final R22 delivery comment on PR #13.
- Fresh self-contained `win-x64` artifact: `artifacts/m07-manual-remediation-22-publish`;
  `Sushi81.Pos.Desktop.exe`, `162816` bytes, SHA-256
  `08E873DBD67438D3E48B6B497B1DC41828B13761CF47DE3AFDC4AC6D8BEB7284`.
- Focused language/M07 WPF evidence: `13/13` Passed; full Release solution: `541/541` Passed,
  `0` failed, `0` skipped; Release build `0` warnings / `0` errors.
- At the R22 delivery boundary, the project-owner evidence ledger preserved D/E/G/H/J/K/N as
  Passed, retained the A/B caveat and I partial status, kept L frozen, and left M pending its
  focused owner retest.
- Exact-head CI and the focused project-owner retest were pending at that historical boundary;
  the closure evidence below supersedes that pending state without changing the historical record.

The R22 finding is recorded in
[`milestone-07-manual-acceptance-findings-04.md`](milestone-07-manual-acceptance-findings-04.md).
This was the pre-closure R22 state. The subsequent owner/controller evidence and R23 closure
disposition are recorded below.

### Final closure bookkeeping — `M07-FINAL-ACCEPTANCE-CLOSURE-23`

- Scope of this closure pass: Markdown/evidence bookkeeping only. No production source,
  tests, tools, artifacts, workflow, authority state, recovery data, or WP7 proof was changed.
- Accepted production implementation head: `e971580ef43d3b50366d51733ca9431ca0997e8d`.
- Pre-closure docs/evidence head: `7a6f6aa409da9b13f667e2640e2852709d7f63b8`.
- Final closure docs/evidence head: this closure commit; the exact SHA is recorded in the
  matching delivery comment on PR #13.
- R22 artifact: `artifacts/m07-manual-remediation-22-publish/Sushi81.Pos.Desktop.exe`,
  SHA-256 `08E873DBD67438D3E48B6B497B1DC41828B13761CF47DE3AFDC4AC6D8BEB7284`.
- Local Release evidence: `541/541` Passed, `0` failed, `0` skipped; build `0` warnings /
  `0` errors.
- Exact-head CI: #630 / run `34694891347`, job `103556562992`, successful.
- Final A–N disposition: all scenarios are recorded as Passed from the accepted owner/
  controller evidence. A/B retain the Windows Sandbox `0x80370106` caveat and target-selector /
  source-exclusion evidence; I retains delayed checkpoint publication as a non-blocking
  operational finding; L remains frozen WP7 proof; M is Passed on the focused R22 owner retest.
- This record is ready for project-owner final acceptance. It does not impersonate the owner by
  declaring overall M07 Passed. PR #13 remains unmerged and M08 remains unauthorized.

## Historical remediation findings carried into the closure record

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
The remediation evidence and accepted replacement identity are recorded below; the old blocked
result remains historical and is not the current closure result.

This checklist records the GitHub owner/controller evidence supplied for closure. Codex does not
declare overall M07 acceptance on the owner's behalf.

## 1. Evidence identity

Fill before testing:

- exact production-code head SHA: `e971580ef43d3b50366d51733ca9431ca0997e8d`
- exact docs/evidence head SHA: this closure commit; exact SHA in the final delivery comment
- PR number: `#13`
- artifact/publish location: `artifacts/m07-manual-remediation-22-publish`
- Release test result: `541/541` Passed, `0` failed, `0` skipped
- Release build warnings/errors: `0 / 0`
- exact-head CI run: #630 / run `34694891347` / job `103556562992` — successful
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

- [x] Launch B and choose join-existing-lineage.
- [x] B creates/shows a new stable device identity/display name.
- [x] B joins the existing current lineage **without any approval action on A**.
- [x] A does not need to click an “approve B” prompt for membership to become valid.
- [x] B clearly remains non-authoritative/read-only after joining.
- [x] B cannot create/edit an order, change payment/lifecycle, edit catalogue/settings or otherwise write business state.
- [x] Pairing/joining did not release/change A authority.
- [x] A can see/select B as an eligible normal current-generation target after metadata converges.
- [x] Restart B and verify the same immutable device identity/membership persists.

Result: **Passed** from combined project-owner and exact-head deterministic evidence. The Windows
Sandbox `0x80370106` limitation remains a caveat; no clean local Sandbox restart is claimed.

## 4. Scenario B — third device and target identity

- [x] Fresh C self-joins independently without A approval.
- [x] A sees both B and C with human-readable names/short identities.
- [x] Duplicate/similar display names remain distinguishable by stable identity.
- [x] Neither B nor C becomes writable by being online/running/paired.
- [x] Target selector excludes A itself and allows exactly one target selection.

Result: **Passed** from combined project-owner and exact-head deterministic evidence. The target
selector/source-exclusion boundary is retained as an explicit caveat.

## 5. Scenario C — close and retain authority, including offline

On authoritative A:

- [x] Close window; dialog clearly offers Close and retain authority / Transfer authority and close / Cancel in current language.
- [x] Safe/default choice is Close and retain authority.
- [x] Disconnect Internet/OneDrive/GitHub access.
- [x] Choose Close and retain authority; application closes without a remote handoff requirement.
- [x] Relaunch A still offline; A remains authoritative and can perform ordinary local business writes.
- [x] Verify ordinary synthetic order/catalogue/payment write succeeds and local recovery behavior remains normal.
- [x] B/C remain read-only and cannot infer authority merely because A was closed/offline.
- [x] Network-dependent handoff/DR actions report unavailability clearly without corrupting authority.

Result: **Passed**.

## 6. Scenario D — normal A → B transfer

Reconnect services and use a small synthetic dataset with an obvious final committed change.

- [x] On A choose Transfer authority and close, target B explicitly.
- [x] UI blocks further A business edits while transfer runs.
- [x] Completed remote unit contains a valid snapshot and matching B-targeted grant.
- [x] Transfer completes only after required server evidence; A closes/remains non-authoritative.
- [x] Start/restart A: A remains read-only and cannot resume business writes.
- [x] On B discover/acquire exact handoff.
- [x] B validates/installs the latest committed synthetic data.
- [x] B becomes writable only after acquisition completes.
- [x] The exact final synthetic change from A is present.
- [x] C sees/observes the same environment but cannot acquire the B-targeted grant and remains read-only.

Result: **Passed** on the accepted R20 owner evidence.

## 7. Scenario E — B → A round trip

- [x] On authoritative B transfer normally back to A.
- [x] B becomes/remains read-only after relinquishment.
- [x] A acquires exact A-targeted transfer and becomes writable.
- [x] B/C cannot acquire A's grant.
- [x] Data continuity remains intact across the round trip.

Result: **Passed** on the accepted R20 owner evidence.

## 8. Scenario F — failure before source relinquishment

Use the provided deterministic/testable failure hook or a controlled network/auth failure that is known to occur before durable relinquishment; do not rely on random unplug timing.

- [x] Start A→B transfer and inject a pre-relinquishment GitHub failure.
- [x] No target-releasing grant is accepted remotely.
- [x] UI reports actionable failure.
- [x] Safe abort returns A to valid authoritative operation.
- [x] A can make a new local synthetic write afterwards.
- [x] B/C remain read-only.

Result: **Passed**.

## 9. Scenario G — failure after source relinquishment

Use the accepted deterministic failure hook to stop after durable source relinquishment but before completed grant publication/Released.

- [x] A transitions to post-relinquishment pending state.
- [x] A cannot perform business writes.
- [x] Closing/restarting A preserves the same read-only pending transfer.
- [x] UI does not offer retarget, force reclaim or “cancel back to writable”.
- [x] Retry continues the exact same transfer/target B.
- [x] Successful retry publishes/completes the exact B-bound transfer.
- [x] B acquires; no third device substituted itself.

Result: **Passed** on the accepted R21 owner evidence.

## 10. Scenario H — GitHub newest-three normal handoff retention

Perform enough complete alternating transfers with synthetic data to create at least four complete historical normal-handoff units.

- [x] After cleanup converges, exactly the newest three complete validated snapshot+grant units are retained as normal handoff history.
- [x] A temporary fourth during completion/cleanup is acceptable.
- [x] No retained complete unit is split by deleting only one member accidentally.
- [x] Incomplete/starter/stray assets are not counted as complete retention units.
- [x] Current authority remains correct even if a cleanup retry is needed.

Result: **Passed** on the accepted R20 owner evidence. The old R19 blocked result remains
historical only.

## 11. Scenario I — real OneDrive DR checkpoint publication

On current authoritative device make distinct durable synthetic changes and observe the checkpoint system.

- [x] Changed data produces a validated DR checkpoint when due.
- [x] Repeated unchanged time does not produce redundant checkpoints merely on a timer tick.
- [x] Publication is rate-limited to maximum normal frequency one completed checkpoint per 15 minutes.
- [x] The newest five complete valid checkpoints are retained after enough changes/time.
- [x] Another real Windows device actually observes the completed checkpoint through OneDrive.
- [x] On the other device verify referenced DB is readable and application/test validation confirms size/SHA-256/SQLite integrity.
- [x] A checkpoint does not make the observing device writable and is not auto-consumed as ordinary read-only refresh.
- [x] Temporarily unavailable OneDrive delays checkpoint but does not stop authoritative local POS writes.

Result: **Passed** with delayed checkpoint publication retained as a non-blocking operational
finding. Fifteen minutes is the maximum normal completion frequency/rate limit, not a delivery
SLA. Changed-only, no-redundant, cross-device, size/SHA, SQLite, non-authority, non-auto-consume,
and cloud-failure-not-blocking-writes evidence is retained.

## 12. Scenario J — fresh replacement PC after old authority is dead

This is a mandatory owner scenario and is the reason self-join cannot require old-source approval.

Prepare current authoritative A with synthetic data and ensure at least one eligible safe recovery candidate exists. Then **fully stop/quarantine A** and treat it as unavailable.

Use a fresh B/replacement installation (or reset B identity exactly as the implementation provides).

- [x] B self-joins the existing lineage without needing any action on stopped A.
- [x] If no ordinary initialization seed is available, B enters a clear paired/uninitialized read-only state rather than deadlocking or inventing authority.
- [x] B offers explicit Disaster Recovery because the normal authority path is unavailable.
- [x] DR UI clearly says old authoritative/designated-target PC must remain stopped/quarantined until reinitialized.
- [x] Operator must explicitly confirm that condition.
- [x] B shows eligible validated safe candidate(s), candidate type/source/version/time and possible data-loss information.
- [x] A completed GitHub handoff+matching grant is eligible where present.
- [x] A OneDrive DR checkpoint is eligible where present.
- [x] A GitHub snapshot lacking its valid matching grant is not offered/accepted as safe recovery data.
- [x] With GitHub/Internet deliberately unavailable, DR cannot complete and B remains read-only; there is no offline force-authority button.
- [x] Restore Internet; B completes the online next-generation activation and local restore.
- [x] B becomes writable only after the new-generation activation/data install/local authority commit succeeds.
- [x] Restored synthetic data matches the selected safe candidate.

Result: **Passed** on the accepted R21 owner evidence.

## 13. Scenario K — returning old-generation PC

After Scenario J, keep B authoritative in the new generation. Return A to service without allowing it to make intentional business writes before it observes the new generation.

- [x] A is identified as old/stale generation and is read-only.
- [x] A cannot create/edit business state or consume an old handoff grant.
- [x] UI explains reinitialization is required.
- [x] Reinitialize A from current-generation validated data/membership.
- [x] A remains non-authoritative/read-only after reinitialization unless B later performs a normal exact A-targeted handoff.
- [x] B remains authoritative throughout reinitialization.

Also confirm the warning/docs do not falsely claim that software could have remotely stopped A had the operator violated the quarantine rule and kept A running while disconnected.

Result: **Passed** on the accepted R21 owner evidence.

## 14. Scenario L — DR single-winner evidence

This is primarily a deterministic/controlled proof, not a millisecond manual race.

Review the recorded M07-WP7 evidence and, where the prepared harness exposes a safe operator-visible proof:

- [x] Two independent recovery contenders for the same lineage/next generation were exercised against a disposable private GitHub test release using synthetic identities/data.
- [x] Exactly one valid immutable activation exists for that generation.
- [x] Losing contender remains non-authoritative.
- [x] Same winner can retry/resume idempotently.
- [x] Starter/incomplete handling grants nobody authority.
- [x] No production token/customer data is present in recorded evidence.

The frozen WP7 proof is the accepted evidence for this scenario; it must not be rerun or
mutated/deleted as part of this closure bookkeeping.

Result: **Passed** from the frozen WP7 proof.

## 15. Scenario M — localization and operational clarity

Exercise the main M07 states in French and Simplified Chinese:

- [x] authoritative/retained;
- [x] non-authoritative read-only;
- [x] paired-uninitialized read-only;
- [x] transfer target selection;
- [x] transfer in progress;
- [x] post-relinquishment pending/retry;
- [x] target acquisition pending;
- [x] DR candidate/warning/quarantine confirmation;
- [x] DR network failure;
- [x] stale generation/reinitialize;
- [x] GitHub/OneDrive setup/connection-test failures.

Verify:

- [x] no clipped/overlapped critical buttons/text at representative supported window sizes;
- [x] language switch does not change business data/device IDs/protocol IDs;
- [x] unrelated catalogue/order/search selections are not needlessly reset by authority status/localization refresh;
- [x] UI remains responsive during hashing/snapshot/upload/download/checkpoint operations.

Result: **Passed** on the focused R22 owner retest. Exact result variants were not recreated after
generation 2 became valid; the accepted deterministic coverage and owner evidence remain the
closure basis.

## 16. Scenario N — ordinary business regression under authority guard

On current authoritative device:

- [x] create and edit order;
- [x] adjust payment/lifecycle;
- [x] catalogue/settings representative mutations;
- [x] verify M06 local recovery still produced/retained correctly.

On a non-authoritative/stale/pending device repeat representative mutations:

- [x] every attempted authoritative business mutation is rejected before persistence;
- [x] no partial SQL state is created;
- [x] read-only consultation still works as designed.

Printing-specific final behavior remains owned by M08 and must not be claimed Passed here beyond existing non-authority guard boundary.

Result: **Passed** on the accepted R21 owner evidence.

## 17. Final owner result

Closure bookkeeping is complete on the accepted evidence set; project-owner final acceptance is
still a separate explicit governance action.

- exact implementation head accepted: `e971580ef43d3b50366d51733ca9431ca0997e8d`
- exact closure docs/evidence head: this closure commit; exact SHA in the final delivery comment
- date: `2026-09-12`
- owner result: **Pending explicit owner declaration**
- blocking findings: none identified in the accepted closure evidence; retain the A/B Sandbox
  `0x80370106` caveat and the I delayed-publication operational finding.
- non-blocking M08+ carry-over: printing/reprinting and later milestones remain out of scope and
  unauthorized.
- screenshots/log/evidence references (sanitized): PR #13 comments `5646313903`, `5646336795`,
  `5646349314`; exact-head CI #630 / run `34694891347` / job `103556562992`.

### Criterion closure to record after Passed

- AC-STO-002: Evidence complete — owner final acceptance pending
- AC-STO-003: Evidence complete — owner final acceptance pending
- AC-STO-004: Evidence complete — owner final acceptance pending
- AC-STO-005: Evidence complete — owner final acceptance pending
- AC-STO-007: Evidence complete — owner final acceptance pending
- AC-STO-008: Evidence complete — owner final acceptance pending
- AC-STO-009: Evidence complete — owner final acceptance pending
- AC-PROD-002 M07 portion: Evidence complete — owner final acceptance pending
- AC-STO-006 regression: Evidence complete; criterion itself Passed in M06 — owner final acceptance pending
- AC-STO-010 M07 transfer/stale/self-join regression: Evidence complete; M06 foundation Passed — owner final acceptance pending

M07 is ready for project-owner final acceptance. Do not record overall M07 Passed, merge PR #13,
or start M08 until the project owner explicitly records acceptance and later explicitly approves
merge.
