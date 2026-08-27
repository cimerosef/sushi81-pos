# Hiboutik paste-order import

**Status:** Draft — Phase 4 working design  
**Last updated:** 2026-08-27  
**Product:** Sushi81 POS  
**Purpose:** Specify the safe emergency workflow for pasting a Hiboutik automatic order-summary email into Sushi81 POS when Hiboutik server-side printing is unavailable.

## 1. Scope

This document specifies the V1 **Hiboutik emergency paste import** workflow.

It covers:

- accepted input boundary;
- deterministic parsing and normalization;
- operator review before commit;
- preservation of Hiboutik source amount and fulfilment information;
- conversion into the approved Sushi81 POS emergency-order data model;
- validation and failure handling;
- duplicate-risk detection boundary;
- persistence and handoff to printing;
- diagnostics and sensitive-data handling.

It does **not** define:

- general email synchronization;
- automatic reading of the operator's mailbox;
- Hiboutik API integration;
- automatic modification of the original Hiboutik order;
- ordinary POS order entry;
- final kitchen/customer ticket content or printer behavior (`printing.md`);
- export to `Gestion SUSHI 81.xlsm` (`export.md`).

## 2. Authoritative baseline

This specification must preserve the already-approved semantics in:

- `product-requirements.md`, especially FR-050 through FR-056;
- `order-lifecycle.md`, especially the Hiboutik emergency-copy lifecycle boundary;
- `business-rules.md` for any POS-side pricing or manual-total behavior eventually approved for this workflow;
- `data-model.md`, especially `Order.source_type = HIBOUTIK_EMERGENCY` and the one-to-one `EmergencyImportDetail` extension;
- `storage-strategy.md` for durability, recovery and multi-device authority.

The following rules are already frozen and are not reopened here:

1. The emergency record is a **local operational/printing copy** of an order that already exists in Hiboutik.
2. It is not a new ordinary POS-originated sale.
3. It must preserve the **original Hiboutik total** separately from the POS operational/actual total.
4. It may retain payment information for reconciliation/reference.
5. It is excluded from ordinary POS-originated turnover totals, ordinary received-payment totals, the POS card amount to newly enter into Hiboutik, and export to `Gestion SUSHI 81.xlsm`.
6. Future/due-today reminder behavior still applies according to structured planned fulfilment date and the approved `advance_order_marker` semantics.
7. Parsed content must be reviewed before it becomes a committed/printed emergency order.
8. A committed order is persisted before printing is attempted.

## 3. Entry point and source boundary

V1 provides a dedicated action for **Hiboutik emergency paste import**.

The operator pastes the textual content of the automatic Hiboutik order-summary email into a dedicated input area.

V1 does not:

- connect to Gmail/Outlook to retrieve the message automatically;
- monitor the clipboard in the background;
- scrape the Hiboutik website;
- call the Hiboutik API;
- execute HTML, scripts or embedded content from the pasted material.

The input is treated as untrusted plain text.

## 4. High-level workflow

The workflow is deliberately two-stage:

### Stage A — Parse and review

1. Operator opens the emergency-import action.
2. Operator pastes the Hiboutik email text.
3. The application normalizes the text without changing business meaning.
4. The parser extracts recognized order information.
5. The application validates the extracted result.
6. A structured review screen shows the values that would become the emergency order.
7. No business record is created yet.

### Stage B — Confirm and persist

1. Operator confirms the reviewed result.
2. The application performs final validation against the current reviewed values.
3. The complete emergency order is written transactionally to the local authoritative SQLite database.
4. Only after the database commit succeeds may the application invoke the printing workflow.
5. Print failure leaves the committed emergency order available for selective reprint.

Closing/cancelling the import before Stage B must leave the business database unchanged.

## 5. Input normalization — technical decision

Before field extraction, the parser may safely normalize presentation-only differences such as:

- CRLF/LF line endings;
- repeated blank lines;
- non-breaking spaces and ordinary Unicode spacing variants;
- surrounding whitespace;
- common Unicode punctuation variants where normalization is unambiguous;
- copied mail-client quoting/formatting artifacts that do not carry business meaning.

Normalization must not silently rewrite:

- digits inside prices, dates, times, telephone numbers or product codes;
- product names;
- addresses;
- comments/instructions;
- quantities;
- fulfilment mode;
- monetary signs.

The parser should prefer label/structure recognition over fragile fixed character positions.

## 6. Parsing architecture — technical decision

The parser is deterministic and testable independently from the WPF UI.

Recommended internal stages:

1. **source recognition** — determine whether the text plausibly matches a supported Hiboutik order-summary format;
2. **header/order metadata extraction**;
3. **customer/fulfilment extraction**;
4. **order-line extraction**;
5. **total extraction**;
6. **cross-field validation**;
7. **structured parse result** with values, confidence/ambiguity flags and field-level errors.

The parser must not commit directly to SQLite.

A parsing exception or unsupported format returns a structured failure result and performs no business write.

## 7. Business information to extract when present

The parser/review model must be able to represent at least the following information where the Hiboutik source provides it:

### 7.1 Source identification

- Hiboutik order/reference number or other source reference, when present;
- source/order timestamp when present.

A Hiboutik source reference is not used as the Sushi81 POS `order_id`.

### 7.2 Fulfilment

- `Retrait` or `Livraison`;
- requested/planned fulfilment date;
- requested/planned fulfilment time.

The parser must not lose a future fulfilment date. A future Hiboutik order must never be silently converted into a same-day order merely because the import occurs today.

### 7.3 Customer/operational information

Where present:

- telephone;
- delivery address;
- free-text customer/preparation instructions or comments.

Telephone formatting follows the normal Sushi81 POS display/normalization behavior after extraction; source digits must not be invented.

### 7.4 Ordered items

For each recognizable item line, the structured review result should preserve as much source information as is available, including:

- quantity;
- source product code when present;
- product name/description;
- source line/unit price information when present;
- option/variant/add-on text when present.

Exact rules for matching imported lines to the current Sushi81 catalogue remain a business/workflow decision to freeze below.

### 7.5 Hiboutik original total

The parser must extract and preserve the order total supplied by Hiboutik.

On commit this value becomes:

`EmergencyImportDetail.hiboutik_original_total_ttc`

It must never be overwritten merely because the POS operational/actual total is later changed.

## 8. Emergency-order data mapping — already approved baseline

A confirmed emergency import uses the normal order structures with:

`Order.source_type = HIBOUTIK_EMERGENCY`

It uses normal `Order` / `OrderItem` / adjustment snapshot structures so the record can be viewed, modified where permitted, printed/reprinted, archived and included in future/due-today operational reminders.

The imported record also owns one `EmergencyImportDetail` row containing at least the immutable original Hiboutik total.

The order receives its own normal Sushi81 POS `order_id` only when the operator confirms the reviewed import.

A newly committed emergency order follows the normal lifecycle starting point and is **Open** until the operator later completes the approved payment/reconciliation close action.

Recorded CB/Espèce information is not invented from the Hiboutik source unless an explicit later rule says that the source text reliably represents actual payment received. The emergency record exists even when no payment has yet been recorded in Sushi81 POS.

## 9. Parser/source metadata — technical decision

`paste-order-import.md` authorizes small Hiboutik-specific technical metadata in `EmergencyImportDetail` when useful for diagnostics and duplicate-risk control, for example:

- `hiboutik_source_reference` when present in the source;
- `imported_at`;
- `parser_version`;
- a one-way fingerprint/hash of normalized source content.

These values do not change the business meaning of the order.

The application should **not retain the full raw pasted email text by default** after a successful commit. The structured business fields are authoritative. Avoiding raw-text retention reduces unnecessary duplication of customer/address information.

Technical logs must not record the full pasted email or other unnecessary customer/payment content.

## 10. Validation principles

### 10.1 Never guess material business facts

If a parser ambiguity could change an actual business fact, the application must surface it rather than silently choosing a value.

Material facts include at least:

- order total;
- fulfilment mode;
- planned fulfilment date;
- product/quantity interpretation;
- any mapped product identity that affects printed preparation information or POS-side pricing.

### 10.2 Blocking parser failures

The import cannot be committed while a required business fact remains unusable under the final approved rules.

Examples of parser-level blocking conditions include:

- text is not recognized as a supported Hiboutik order summary;
- no usable Hiboutik original total can be established;
- item structure is too malformed to create the required order lines;
- a required material field remains ambiguous after the approved review/correction workflow.

The exact required-field set for fulfilment and item resolution depends on the business decisions still open below.

### 10.3 No partial commit

Emergency import is atomic.

If final validation or persistence fails:

- no partial order/header/items/payment/source detail may remain committed;
- no print job may be treated as the authoritative creation event;
- the operator remains able to correct/retry the import.

## 11. Future-order behavior

The emergency order participates in the same future-order derivation as ordinary orders.

When a confirmed emergency import has a planned fulfilment date later than the current business date:

- it is treated as a future order for operational reminders;
- `advance_order_marker` is set according to the already-approved sticky rule;
- when its planned date arrives it appears in the due-today advance-order reminder unless Cancelled.

The emergency source type does not exempt the order from this reminder behavior.

## 12. Financial/statistical boundary

For all emergency imports:

- `hiboutik_original_total_ttc` remains available for comparison;
- `Order.total_ttc` represents the current POS operational/actual amount under the final approved emergency-import workflow;
- current CB/Espèce amounts may be recorded for reconciliation/reference;
- discrepancy is derived by comparing the original Hiboutik amount, POS operational/actual amount and recorded payment outcome.

However emergency imports are always excluded from:

- ordinary POS-originated real-time turnover;
- ordinary POS-originated received-payment totals;
- the ordinary POS card amount that must newly be represented in Hiboutik;
- `Gestion SUSHI 81.xlsm` export.

This exclusion is based on `source_type`, not on UI filtering or operator memory.

## 13. Duplicate-risk control — technical detection boundary

Creating the same Hiboutik emergency order twice can create operational risk, especially duplicate kitchen preparation.

V1 should therefore detect likely repeat imports using, in descending strength when available:

1. exact Hiboutik source/reference identifier;
2. normalized-source fingerprint;
3. secondary comparison of relevant source facts when needed for warning diagnostics.

The detection mechanism is technical. The exact operator behavior after a likely duplicate is detected — hard block, open existing record, or allow an explicit override — is a business/operational decision still to freeze.

## 14. Printing handoff

This document freezes only the boundary:

- persistence succeeds before printing starts;
- print-data generation uses the committed emergency order snapshot;
- a print failure does not roll back or delete the committed order;
- the emergency record remains reprintable under the final `printing.md` rules.

Which documents print automatically and the handling of non-authoritative read-only devices are deferred to `printing.md`.

## 15. Testing and sanitized fixtures — technical requirement

The parser must have automated tests using synthetic or sanitized examples.

Before Phase 4 is frozen, the repository should contain sanitized representative Hiboutik source fixtures covering at least:

- ordinary same-day Retrait;
- ordinary same-day Livraison;
- future fulfilment date/time;
- optional customer information absent/present;
- multiple order lines;
- option/variant text if Hiboutik emits it;
- decimal/spacing variants actually observed in the source format;
- malformed/unsupported input;
- duplicate-source/reference scenario.

No real customer name, telephone, address, email, payment credential or other sensitive production information may be committed to Git.

## 16. Business/workflow decisions still requiring confirmation

The existing approved documents do not yet freeze the following operator-facing choices. These must be resolved sequentially before this document can become Approved:

1. **Review-screen editability:** which parsed business fields the operator may correct before committing the emergency order.
2. **Catalogue matching:** whether/how imported Hiboutik item lines must be matched to current Sushi81 catalogue products and what happens to an unmatched line.
3. **Initial POS operational total:** whether the emergency order initially takes the Hiboutik original total or is recalculated immediately under Sushi81 POS pricing rules before any operator adjustment.
4. **Missing/ambiguous fulfilment date:** whether commit is blocked until the operator explicitly supplies/confirms the date rather than defaulting to today.
5. **Duplicate detection response:** whether a detected existing Hiboutik emergency order is blocked/opened/reprinted or may be duplicated through an explicit override.

These are not technical implementation details because they change what the operator can do and may create real preparation/reconciliation risk.

## 17. Approval rule

This document remains **Draft — Phase 4 working design** until the business/workflow decisions in section 16 are confirmed and representative sanitized Hiboutik source samples have been validated against the parser specification.

Pure parser implementation details that do not alter the business meaning or operator permissions may be selected directly according to the project priority order: reliability > simplicity > maintainability > operational clarity > novelty.