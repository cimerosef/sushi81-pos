# Hiboutik paste-order import

**Status:** Draft — Phase 4 working design  
**Last updated:** 2026-08-27  
**Product:** Sushi81 POS  
**Purpose:** Specify the simple fallback workflow that converts pasted Hiboutik order-summary text into an ordinary Sushi81 POS order when Hiboutik server-side printing is unavailable.

## 1. Scope

V1 provides a **Hiboutik paste-order import** as a fast order-creation aid.

Its purpose is deliberately narrow:

**paste Hiboutik order text -> pre-populate a normal Sushi81 POS order -> operator reviews/edits the normal order -> confirm -> print normally.**

This function is not a Hiboutik synchronization, reconciliation or duplicate-order-management subsystem.

It does not define the final ticket layout or printer behavior; those belong to `printing.md`.

## 2. Authoritative simplification decision

The approved Phase 4 decision in `docs/decisions/hiboutik-paste-simplification.md` supersedes the earlier, more complex emergency-order design.

The imported order must have **no special operator-facing order type or interface**.

After parsing:

- the same ordinary order-entry screen is used;
- the same product/cart editing controls are used;
- the same fulfilment, telephone, address, comment, total and payment fields are used;
- the same confirmation, modification, cancellation and printing behavior applies;
- there is no dedicated emergency-order screen;
- there is no special colour/style, dashboard counter or discrepancy warning;
- there is no dedicated Hiboutik order-number field;
- there is no dedicated Hiboutik reconciliation panel;
- there is no separate immutable Hiboutik-original-total field in V1.

If the operator wants to retain the Hiboutik order number/reference, it is entered manually in the normal order **comment** field.

## 3. Entry point

The application provides a simple action such as **Paste Hiboutik order** from an appropriate normal order-creation entry point.

The operator:

1. opens the paste action;
2. pastes the textual content of the Hiboutik automatic order-summary email;
3. asks the application to parse it;
4. is returned to the ordinary order-entry screen with recognized values pre-populated;
5. completes any required option confirmations and reviews/edits the order exactly as a manually created order;
6. confirms the order through the normal confirmation action.

No order is committed merely by pasting or parsing text.

## 4. Source boundary

V1 does not:

- connect to Gmail/Outlook automatically;
- monitor the mailbox;
- scrape the Hiboutik website;
- call the Hiboutik API;
- monitor the clipboard in the background;
- execute HTML, scripts or embedded content from pasted text.

The pasted source is treated as untrusted plain text.

## 5. Parsing architecture — technical decision

The parser is deterministic and independently testable.

It should use tolerant label/structure recognition rather than fixed character positions.

Safe normalization may include:

- CRLF/LF normalization;
- repeated blank-line cleanup;
- non-breaking-space normalization;
- trimming surrounding whitespace;
- harmless Unicode punctuation/spacing normalization;
- removal of copy/paste formatting artifacts that do not carry business meaning.

Normalization must not silently alter business values such as:

- product codes or names;
- quantities;
- prices;
- dates/times;
- telephone digits;
- addresses;
- comments/instructions.

A parser exception or unsupported format performs no business write.

## 6. Ordinary order fields that may be pre-populated

When reliably present in the pasted Hiboutik text, the parser may populate ordinary Sushi81 POS fields including:

- ordered products identified by product code;
- quantities;
- fulfilment mode (`Retrait` / `Livraison`);
- planned fulfilment date;
- planned fulfilment time;
- telephone;
- delivery address;
- customer/preparation comments;
- source order total, subject to the final total rule still to be frozen below.

The pasted Hiboutik order text used by this workflow is **not expected to contain the Sushi81 product-option selections**. Option values must therefore not be invented or inferred from absence.

All values populated by parsing are ordinary editable order values after parsing.

The parser must preserve a future fulfilment date when the source provides one. A future Hiboutik order must not silently become a same-day order merely because it is pasted today.

## 7. Product-line conversion and option completion — approved Phase 4 rule

Sushi81 POS and Hiboutik use strictly aligned product catalogues and the same product codes.

### 7.1 Product matching

Each Hiboutik product line is matched to the current Sushi81 POS catalogue by **exact product code**.

Normal paste-import behavior does not require fuzzy matching by product name.

If the parser cannot establish a usable product code or the code is unexpectedly absent from the current catalogue, the application must not silently substitute or guess another product. The affected line must remain unresolved for operator correction before final order confirmation.

### 7.2 Products without options

If the matched product has no enabled option groups, the parsed product and quantity may be added directly to the ordinary cart.

### 7.3 Products with options

If the matched product has one or more enabled option groups, the operator must manually confirm the option configuration because the pasted Hiboutik text does not provide those selections.

The option confirmation must use the **same ordinary option-selection UI and validation rules used during manual POS order entry**.

No separate Hiboutik-specific option editor or rule set is required.

This confirmation is required for **all enabled option groups**, including groups configured as optional.

The reason is that an empty option in the pasted source cannot distinguish between:

- the customer genuinely choosing no option; and
- the customer choosing an option that the copied Hiboutik text simply does not contain.

For an optional group, the normal option UI must therefore allow the operator to explicitly confirm that no selection is required when that is the correct result.

All normal catalogue rules continue to apply, including:

- required versus optional groups;
- single-select versus multi-select;
- configured minimum/maximum selections;
- active/inactive choices;
- configured display order;
- preset and custom option adjustments;
- ordinary discount and VAT treatment.

The order cannot be finally confirmed while a pasted product with enabled options still lacks the required operator option confirmation.

After option confirmation, the imported line is an ordinary cart line and may be edited normally, including quantity changes, option changes, adding/removing products and other approved cart actions.

This rule is frozen in `docs/decisions/hiboutik-paste-option-confirmation.md`.

## 8. Normal order lifecycle after parsing

After the parser populates the order-entry screen, the order is treated like an ordinary in-progress order.

Therefore:

- leaving/cancelling before confirmation creates no durable order;
- confirmation allocates the normal Sushi81 POS order ID;
- the confirmed order starts under the normal lifecycle rules;
- future-order behavior follows the normal planned-fulfilment-date and `advance_order_marker` rules;
- modification/cancellation uses the normal order workflow;
- the operator does not manage a separate emergency-order lifecycle.

The operator may manually type the Hiboutik order number into the normal comment field before or after confirmation, subject to ordinary order editing rules.

## 9. Minimal hidden source marker

Although the interface and workflow are ordinary, implementation may persist one small non-user-facing source discriminator for an order created through the Hiboutik paste-import entry point.

The operator does not see, edit or manage this marker.

Its sole business purpose is to preserve the already-established anti-double-counting boundary: the underlying order already exists in Hiboutik, so a pasted Hiboutik order must not be treated as a new POS-originated sale in downstream POS-only totals/export.

Accordingly it remains automatically excluded from:

- ordinary POS-originated turnover totals;
- ordinary POS-originated received-payment totals;
- the ordinary POS card amount that must newly be entered/represented in Hiboutik;
- export to `Gestion SUSHI 81.xlsm`.

No additional Hiboutik-specific business entity is required solely for this marker.

## 10. No dedicated Hiboutik metadata model

V1 does **not** require:

- `EmergencyImportDetail`;
- `hiboutik_original_total_ttc`;
- a dedicated Hiboutik reference-number field;
- raw pasted-email retention after successful conversion;
- parser fingerprints for operator-facing duplicate management;
- a Hiboutik-specific reconciliation status;
- a dedicated imported-order discrepancy state.

If a source order number is useful operationally, the operator records it manually in the normal comment field.

Technical logs must not retain full pasted customer/order text unnecessarily.

## 11. Validation and failure behavior

### 11.1 Parser failure

If the source cannot be parsed reliably enough to create useful order lines, the application must report the problem and leave the business database unchanged.

The operator may correct the pasted text, retry, or abandon the import and create the order manually.

### 11.2 Ambiguous fields

The parser must not invent material business facts.

If a field is ambiguous, it should be left empty or clearly flagged for operator review rather than silently guessed when the guess could affect preparation, fulfilment or price.

### 11.3 Final validation

The final confirmation uses the **normal order validation rules** from `business-rules.md` and `order-lifecycle.md`, together with the required option-confirmation rule in section 7.

The paste importer does not create a second, parallel validation system.

## 12. Persistence and printing boundary

The confirmed order must be committed successfully before printing is attempted.

Therefore:

1. operator confirms the normal order;
2. order is durably committed to SQLite;
3. normal printing workflow is invoked;
4. print failure does not roll back or delete the order;
5. the order remains retrievable and reprintable under `printing.md`.

The pasted-source workflow must not make printing itself the event that creates the business record.

## 13. Duplicate handling

V1 does not require a dedicated Hiboutik duplicate-detection subsystem.

Because the operator may record the Hiboutik order number manually in the ordinary comment field and the paste tool is intended as a lightweight fallback, duplicate prevention should not introduce a special order workflow unless later real-world use demonstrates a concrete need.

This intentionally removes the earlier planned source-reference/fingerprint duplicate-control mechanism.

## 14. Testing and sanitized fixtures

The parser must be covered by automated tests using synthetic or sanitized Hiboutik source examples.

Representative fixtures should cover, where the real Hiboutik format supports them:

- same-day Retrait;
- same-day Livraison;
- future fulfilment date/time;
- multiple products and quantities;
- products without options;
- products whose Sushi81 catalogue definition requires one or more option confirmations despite the pasted source containing no option selection data;
- optional option groups explicitly confirmed with no selection;
- optional telephone/address/comment information;
- ordinary formatting/spacing variations;
- malformed/unsupported text;
- an unexpectedly unknown/missing product code that must not be guessed by name.

No real customer telephone, address, name or other sensitive production information may be committed to Git.

## 15. Remaining business decision

The Phase 4 simplification and the approved product/option rule now resolve:

- special review fields;
- original-Hiboutik amount preservation;
- dedicated reference fields;
- discrepancy handling;
- duplicate controls;
- catalogue product matching;
- missing option information and option confirmation.

One business choice still materially affects the resulting normal order and therefore requires confirmation:

**Initial order total — whether the total contained in the pasted Hiboutik order should become the initial authoritative order total, or whether the order should initially use the normal Sushi81 POS catalogue calculation after product lines and operator-confirmed options are established.**

All lower-level parser choices that do not change this business meaning are technical implementation decisions and may be selected directly under the project priority order.

## 16. Approval rule

This document remains **Draft — Phase 4 working design** until the remaining initial-total decision is frozen and the parser has been checked against representative sanitized Hiboutik source examples.

The final design must remain a lightweight order-creation convenience rather than growing back into a separate Hiboutik-order subsystem.
