# M07 final Windows/WPF manual acceptance

**Status:** Prepared checklist — NOT EXECUTED / NOT PASSED  
**Prepared:** 2026-09-07  
**Milestone:** M07 — Pairing, target-directed formal handoff and disaster recovery  
**Execution gate:** CLOSED at preparation time  
**Implementation contract:** `milestone-07-pairing-handoff-disaster-recovery.md`

This checklist is executed by the project owner only after ChatGPT review/remediation is complete and an exact candidate production head/artifact is identified. Codex must not mark these scenarios Passed on the owner's behalf.

## 1. Evidence identity

Fill before testing:

- exact production-code head SHA:
- exact docs/evidence head SHA:
- PR number:
- artifact/publish location:
- Release test result:
- Release build warnings/errors:
- exact-head CI run:
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

Result: Pending

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

Result: Pending

## 7. Scenario E — B → A round trip

- [ ] On authoritative B transfer normally back to A.
- [ ] B becomes/remains read-only after relinquishment.
- [ ] A acquires exact A-targeted transfer and becomes writable.
- [ ] B/C cannot acquire A's grant.
- [ ] Data continuity remains intact across the round trip.

Result: Pending

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

Result: Pending

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
