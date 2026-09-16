# M09 — Hiboutik paste-order fallback — implementation contract

**Status:** Implemented; owner manual acceptance Passed; ready for separate merge approval
**Date:** 2026-09-14  
**Milestone:** M09  
**Primary acceptance ownership:** AC-HIB-001 through AC-HIB-009, as amended by `acceptance-criteria-amendment-m09-hiboutik-paste-fallback.md`  
**M11 later cross-check:** final `Gestion SUSHI 81` export exclusion

## 1. Objective

Implement the narrow Hiboutik fallback that lets the operator paste a real Hiboutik product-detail block, obtain an ordinary Sushi81 order draft quickly, explicitly resolve any unknown lines, complete normal order fields/options, and then use the existing ordinary pricing/confirmation/printing lifecycle.

M09 must not create a second emergency-order subsystem.

## 2. Non-negotiable business semantics

The implementation must preserve all of the following:

- paste/parse does not create a durable order;
- input is untrusted plain text;
- input scope is the Hiboutik product-detail block, not full-email metadata extraction;
- per-item `Total : ...` and final `TOTAL ...` lines are tolerated automatically;
- known `1 x Livraison (0)` is a source technical/service line, not a Sushi81 cart product;
- automatic product matching is current active catalogue exact code only;
- no fuzzy name-based automatic matching;
- unknown/material source text cannot disappear silently;
- unresolved lines require explicit operator product selection or explicit not-a-product/ignore disposition;
- unresolved lines block final confirmation;
- products with enabled option groups require ordinary explicit option confirmation, including explicit no-selection for optional groups;
- ordinary POS pricing is authoritative;
- nullable `source_total_ttc` is read-only reference data only;
- source indicator is system-controlled/non-editable but may be shown passively in ordinary list/detail;
- later order lifecycle/edit/payment/print behavior is ordinary;
- POS-originated reporting exclusions remain driven by `source_type`;
- no emergency screen/status/counter/discrepancy/duplicate/payment subsystem;
- no raw pasted text retention after conversion.

## 3. Required implementation shape

Implementation may choose internal class/file names, but should preserve the existing architecture boundaries.

Recommended shape:

### 3.1 Pure parser

Create an independently testable parser in Application or a similarly appropriate non-UI layer.

Input:

- plain `string` only.

Output should be immutable structured parse data, sufficient to distinguish:

- parsed product candidate line;
- known ignored source line;
- unresolved line;
- final/source total parse result and reliability.

The parser must perform no:

- SQLite access;
- network access;
- clipboard monitoring;
- printing;
- business write;
- catalogue fuzzy search.

### 3.2 Exact-code catalogue resolution

Add/use a catalogue query that resolves a product by exact current operator-facing product code among active products.

Do not implement exact matching by calling a broad name/code search and choosing the first result.

A technically separate exact-code query is preferred because it makes AC-HIB-004 mechanically testable.

### 3.3 Transient import session state

The WPF/application order-entry draft needs transient import state for:

- source line text for unresolved review;
- parsed quantity/code when reliable;
- selected manual resolution product when applicable;
- explicit ignore decision;
- imported-line option-confirmation completeness;
- nullable reliable source total.

This state must not be persisted as a Hiboutik detail entity.

### 3.4 Ordinary cart population

Resolved products without enabled option groups may enter the ordinary cart immediately with parsed quantity.

Resolved/manual products with enabled option groups must go through the existing ordinary option-selection UI before they become confirmable imported cart lines.

After insertion, lines are ordinary cart lines and support ordinary quantity/edit/remove behavior.

### 3.5 Source-aware ordinary new-order confirmation

Extend the ordinary new-order confirmation input/path so that provenance is supplied internally/systemically rather than user-editable.

Manual POS creation must continue to produce `OrderSourceType.Pos`.

The Hiboutik paste workflow must produce `OrderSourceType.HiboutikPaste`.

Do not fork/duplicate ordinary pricing, validation, snapshot creation, persistence, recovery notification or M08 printing orchestration.

### 3.6 `source_total_ttc`

Add nullable durable source-reference amount to the logical/physical order representation.

Required semantics:

- only meaningful for `HIBOUTIK_PASTE`;
- nullable;
- captured on new paste-created order when parser result is reliable;
- not operator-editable;
- preserved across later ordinary order modifications;
- never recalculated from current catalogue;
- never used as authoritative total;
- not included as separate money in reporting/export/printing.

A migration must be versioned, additive and safe for existing business data.

### 3.7 Passive source presentation

Ordinary order list/detail should expose enough read-only information for evening matching:

- compact `Hiboutik` source indication for paste-created orders;
- source total when non-null.

Do not add separate navigation, dashboard counter or emergency visual system.

## 4. Parser contract

### 4.1 Normalization

Permitted harmless normalization includes:

- CRLF/LF normalization;
- NBSP to ordinary-space normalization;
- leading/trailing whitespace trimming;
- harmless repeated whitespace/blank-line normalization where structural meaning is not lost;
- harmless Unicode punctuation normalization needed for copied email text.

Do not normalize product code characters, quantities or monetary digits into guessed values.

### 4.2 Product candidate syntax

The real source supports a practical shape equivalent to:

`<positive quantity> x <candidate code> <remaining source description>`

The parser must not need the source display description/price to equal current catalogue values.

A parsed candidate code remains source text until exact catalogue resolution succeeds.

### 4.3 Known ignored lines

At minimum support:

- `Total : <amount>`;
- `TOTAL <amount>`;
- known Hiboutik `Livraison (0)` technical/service product-like line.

These are not cart products.

### 4.4 Unknown lines

A non-blank material line that is not safely recognized must become unresolved, not be dropped.

Formatting-only harmless lines may be normalized away only where there is no plausible business meaning.

### 4.5 Source-total reliability

Preferred rule:

1. parseable final `TOTAL` present -> use it as source reference;
2. otherwise derive from source/per-item totals only if the block provides a complete unambiguous set;
3. otherwise null.

Implementation must have tests showing it refuses to create a false precise source total from an incomplete block.

A source total may be captured even when one product line still requires operator product resolution, provided the monetary source block itself is complete/unambiguous.

## 5. Unresolved-line operator workflow

The ordinary order-entry area may include a compact import-review panel/dialog. It is not an emergency-order screen.

For every unresolved line show enough source context to decide safely, without persisting it after conversion.

Required actions:

- **Select product**: invoke/reuse ordinary current active catalogue selection/search;
- **Ignore / not a product**: explicit operator decision.

Rules:

- no silent auto-ignore for unknown material text;
- manual product selection may use name/code browsing because the human is making the decision;
- reliably parsed quantity should prefill the resolved cart quantity;
- invalid/missing quantity requires explicit correction;
- if selected product has options, existing ordinary option UI is mandatory;
- final order confirmation disabled/rejected while unresolved lines remain.

## 6. Option-confirmation semantics

For imported lines, absence of source option data is not confirmation.

Each enabled option group requires an explicit reviewed state.

For optional groups, the implementation needs a way to distinguish:

- not reviewed yet; from
- explicitly reviewed with no selection.

This may be transient UI/application state and must not create durable Hiboutik metadata.

Ordinary configured min/max/required/active-choice and option-adjustment pricing/VAT rules remain unchanged.

## 7. Pricing/source-total separation

Required regression example:

- source block total = €35.90;
- imported/reviewed POS cart normal pre-discount amount = €35.90;
- Retrait 10% applies -> authoritative POS total = €32.31;
- persisted `source_total_ttc` remains €35.90;
- `Order.total_ttc` is €32.31;
- receipt/customer selling total is €32.31;
- payment Close arithmetic targets €32.31;
- source total is visible only as reference in ordinary order lookup/detail.

Also test catalogue-price divergence:

- source line/source total may reflect old Hiboutik price;
- current Sushi81 catalogue exact code still determines current POS pricing;
- no source amount overrides current pricing.

## 8. Authority/recovery/write boundary

Parser and transient review must not call the business write guard because they do not persist business state.

Final confirmation must use the existing centralized write-authority guard.

Failure cases:

- non-authoritative final confirmation -> ordinary authority-blocked result, no order write;
- DB failure -> no partial order/source-total write;
- durable order commit followed by print failure -> order remains committed/retrievable/reprintable;
- notifier/snapshot scheduling failure -> does not roll back committed order, same as existing ordinary behavior.

## 9. Reporting/search behavior

For `HIBOUTIK_PASTE` orders verify:

Excluded:

- ordinary POS-originated turnover;
- ordinary POS-originated received totals;
- ordinary POS-originated CB amount-to-represent-in-Hiboutik logic when implemented/currently exposed;
- M11 final export selection.

Included/ordinary:

- live order search;
- planned-date browsing;
- future count/view;
- due-today advance-order workflow;
- overdue-unsettled workflow;
- payment editing;
- Close/reopen/cancel;
- printing/reprinting.

## 10. Localization

Provide French and Simplified Chinese labels/messages for at least:

- paste action;
- paste instructions (“paste the Hiboutik product block; Total lines are ignored automatically”);
- unresolved count/state;
- select product;
- ignore/not a product;
- unresolved-confirmation block message;
- Hiboutik source label;
- Hiboutik source amount label/unavailable state;
- parser unsupported/empty/no-useful-lines messages.

User-entered/source business text is not translated.

## 11. Work packages for Codex

Once implementation is separately authorized, handoffs should keep product design out of Codex and use small execution packages.

### WP1 — Domain/data migration and exact-code seam

- add nullable order source-total field across domain/persistence;
- versioned SQLite migration/backward-safe read/write;
- preserve field across lifecycle modifications;
- exact active-code catalogue query;
- unit/integration tests.

### WP2 — Pure parser and synthetic fixtures

- implement normalization/classification/source-total reliability;
- use synthetic fixture(s) only;
- parser unit tests including malformed/unknown/incomplete totals.

### WP3 — Application import orchestration

- parser + exact-code resolution;
- import result/session DTOs;
- unresolved resolution state;
- source provenance/source-total transfer into ordinary draft confirmation;
- option-confirmation completeness rules;
- tests.

### WP4 — WPF workflow/localization

- compact paste entry/action;
- source text input and parse action;
- unresolved review with select-product/explicit-ignore;
- ordinary option UI reuse;
- passive Hiboutik/source-total display in ordinary order browser/detail;
- FR/ZH localization;
- presentation tests where practical.

### WP5 — Integration/regression closure

- authority/no-write/failure cases;
- source-total vs POS pricing/discount separation;
- source preservation on modification;
- reporting exclusion/inclusion behavior;
- M08 commit-before-print/print-failure/reprint regressions;
- Release full test suite/build/publish as required by milestone gate.

### WP6 — Evidence and manual-acceptance build

- update worklog/traceability;
- produce exact-head Windows/WPF build for owner manual acceptance;
- no merge without owner approval.

Codex must not start M10.

## 12. Automated testing/failure matrix

At minimum implement tests for:

| Area | Required case |
|---|---|
| Parser | CRLF/LF and NBSP/spacing variants |
| Parser | multiple product lines / quantities |
| Parser | per-item `Total :` lines ignored as cart items |
| Parser | final `TOTAL` captured as source total |
| Parser | final `TOTAL` absent + complete per-line totals derive source total |
| Parser | incomplete per-line totals -> source total null |
| Parser | `Livraison (0)` known technical line ignored as cart item |
| Parser | unknown material line -> unresolved |
| Parser | malformed/unsupported input -> no business write |
| Resolution | exact active code resolves |
| Resolution | unknown code unresolved; no name guess |
| Resolution | missing code unresolved |
| Resolution | manual product selection preserves reliable quantity |
| Resolution | explicit ignore resolves non-product line |
| Validation | unresolved line blocks final confirmation |
| Options | product without enabled groups enters cart directly |
| Options | required option group confirmation |
| Options | optional group explicit no-selection confirmation |
| Pricing | source display price ignored |
| Pricing | source total differs from current POS calculation |
| Pricing | Retrait discount creates POS/source total divergence |
| Pricing | ordinary manual total override still works |
| Persistence | paste/parse/abandon creates no order |
| Persistence | confirmed source type = `HIBOUTIK_PASTE` |
| Persistence | nullable source total round-trips |
| Persistence | source total preserved across modification |
| Authority | read-only/non-authoritative final confirm blocked |
| Reporting | paste order excluded from POS turnover |
| Reporting | payment deltas excluded from POS received summary |
| Operations | paste order still searchable/future/due/overdue |
| Printing | commit precedes initial print |
| Printing | initial print failure does not roll back |
| Printing | ordinary reprint works from latest committed state |
| Privacy | logs/tests do not retain real pasted production data |
| Localization | FR/ZH keys present and switchable |

## 13. Out of scope

M09 must not implement:

- Gmail/Outlook connector;
- Hiboutik API/scraping;
- background clipboard watcher;
- duplicate detector;
- dedicated Hiboutik reference field;
- discrepancy state/panel;
- dedicated reconciliation workflow;
- emergency order subtype/screen/counter;
- M10 catalogue `.xlsx`;
- M11 export workbook implementation;
- M12 archives;
- M13 installer work.

## 14. Completion gate

The M09 completion conditions are satisfied for the accepted production candidate `7d0144d452231fe92cf7c31e027e9b1bb6f5a43d`: required automated evidence and clean Release build evidence are recorded in PR #17, owner Windows/WPF manual acceptance is **PASSED**, AC-HIB-001 through AC-HIB-009 traceability is reconciled in the final manual-acceptance record, and no unresolved M09 implementation/specification blocker remains.

The final documentation/evidence closure head is intentionally later than the accepted production candidate and does not replace it. PR #17 remains OPEN / unmerged pending separate explicit project-owner merge approval. Issue #4 remains the sole Codex execution gate; `CODEX_DONE` does not authorize merge, M10, M11 or any post-M09 enhancement.
