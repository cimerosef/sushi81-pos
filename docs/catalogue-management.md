# Catalogue management

**Status:** Draft — Phase 2 first batch  
**Last updated:** 2026-08-26  
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

## 2. Authoritative Phase 1 baseline

The target catalogue must support at least the product information already required by the approved Phase 1 documents:

- permanent product code/identity;
- product name;
- category;
- TTC selling price;
- VAT rate/category;
- active/inactive status;
- discount eligibility;
- category shortcut/display support where retained;
- one or more product options/choices where applicable.

The following requirements are already fixed:

1. product browsing/search must work by code and name;
2. inactive products must remain available for history but disappear from normal order selection;
3. catalogue changes must not rewrite historical order-item data;
4. confirmed orders must retain a sale-time snapshot sufficient to reproduce historical name, base price, VAT and selected option/adjustment information;
5. catalogue maintenance must be possible inside the application without editing Excel;
6. batch import/export must be supported through an approved tabular format such as Excel or CSV;
7. ordinary order entry may not overwrite the catalogue product's base unit price;
8. a product option can have a predefined price adjustment and the operator may need a custom option adjustment for exceptional real-world cases.

## 3. Product identity

The current system uses `Code` as the permanent unique product identifier and does not intend codes to be reused for a different product.

The Phase 2 target should preserve that historical-safety principle unless explicitly changed:

- a code identifies one logical product across time;
- deactivation is preferred over deleting a product that has historical sales;
- a code previously used for one product should not later identify an unrelated product.

Exact rules for code editing and code reuse remain to be approved.

## 4. Product maintenance

The application must support practical maintenance of the catalogue without Excel.

Target actions include:

- add product;
- edit approved product/commercial attributes;
- activate/deactivate product;
- manage category assignment;
- manage discount eligibility;
- manage product options and their price adjustments.

Phase 2 must distinguish attributes that are safe to edit from identity/history-sensitive attributes that should be immutable or tightly controlled.

## 5. Categories

The current workbook uses both `Categorie` and `RaccourciCat` to support category filtering and compact display labels such as `[BR]`, `[ML]` and `[RP]`.

The target application must preserve fast category-based product selection. Phase 2 must decide:

- whether category shortcut remains a separate editable field;
- category display/order rules;
- how category rename affects existing products;
- whether inactive/unused categories are retained or removed.

## 6. Product options / choices

The target catalogue must replace the current practice of typing flavour/menu choices into the order-level comment when the choice belongs to a specific product.

A product may need one or more structured option groups, for example a flavour or menu variant. The selected option must remain attached to the corresponding historical order item.

Phase 2 must define:

- whether option groups are single-select, multi-select or both;
- whether an option selection is required or optional;
- minimum/maximum selections where relevant;
- option labels and display order;
- predefined price adjustments;
- custom option-adjustment behavior;
- whether option availability can be activated/deactivated independently from the parent product.

## 7. Historical stability

Catalogue maintenance must never silently rewrite historical orders.

When an order is committed, the order-item snapshot must contain enough sale-time information that later changes to:

- product name;
- current price;
- VAT;
- discount eligibility;
- option name;
- option price adjustment;
- active/inactive status

do not alter the historical order's financial or printed meaning.

## 8. Batch import/export

The application must provide a practical way to export and re-import catalogue data for bulk maintenance.

The exact target format remains to be approved. Phase 2 must define:

- Excel, CSV or both;
- mandatory and optional columns;
- stable identifiers for products, categories and options;
- how new products are distinguished from updates;
- duplicate-code handling;
- invalid VAT/price/category handling;
- preview/validation before applying changes;
- whether import may deactivate products;
- whether import may delete anything;
- rollback/recovery behavior after a failed import;
- export encoding and column order.

No import should partially apply a logically invalid catalogue update without clearly reporting the result.

## 9. Decisions still to freeze

Before this document becomes baseline, Phase 2 must explicitly approve at least:

1. final product fields and which are mandatory;
2. product-code immutability/reuse rules;
3. category and category-shortcut model;
4. final option-group model;
5. required/optional option-selection behavior;
6. predefined and custom option-price adjustment rules;
7. how discount eligibility applies to option adjustments;
8. in-application catalogue editing workflow;
9. final batch import/export format and columns;
10. update/conflict/deactivation/delete behavior during import;
11. validation and preview requirements.

## 10. Approval rule

This file remains a Draft until the catalogue and option rules above are explicitly approved. Implementation must preserve historical order stability and must not infer destructive catalogue behavior from generic CRUD conventions.