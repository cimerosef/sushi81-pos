# V1 implementation status and acceptance traceability

**Status:** Active implementation control document  
**Last updated:** 2026-09-07  
**Current state:** M01 through M06 are Passed and merged. M07 — Pairing, target-directed formal handoff and disaster recovery — is **Authorized / In progress** under `docs/implementation/milestone-07-authorization.md`. The dedicated branch is `codex/m07-pairing-handoff-disaster-recovery`. Codex execution may begin only after the M07 PR, Issue #4 mailbox pointer and one complete `CODEX_HANDOFF_READY` record are all created and cross-verified, then Issue #4 is reopened. M08 and later milestones have not started.

> Historical implementation/evidence detail through M06 is preserved byte-for-byte at [`implementation/archive/implementation-status-through-m06-2026-09-07.md`](implementation/archive/implementation-status-through-m06-2026-09-07.md). This living document intentionally contains current control state only; historical evidence must not be rewritten merely to update current milestone status.

## 1. Status vocabulary

- `Not started` — no conforming implementation evidence yet.
- `Preparation` — specification/contract/checklist preparation is occurring, but implementation is not authorized and Codex must not execute.
- `In progress` — the explicitly authorized milestone is being implemented/revalidated or still has an open gate.
- `Partial` — some evidence exists, but the complete acceptance criterion/gate is not yet satisfied.
- `Passed` — required automated/manual evidence is recorded and passes on the applicable build.
- `Blocked — amendment required` — a genuine material specification conflict prevents conforming implementation.
- `Blocked — architecture decision required` — an approved protocol remains safe, but a required technical capability still needs an architecture decision/proof.
- `Not applicable — amended` — allowed only when an approved specification amendment explicitly makes the criterion inapplicable.

Only `Passed` and properly approved `Not applicable — amended` satisfy final V1 acceptance.

## 2. Milestone status

| Milestone | Status | Current authoritative result |
|---|---|---|
| M01 — Foundation and safe persistence spine | Passed | Merged through PR #1; automated and Windows/WPF evidence Passed. |
| M02 — remote handoff feasibility gate | Passed | Original competitive OneDrive model remains historically Blocked; approved target-directed GitHub Release Asset transport revalidation Passed. |
| M03 — Catalogue and settings | Passed | Merged through PR #5; final Windows/WPF acceptance Passed. |
| M04 — Order-entry vertical slice | Passed | Merged through PR #6 at `ab218263bd4eee9c1be203d36acc552988cef43a`; final Windows/WPF acceptance Passed. |
| M05 — Lifecycle/payments/search/dashboard | Passed | Merged through PR #10 at `79499d7c6ed65a74f524097c1507ca648dc151c3`; accepted production head `84c1c534c1df105ccb1839cbc6dfc9e0e055bb70`; final Windows/WPF acceptance Passed. |
| M06 — Local recovery/read-only enforcement | Passed | PR #11 merged at `2c5eb52740d0c12e3e837579ecceac6d0600b59e`; accepted production repair head `4a0c1ca9e44a6c48899e6ef8dc211172371e4d20`; final docs/PR head `86326d81551aa4cb5cdcbc6826b8c740317b34c4`; Release tests 364/364, build 0 warnings/errors, exact-head CI `34091370109`, project-owner Windows/WPF acceptance Passed. |
| M07 — Pairing, target-directed formal handoff and disaster recovery | In progress | Project-owner implementation authorization recorded 2026-09-07 in `milestone-07-authorization.md`. Dedicated branch `codex/m07-pairing-handoff-disaster-recovery`; execution setup is being completed. Merge is not authorized. |
| M08 — Printing and reprinting | Not started | Pending M07. |
| M09 — Hiboutik paste fallback | Not started | Pending M08. |
| M10 — Catalogue `.xlsx` | Not started | Pending M09. |
| M11 — Gestion export | Not started | Pending M10. |
| M12 — Annual archive/historical access | Not started | Pending M11. |
| M13 — Installer and final acceptance | Not started | Pending M12. |

## 3. Current acceptance ownership relevant to M06/M07

Historical criterion-by-criterion evidence through M06 remains in the archived status record. The current transition facts are:

| Criterion | Owner | Current state | Note |
|---|---:|---|---|
| AC-PROD-002 | M07 | In progress | M07 must preserve local-first authoritative operation when ordinary Internet/OneDrive services are unavailable; M08 retains the final real printer-adapter offline cross-check. |
| AC-STO-002 | M07 | In progress | N-device single-writer and target-directed normal transfer, including approved self-join semantics. |
| AC-STO-003 | M07 | In progress | Close-retain and strict source relinquishment-before-grant ordering. |
| AC-STO-004 | M07 | In progress | Exact-target validation/acquisition. |
| AC-STO-005 | M07 | In progress | No silent takeover/source rollback/target substitution; genuine loss uses explicit DR. |
| AC-STO-006 | M06 | Passed | M06 local recovery generation/debounce/retention accepted with final 364-test and owner evidence. |
| AC-STO-007 | M07 | In progress | Newest-three complete GitHub handoff-unit retention. |
| AC-STO-008 | M07 | In progress | Changed-only OneDrive DR checkpoints, maximum normal frequency once per 15 minutes, newest five. |
| AC-STO-009 | M07 | In progress | Explicit generation-advancing DR under the 2026-09-07 approved quarantine/safe-candidate rules. |
| AC-STO-010 | M06 | Passed foundation; M07 regression required | M06 centralized Application write guard, fail-closed startup and persistent read-only/recovery presentation are accepted. M07 must exercise them in real transfer/pending/stale/self-join states; printing remains AC-PRINT-010/M08. |

## 4. M07 approved material direction

The project owner approved the following product/safety semantics on 2026-09-07. Detailed controlling wording is in `docs/decisions/m07-self-join-disaster-recovery.md` and the M07 acceptance amendment.

1. **Self-join without authority approval.** A newly installed computer may join an existing Sushi81 POS lineage without approval from the old/current authoritative computer. Joining establishes device identity/membership only and never grants write authority. In a healthy system it remains read-only until a normal handoff is explicitly targeted to it. If the former authoritative computer is genuinely dead/unavailable, the newly joined replacement may enter explicit Disaster Recovery.
2. **Operationally fenced exceptional Disaster Recovery.** Normal authoritative offline operation remains supported. DR requires explicit confirmation that the former authoritative/designated-target device is genuinely unavailable and will remain stopped/quarantined until reinitialized; DR itself requires online single-winner generation activation before the recovery device may become writable. Software cannot remotely revoke a still-running disconnected stale writer if the operator violates that quarantine precondition.
3. **Freshest validated safe recovery data.** DR may use a validated OneDrive recovery checkpoint or a complete validated GitHub handoff snapshot+matching grant. A snapshot without its valid matching grant is never an eligible DR source. DR always advances generation rather than turning the old target binding into an ordinary acquisition.

## 5. M07 governance gate

Implementation authorization is durable in `docs/implementation/milestone-07-authorization.md`. The controller may now establish the dedicated PR/mailbox/handoff and then reopen Issue #4 only after all pointers are consistent.

Codex must still fail closed unless all of the following are simultaneously true:

- Issue #4 is OPEN;
- Issue #4 points to the exact active M07 PR and branch;
- the M07 authorization record exists;
- the PR contains one valid unprocessed top-level `CODEX_HANDOFF_READY` record;
- the handoff ID, contract and branch/PR identity are unambiguous.

Authorization does not authorize merge. M07 still requires implementation evidence, ChatGPT review/remediation, exact-head project-owner Windows/WPF multi-device acceptance, green CI and separate explicit merge approval.

M08 must remain Not started until M07 is Passed and merged.

## 6. Evidence preservation

Historical M01–M06 evidence, including original M02 blocker/revalidation history and all milestone-specific manual acceptance records, remains authoritative in its original files and the archived implementation-status snapshot. Current-state cleanup must never rewrite those historical results merely to make them read as if they had always described later milestones.
