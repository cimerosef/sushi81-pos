# Catalogue management

**Status:** Approved — Phase 2 baseline, amended 2026-08-30  
**Last updated:** 2026-08-30  
**Product:** Sushi81 POS  
**Purpose:** Freeze the V1 current catalogue, category, product-option and Excel batch-maintenance behavior before implementation.

## 1. Scope

This document defines how Sushi81 POS manages the current operational catalogue.

It covers:

- product identity and lifecycle;
- current product/category attributes;
- activation/deactivation and deletion;
- filtered bulk activation/deactivation;
- discount eligibility;
- structured product options/choices;
- option price adjustments and display order;
- dedicated in-application catalogue maintenance;
- complete `.xlsx` catalogue import/export;
- safe create/update/add-only semantics;
- validation, preview and atomic commit;
- historical-order independence from later catalogue changes.

Physical SQL structure belongs to `data-model.md` / `architecture.md`. Pricing/VAT of adjustments belongs to `business-rules.md`. Exact screen layout is an implementation-level UI choice so long as the workflow and visibility requirements below are preserved.

## 2. Catalogue principles

The target catalogue supports at least:

- opaque internal product identity;
- operator-facing product code;
- product name;
- category;
- TTC selling price;
- VAT rate/category;
- active/inactive state;
- Retrait-discount eligibility;
- structured product options where enabled.

Authoritative principles:

1. Product browsing/search works by code and name.
2. Inactive products disappear from normal new-order selection but remain available for later reactivation.
3. Current catalogue changes never rewrite historical order snapshots.
4. Confirmed order lines preserve sale-time product/category/code/name/price/VAT/options/adjustments needed for historical interpretation.
5. Ordinary catalogue maintenance is possible inside the application without Excel.
6. Excel `.xlsx` import/export is a bulk-maintenance convenience, not the only maintenance method.
7. Ordinary order entry may not overwrite a catalogue product's base unit price.
8. Product options may carry preset and operator-entered adjustments under `business-rules.md`.
9. The in-application catalogue may apply an explicitly confirmed activation/deactivation change atomically to the complete result of the current composed search/category/status filters.

## 3. Product identity and current-code uniqueness

The visible product `Code` is an editable operational/catalogue value, not the permanent identity of historical sales.

Each current Product has a separate opaque internal `product_id` managed by the application.

Approved behavior:

- current product codes are unique;
- code may be edited;
- a released/deleted code may later be reused;
- historical use does not permanently reserve the code;
- historical order lines remain unchanged after a current product is renamed, recoded or deleted;
- edits/imports that would leave duplicate current codes are rejected.

A product code therefore identifies exactly one **current** product at a time, while historical snapshots remain self-contained.

## 4. Product maintenance

The dedicated catalogue-management area supports:

- create product;
- edit product code and approved commercial attributes;
- assign/change category;
- activate/deactivate product;
- bulk activate/deactivate the complete current filtered result;
- permanently delete product;
- manage Retrait-discount eligibility;
- manage product option groups/choices.

### 4.1 Required base fields

A current product requires:

- product code;
- product name;
- category;
- TTC selling price;
- VAT rate/category.

It also persists:

- active/inactive state;
- discount-eligible/not-eligible state;
- product-level options enabled/disabled state.

No additional mandatory commercial product field is part of the V1 baseline.

### 4.2 Deactivation versus permanent deletion

**Deactivation** is used for a temporarily unavailable product:

- record remains current catalogue data;
- it may be reactivated;
- it is hidden from normal new-order selection.

**Permanent deletion** removes the current product record:

- explicit confirmation is required;
- deletion is allowed even when the product exists in historical orders because those orders retain snapshots;
- deletion never deletes/rewrites historical orders;
- deletion releases the visible code for reuse.

Deleting a row from an Excel import workbook is **not** the permanent-deletion mechanism.

Bulk maintenance under section 7 changes only active/inactive state and never acts as a bulk permanent-delete workflow.

## 5. Categories

Every current Product belongs to one Category.

Each Category has an opaque internal `category_id` and an editable operator-facing name.

### 5.1 Category-name uniqueness

Every current category name is business-visible unique.

Therefore:

- two current categories may not have names that appear equivalent to the operator;
- category creation/rename creating a duplicate is rejected;
- Excel import creating a duplicate current category name is a blocking Error;
- implementation normalization must not permit duplicates solely through surrounding whitespace or letter case;
- category name is not the technical primary key despite its business uniqueness.

Historical order lines retain their saved sale-time category-name snapshot, so later category rename never rewrites historical orders.

This rule is also recorded in `docs/decisions/category-name-uniqueness.md`.

### 5.2 Category maintenance and UI boundary

The application must allow the current category names needed by product maintenance to be created/renamed and products to be reassigned between categories.

V1 does not require the former VBA `RaccourciCat` field or a specific category-shortcut persistence model.

Category navigation may use tabs, buttons, grouping, filtering or another compact mechanism. Exact visual order/shortcut controls are implementation-level UI choices provided:

- category-based product selection remains fast;
- current category-name uniqueness is preserved;
- no historical order depends on current category state.

A separate operator-facing category deletion workflow is not required for V1. Unused categories may remain as harmless current catalogue data; future cleanup functionality may be added later if a concrete need appears.

## 6. Structured product options

Not every product has options. Option capability is configured per product.

When enabled, one product may have one or more OptionGroups.

### 6.1 Option-group rules

Each group defines:

- name/prompt label;
- `SINGLE` or `MULTI` selection mode;
- required or optional semantics;
- multi-select minimum/maximum where applicable;
- persisted display order within the product.

Validation:

- required single-select => exactly one choice;
- optional single-select => zero or one;
- required multi-select => minimum at least one;
- optional multi-select may use minimum zero;
- minimum cannot exceed maximum.

### 6.2 Individual options

Each Option supports at least:

- label/name;
- fixed positive, negative or €0.00 price adjustment to business cent precision;
- active/inactive state;
- persisted display order inside its group.

Inactive choices are hidden for new orders but remain preserved in historical snapshots.

Price/discount/VAT behavior follows `business-rules.md`, including:

- positive adjustment not receiving the normal Retrait discount and using 5.5% VAT;
- negative adjustment reducing the discountable product amount and inheriting product VAT;
- €0.00 choice affecting description only;
- custom operator-entered adjustments requiring a non-empty description.

### 6.3 Display order

The operator controls/persists:

- OptionGroup display order on a product;
- Option display order within a group.

Order entry uses that saved order. Automatic alphabetical sorting must not override it.

The implementation may use drag-and-drop, up/down actions or another simple control.

### 6.4 Order-entry prompting

When a product is selected:

- no enabled option groups => ordinary direct add-to-cart workflow;
- enabled groups => the ordinary option-selection UI appears automatically;
- group required/optional/single/multi/min/max rules are enforced;
- inactive choices are hidden;
- chosen labels/adjustments are attached to that specific order line and later snapshotted.

The operator should not have to remember to open a separate option editor after adding an option-enabled product.

## 7. Dedicated in-application catalogue workflow

Catalogue maintenance is separated from normal live order entry to reduce accidental business-data edits.

The catalogue area provides a searchable/filterable current product list and ordinary actions equivalent to:

- new product;
- edit selected product;
- save;
- cancel unsaved edit;
- activate/deactivate one selected product;
- bulk activate/deactivate the complete current filtered result;
- permanent delete with confirmation;
- manage category assignment;
- manage structured options.

Changes become authoritative when explicitly saved/confirmed rather than being persisted character-by-character while typing.

Exact layout/control styling is delegated to implementation as long as this workflow remains practical and does not mix accidental catalogue editing into routine order entry.

### 7.1 Filtered bulk activation/deactivation

V1 provides explicit bulk **Activate** and **Deactivate** actions for operational catalogue maintenance.

The selection boundary is the same composed filter state used by the catalogue list:

- code/name keyword search;
- category filter;
- status filter (`All`, `Active`, `Inactive`).

All filters compose together. A bulk action targets the **complete current filtered Product result**, not merely rows visible in the current viewport.

When the operator starts a bulk action:

1. the application captures an immutable snapshot of the matching current Product IDs and the target active state;
2. the application calculates the total matched count and the number that would actually change;
3. products already in the target state are skipped;
4. the operator receives a clear confirmation showing at least the target action, matched count and effective-change count;
5. explicit confirmation is required before any write;
6. zero effective changes produce no business write and must be represented by disabled action or clear no-change feedback.

After confirmation, the required state changes are one atomic business mutation. All target products change state or none do. If a captured Product no longer exists or another conflict prevents safe application, the whole mutation fails without partial success and the catalogue is refreshed with clear feedback.

The operation changes **only** Product active/inactive state. It never alters Product identity/code/name/category/price/VAT/discount eligibility/options-enabled state, OptionGroups, Options or their display order, and it never rewrites historical order snapshots.

After success the catalogue automatically refreshes while preserving the current search/category/status filter values. A status filter may therefore make successfully changed rows disappear naturally from the visible result.

Bulk permanent deletion is explicitly outside this workflow and is not introduced by this amendment.

All new labels, confirmation text and result/error messages are localized in French and Simplified Chinese; operator-entered catalogue data remains unchanged when language switches.

The approved decision record is `docs/decisions/filtered-catalogue-bulk-activation.md`.

## 8. Historical catalogue independence

Confirmed historical order lines are snapshots, not views over the current catalogue.

Later changes to any of the following must not change an already committed historical order merely because the catalogue changed:

- product code;
- product name;
- category/name;
- base price;
- VAT;
- discount eligibility;
- option group/choice labels;
- option adjustment;
- option/product active state;
- current-product deletion;
- reuse of an old visible code.

When the operator intentionally opens/modifies/saves an existing order, that order's latest saved snapshot may change under the normal order-modification rules. This is different from passive catalogue change rewriting history.

## 9. Complete Excel `.xlsx` import/export

### 9.1 Official V1 format

Excel `.xlsx` is the official complete V1 catalogue batch import/export format.

CSV is not required as a complete hierarchical catalogue format in V1.

A complete workbook contains at least these logical sheets:

1. `Products`;
2. `OptionGroups`;
3. `Options`.

The workbook is both:

- a complete export/review artifact;
- a standard bulk-maintenance/re-import template.

Exact localized visible header wording and purely technical helper-column names may be chosen during implementation, but their business meaning must preserve this specification.

### 9.2 Category representation in the workbook — V1 consistency rule

V1 does **not** require a separate `Categories` worksheet.

The visible `Products` sheet carries the product's operator-facing category name.

During import:

- a category name matching an existing normalized current category assigns that category;
- a new valid unique category name referenced by one or more imported Products may be created atomically as part of the successful import;
- changing a Product row's category name reassigns that Product to the resolved/new category; it does **not** mean “globally rename the previous category”;
- global current-category rename remains an in-application catalogue action;
- resulting duplicate current category names remain blocking Errors.

This keeps the workbook understandable without exposing `category_id` as a business field or introducing a fourth required worksheet solely for category identity.

### 9.3 Technical IDs are transparent/non-editable

In a workbook exported for update/re-import, existing Products, OptionGroups and Options may contain technical internal IDs/reference columns needed for safe matching.

Those technical values:

- are not operator-facing business identifiers;
- must be hidden/protected/locked or otherwise non-editable in the normal workbook workflow;
- are not documented as values the operator should maintain manually;
- are validated defensively if corrupted outside the intended workflow.

The application must reject unsafe matching rather than guessing which current record was intended.

### 9.4 Normal create/update semantics

For a normal workbook exported by Sushi81 POS:

- valid existing internal ID => update that existing record;
- product code change with the same product ID => recode/edit the same current product;
- blank internal ID => create a new record;
- newly created internal IDs are allocated only on successful commit.

For newly added OptionGroups/Options, the workbook/importer must maintain understandable parent relationships without requiring the operator to manually manage database IDs.

The exact technical helper mechanism for new-record relationships is an implementation detail.

### 9.5 Explicit add-only mode

V1 also supports importing a workbook without existing internal IDs in explicit **add-only mode**.

This supports:

- first catalogue initialization;
- later batches containing only entirely new catalogue records.

Rules:

- no-ID rows are create candidates only;
- importer must not match them to existing records by code/name to perform implicit updates;
- existing records are not modified merely because a no-ID row resembles them;
- duplicate current product code is a blocking Error, not an implicit update;
- category resolution follows section 9.2;
- successful creation assigns new internal IDs;
- new OptionGroups/Options are connected to their new parent Product/Group through an implementation-managed relationship mechanism that does not require operator database-ID knowledge.

### 9.6 Row absence and state changes

Removing a Product/OptionGroup/Option row from a workbook does **not** delete the corresponding current record.

A missing row means “not included in this import”.

Permanent Product deletion remains an explicit in-application action.

Excel import may create/update and explicitly activate/deactivate supported current records through visible fields.

## 10. Import validation, preview and atomicity

### 10.1 Validate before commit

Before changing the current catalogue, the application validates the complete workbook and shows a clear preview/summary.

The preview includes useful counts such as:

- records to create;
- records to modify;
- records to activate/deactivate;
- blocking Errors;
- non-blocking Warnings.

Where practical, affected rows/records are inspectable before final confirmation.

For a no-ID workbook, the preview clearly indicates **add-only mode** and that existing records will not be updated.

### 10.2 Errors versus warnings

At minimum:

- **Error** => import cannot commit until corrected;
- **Warning** => deserves attention but does not automatically block commit.

Blocking validation includes at least:

- duplicate current product codes;
- duplicate current category names;
- missing required Product fields;
- invalid prices/VAT values;
- invalid/inconsistent parent relationships;
- invalid single/multi selection configuration;
- multi-select minimum greater than maximum;
- malformed/unusable technical IDs in update mode;
- add-only product-code conflicts with the existing current catalogue.

Blocking problems identify the relevant worksheet/row sufficiently for correction.

A large but structurally valid change may be a Warning rather than being blocked merely because it is large.

### 10.3 Atomic commit

If any blocking Error exists, **no** catalogue change is committed.

If validation passes and the operator confirms, the accepted catalogue changes are committed together atomically.

The application must not leave a half-updated catalogue because only part of a workbook succeeded.

### 10.4 Import-result retention

A successful import does not require a permanent operator-facing import-history/report subsystem in V1.

The operator receives:

- preview before commit;
- clear success/current-operation result after commit.

Afterwards, the current database is authoritative for current catalogue state.

Normal technical logs may still exist for diagnostics.

## 11. Frozen V1 catalogue invariants

Implementation must preserve all of the following:

- opaque current Product/Category/OptionGroup/Option identities remain distinct from operator-facing labels;
- current product codes are unique but editable/reusable;
- current category names are business-visible unique;
- historical orders are independent snapshots;
- product deactivation is distinct from permanent deletion;
- current Product deletion never deletes historical order data;
- code/name search, category filter and status filter compose as the catalogue maintenance selection boundary;
- the complete current filtered result can be bulk activated/deactivated only after explicit impact confirmation;
- filtered bulk activation/deactivation skips already-target-state products, performs no write for zero effective changes and commits all required state changes atomically;
- filtered bulk state changes alter only Product active/inactive state and never provide bulk permanent deletion;
- category-based navigation remains practical without requiring the legacy VBA shortcut field;
- structured per-product options support required/optional single/multi behavior and min/max validation;
- individual options support active state, positive/negative/zero adjustments and saved display order;
- option prompting occurs automatically for option-enabled products;
- ordinary catalogue edits are available inside the application;
- `.xlsx` is the official complete batch-maintenance format;
- existing technical IDs mean safe updates; blank IDs mean creates;
- technical IDs are hidden/protected/non-business fields;
- explicit add-only mode never silently updates existing records by guessing business fields;
- category names in Products are resolved/created under section 9.2 rather than exposing category IDs;
- workbook row removal does not imply deletion;
- import is previewed, validated and atomic;
- no permanent operator-facing import history is required.

## 12. Approval

This document is the **Approved — Phase 2 catalogue baseline**, incorporating the approved Phase 3 category-name-uniqueness decision, Phase 5 consistency clarification of category/workbook semantics, and the approved 2026-08-30 V1 amendment for filtered bulk activation/deactivation.

There are no remaining unresolved V1 catalogue business/data-flow questions for the behavior frozen here.

Exact screen layout, category navigation styling and technical helper-column/relationship representation may be selected during implementation only where they preserve every frozen semantic above, `acceptance-criteria.md`, and `acceptance-criteria-amendment-filtered-catalogue-bulk-activation.md`.