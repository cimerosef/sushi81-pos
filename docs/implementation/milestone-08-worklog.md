# M08 worklog — printing and reprinting

**Status:** Preparation complete — **IMPLEMENTATION NOT AUTHORIZED**  
**Prepared:** 2026-09-12  
**Preparation entry baseline:** `main@9ea7d5e15bceba6932cb2caba50d0afb64ca1ff9`  
**Contract:** `docs/implementation/milestone-08-printing-reprinting.md` + `milestone-08-contract-addendum-print-layout-identity.md`  
**Authorization:** `docs/implementation/milestone-08-authorization.md` — NOT AUTHORIZED  
**Execution gate:** CLOSED

This worklog records M08 preparation and, only after later explicit authorization, implementation/evidence. Preparation entries do not authorize Codex.

## 1. Entry state

- M07 — Passed by project-owner final acceptance.
- PR #13 — merged.
- M07 merge commit: `9ea7d5e15bceba6932cb2caba50d0afb64ca1ff9`.
- Accepted M07 production implementation head: `e971580ef43d3b50366d51733ca9431ca0997e8d`.
- Final M07 closure docs/evidence head: `d586c847f2dd541815b8c00565c58b3685a3e4be`.
- Final accepted M07 CI: #631 / run `34698627867`, success; 541/541 tests Passed; build 0 warnings / 0 errors.
- Issue #4: CLOSED.
- Active handoff: none.
- M08 implementation branch: none.
- M08 implementation PR: none.
- M09+: not authorized.

## 2. Preparation audit — 2026-09-12

### Specifications and production seams

Current `main` was used to re-read the frozen/amended printing/lifecycle/data/architecture/storage/control documents and accepted M07 evidence.

Findings:

- existing M04 `IOrderPrintDispatcher` seam found;
- `OrderEntryService` already commits + reloads before dispatcher invocation;
- dispatcher failure already preserves order persistence success;
- production still uses `NoOpOrderPrintDispatcher`;
- no production Windows print-queue/spooler adapter exists;
- no current kitchen/customer reprint WPF commands exist;
- no printer fields exist in `LocalConfiguration`;
- M05 modification/payment/lifecycle writes do not auto-print;
- current committed `OrderSnapshot` is sufficient for order/item/adjustment/tax/payment print content without current Catalogue dependency;
- M07 non-authoritative/read-only warning and authority guard provide the required boundary; printing remains outside the business-write permission gate.

M08 criterion ownership: `AC-PRINT-001`–`008`, `010`, `011`, `AC-ARCH-006`, plus the production printing cross-check of `AC-LIFE-001`. `AC-PRINT-009` remains M12.

## 3. Owner print-reference decision — M08-D1 resolved

On 2026-09-12 the project owner supplied `modèle impression.pdf` in the ChatGPT Sushi81 POS project resources and explicitly selected:

- page 1 as the desired kitchen-ticket style, with customer telephone/remarks/order information placed between the two upper dashed separators;
- page 2 as the desired customer-ticket style, matching the current Hiboutik receipt structure and thermal appearance as closely as practical.

Durable GitHub records:

- `docs/decisions/m08-print-layout-and-receipt-identity.md`;
- `docs/implementation/milestone-08-contract-addendum-print-layout-identity.md`.

Frozen Sushi 81 customer identity:

- `Sushi 81`;
- `12 Rue Gaston Darley`;
- `77140 Nemours - FRA`;
- SIRET `90805211100014`;
- TVA `FR03908052111`;
- APE/NAF `5610C`.

Storage decision: receipt identity is authoritative SQLite business configuration and travels with the business lineage; printer queues remain local technical configuration.

Font finding: the supplied PDF is image-based and does not expose a named font family. Hiboutik documentation indicates receipt typography normally uses the printer's resident fonts/default smallest font (Font A selection is supported). M08 therefore targets the same narrow monospaced thermal receipt appearance through the approved Windows spooler architecture, with actual printed similarity judged during owner acceptance. No raw ESC/POS adapter is authorized merely to force a font match.

The customer sample's Hiboutik branding/footer is a source-product footer and will not be copied onto Sushi81 POS output. Current V1 also has no customer-name field, so M08 does not invent one solely to imitate the sample; it uses committed telephone/address/order data that actually exists.

## 4. Technical proposal prepared

The contract/addendum freeze:

- deterministic Application-owned kitchen/customer models;
- Windows `PrintQueue`/fixed-document transport boundary;
- independent kitchen/customer outcomes and retry;
- local printer queue configuration;
- authoritative SQLite receipt identity migration/settings extension;
- explicit live-order kitchen/customer reprint;
- future/cancel/reprint markings;
- non-authoritative printing without authority/freshness implication;
- async/responsive WPF behavior;
- deterministic failure injection and automated test matrix;
- owner real-printer visual comparison against page 1/page 2 references;
- M12-only archive printing boundary.

## 5. Current-control reconciliation

The active control surfaces have been advanced to the post-M07/M08-preparation state:

- `docs/README.md`;
- `docs/implementation-status.md`;
- `docs/implementation/README.md`;
- Issue #4 pointer/body.

Historical M07 evidence remains in its original contract/worklog/manual-acceptance/PR records.

## 6. Governance state after preparation

M08 has no known material specification blocker and is ready for a separate project-owner implementation-authorization decision.

Until that decision occurs, Codex must continue to report:

`M08_NOT_AUTHORIZED: execution gate closed / no active handoff.`

No production implementation evidence belongs in this worklog yet.

## 7. Implementation entries

**None.**

Do not add production implementation evidence until:

- project owner explicitly approves M08 implementation;
- the authorization record is updated to Authorized with the exact preparation head;
- a dedicated M08 branch/PR exists;
- a valid top-level M08 `CODEX_HANDOFF_READY` exists;
- Issue #4 is OPEN.

`POST_TASK_POWER_ACTION: NONE`
