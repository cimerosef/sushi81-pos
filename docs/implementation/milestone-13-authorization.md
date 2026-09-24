# M13 — implementation authorization

**Status:** OWNER-AUTHORIZED / EXECUTION NOT YET ENABLED  
**Milestone:** M13  
**Implementation branch:** `codex/m13-installer-final-acceptance-authorized`  
**Baseline:** `f59663c6b47ab21114c24360544e4e25094f4722`
**Durable mailbox:** Draft PR #26

## Authorization basis

The current Approved implementation plan states that after M12 merge the owner has authorized M13 preparation/authorization as the final V1 milestone without another owner confirmation.

M12 is now merged. This record therefore makes the already-approved M13 authorization durable on the M13 branch.

## Execution gate remains separate

Authorization is not an executable handoff.

Codex may execute only when all of the following are simultaneously true:

1. Issue #4 is OPEN;
2. Issue #4 points to the active M13 Draft PR;
3. the PR contains one exact unconsumed `CODEX_HANDOFF_READY`;
4. the handoff ID, start head, branch, PR and scope all match;
5. no controller stop/blocker is active.

At preparation time GitHub reports the source repository as public, conflicting with the project's private-repository expectation. Therefore Issue #4 remains CLOSED and no M13 READY is executable until that repository-safety discrepancy is resolved.

## Authorized milestone scope

M13 may implement only the Approved final-V1 scope:

- Gestion export retention/compaction;
- final FR/zh-CN parity;
- self-contained Windows x64 publish and per-user Inno Setup installer;
- upgrade/reinstall preservation;
- diagnostics/performance/repository-security hardening;
- final acceptance regression, release provenance and operating guide.

Each package still requires a unique one-time READY/DONE pair. Codex may not infer authorization for later packages from completion of an earlier one and may never merge the PR.
