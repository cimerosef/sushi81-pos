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
- categories;
- discount eligibility;
- structured product options/choices;
- option-price adjustments;
- in-application catalogue maintenance;
- catalogue import/export and validation;
- historical-order stability after catalogue changes.

It does not define the physical database schema, order lifecycle or final screen layout.

## 2. Authoritative Phase 1 baseline and Phase 2 refinement

The target catalogue supports at least:

- operator-facing product code;
- product name;
- category;
- TTC selling price;
- VAT rate/category;
- active/inactive status;
- Retrait-discount eligibility;
- structured product options where applicable.

The following requirements remain fixed:

1. product browsing/search works by code and name;
2. inactive products disappear from normal order selection but may remain for later reactivation;
3. catalogue changes never rewrite historical order-item data;
4. confirmed orders retain a sale-time snapshot sufficient to reproduce historical product code, name, price, VAT and selected option/adjustment information;
5. catalogue maintenance is possible inside the application without editing Excel;
6. batch import/export is supported through the approved Excel format defined below;
7. ordinary order entry may not overwrite the catalogue product's base unit price;
8. product options may carry predefined price adjustments and operator-entered custom adjustments under `business-rules.md`.

Historical orders and the live catalogue are intentionally separate. The catalogue describes current/future ordering; a confirmed order stores its own sale-time data and does not depend on the later catalogue state.

## 3. Product identity — approved Phase 2 principle

The operator-facing product `Code` is an **editable operational/catalogue code**, not the permanent identity of historical sales.

Approved principles:

- historical order lines are self-contained sale records;
- later catalogue edits do not alter existing confirmed orders;
- the operator may change a catalogue product's code;
- a code may later be reassigned to different catalogue content;
- historical use of a code does not permanently reserve it;
- letter-plus-number code sequences may therefore be reorganized without permanent historical gaps.

Implementation should use a separate opaque internal product identifier, such as an internal `product_id`, so catalogue records can be handled safely without making the operator-facing code immutable. This identifier is not part of normal order entry.

### Current-catalogue code uniqueness — approved Phase 2 decision

Within the current catalogue, a product code identifies **exactly one product at a time**.

Therefore:

- two current catalogue products may not have the same code;
- a code may be edited, released and later reused;
- historical orders containing an old use of the code do not block reuse;
- edits/imports that would leave duplicate current codes must be rejected.

## 4. Product maintenance — approved Phase 2 decisions

The application supports:

- add product;
- edit product code and commercial attributes;
- activate/deactivate product;
- permanently delete product;
- manage category assignment;
- manage discount eligibility;
- manage product options and price adjustments.

Editing or deleting current catalogue data must never rewrite historical orders.

### Base product fields — approved Phase 2 decision

Required fields:

- **product code**;
- **product name**;
- **category**;
- **TTC selling price**;
- **VAT rate/category**.

Each product also stores:

- **active/inactive**;
- **eligible/not eligible for the normal Retrait discount**.

These two values are boolean/toggle settings. Product options are structured separately and are not embedded as ad-hoc text in the base fields.

No additional mandatory commercial product field is introduced at this stage.

### Deactivation and permanent deletion — approved Phase 2 decision

- **Deactivation** is for a temporarily unavailable product that may return later.
- A deactivated product remains in the catalogue, may be reactivated, and is hidden from normal order selection.
- **Permanent deletion** removes the product from the current catalogue.
- Permanent deletion is allowed even if the product appeared in historical orders because those orders contain independent snapshots.
- Deletion must never delete or alter historical orders.
- Deletion immediately releases the operator-facing code for reuse.
- Permanent deletion requires a simple explicit confirmation.

## 5. Categories — business concept approved; shortcut/display model deferred

Every current catalogue product must have a **category**.

The legacy Excel/VBA field `RaccourciCat` is **not** automatically carried into the target model. It existed mainly to support the constraints of the old VBA filtering/display mechanism.

Approved direction:

- category names are editable catalogue data;
- changing a category or a product's category affects only the live catalogue;
- fast category-based product selection must be preserved;
- the target UI may use filtering, grouping, tabs, buttons, ordering or another better navigation mechanism;
- `RaccourciCat` is retained only if later UI/interaction design shows a real operational need.

Category visual order, shortcuts and any UI-dependent unused-category handling are deferred to later UI design.

## 6. Product options / choices — approved Phase 2 model

Not every product has options. Option capability is configured **per product**.

The operator may at any time:

- enable options for a product;
- add/edit/remove option groups and choices;
- configure a group as **single-select** or **multi-select**;
- activate/deactivate individual choices;
- change option display order.

The data design must allow one product to have more than one option group even though current Sushi 81 options are primarily single-select.

### Required/optional selection and multi-select limits

Each option group independently defines whether it is:

- **required**; or
- **optional**.

For single-select:

- required = exactly one choice;
- optional = zero or one choice.

For multi-select, the operator can configure:

- minimum number of selections;
- maximum number of selections.

A required multi-select group has a minimum of at least one. An optional group may use a minimum of zero.

### Individual option maintenance and predefined price adjustment

Each individual option supports at least:

- text label/name;
- fixed price adjustment to euro-cent precision;
- positive, negative or exactly €0.00 adjustment;
- independent active/inactive state.

An inactive option is not offered for new orders but remains unchanged in historical order snapshots.

Predefined adjustments follow `business-rules.md`:

- positive adjustments do not receive the normal Retrait discount and use 5.5% VAT;
- negative adjustments reduce the discountable product amount before discount and inherit the product VAT rate;
- €0.00 adjustments affect description only;
- approved precision and rounding rules apply.

Custom operator-entered adjustments remain governed by `business-rules.md`, including the required non-empty description.

### Display order

The operator controls and persists the display order of:

- option groups on a product;
- individual options within each group.

The order-entry prompt must use that saved order. Automatic alphabetical sorting must not override it. The UI may implement reordering through drag-and-drop, up/down controls or another simple mechanism.

### Order-entry prompting

When a product is selected during order entry:

- no enabled options => normal direct add-to-cart workflow;
- enabled structured options => automatically present an option-selection prompt;
- the operator does not have to remember to open a separate editor;
- configured single/multi-select rules and min/max validation are enforced;
- inactive choices are hidden;
- selected labels and price adjustments are attached to that specific order line and copied into the order snapshot.

The exact visual form of the prompt remains a UI-design choice.

## 7. In-application catalogue editing workflow — approved Phase 2 direction

Catalogue maintenance is separated from the normal order-entry screen so routine ordering is not mixed with accidental catalogue edits.

The target application provides a dedicated catalogue-management area with:

- searchable/filterable product list;
- selection of a product for editing;
- editing of code, name, category, TTC price, VAT, active state and Retrait-discount eligibility;
- access from the same product editor to its structured option configuration;
- explicit actions for **new product**, **save**, **cancel**, **deactivate/reactivate** and **delete**;
- changes become effective when explicitly saved rather than being persisted character-by-character while typing;
- deletion follows the approved confirmation rule.

The exact layout and controls are deferred to UI design. The approved workflow goal is fast maintenance without Excel for ordinary changes, while reducing accidental changes during live ordering.

**Excel import/export is only an additional bulk-maintenance convenience. It is not the sole or required way to maintain the catalogue.** Ordinary individual product, option and status changes remain fully available from the application's catalogue-management area.

## 8. Historical stability — approved Phase 2 principle

Historical order lines and the current catalogue are deliberately **decoupled after order confirmation**.

When an order is committed, its order-item snapshot contains enough sale-time information that later changes to any of the following do not change the historical order:

- product code;
- name;
- price;
- VAT;
- discount eligibility;
- category;
- option label;
- option adjustment;
- option/product active state;
- deletion from catalogue;
- later reuse of the same product code for different content.

Historical reporting reads values stored on the order/order line rather than reconstructing old sales from the current catalogue.

## 9. Batch import/export — approved Phase 2 model except final technical-column/report details

### Primary format — approved

V1 uses **Excel `.xlsx` as the official complete catalogue batch import/export format**.

CSV is not a core V1 requirement. It may be added later as an auxiliary interchange format if a concrete need appears, but the product must not complicate the catalogue model merely to make the complete hierarchical catalogue fit into one CSV file.

This decision reflects the actual operational need:

- Sushi 81 already works naturally with Excel;
- one workbook can represent products, option groups and individual options cleanly in separate worksheets;
- a workbook is easier for the operator to review and edit than several related CSV files;
- Excel can serve both as an export and as the standard bulk-maintenance/import template.

### Workbook structure — approved direction

The complete workbook uses separate logical sheets for at least:

1. **Products** — product base fields and catalogue state;
2. **OptionGroups** — groups attached to products and their selection rules/order;
3. **Options** — individual choices, adjustments, active state and order.

Exact user-facing sheet names may be localized later, but the exported/imported workbook must have a stable documented structure.

### Internal identifiers and create/update behavior — approved

Internal identifiers are technical keys used to tell the application which existing catalogue record is being edited. They are not operator-facing product codes and do not prevent codes from being changed or reused.

For a normal workbook exported by Sushi81 POS:

- an existing product/group/option is exported with its internal ID;
- a row with a valid existing internal ID means **update that existing record**;
- changing a product code while keeping the same internal ID is therefore an edit/re-numbering of the same current catalogue record;
- a newly added row with a blank internal ID means **create a new record**;
- the application assigns the new internal ID only when the import is successfully committed.

The operator normally does not need to read or manage these identifiers manually.

### Add-only import mode — approved

Sushi81 POS must also support importing a workbook that does **not** contain existing internal IDs in an explicit **add-only mode**.

This is required in particular for first-time catalogue initialization, when the application's catalogue may be empty and no internal IDs exist yet.

In add-only mode:

- rows without existing internal IDs are treated only as candidates for **new catalogue records**;
- the application must **not** try to match those rows to existing products by product code, name or other business fields in order to perform updates;
- no existing product, option group or option may be modified merely because a no-ID row happens to resemble it;
- current-catalogue uniqueness and all other validation rules still apply;
- if an add-only import would create a product code that already exists in the current catalogue, that conflict is a blocking Error rather than an implicit update;
- successful creation assigns new internal IDs to the imported records;
- the same mode may also be used later to add a batch of entirely new catalogue records, not only during first installation.

The workbook/template must provide enough import-time relationship information for newly created option groups and options to be linked to their newly created parent product/group during the same atomic import. The final technical reference-column names can be defined in the implementation template without exposing database IDs as business identifiers.

### Deletion and activation behavior — approved

- Removing a product, option group or option row from the workbook does **not** delete the corresponding live catalogue record.
- Permanent deletion remains an explicit action in the application's catalogue-management interface.
- Batch import may create, modify, activate and deactivate records through explicit fields in the workbook.
- A missing row is therefore interpreted as “not included in this import”, not as “delete this record”.

### Preview and confirmation — approved

Before any live catalogue change is committed, the application must validate the workbook and show a clear preview/summary of intended actions.

The preview should report at least useful counts such as:

- records to create;
- records to modify;
- records to activate/deactivate;
- validation errors;
- warnings.

Where practical, the operator must be able to inspect which rows/records are affected before giving final confirmation.

For a workbook without existing internal IDs, the preview must make it clear that the import is operating in **add-only mode** and that no existing catalogue records will be updated.

### Atomic import — approved

A logically invalid import must not be partially applied.

The catalogue update is atomic:

- if blocking validation errors exist, no catalogue change is committed;
- if validation passes and the operator confirms, all accepted changes are committed together;
- the application must not leave the catalogue in a half-updated state because only part of a workbook succeeded.

Blocking validation includes at least situations such as:

- duplicate current product codes;
- required product fields missing;
- invalid prices or VAT values;
- references to nonexistent or inconsistent parent records;
- invalid single/multi-select configuration;
- a multi-select minimum greater than its maximum;
- malformed or unusable technical identifiers when an update mode relies on them;
- add-only rows that conflict with an already existing current product code.

Blocking problems must be reported with enough detail to find and correct the relevant worksheet/row.

### Errors versus warnings — approved

Import feedback has at least two practical levels:

- **Error** — the workbook cannot be committed until the issue is corrected;
- **Warning** — the condition deserves operator attention but does not prevent import.

For example, a resulting duplicate current product code is an Error. A large but structurally valid set of price changes may be shown as a Warning rather than being blocked automatically.

## 10. Decisions still to freeze

Before this document becomes baseline, Phase 2 still needs to approve only the remaining small Excel-handling details:

1. how technical internal-ID/reference columns are visually protected, hidden or deemphasized in exported workbooks;
2. whether a successful import result/report needs to be retained after import or whether the preview/final result shown at import time is sufficient.

The following are already approved and no longer open questions:

- required base product fields and state flags;
- historical order/catalogue separation;
- editable/reusable product codes with current-catalogue uniqueness;
- product deactivation and confirmed permanent deletion;
- category required, with legacy `RaccourciCat` deferred to UI design;
- structured per-product options with single-select and multi-select support;
- required/optional option groups and multi-select min/max;
- individual option activation, positive/negative/zero price adjustments and operator-defined display order;
- automatic option prompting during order entry;
- dedicated in-application catalogue maintenance separated from normal ordering;
- Excel import/export is an optional bulk-maintenance convenience and not the only way to edit the catalogue;
- Excel `.xlsx` is the official complete V1 batch import/export format;
- CSV is not a required complete-catalogue format in V1;
- complete Excel export/import is organized into product, option-group and option sheets;
- existing internal IDs identify updates while blank IDs identify new records in normal exported workbooks;
- explicit add-only import supports workbooks without existing internal IDs, including first-time catalogue initialization;
- add-only mode never silently updates existing records by guessing from business fields;
- removing rows from Excel does not delete live catalogue records;
- activation/deactivation may be performed explicitly through Excel import;
- import is validated and previewed before commit;
- any blocking error prevents the entire import from being applied;
- accepted imports are atomic rather than partially committed;
- validation distinguishes blocking Errors from non-blocking Warnings.

## 11. Approval rule

This file remains a Draft until the remaining Excel technical-column and import-report handling details are explicitly approved. Implementation must preserve historical snapshot independence, mutable but unique current product codes, safe catalogue maintenance, structured option behavior, the approved Excel-first batch-maintenance model, and explicit add-only initialization/import behavior.