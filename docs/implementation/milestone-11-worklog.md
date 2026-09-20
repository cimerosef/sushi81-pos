# M11 — Gestion intermediate export — worklog

This is an append-only milestone evidence log. Historical entries are not rewritten to hide failures or later remediation.

## 2026-09-20 — M11-PREPARATION-READINESS-01

Starting authoritative main:

`299df8b44a1959497ad46f861e44db32913b4d11`

Verified:

- M10 PR #22 merged;
- controller final closure comment `5750090951`;
- Issue #4 CLOSED;
- no active Codex handoff;
- M11 production implementation not started.

Readiness review covered AC-EXP-001..011, AC-HIB-008 export exclusion, AC-ARCH-005 export boundary, export decisions, current order/payment/snapshot SQLite schema, authority/recovery and M10 ClosedXML seams.

Two material specification ambiguities were surfaced to the project owner:

1. `SettlementDate` business meaning;
2. UPDATE/CANCEL applicability/precedence after prior successful export.

Owner decisions:

- SettlementDate = actual effective payment business date on which the committed order becomes fully settled;
- Open orders never emit CREATE/UPDATE; Closed is the lifecycle export gate;
- un-emitted pending UPDATE is superseded by later CANCEL;
- CANCEL may be emitted for an already-exported order without requiring current Closed/settled state;
- workbook fields remain fixed contract fields rather than per-run selectable fields.

The approved decision and acceptance amendment were prepared. Readiness disposition: PASS / ready for separate implementation authorization.

No production code, schema or executable handoff was created during this preparation step.
