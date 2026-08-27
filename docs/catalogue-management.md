# Catalogue management

**Status:** Draft — Phase 2 first batch  
**Last updated:** 2026-08-27  
**Product:** Sushi81 POS  
**Purpose:** Freeze the target product catalogue, product-option and batch-maintenance behavior before implementation.

## 1. Scope

This document defines how Sushi81 POS stores and manages the operational product catalogue from the operator's point of view.

It covers:

- product identity and lifecycle;
- editable catalogue attributes;
- activation/deactivation;
- categories and category shortcuts;
- discount eligibility;
- structured product options/choices;
- option-price adjustments;
- catalogue import/export and validation;
- historical-order stability after catalogue changes.

It does not define the physical database schema, order lifecycle or final screen layout.

## 2. Authoritative Phase 1 baseline and Phase 2 refinement

The target catalogue must support at least the product information already required by the approved Phase 1 documents:

- product code used by the operator;
- product name;
- category;
- TTC selling price;
- VAT rate/category;
- active/inactive status;
- discount eligibility;
- category shortcut/display support where retained;
- one or more product options/choices where applicable.

The following requirements remain fixed:

1. product browsing/search must work by code and name;
2. inactive products disappear from normal order selection but may remain in the current catalogue for later reactivation;
3. catalogue changes must never rewrite historical order-item data;
4. confirmed orders must retain a complete sale-time snapshot sufficient to reproduce historical product code, name, base price, VAT and selected option/adjustment information;
5. catalogue maintenance must be possible inside the application without editing Excel;
6. batch import/export must be supported through an approved tabular format such as Excel or CSV;
7. ordinary order entry may not overwrite the catalogue product's base unit price;
8. a product option can have a predefined price adjustment and the operator may need a custom option adjustment for exceptional real-world cases.

Phase 2 explicitly refines the earlier idea that the operator-facing product code should act as a permanent historical identity. Historical orders and the live catalogue are intentionally separated. The catalogue describes what is available for current/future ordering; a confirmed order stores its own sale-time product data and does not depend on the later state of the catalogue.

## 3. Product identity — approved Phase 2 principle

The operator-facing product `Code` is an **editable operational/catalogue code**, not the permanent business identity of historical sales.

Approved principles:

- historical order lines are self-contained sale records and do not obtain their current meaning by looking up the live catalogue;
- once an order is confirmed, its stored product code, product name, price, VAT, options and adjustments remain fixed unless the order itself is explicitly edited under the order-lifecycle rules;
- later catalogue edits do not alter any existing order;
- the operator may change a catalogue product's code;
- a code may later be reassigned to substantially different catalogue content when this is operationally useful;
- prior historical use of a code does not permanently reserve that code;
- catalogue code sequences such as letter-plus-number series may therefore be reorganized without creating permanent gaps solely for historical reasons.

At the database level, implementation should use a separate opaque/internal product identifier (for example an internal `product_id`) so the application can safely identify catalogue records without forcing the operator-facing `Code` to be immutable. That internal identifier is an implementation concern and is not intended to become part of the normal ordering workflow or visible business code.

### Current-catalogue code uniqueness — approved Phase 2 decision

Within the current catalogue, a product code must identify **exactly one catalogue product at a time**.

Therefore:

- two simultaneously existing catalogue product records may not have the same operator-facing code;
- this uniqueness rule applies to the current catalogue state, not to historical orders;
- a code may be edited, released and later reused for another product, provided no second current catalogue record uses that same code at the same time;
- historical orders containing a previously used code do not conflict with or block reuse of that code in the current catalogue;
- the application must reject or clearly prevent an edit/import that would leave duplicate product codes in the current catalogue.

This preserves unambiguous search and ordering while keeping the code system fully reorganizable over time.

## 4. Product maintenance

The application must support practical maintenance of the catalogue without Excel.

Target actions include:

- add product;
- edit product code;
- edit approved product/commercial attributes;
- activate/deactivate product;
- manage category assignment;
- manage discount eligibility;
- manage product options and their price adjustments.

Because historical orders are snapshot-based and independent from the live catalogue, editing current catalogue attributes — including the product code, name, price, VAT or category — must not rewrite historical orders.

Whether the application should also allow permanent deletion of current catalogue records, and under what safeguards, remains to be approved. Historical-order preservation by itself is not a reason to prohibit deletion, because historical orders do not depend on the live catalogue record.

## 5. Categories

The current workbook uses both `Categorie` and `RaccourciCat` to support category filtering and compact display labels such as `[BR]`, `[ML]` and `[RP]`.

The target application must preserve fast category-based product selection. Phase 2 must decide:

- whether category shortcut remains a separate editable field;
- category display/order rules;
- how category rename affects existing products;
- whether inactive/unused categories are retained or removed.

## 6. Product options / choices

The target catalogue must replace the current practice of typing flavour/menu choices into the order-level comment when the choice belongs to a specific product.

A product may need one or more structured option groups, for example a flavour or menu variant. The selected option must be copied into the corresponding historical order item at confirmation time so later catalogue-option changes do not alter that order.

Phase 2 must define:

- whether option groups are single-select, multi-select or both;
- whether an option selection is required or optional;
- minimum/maximum selections where relevant;
- option labels and display order;
- predefined price adjustments;
- custom option-adjustment behavior;
- whether option availability can be activated/deactivated independently from the parent product.

## 7. Historical stability — approved Phase 2 principle

Catalogue maintenance must never silently rewrite historical orders.

Historical order lines and the current catalogue are deliberately **decoupled after order confirmation**.

When an order is committed, the order-item snapshot must contain enough sale-time information that later changes to:

- product code;
- product name;
- current price;
- VAT;
- discount eligibility;
- category;
- option name;
- option price adjustment;
- active/inactive status;
- or even later reuse of the same operator-facing product code for different catalogue content

do not alter the historical order's financial or printed meaning.

Historical reporting therefore reads the values stored on the order/order line rather than reconstructing old sales from the current catalogue.

## 8. Batch import/export

The application must provide a practical way to export and re-import catalogue data for bulk maintenance.

The exact target format remains to be approved. Phase 2 must define:

- Excel, CSV or both;
- mandatory and optional columns;
- stable internal handling for products, categories and options without exposing an immutable business-code requirement;
- how new products are distinguished from updates;
- duplicate current-code handling;
- code changes/reassignments during import;
- invalid VAT/price/category handling;
- preview/validation before applying changes;
- whether import may deactivate products;
- whether import may delete anything;
- rollback/recovery behavior after a failed import;
- export encoding and column order.

Any import result that would leave two current catalogue products with the same code must be rejected or returned for correction before the catalogue update is committed.

No import should partially apply a logically invalid catalogue update without clearly reporting the result.

## 9. Decisions still to freeze

Before this document becomes baseline, Phase 2 must explicitly approve at least:

1. final product fields and which are mandatory;
2. permanent deletion behavior for current catalogue products;
3. category and category-shortcut model;
4. final option-group model;
5. required/optional option-selection behavior;
6. predefined option-price-adjustment catalogue behavior consistent with `business-rules.md`;
7. in-application catalogue editing workflow;
8. final batch import/export format and columns;
9. update/conflict/deactivation/delete behavior during import;
10. validation and preview requirements.

The following identity/history principles are already approved and are no longer open questions:

- confirmed historical orders are independent snapshots and do not depend on the live catalogue;
- operator-facing product codes are editable and may be reused/reassigned over time;
- prior historical use does not permanently reserve a product code;
- within the current catalogue, one product code may belong to only one product at a time;
- later catalogue edits, including code changes, never alter existing confirmed orders;
- implementation may use a hidden immutable internal identifier for safe database handling without exposing that identifier as the business product code.

## 10. Approval rule

This file remains a Draft until the catalogue and option rules above are explicitly approved. Implementation must preserve the strict separation between historical order snapshots and the mutable live catalogue, must keep current catalogue product codes unique, and must not treat the operator-facing product code as an immutable historical identity.