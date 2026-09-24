# M12 — Annual archive and historical access — authorization

**Status:** AUTHORIZED
**Owner authorization basis:** project-owner M12 transition instruction after successful M11 merge, verified against merged GitHub state on 2026-09-21
**Authorized implementation branch:** `codex/m12-annual-archive-authorized`
**Active mailbox:** M12 Draft implementation PR
**M13:** NOT AUTHORIZED

## Authorization

M12 implementation is authorized.

Execution remains package-gated by `AGENTS.md`:

- Codex may execute only the single exact `CODEX_HANDOFF_READY` identified by the OPEN Issue #4 pointer.
- Every handoff must bind exact `START_HEAD`, `BRANCH`, `PR` and `SCOPE`.
- One READY has one matching DONE and IDs are never reused.
- No rebase/history rewrite.
- No merge without controller/owner governance.
- Completion of one M12 package does not authorize the next package.

The first executable package is WP1 only as defined in `milestone-12-annual-archive-historical-access.md`.

The owner-approved 2026-09-21 local-archive amendment removes OneDrive from annual archive completion and therefore removes the former WP2 remote-publication acknowledgement blocker. WP2 must instead preserve `AC-STO-013` through staged validation, durable local canonical promotion, reopen-validation and only then exact live removal.

M13 remains unauthorized.
