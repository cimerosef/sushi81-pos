# M05 controlled implementation authorization

**Status:** Approved authorization  
**Approved by:** project owner  
**Approval date:** 2026-09-02  
**Milestone:** M05 — Lifecycle, payments, search and operational dashboard  
**Implementation branch:** `codex/m05-lifecycle-payments-search-dashboard`  
**Base:** `main` after M04 merge commit `ab218263bd4eee9c1be203d36acc552988cef43a`  
**Implementation contract:** `milestone-05-lifecycle-payments-search-dashboard.md`  
**Decision amendment:** `../decisions/m05-lifecycle-payment-modification-clarifications.md`  
**POST_TASK_POWER_ACTION:** `NONE`

## Authorization

The project owner approved the M05 business decisions D1, D2 and D3 on 2026-09-02 and authorizes preparation and controlled implementation of M05 under the repository's durable mailbox protocol.

This authorization does not by itself authorize Codex to execute at arbitrary times. Production implementation begins only when:

1. the active M05 PR mailbox exists;
2. its branch/PR pointer is recorded durably;
3. a unique matching `CODEX_HANDOFF_READY` is published on that PR;
4. GitHub issue #4 — `Codex execution gate — Sushi81 POS` — is OPEN.

The issue #4 gate must remain CLOSED while ChatGPT prepares the authoritative contract, branch, PR, pointer and handoff. Once those are complete and internally consistent, ChatGPT is authorized by the project owner to reopen issue #4 so Codex can immediately consume the queued handoff.

## Scope authority

Codex may implement only the behavior required by:

- the frozen-and-amended V1 specification;
- `milestone-05-lifecycle-payments-search-dashboard.md`;
- `../decisions/m05-lifecycle-payment-modification-clarifications.md`;
- inherited execution/interactive/control-state/power governance.

Codex must not infer or start M06 or later work.

## Merge gate

The M05 implementation PR must remain open/unmerged after Codex reports completion. Only an explicit project-owner merge approval authorizes merging M05 into `main`.

A `CODEX_DONE` record is evidence of implementation completion only; it is never merge authorization.