# M06 implementation worklog — local recovery and authoritative/read-only enforcement

**Milestone:** M06 — Local recovery and authoritative/read-only enforcement  
**Branch:** `codex/m06-local-recovery-read-only`  
**Base:** M05 merge commit `79499d7c6ed65a74f524097c1507ca648dc151c3`  
**Contract:** `milestone-06-local-recovery-read-only-enforcement.md`  
**Authorization:** `milestone-06-authorization.md`  
**Initial handoff:** `CODEX_HANDOFF_READY: M06-IMPLEMENT-01`  
**POST_TASK_POWER_ACTION:** `NONE`

## Preparation state

M05 is Passed and merged through PR #10. Accepted M05 production-code head: `84c1c534c1df105ccb1839cbc6dfc9e0e055bb70`. Final M05 docs head: `217d187dd3ef5498c11f21bc516eccc6737fa952`. Main merge commit: `79499d7c6ed65a74f524097c1507ca648dc151c3`.

The M05 merge occurred after the final status documents were written; current-state references that still describe PR #10 as open/unmerged are stale merge-state text and must be cleaned without rewriting truthful historical evidence sections.

M06 preparation review found no unresolved product/business/data-semantic decision. The frozen specification is sufficient for controlled implementation.

## Scope boundary

M06 implements:

- production local recovery wiring for all durable business mutations currently present through M05;
- committed-change notification/debounce/single-flight/shutdown flush;
- validated latest-five local recovery retention and failure behavior;
- durable local authority/read-only state and safe startup reconstruction;
- centralized Application-level business-write blocking;
- persistent localized WPF read-only/pending/recovery-required presentation;
- read-only consultation while mutation controls are blocked.

M06 does not implement M07 pairing/real handoff/GitHub transfer/target acquisition/cloud DR/Disaster Recovery, M08 printing, M10 catalogue workbook import, M12 annual archive or later milestones.

## Acceptance mapping

| Criterion | M06 responsibility | Final evidence status |
|---|---|---|
| AC-STO-006 | Owner — local recovery generation/retention and all current durable mutation triggers; preserve mandatory seam for M10 import cross-check | Pending implementation |
| AC-STO-010 | Owner — persistent non-authoritative/pending read-only state, centralized blocking and WPF presentation; M08 printing/M12 archive later cross-checks | Pending implementation |
| AC-PROD-002 | Partial — authoritative local operation and local authority/recovery enforcement remain network-independent | Pending implementation |
| AC-NFR-004 | Supporting failure-path evidence; final owner M13 | Pending implementation |

## Final current mutation inventory

Codex must replace this preparation list with the exact audited production inventory and concrete service/method paths before completion.

Expected minimum:

- new-order confirmation;
- same-ID existing-order save/modification;
- cumulative CB/Espèce payment update;
- Close;
- Cancel;
- Category create/update/delete;
- Product create/update/delete/activate/deactivate;
- filtered bulk product Activate/Deactivate;
- OptionGroup create/update/reorder/delete;
- Option create/update/reorder/activation/deactivation/delete;
- BusinessSettings save;
- any additional durable business write found on the M05 merged baseline.

For each final entry record:

- Application use-case/service path;
- authority-guard point;
- transaction/commit point;
- durable-change notification point;
- no-op/rollback behavior;
- automated evidence path/test.

## Implementation topology

To be completed by Codex.

Record:

- main-agent/subagent topology;
- any safely parallel workstreams;
- shared seam stabilized before parallel delegation;
- integration ownership and final review performed by the main agent.

## Durable authority-state design

To be completed by Codex after implementation.

Record:

- persistent state file/path and schema/version;
- atomic write strategy;
- runtime state/reason mapping;
- one-time accepted M01–M05 legacy single-device bootstrap condition;
- post-bootstrap missing/corrupt/future/contradictory fail-closed behavior;
- startup ordering;
- restart reconstruction evidence.

## Recovery mutation/scheduler design

To be completed by Codex.

Record:

- common committed-business-change seam chosen;
- how no-op/rollback/blocked attempts are excluded;
- debounce behavior;
- single-flight/latest-sequence behavior;
- shutdown flush behavior;
- snapshot failure behavior after a successful business commit;
- latest-five retention behavior.

## WPF/read-only design

To be completed by Codex.

Record:

- persistent banner/status implementation;
- FR/zh-CN localization keys;
- ordinary read-only vs pending/transitioning vs recovery-required presentation;
- which mutation controls are disabled/unavailable;
- which read-only search/view/dashboard routes remain available;
- stale-data wording and available freshness/version information;
- confirmation no M07 target-selection/force-acquire UI was added.

## Required failure-injection evidence

Record exact test names/results for:

- transaction rollback → no notifier/snapshot;
- authority rejection → zero business writes/no notifier;
- staging/backup failure;
- integrity failure;
- checksum/metadata mismatch;
- recovery promotion failure;
- retention cleanup failure;
- failed sixth snapshot preserves prior five;
- incomplete/staging unit exclusion;
- nearby-commit debounce;
- durable change during active snapshot not lost;
- shutdown flush;
- post-commit snapshot failure preserves business data;
- read-only/pending/recovery-required restart persistence;
- malformed/future authority state fail closed;
- direct Application bypass attempt blocked;
- read-only query/view/dashboard usability;
- FR/zh-CN transition preserves authority and business state.

## Automated verification

To be filled with exact final-head results.

- `dotnet --info`: Pending.
- `dotnet restore Sushi81.Pos.sln --locked-mode`: Pending.
- Release build: Pending; required 0 warnings / 0 errors.
- Full Release tests: Pending; required all passed.
- Self-contained `win-x64` publish: Pending.
- `git diff --check`: Pending.
- Exact-head GitHub Actions CI: Pending.

## Windows/WPF project-owner acceptance

Durable checklist: `milestone-06-final-manual-acceptance.md`.

Status: **Pending project-owner execution.**

Codex must not mark this Passed by simulation or automated tests alone.

## Later-milestone cross-checks

Record but do not implement:

- M07 must drive the durable M06 authority state through real pairing/handoff/target-acquisition/DR transitions;
- M08 must verify `AC-PRINT-010` printing remains permitted from read-only local data with stale warning;
- M10 catalogue workbook import must use the M06 authority/recovery mutation seam;
- M12 annual archive execution must be blocked in read-only state and archive read-only consultation must remain compatible;
- M13 performs final production-target regression.

## Data safety

Final completion record must confirm:

- tests used only synthetic/sanitized values;
- no real customer/order/payment data was added;
- no credentials/tokens were committed;
- no local `live.db`, Recovery files, authority-state files, logs or publish artifacts were committed;
- authority/recovery failures never auto-delete/recreate the production database.

## Completion state

Pending implementation. PR must remain open/unmerged. M07 is not authorized by M06 completion.
