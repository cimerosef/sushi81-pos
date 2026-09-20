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


## 2026-09-20 — M11-PREPARATION-CONTROL-PACKAGE-02

Created documentation-only preparation line:

- branch: `prep/m11-gestion-export`;
- draft PR #23: `M11 preparation: Gestion intermediate export readiness and contracts`;
- Issue #4 remained CLOSED;
- no `CODEX_HANDOFF_READY` was published;
- authorization record remains NOT AUTHORIZED.

The implementation split is frozen as WP1–WP4 in `milestone-11-gestion-export.md`. The first WP1 execution handoff is prepared only as a non-executable template pending separate explicit project-owner implementation authorization.


## 2026-09-20 — M11-PREPARATION-CONTRACT-HARDENING-03

The preparation review additionally made the frozen workbook mapping executable without adding new business workflow:

- legitimate zero-total Closed order SettlementDate = business-local ClosedAt date;
- positive-total SettlementDate remains effective-payment-date derived;
- CANCEL payload is anchored to the last successfully emitted positive snapshot so un-emitted later edits cannot leak downstream;
- CANCEL optional date filtering uses that last-emitted fulfilment date;
- UnitBaseTTC / OptionAdjustmentTTC / LineTTC / VATRate and TaxBreakdown mappings are explicitly frozen.

The WP1 contract now recommends a derived-correction export ledger: compare the current canonical positive snapshot with the last successful positive emission instead of creating a second order-lifecycle subsystem.

### Preparation CI observation

PR #23 exact-head GitHub Actions attempts on the documentation-only preparation branch failed before any workflow step started:

- job reported `steps: null`;
- no job log blob was produced;
- repeated reruns showed the same pre-step failure;
- the immediately preceding merged-main CI #788 on `299df8b...` was green.

This is recorded as a CI-runner/startup infrastructure observation, not as source/test failure evidence. Issue #4 remains CLOSED and no Codex execution is authorized.


## 2026-09-20 — M11-IMPLEMENTATION-AUTHORIZATION-04

The project owner explicitly approved:

`批准 M11 implementation`

Controller transition:

- durable M11 implementation authorization: GRANTED;
- dedicated branch created from exact finalized preparation head `a8df4a6c671de7e0050a539e156e15caa9c791f8`;
- implementation branch: `codex/m11-gestion-export-authorized`;
- dedicated implementation PR/mailbox: #24 — `M11: Gestion intermediate export`;
- WP1 is the first package eligible for an executable handoff;
- WP2/WP3/WP4 remain non-executable;
- merge remains unauthorized;
- M12/M13 remain unauthorized.

Issue #4 is opened only after PR #24 and Issue #4 carry the same exact WP1 `CODEX_HANDOFF_READY` identifier and starting head.
