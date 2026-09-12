# M08 preparation readiness — printing and reprinting

**Status:** Preparation in progress — **NOT IMPLEMENTATION-AUTHORIZED**  
**Prepared:** 2026-09-12  
**Entry baseline:** `main@9ea7d5e15bceba6932cb2caba50d0afb64ca1ff9` after M07 merge  
**Execution gate:** CLOSED

## 1. Current GitHub state verified

- PR #13 — M07: CLOSED / MERGED.
- M07 merge commit / entry `main` head: `9ea7d5e15bceba6932cb2caba50d0afb64ca1ff9`.
- M07 accepted production implementation head: `e971580ef43d3b50366d51733ca9431ca0997e8d`.
- M07 final closure docs/evidence head: `d586c847f2dd541815b8c00565c58b3685a3e4be`.
- M07 final accepted exact-head CI: #631 / run `34698627867` / job `103566341254`, success; 541/541 Passed; build 0 warnings / 0 errors.
- Project-owner M07 final acceptance is durable in PR #13 Conversation comment `5646455650`.
- M07 closure-controller review is PR review `5186671566`.
- Issue #4 is CLOSED; active handoff: none.
- No M08 branch exists.
- No M08 implementation PR exists.
- No M08 `CODEX_HANDOFF_READY` exists.

Therefore M08 **preparation** may proceed, but Codex implementation may not.

## 2. Specification/criterion map

M08 owns:

| Criterion | M08 obligation |
|---|---|
| AC-LIFE-001 printing cross-check | order durable before automatic output attempt |
| AC-PRINT-001 | automatic one kitchen + one customer output after successful new-order commit |
| AC-PRINT-002 | mandatory kitchen/customer content from committed state |
| AC-PRINT-003 | future date/time operationally prominent |
| AC-PRINT-004 | output failure never rolls back order; identify failed doc; independent retry |
| AC-PRINT-005 | saved existing-order modification does not auto-reprint |
| AC-PRINT-006 | explicit reprint uses latest committed state, never unsaved edits |
| AC-PRINT-007 | `RÉIMPRESSION` / `DUPLICATA` |
| AC-PRINT-008 | Cancelled remains printable with prominent `ANNULÉ`; reprint mark retained |
| AC-PRINT-010 | non-authoritative/read-only device may print local committed copy with stale warning, no authority implication |
| AC-PRINT-011 | no B2B invoice subsystem |
| AC-ARCH-006 | deterministic app-owned print model followed by Windows spooler/queue adapter |

Deferred explicitly to M12:

- `AC-PRINT-009` archived-order printing.

## 3. Existing foundation audit

### 3.1 Commit-before-print seam — ready

M04 production Application code already has `IOrderPrintDispatcher` and new-order orchestration already:

- validates/builds order;
- commits through the order store;
- exits the business transaction;
- reloads the committed snapshot;
- only then calls the dispatcher;
- preserves persistence success when dispatcher throws.

This is the correct M08 insertion point and must be evolved, not bypassed.

### 3.2 Existing production dispatcher — placeholder only

Production `CompositionRoot` currently passes `NoOpOrderPrintDispatcher` to `OrderEntryService`.

Conclusion: final Windows output adapter is not yet implemented.

### 3.3 Current committed snapshot — substantially sufficient

`OrderSnapshot` and nested item/adjustment/tax snapshots already contain:

- stable order ID/reference;
- creation/update/cancel/close timestamps;
- fulfilment;
- planned date/time and advance marker;
- phone/address/comment;
- total/manual-total/discount/fee facts;
- ordered line snapshots and saved display order;
- option/custom-adjustment snapshots;
- persisted VAT breakdown;
- cumulative Card/Cash values.

This is sufficient for deterministic kitchen/customer order content and latest-payment reprint without consulting current Catalogue.

Only the customer receipt business/statutory identity block is not frozen in current GitHub data/settings.

### 3.4 Existing-order modification boundary — ready

M05 lifecycle operations save modifications/payment/cancel/close without invoking the M04 print dispatcher.

Conclusion: “save modification does not auto-reprint” already exists structurally; M08 must add explicit reprint without introducing a save-triggered output hook.

### 3.5 WPF reprint UI — absent

Current live-order WPF contains no kitchen/customer reprint command/action.

M08 must add them in the existing-order detail workflow and preserve current search/date/selection/editor state.

### 3.6 Windows spooler adapter — absent

No production Infrastructure print/spooler/`PrintQueue` adapter currently exists.

M08 must add the frozen Windows printing boundary.

### 3.7 Printer configuration — absent

Current `LocalConfiguration` contains UI culture, OneDrive/GitHub/M07 setup and device display name, but no kitchen/customer printer queue fields.

M08 must add local printer configuration. This is technical per-installation state, not SQLite business data and not M07 authority-controlled business state.

### 3.8 Authority/read-only boundary — foundation ready

M07 provides persistent non-authoritative/stale warnings and centralized business-write guard semantics.

Approved printing explicitly allows non-authoritative printing. M08 therefore must **not** use `IWriteAuthorityGuard` as a print permission gate. Printing reads local committed state and performs a local external side effect while leaving all authority state unchanged.

## 4. Technical choices frozen for implementation proposal

Subject only to owner resolution of the receipt-identity material gap:

1. Keep deterministic print-data generation Application-owned and printer-independent.
2. Use separate Kitchen and Customer document models/intents/outcomes.
3. Use WPF fixed-document/document-paginator composition.
4. Use ordinary Windows `System.Printing` / `PrintQueue` integration.
5. Store kitchen/customer queue selection in local technical configuration.
6. Submit kitchen and customer independently; failure of one does not suppress the other.
7. Preserve async/responsive WPF behavior; no transaction held across output.
8. Add explicit live-order kitchen/customer reprint actions.
9. Block/withhold reprint from unsaved edited values; save or abandon edit first.
10. Explicit reprints use required marks; Cancelled adds `ANNULÉ`.
11. Known failed initial queue submission can retry initial intent; ambiguous submission cannot silently create an indistinguishable duplicate and routes additional operator output through explicit reprint semantics.
12. Non-authoritative printing remains available with the existing stale/read-only warning truthfully visible.
13. No print-history business table or business-revision advance.
14. No archive integration before M12.

## 5. Material owner decision required

### Decision M08-D1 — authoritative customer receipt identity block

Approved spec requires ordinary Sushi 81 business/statutory receipt information and VAT identification on the customer ticket, but the repository does not currently specify the exact values or where authoritative editable values live.

The owner must freeze:

1. the exact customer-facing identity fields/lines that must print;
2. the exact values (or an approved source of those values);
3. whether these values are:
   - authoritative business settings stored in SQLite and transferred with the business lineage; or
   - a deliberately fixed application/configuration identity block.

Technical lead recommendation: if these values may ever change, store them as authoritative business settings so every device prints the same current approved identity after normal handoff/DR. Do not make them ordinary per-device printer configuration.

Until D1 is resolved, M08 implementation authorization is blocked.

## 6. No other owner-level gap found

The following do **not** require separate owner decisions under current delegation:

- exact Windows API/writer choice inside the approved spooler boundary;
- print-document class/interface names;
- internal renderer geometry/typography within visibility/readability requirements;
- adapter error taxonomy;
- queue enumeration mechanism;
- failure-injection seam design;
- asynchronous implementation technique;
- test-fixture architecture;
- parallel work-package decomposition.

## 7. Governance/document drift found

Several older living/overview documents still contain pre-merge M07 wording (M07 preparation/in progress, M08 not started). These statements are historically stale after merge commit `9ea7d5e...`.

Before an executable M08 handoff is published, current-control documentation must be reconciled to state:

- M07 Passed / merged;
- M08 Preparation / implementation not authorized;
- Issue #4 CLOSED;
- no active handoff.

Do not rewrite historical M07 worklog entries merely to make them look current; add current-state control records instead.

## 8. Readiness checklist

- [x] M07 project-owner acceptance Passed.
- [x] PR #13 actually merged to `main`.
- [x] Issue #4 CLOSED / no handoff.
- [x] No pre-existing M08 branch/PR found.
- [x] Approved printing baseline re-read.
- [x] M08 criterion ownership mapped.
- [x] M04 post-commit seam audited.
- [x] M05 no-auto-reprint behavior audited.
- [x] M06/M07 authority/read-only boundary audited.
- [x] Current OrderSnapshot sufficiency audited.
- [x] Windows spooler adapter absence confirmed.
- [x] printer configuration absence confirmed.
- [x] current WPF reprint UI absence confirmed.
- [x] technical architecture proposal prepared.
- [ ] M08-D1 customer receipt identity resolved and durable in GitHub.
- [ ] current-control status drift reconciled.
- [ ] project-owner explicitly approves M08 implementation.
- [ ] authorization record changed to Authorized.
- [ ] dedicated M08 branch/PR created.
- [ ] executable M08 handoff published.
- [ ] Issue #4 opened.

**Current disposition:** Preparation is valid; implementation remains blocked by M08-D1 and explicit owner authorization.
