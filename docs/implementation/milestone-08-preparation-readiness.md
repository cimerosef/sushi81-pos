# M08 preparation readiness — printing and reprinting

**Status:** Preparation complete — **READY FOR PROJECT-OWNER IMPLEMENTATION AUTHORIZATION**  
**Prepared:** 2026-09-12  
**Entry baseline:** `main@9ea7d5e15bceba6932cb2caba50d0afb64ca1ff9` after M07 merge  
**Execution gate:** CLOSED

## 1. Current GitHub state verified

- PR #13 — M07: CLOSED / MERGED.
- M07 merge commit / entry `main` head: `9ea7d5e15bceba6932cb2caba50d0afb64ca1ff9`.
- M07 accepted production implementation head: `e971580ef43d3b50366d51733ca9431ca0997e8d`.
- M07 final closure docs/evidence head: `d586c847f2dd541815b8c00565c58b3685a3e4be`.
- M07 final accepted exact-head CI: #631 / run `34698627867` / job `103566341254`, success; 541/541 Passed; build 0 warnings / 0 errors.
- Project-owner M07 final acceptance is durable in PR #13.
- Issue #4 is CLOSED; active handoff: none.
- No M08 branch exists.
- No M08 implementation PR exists.
- No M08 `CODEX_HANDOFF_READY` exists.

Therefore M08 preparation is allowed, but Codex implementation remains forbidden until separate owner authorization and the complete gate sequence.

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

Deferred explicitly to M12: `AC-PRINT-009` archived-order printing.

## 3. Existing foundation audit

### 3.1 Commit-before-print seam — ready

M04 production Application code already has `IOrderPrintDispatcher`. New-order orchestration commits, exits the business transaction, reloads the committed snapshot and only then invokes output. Dispatcher failure preserves persistence success.

This is the correct M08 insertion point and must be evolved rather than bypassed.

### 3.2 Existing production dispatcher — placeholder only

Production `CompositionRoot` currently passes `NoOpOrderPrintDispatcher`; no real Windows printer/spooler adapter is composed.

### 3.3 Current committed snapshot — substantially sufficient

`OrderSnapshot` and nested item/adjustment/tax snapshots already contain stable order identity, timestamps, fulfilment/planned date-time, phone/address/comment, total/discount/fee facts, line/option/custom-adjustment snapshots, persisted VAT breakdown and cumulative Card/Cash values.

Historical customer/kitchen content therefore does not require current Catalogue joins.

### 3.4 Existing-order modification boundary — ready

M05 lifecycle operations save modifications/payment/cancel/close without automatic output. M08 adds explicit reprint actions without introducing save-triggered printing.

### 3.5 WPF reprint UI — absent

Current live-order WPF has no kitchen/customer reprint command; M08 must add them while preserving search/date/selection/editor state.

### 3.6 Windows spooler adapter — absent

No production Infrastructure `PrintQueue` adapter currently exists. M08 must add the frozen Windows printing boundary.

### 3.7 Printer configuration — absent

Current local technical configuration has no kitchen/customer printer queue fields. M08 adds per-installation queue configuration; it is not SQLite business authority.

### 3.8 Authority/read-only boundary — foundation ready

M07 provides persistent non-authoritative/stale warnings and centralized business-write guard semantics. Printing is explicitly allowed from a non-authoritative local committed copy, so `IWriteAuthorityGuard` must not be used as a print permission gate.

## 4. Owner decision M08-D1 — RESOLVED

The project owner supplied `modèle impression.pdf` and selected:

- page 1 as the target kitchen-ticket visual style;
- page 2 as the target customer-ticket/Hiboutik-style receipt.

The durable controlling decision is:

- `docs/decisions/m08-print-layout-and-receipt-identity.md`;
- `docs/implementation/milestone-08-contract-addendum-print-layout-identity.md`.

Frozen customer receipt identity:

- `Sushi 81`;
- `12 Rue Gaston Darley`;
- `77140 Nemours - FRA`;
- SIRET `90805211100014`;
- TVA `FR03908052111`;
- APE/NAF `5610C`.

Storage semantics are frozen: this identity is authoritative SQLite business configuration; printer queue selection remains local technical configuration.

The reference's exact Hiboutik typeface is printer-resident rather than a portable named font. M08 must target the same narrow monospaced thermal-receipt appearance through the approved Windows spooler boundary and owner acceptance will judge actual printed similarity. Exact font-family identity is not an authorization blocker.

## 5. Technical choices frozen for implementation proposal

1. Keep deterministic print-data generation Application-owned and printer-independent.
2. Use separate Kitchen and Customer document models/intents/outcomes.
3. Use WPF fixed-document/document-paginator composition.
4. Use ordinary Windows `System.Printing` / `PrintQueue` integration.
5. Store kitchen/customer queue selection in local technical configuration.
6. Store receipt identity in authoritative SQLite business configuration via an additive migration/settings extension.
7. Submit kitchen and customer independently; failure of one does not suppress the other.
8. Preserve async/responsive WPF behavior; no transaction held across output.
9. Add explicit live-order kitchen/customer reprint actions.
10. Block/withhold reprint from unsaved edited values; save or abandon edit first.
11. Explicit reprints use required marks; Cancelled adds `ANNULÉ`.
12. Known failed initial queue submission can retry initial intent; ambiguous submission cannot silently create an indistinguishable duplicate and routes additional operator output through explicit reprint semantics.
13. Non-authoritative printing remains available with the existing stale/read-only warning truthfully visible.
14. No print-history business table or business-revision advance solely because paper was printed.
15. No archive integration before M12.
16. Kitchen composition follows owner-selected page-1 structure; customer composition follows owner-selected page-2 structure without false Hiboutik branding.

## 6. Current-control drift — reconciled for M08 gate

The active control surfaces now state the current transition truth:

- `docs/README.md`: M01–M07 Passed/merged; M08 Preparation/not authorized;
- `docs/implementation-status.md`: M07 Passed; M08 Preparation;
- `docs/implementation/README.md`: M08 preparation package and closed gate;
- Issue #4: M07 merged, M08 preparation only, CLOSED/no handoff.

Historical M07 worklogs/contracts remain unchanged as historical evidence.

## 7. Readiness checklist

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
- [x] M08-D1 customer receipt identity/layout resolved and durable in GitHub.
- [x] current-control status drift reconciled for the M08 gate.
- [ ] project-owner explicitly approves M08 implementation.
- [ ] authorization record changed to Authorized with exact preparation head.
- [ ] dedicated M08 branch/PR created.
- [ ] executable M08 handoff published.
- [ ] Issue #4 opened.

**Current disposition:** M08 preparation is complete and has no known material specification blocker. It is ready for explicit project-owner implementation authorization. Codex remains stopped until that separate authorization is given and the normal execution gate is established.
