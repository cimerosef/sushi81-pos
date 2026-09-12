# M08 implementation contract — printing and reprinting

**Status:** Prepared for project-owner review — **IMPLEMENTATION NOT AUTHORIZED**  
**Prepared:** 2026-09-12  
**Milestone:** M08 — Printing and reprinting  
**Preparation entry baseline:** `main` merge commit `9ea7d5e15bceba6932cb2caba50d0afb64ca1ff9` (PR #13 / M07 merged)  
**Execution gate:** GitHub issue #4 must remain **CLOSED** during preparation  
**Codex instruction status:** This file is a non-executable preparation contract. It does not create a branch/PR/handoff and does not authorize Codex.

## 1. Authority and governing sources

GitHub current `main`, Approved specification documents, current implementation code and accepted evidence are the only authority. Prior chat memory and legacy Excel/VBA behavior are not specification authority.

M07 is Passed and merged. M08 must extend the accepted M04–M07 production code without changing order/payment semantics, durable-write ordering, local-recovery behavior, write authority, handoff, DR generation, stale-device fencing or business-data meaning.

M08 inherits at minimum:

- `docs/v1-specification-freeze.md`;
- `docs/acceptance-criteria.md`;
- `docs/printing.md`;
- `docs/product-requirements.md`;
- `docs/order-lifecycle.md`;
- `docs/data-model.md`;
- `docs/architecture.md`;
- `docs/storage-strategy.md`;
- Approved printing decision records under `docs/decisions/`;
- `docs/implementation/agent-execution-contract.md`;
- `docs/implementation/interactive-quality-gate.md`;
- `docs/implementation/control-state-preservation.md`;
- `docs/implementation/post-task-power-policy.md`;
- accepted M04/M05/M06/M07 implementation/evidence records.

Priority remains:

**reliability > simplicity > maintainability > operational clarity > novelty.**

## 2. M08 acceptance ownership

M08 owns implementation/evidence for:

- `AC-PRINT-001` through `AC-PRINT-008`;
- `AC-PRINT-010`;
- `AC-PRINT-011`;
- `AC-ARCH-006` — Windows printing boundary;
- final production cross-check of the printing portion of `AC-LIFE-001` — durable commit before automatic printing.

`AC-PRINT-009` archived-order printing is **not** M08 implementation scope. It belongs to M12 together with archive selection/hydration/read-only archive access.

M09 Hiboutik paste fallback, M10 Catalogue `.xlsx`, M11 Gestion export, M12 archive and M13 installer/final acceptance remain unauthorized.

## 3. Current production foundation audit

At the M08 preparation entry baseline:

1. M04 already defines `IOrderPrintDispatcher.DispatchAsync(OrderSnapshot committedOrder, ...)` as an Application-owned post-commit seam.
2. `OrderEntryService.ConfirmNewOrderAsync` persists the order, completes the transaction, reloads the committed `OrderSnapshot`, and only then crosses the dispatcher seam. Dispatcher failure returns persistence success/output failure and does not remove the order.
3. Production `CompositionRoot` still wires `NoOpOrderPrintDispatcher`; no real Windows printer/spooler adapter is composed.
4. Infrastructure contains no production print-queue/spooler implementation.
5. WPF contains no production kitchen/customer reprint actions.
6. M05 existing-order save/close/cancel operations do not automatically dispatch printing, which already preserves the approved “saved modification does not auto-reprint” boundary.
7. Current `OrderSnapshot` contains the durable business facts needed for kitchen/customer document generation: stable reference/ID, source/status/timestamps, fulfilment, planned date/time, advance marker, telephone/address/comment, authoritative total, manual-total state, pickup-discount snapshot, delivery fee, item snapshots, adjustment snapshots, persisted tax breakdown and current cumulative Card/Cash values.
8. Historical item/adjustment/tax rendering can therefore remain independent from current Catalogue and current VAT settings.
9. Current `LocalConfiguration` has no printer queue fields.
10. M07’s persistent non-authoritative/read-only warning and centralized `IWriteAuthorityGuard` remain the authority boundary. Printing is an external local side effect, not a business-authoritative write.

### 3.1 Preparation blocker — customer receipt identity

Approved documents require the customer ticket to contain ordinary Sushi 81 business identity/statutory receipt information including applicable VAT identification, but current GitHub does not freeze the exact identity block/values or an authoritative storage/configuration model for them.

This is a material printed-content/data-settings decision and must be resolved by the project owner before M08 implementation authorization. M08 must not guess legal/business identity values or silently invent a new authoritative business-settings schema.

All other items below are technical implementation choices already delegated to the technical lead unless a later repository fact exposes a genuine material conflict.

## 4. Explicit scope

M08 must deliver:

1. deterministic kitchen print-data model from committed order state;
2. deterministic customer print-data model from committed order state;
3. fixed-document/WPF ticket layout composition;
4. Windows print-queue enumeration and submission adapter;
5. independent local kitchen/customer printer queue configuration;
6. automatic one-kitchen + one-customer submission after successful **new-order** commit;
7. independent generation/submission failure reporting and retry;
8. explicit kitchen/customer reprint actions from latest committed live order state;
9. future-order prominence;
10. explicit `RÉIMPRESSION`, `DUPLICATA` and `ANNULÉ` semantics;
11. latest committed payment information on customer reprint;
12. non-authoritative/read-only printing without authority/freshness claims;
13. FR/zh-CN UI/status/error/retry localization;
14. deterministic failure-injection and print-model tests;
15. real Windows/WPF/print-queue project-owner acceptance.

## 5. Explicit non-scope

M08 must not introduce:

- archived-order selection/hydration/printing (`AC-PRINT-009`, M12);
- B2B invoice/company-customer/invoice-number/credit-note workflow;
- Hiboutik paste import (M09);
- Catalogue workbook behavior (M10);
- Gestion export (M11);
- annual archive execution (M12);
- installer/final V1 acceptance (M13);
- raw ESC/POS or printer-vendor command language as the primary V1 integration;
- printer-driven business persistence or printer acknowledgements as an order-commit condition;
- a print history/business audit subsystem;
- a new order revision-history model;
- any authority grant/transfer/freshness proof caused by printing.

## 6. Architecture

M08 preserves the four-layer structure.

### 6.1 Application-owned print contracts

Introduce/refine narrow Application contracts equivalent to:

- `PrintDocumentKind`: Kitchen / Customer;
- `PrintIntent`: InitialAutomatic / InitialRetry / ExplicitReprint;
- immutable deterministic kitchen/customer print-data records;
- `IOrderPrintDocumentFactory` or equivalent pure document-model factory;
- `IPrintQueueTransport` / `IPrintSpooler` boundary for local Windows submission;
- `IOrderPrintService` / reprint orchestration for independently printing one document;
- an evolved initial dispatcher result that can represent kitchen and customer outcomes independently.

Do not put WPF visual objects, `PrintQueue`, driver objects or printer names into Domain records or persisted Order data.

### 6.2 Deterministic business print model

The deterministic model is generated from:

- the exact committed `OrderSnapshot` actually held by the device;
- the approved receipt-identity source once owner-frozen;
- an explicit business date for “future right now” presentation;
- document kind and print intent.

It must not read current Catalogue for historical line content, current VAT rules for persisted tax reconstruction, unsaved WPF edit controls, authority state as a source of order facts, or printer-driver state as business content.

Ticket labels/markings are stable operational output and must not vary merely because two paired devices currently display different UI languages. M08 UI/status localization is FR/zh-CN; ticket business data remains exactly the persisted operator-entered data. The required French reprint/cancellation markings are literal specification values.

### 6.3 Windows spooler boundary

Use ordinary Windows/WPF printing infrastructure as frozen in `architecture.md`:

- `System.Printing` / `PrintQueue` for installed queue discovery and submission;
- WPF fixed-document / `DocumentPaginator` / XPS-capable writer path where practical;
- queue/driver/page mechanics isolated behind the transport adapter;
- no Excel/COM or printer-vendor SDK requirement;
- no order transaction may stay open while generating or submitting print output.

A submission is considered technically accepted when the Windows printing API successfully accepts/completes the enqueue operation at the application boundary. V1 does not attempt to prove that paper physically emerged.

## 7. Local printer configuration

Printer selection is local installation configuration, not business lineage data.

Extend the existing local technical configuration with separate optional stable queue identifiers/names for:

- Kitchen printer queue;
- Customer printer queue.

They may reference the same physical/Windows queue.

Requirements:

- enumerate currently installed Windows queues on demand;
- display human-readable names;
- preserve configured values across restart;
- detect unavailable/missing configured queue and show actionable localized status;
- allow re-selection without changing order/business data or authority;
- printer configuration remains available on a non-authoritative/read-only device because it is local technical setup, not a business write;
- printer configuration is not synchronized through M07 lineage/handoff/DR business authority state.

No SQLite business-data migration is required solely for printer queue configuration.

## 8. Automatic initial-print sequence

For a valid new order:

1. validate/pricing/business rules;
2. allocate stable order identity;
3. commit complete order atomically to SQLite;
4. finish the DB transaction;
5. complete the existing durable-change notification/recovery semantics;
6. reload the successfully committed `OrderSnapshot`;
7. build kitchen print model from that committed snapshot;
8. build customer print model from that committed snapshot;
9. attempt kitchen submission;
10. attempt customer submission even if kitchen generation/submission failed;
11. return/report independent outcomes without changing persistence success.

M08 may refactor the M04 dispatcher contract so independent outcomes are first-class, but it must preserve the invariant that the order is already durable before any automatic output side effect begins.

The UI must remain responsive while Windows printing work is pending. Do not block the WPF dispatcher with spooler/network-style waits.

## 9. Independent failure and retry semantics

Each document has its own state/outcome at the current operator action:

- not attempted because model generation for that document failed;
- known submission failure / queue unavailable;
- submitted/accepted at the Windows adapter boundary;
- ambiguous/unknown submission outcome if the adapter cannot prove whether the queue accepted it.

One document’s failure must never suppress the other document’s attempt.

The committed order remains intact for every output failure.

### 9.1 Retry distinction

A known failed automatic submission may be retried independently as the same initial-output intent when the adapter can prove the original job was not accepted.

If submission outcome is genuinely ambiguous, M08 must fail conservatively: do not silently emit an indistinguishable second “first” ticket. The UI must report the uncertainty and any operator-initiated additional copy must use explicit reprint semantics/marking.

An explicit normal reprint action always uses reprint markings regardless of whether a previous physical page was lost.

Retry/reprint never allocates a new order ID and never changes lifecycle/payment/authority state.

## 10. Kitchen document

Kitchen model must include at least:

- order reference/ID;
- original creation/confirmation time;
- fulfilment mode;
- planned fulfilment date and time;
- telephone when present;
- address when present;
- full operational comment;
- saved lines in persisted display order;
- quantity, product code and product name;
- saved options/adjustments in deterministic order with operationally readable labels;
- current committed authoritative total.

It does not need CB/Cash merely for payment recording.

### 10.1 Future prominence

When `PlannedFulfilmentDate > IBusinessClock.BusinessDate` at generation time, render a large, difficult-to-miss future-order header containing the exact planned date/time. Persisted date/time remains printed even when a later reprint occurs on the fulfilment day.

Do not use `AdvanceOrderMarker` alone to label an order “future” after its planned date has changed or the business date has caught up.

### 10.2 Reprint/cancel marking

- explicit kitchen reprint: prominent literal `RÉIMPRESSION`;
- any kitchen document generated from current `Cancelled` state: prominent literal `ANNULÉ`;
- cancelled explicit reprint: both markings remain visible.

## 11. Customer document

Customer model must include at least:

- owner-approved Sushi 81 receipt identity block;
- order reference/ID and original order date/time;
- fulfilment mode and planned date/time;
- item quantities/descriptions and saved prices/adjustments;
- current committed authoritative TTC total;
- persisted `OrderTaxBreakdown` VAT buckets and included VAT amounts;
- applicable current committed cumulative Card/Cash payment information;
- required VAT/business identification in the approved receipt block.

Manual-total orders render the persisted authoritative single-10%-VAT snapshot; M08 must never reconstruct a mixed VAT calculation from current product data.

### 11.1 Payment-updated customer reprint

After a later committed payment correction, customer reprint uses the latest committed `OrderSnapshot`, including current cumulative Card/Cash values and current committed total/status. No payment edit itself auto-prints.

### 11.2 Reprint/cancel marking

- explicit customer reprint: prominent literal `DUPLICATA`;
- any customer document generated from current `Cancelled` state: prominent literal `ANNULÉ`;
- cancelled explicit reprint: both markings remain visible.

## 12. Existing-order reprint workflow

Provide independent actions in the live-order workflow:

- Reprint kitchen ticket;
- Reprint customer ticket.

Rules:

1. load/reload by stable order ID through the committed store path immediately before generation;
2. print the latest committed state actually available on that device;
3. retain the same order ID;
4. never read unsaved editor values as business authority;
5. if the order is currently in an unsaved edit mode, the UI must not present those edits as printable/current. The operator must save or abandon the edit before the explicit reprint action can proceed;
6. saving a modification, payment correction, close, reopen or cancellation does not itself auto-print;
7. Cancelled orders remain reprintable with required markings.

The WPF implementation must preserve current search/date/selected-order/editor/filter state except where the print action’s own busy/status fields intentionally change.

## 13. Non-authoritative/read-only device semantics

Printing is not a business-authoritative mutation and must not be gated as a write by `IWriteAuthorityGuard`.

A paired non-authoritative/read-only device may print/reprint the committed live-data copy it actually holds.

Requirements:

- existing M07 persistent non-authoritative/stale warning remains visible and truthful at the print surface;
- print/reprint controls remain available when a committed order can be viewed locally;
- no call may promote authority, change generation, change handoff state, consume a DR checkpoint, claim synchronization, or mark data fresh;
- printed content uses the device’s locally available committed snapshot only;
- technical printer configuration remains permitted;
- stale-generation/recovery-required safety warnings must not disappear because a print succeeds.

M08 tests must cover at least current non-authoritative read-only, released former source and another stale/read-only phase without any authority mutation.

## 14. Localization and operator feedback

All new WPF controls, printer setup, queue-unavailable messages, generation/submission errors, retry/reprint status and stale-data printing explanations require FR and zh-CN resources.

Never display raw exception text containing technical paths/driver internals as the primary operator message. Safe categorized diagnostics may be logged with existing redaction rules.

Language switching must preserve:

- selected order;
- current filters/search/date;
- printer selections;
- current print outcome/retry availability;
- unrelated order edit state;
- M07 warning/authority action state.

Actual ticket business identity/order data is not translated by the UI language switch.

## 15. Failure injection

Provide deterministic failure seams/tests for at least:

1. kitchen model generation failure;
2. customer model generation failure;
3. missing kitchen queue;
4. missing customer queue;
5. kitchen submit throws before acceptance;
6. customer submit throws before acceptance;
7. kitchen failure + customer success;
8. kitchen success + customer failure;
9. both fail;
10. ambiguous/unknown submission result;
11. cancellation token during output after order commit;
12. spooler unavailable after commit;
13. reprint generation/submission failure;
14. local configuration save/reload and missing configured queue;
15. process/app restart after committed order but before retry.

Every case must prove business persistence/authority state remains correct.

## 16. Automated test matrix

### 16.1 Pure deterministic print-model tests

Cover at least:

- Retrait and Livraison;
- same-day and future order;
- phone/address absent and present;
- multi-line order ordering;
- option groups and custom adjustments;
- long comment/address/product/option text wrapping inputs;
- mixed VAT persisted snapshot;
- manual total with persisted single 10% VAT snapshot;
- Card/Cash unpaid/partial/mixed/settled presentation;
- explicit kitchen/customer reprint marks;
- Cancelled marks and cancelled reprint dual marks;
- no current-Catalogue dependency;
- stable deterministic model from the same committed state and explicit business date.

### 16.2 Application/integration tests

Cover:

- durable commit before any automatic submission;
- both initial document attempts occur independently;
- print failure does not roll back order;
- retry does not allocate new order ID;
- save modification/payment/close/cancel produces zero automatic print submissions;
- reprint reloads latest committed state;
- unsaved edit is never sent as committed output;
- read-only/non-authoritative reprint succeeds without write-guard promotion/mutation.

### 16.3 Windows adapter tests

Use adapter abstraction/fakes for deterministic CI and Windows-only focused tests for:

- installed queue enumeration;
- stable configured queue lookup;
- unavailable queue classification;
- fixed-document writer submission on a controlled Windows print queue where available;
- no printer-specific raw protocol dependency.

CI must not require a physical printer.

### 16.4 STA/WPF tests

Exercise actual shown/loaded WPF paths for:

- local printer configuration;
- automatic output-success/failure status after a committed order;
- independent Retry actions;
- order-detail kitchen/customer reprint actions;
- reprint disabled/blocked while unsaved edit mode is active;
- Cancelled order reprint actions;
- read-only stale warning + available print action;
- FR → zh-CN → FR;
- busy state and dispatcher responsiveness;
- preservation of M03/M04/M05/M07 control state.

## 17. Migration and data-safety implications

Expected M08 data-safety shape:

- no change to Order/Payment/Authority/Handoff/DR schema is required by printing itself;
- printer queues are additive local technical configuration;
- no print job/history row is required in SQLite;
- no output failure can participate in order transaction rollback;
- no printer operation can advance business revision;
- no print operation can trigger local/cloud recovery merely because paper was requested;
- no print operation can alter authority state.

The customer receipt-identity decision may require a deliberate business-settings/schema amendment if the owner chooses authoritative in-database editable identity fields. That choice is intentionally unresolved in preparation and must be frozen before implementation.

## 18. Regression boundaries

M08 must prove no regression in:

- M04 new-order pricing/confirmation and exact-ID reload;
- M05 same-ID modification/payment/close/cancel/search/dashboard;
- M06 local recovery and write guard;
- M07 pairing, target-directed handoff, generation/DR/checkpoint/stale fencing;
- current FR/zh-CN selection-state behavior;
- durable business revision advancement only for business writes;
- startup/restart behavior with old `local-settings.json` that lacks printer fields.

## 19. Parallel execution plan

The Codex main agent must freeze shared print contracts first and remain the integrator.

Likely safe lanes after interfaces are frozen:

- **Lane A — deterministic print model/layout:** pure/Application model builder + model snapshot tests;
- **Lane B — Windows spooler/local printer configuration:** Infrastructure queue adapter + configuration tests;
- **Lane C — WPF reprint/printer-status UI/localization:** only after A/B contracts stabilize;
- **Lane D — failure/integration evidence:** independent test fixtures once orchestration contracts stabilize.

Must remain serial/main-agent owned:

- changes to shared Application print contracts;
- `OrderEntryService` initial-dispatch integration;
- `CompositionRoot` wiring;
- authority/read-only integration decisions;
- final WPF integration and manual-acceptance preparation;
- GitHub gate/handoff/merge transitions.

Subagents must not independently reinterpret print content, add invoice behavior, modify authority semantics or start M09+.

## 20. Required verification before CODEX_DONE

Each implementation/review handoff must run the strongest applicable focused tests plus full Release verification. Final M08 candidate requires at least:

- restore;
- Release build, 0 warnings / 0 errors target;
- full Release solution tests, 0 failed / 0 skipped unless an explicitly reviewed reason exists;
- focused deterministic print tests;
- focused STA/WPF tests;
- self-contained `win-x64` publish;
- exact-head GitHub CI;
- no physical-printer manual acceptance claimed by Codex.

`POST_TASK_POWER_ACTION: NONE` unless a future handoff explicitly says otherwise.

## 21. Project-owner real Windows/manual acceptance gate

M08 cannot be Passed from unit/CI evidence alone. Owner acceptance must use the exact reviewed Windows artifact and real Windows printer queues, and must cover at least:

- initial new order prints kitchen + customer after durable save;
- actual ticket readability/layout on the real printer(s);
- future-order prominence;
- one queue failure while the other document still succeeds;
- independent retry;
- same-ID modification does not auto-print;
- explicit kitchen/customer reprint;
- latest committed payment reflected on customer reprint;
- Cancelled `ANNULÉ` + reprint marks;
- read-only paired-device printing with warning and no authority change;
- local printer configuration/restart;
- FR/zh-CN failure/retry UI;
- ordinary M07 authority/handoff behavior remains intact.

Archive printing is not part of M08 owner acceptance.

## 22. Authorization boundary

This prepared contract does **not** authorize implementation.

Before any Codex M08 production change:

1. resolve the material customer receipt-identity gap in GitHub;
2. complete readiness review;
3. obtain explicit project-owner M08 implementation approval;
4. make `milestone-08-authorization.md` durable as Authorized;
5. create the dedicated M08 implementation branch and PR from the then-current preparation `main`;
6. update Issue #4 pointer to that exact PR/branch/handoff;
7. publish exactly one top-level `CODEX_HANDOFF_READY: <id>` on the active M08 PR;
8. only then open Issue #4.

Until all eight steps are satisfied, Codex must report M08 not authorized and make no project changes.
