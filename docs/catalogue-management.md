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
- activation/deactivation and deletion;
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

## 4. Product maintenance — approved Phase 2 decisions

The application must support practical maintenance of the catalogue without Excel.

Target actions include:

- add product;
- edit product code;
- edit approved product/commercial attributes;
- activate/deactivate product;
- permanently delete a catalogue product;
- manage category assignment;
- manage discount eligibility;
- manage product options and their price adjustments.

Because historical orders are snapshot-based and independent from the live catalogue, editing or deleting current catalogue attributes — including the product code, name, price, VAT or category — must not rewrite historical orders.

### Base product fields — approved Phase 2 decision

The normal current-catalogue product record uses the same practical commercial structure as the existing Sushi 81 catalogue.

Required product fields are:

- **product code**;
- **product name**;
- **category**;
- **TTC selling price**;
- **VAT rate/category**.

In addition, every product has the following catalogue state/settings fields:

- **active/inactive** status;
- **eligible/not eligible for the normal Retrait discount**.

The active/inactive and discount-eligibility values are represented as ordinary boolean/toggle settings rather than text that the operator must type manually. Their exact default values for a newly created product may be chosen during detailed UI/implementation design, but both values must always be explicitly stored for the product.

Product options are not embedded as ad-hoc text inside these base fields. They are managed separately as structured option data under section 6.

No additional mandatory commercial product field is introduced at this stage beyond the fields above.

### Deactivation and permanent deletion — approved Phase 2 decision

The current catalogue supports both **deactivation** and **permanent deletion**, with deliberately different meanings:

- **deactivation** is used when a product is temporarily unavailable or may be sold again later;
- a deactivated product remains in the catalogue and can be reactivated;
- deactivated products do not appear in normal order selection;
- **permanent deletion** removes the product from the current catalogue entirely;
- permanent deletion is allowed even when the product has appeared in historical orders, because those orders retain independent sale-time snapshots;
- deleting a catalogue product must never delete, rewrite or damage any historical order data;
- deleting a product releases its operator-facing code immediately so that code may be reused by another current catalogue product;
- the application must require a simple explicit confirmation before permanent deletion to reduce accidental deletion.

Historical sales are therefore not a reason to block deletion. The operator chooses between deactivation and deletion according to whether the current catalogue entry is expected to be reused.

## 5. Categories — partially approved; shortcut/display model deferred to UI design

Every current catalogue product must have a **category**. Category itself is therefore part of the approved product model and is not optional.

The current Excel/VBA workbook also uses `RaccourciCat` values such as `[BR]`, `[ML]` and `[RP]`. Those shortcuts exist primarily because the current VBA interface needs compact labels and a specific filtering/order mechanism.

Phase 2 does **not** treat that legacy shortcut field as an automatically required target field.

Approved direction:

- the target application must preserve fast and practical category-based product selection;
- category names must remain editable catalogue data;
- changing a category name or product-category assignment affects only the live catalogue and never rewrites historical orders;
- the target UI may use a better filtering, grouping, ordering or navigation mechanism than the legacy VBA shortcut system;
- `RaccourciCat` should be retained only if later UI/interaction design demonstrates a real operational benefit;
- no permanent business rule should be invented solely to reproduce an old Excel/VBA UI constraint.

The following details are therefore intentionally deferred to the later UI/interaction design stage:

- whether a separate category shortcut field exists at all;
- how categories are visually ordered or grouped on the order-entry screen;
- whether the operator manually controls category display order;
- what compact labels, buttons, tabs or other navigation mechanism are used;
- category deletion/unused-category behavior if it depends on the chosen category-management UI.

This deferral is intentional and is not a missing business decision: the required business concept is the category itself; shortcut and display-order mechanics are presentation/interaction concerns unless a later design proves otherwise.

## 6. Product options / choices — partially approved Phase 2 decision

Not every catalogue product needs structured options. Product-option capability is therefore **enabled and configured per product**, rather than being mandatory for every product.

The operator must be able to maintain this behavior directly in the application at any time:

- a product with no options can remain a normal product with no option-selection step;
- the operator may later enable structured options for that product;
- the operator may add, edit, remove, activate or otherwise maintain the available option labels/choices without changing source code;
- the operator may configure an option set/group as **single-select** or **multi-select**;
- both single-select and multi-select behavior must be supported by the target design, even though Sushi 81's current options are single-select;
- product options are catalogue data and later catalogue-option edits do not rewrite the option selections already stored on historical orders.

The target structure should not prevent a product from having more than one logical option group if later operational needs require it. The exact UI for creating/managing multiple groups can be finalized during detailed interaction design, but the data/behavior design must not assume that all choices for a product forever belong to one single flat list.

### Order-entry prompting — approved Phase 2 decision

When the operator selects/adds a product during order entry:

- if that product has no enabled option group/choice requirement, it is added through the normal fast-ordering workflow without an unnecessary option dialog;
- if that product has enabled structured options, the application must automatically present an option-selection dialog/prompt immediately as part of adding that product;
- the operator should not have to remember to open a separate option editor manually after adding the product;
- the prompt must present the choices according to the configured single-select or multi-select behavior;
- the selected option labels and any price adjustments are attached to that specific order line and copied into the order snapshot.

The exact visual form of the prompt — modal dialog, popover, side panel or another interaction pattern — is a UI-design choice. The required behavior is that selection is surfaced automatically and clearly at product-add time.

The following option details still need to be frozen:

- whether an enabled option group is always required to have a selection or may be optional;
- for multi-select groups, whether configurable minimum/maximum selection counts are needed;
- display order of option groups and option labels;
- predefined option-price-adjustment catalogue maintenance, consistent with `business-rules.md`;
- whether individual options can be temporarily deactivated independently from the parent product.

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
- deletion from the current catalogue;
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
- whether import may permanently delete products;
- rollback/recovery behavior after a failed import;
- export encoding and column order.

Any import result that would leave two current catalogue products with the same code must be rejected or returned for correction before the catalogue update is committed.

No import should partially apply a logically invalid catalogue update without clearly reporting the result.

## 9. Decisions still to freeze

Before this document becomes baseline, Phase 2 must explicitly approve at least:

1. required/optional option-selection behavior and any multi-select selection limits;
2. predefined option-price-adjustment catalogue behavior consistent with `business-rules.md`;
3. whether individual options can be independently activated/deactivated;
4. in-application catalogue editing workflow;
5. final batch import/export format and columns;
6. update/conflict/deactivation/delete behavior during import;
7. validation and preview requirements.

The following product-field/identity/lifecycle/history/category/option principles are already approved and are no longer open questions:

- the required base product fields are code, name, category, TTC selling price and VAT rate/category;
- every product also stores active/inactive and Retrait-discount-eligibility settings;
- product options are managed separately from the base product fields and are enabled/configured only for products that need them;
- the operator may enable and maintain product options directly in the application;
- both single-select and multi-select option behavior are supported;
- when a product has enabled structured options, order entry automatically presents an option-selection prompt when that product is selected/added;
- option selections are attached to the specific order line and retained in the order snapshot;
- confirmed historical orders are independent snapshots and do not depend on the live catalogue;
- operator-facing product codes are editable and may be reused/reassigned over time;
- prior historical use does not permanently reserve a product code;
- within the current catalogue, one product code may belong to only one product at a time;
- later catalogue edits, including code changes, never alter existing confirmed orders;
- current catalogue products may be deactivated for later reactivation;
- current catalogue products may also be permanently deleted, even if previously sold, without affecting historical orders;
- permanent deletion releases the product code for immediate reuse;
- every product has a category;
- the legacy `RaccourciCat` field is not frozen as a required target field and will be retained only if later UI design demonstrates a real need;
- implementation may use a hidden immutable internal identifier for safe database handling without exposing that identifier as the business product code.

## 10. Approval rule

This file remains a Draft until the catalogue and option rules above are explicitly approved. Implementation must preserve the strict separation between historical order snapshots and the mutable live catalogue, must keep current catalogue product codes unique, must support both deactivation and confirmed permanent deletion, and must not treat the operator-facing product code as an immutable historical identity. UI-specific category shortcut/display mechanics must not be hard-coded before the later interaction design is approved.