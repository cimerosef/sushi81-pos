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


## 2026-09-20 — M11-WP2-CONTROLLER-ACCEPTANCE-05

WP2 implementation head:

`a9774e2a6795adafee75b18f061289e47811caf5`

Controller review verified the ClosedXML four-sheet contract, native Excel values, staged validation/finalization ordering, unrelated-target protection, retry after ledger commit failure, and exact successful-batch regeneration from immutable payload.

Evidence:

- WP2 focused tests: 4/4 passed;
- full Release solution: 805 passed / 0 failed / 0 skipped;
- Release build: 0 warnings / 0 errors;
- exact-head CI #804 / run `35530438990`: SUCCESS;
- controller acceptance: PR #24 comment `5752144083`.

Disposition: **WP2 ACCEPTED**.

The collaboration protocol was also hardened so a future executable handoff uses one authoritative top-level PR comment whose first line is exactly `CODEX_HANDOFF_READY: <id>`, with START_HEAD/BRANCH/PR/SCOPE on separate lines. Issue #4 remains the execution switch/status pointer rather than a competing full task copy.


## 2026-09-20 — M11-WP4-INTEGRATION-OWNER-CANDIDATE-07

- Authorization/gate state: Issue #4 was OPEN and dynamically pointed to PR #24 / branch `codex/m11-gestion-export-authorized`; this handoff started at exact WP3 head `3c9e110f7d6c5a93c773b2289f5b43f5c59fb14f`. The project owner explicitly approved execution. Merge, owner manual acceptance, M12 and M13 remain excluded.
- Scope: end-to-end export integration hardening, M09 Hiboutik exclusion cross-check, authority/recovery regression, exact regeneration/retry closure, full Release regression, and exact self-contained `win-x64` owner-candidate preparation. No production business/spec redesign was introduced.
- New focused evidence: `M11Wp4IntegrationTests` covers real SQLite + ClosedXML boundaries for Hiboutik exclusion after cancellation/date filtering, immutable regeneration with a later pending UPDATE, prepared-file retry/idempotency, and non-authoritative preview/write blocking.
- Documentation reconciliation: current status and the owner checklist now distinguish automated candidate preparation from owner A–E acceptance. The checklist remains **NOT YET EXECUTED** and M11 remains not Passed.
- Completion boundary: final source head, full Release test/build totals, exact-head CI, candidate EXE/ZIP paths/sizes/SHA-256, package entry counts and forbidden-data scan are recorded only in the matching durable `CODEX_DONE` after the final push.


## 2026-09-21 — M11-OWNER-A-REPAIR-DATEPICKER-WATERMARK-10

- Authorization/gate state: Issue #4 was OPEN and dynamically pointed to PR #24 / branch `codex/m11-gestion-export-authorized`; this narrow repair started at exact Repair 09 head `5da453affdbde8477ffabdbd5c1339eac592d21c`. No merge, M12/M13 work, business-data rewrite, schema change, export-semantic change, or payment/Close change is authorized.
- Controller disposition carried forward: Repair 09 export finalization, PREPARED retry visibility and canonical `销售数据导出` naming were accepted; only the owner-visible WPF DatePicker watermark implementation/evidence remained `CHANGES_REQUIRED`. The prior candidate remains ineligible for owner retest.
- Narrow implementation: the UI thread `CurrentUICulture` now follows the selected app language; the two M11 DatePickers retain their culture-specific `Language` for calendar/date presentation; and each existing WPF `DatePickerTextBox` updates its real `PART_Watermark` template visual after an in-session language switch. `CurrentCulture` remains untouched so business display/serialization behavior is not implicitly changed.
- Strengthened STA evidence: the real MainWindow M11 DatePickers are empty, their rendered template watermark is asserted as `Sélectionner une date` in fr-FR, `选择日期` in zh-CN, and French again after switching back; the test also asserts the Chinese visible feature name and unchanged `CurrentCulture`.
- Local verification: focused DatePicker watermark test 1/1; full Architecture/Desktop/localization suite 192/192; full Infrastructure Integration suite 303/303; full Release solution 830/830 passed, 0 failed, 0 skipped; Release build 0 warnings / 0 errors; restore and `git diff --check` clean.
- Delivery boundary: the newest self-contained owner candidate, exact final head, exact-head CI and artifact hashes are recorded only after the final push. The candidate remains **NOT YET RETESTED** by the owner; scenarios A–E are not executed and M11 remains not Passed.


## 2026-09-21 — M11-WP4-ARTIFACT-DELIVERY-08

- Authorization/gate state: Issue #4 was OPEN and dynamically pointed to PR #24 / branch `codex/m11-gestion-export-authorized`; this delivery-only handoff started from accepted WP4 source head `dc8091fccb31923bacc16cbff7b49ada771f55fb`. No application, test, business-rule or schema source was changed.
- Delivery repair: `.github/workflows/ci.yml` now contains a branch-scoped `m11-owner-candidate-artifact` job. It checks out and verifies `dc8091fccb31923bacc16cbff7b49ada771f55fb`, performs a self-contained `win-x64` normal publish, runs the forbidden-data filename scan, packages the publish directory as a ZIP, and uploads it with 90-day retention.
- Artifact evidence: successful GitHub Actions run `35584565162` / [run #814](https://github.com/cimerosef/sushi81-pos/actions/runs/35584565162), artifact `M11-WP4-owner-candidate-win-x64-dc8091f`, ID `10631573948`, GitHub-reported size `66.6 MB`, ZIP bytes `70,111,153`, digest `sha256:a67e44c31aab629dc9b542d1b29a61c7a606f2a5721e0985a6a65bd64333e677`.
- Reproduced hashes: executable SHA-256 `94854C2F292E658466A10DFA1EE538FE28B1D5511DD6FC94D2D618ECA7E45A8C`; uploaded ZIP SHA-256 `C611EE17835F3617B80FD7E8DD7B209C22AA530AD4C19014B376AA2EF7BB4E67`. These differ from the earlier local hashes because hosted-runner publish/ZIP output is not byte-identical; no byte-identity claim is made.
- Safety/acceptance boundary: the package scan passed; no live.db/SQLite business data, recovery snapshots, real settings, credentials/tokens/secrets, logs, generated workbooks, CSV/business data or owner production files were included. Owner manual acceptance A–E remains **NOT YET EXECUTED**; M11 remains not Passed; PR #24 remains unmerged; M12/M13 remain unauthorized.


## 2026-09-21 — M11-OWNER-A-REPAIR-EXPORT-LOCALIZATION-09

- The previous owner candidate failed scenario A: the eight-CREATE export stopped at finalization with localized `导出未能完成。`, while the durable PREPARED batch remained pending. Owner acceptance was stopped; B–E were not continued.
- The authorized repair is limited to the existing M11 boundary: optional `null`/empty-string workbook validation now matches the existing blank-cell writer; a failed export refreshes the durable pending-batch list so the same BatchId remains retryable; the selected `fr-FR`/`zh-CN` culture is applied through WPF `Language` to the two M11 DatePickers during in-session language changes; and the visible Chinese feature name is `销售数据导出`.
- New focused evidence: a real SQLite + production ClosedXML regression preserves empty optional Telephone/Address/Comment payload values, validates the workbook, finalizes the original PREPARED BatchId once, and rejects a duplicate finalization. A real STA/WPF regression covers French/Chinese DatePicker culture switching and visible Chinese naming.
- Local Release solution regression after the repair: **830 passed, 0 failed, 0 skipped**. The repaired owner candidate is not yet retested by the project owner; M11 remains not Passed, PR #24 remains unmerged, and M12/M13 remain unauthorized. Exact source head, branch CI, artifact identity and hashes are recorded in the matching durable `CODEX_DONE` after push.
