# Catalogue management

**Status:** Approved — Phase 2 baseline, amended 2026-09-17  
**Last updated:** 2026-09-17  
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

Each Category has an opaque internal `category_id`, an editable operator-facing name and an optional editable operator-facing `short_code` used for compact navigation.

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

The approved M04 operator-retrieval amendment defines the current shortcut semantics:

- `short_code` is optional and independent from the category name; it is never derived from the name and is not an exact-one-character field;
- the displayed code is trimmed and modestly length-bounded, while uniqueness is checked case-insensitively after Unicode normalization;
- code creation/editing is atomic; a name-only rename preserves an existing code, and an explicit blank code clears it;
- category maintenance shows the code with the full name, while Caisse navigation shows the code as the primary label and falls back to the full name when no code exists;
- coded categories use deterministic normalized-code order, followed by uncoded categories in deterministic ID order;
- the current M04 implementation persists the code in SQLite; M10 `.xlsx` preservation/change semantics are defined by section 9.2 and `docs/decisions/m10-category-short-code-workbook-semantics.md`.

Category navigation may use tabs, buttons, grouping, filtering or another compact mechanism. Exact visual controls remain implementation-level provided:

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

In the `Products` sheet, VAT is stored in percentage points: enter plain numeric `5.5`, `10` or `20` for 5.5%, 10% or 20%. A genuinely percentage-formatted numeric Excel cell is also accepted: Excel's stored `0.055` displayed as `5.5%` is normalized to the same `5.5` percentage-point rate on import. The number format, not the value's size, determines this conversion; an unformatted `0.055` remains `0.055` percentage points. Export writes the canonical percentage-point number with a display format that preserves its precision. Formula cells remain unsupported, and the normal 0–100 percentage-point validation still applies after normalization.

### 9.2 Category representation in the workbook — V1 consistency rule

V1 does **not** require a separate `Categories` worksheet.

The visible `Products` sheet carries both:

- the Product's operator-facing Category name;
- a visible business `Category short code` field preserving the current Category `short_code` without exposing `category_id`.

During import, Category name remains the Category-resolution key for the workbook:

- a category name matching an existing normalized current category assigns that category;
- a new valid unique category name referenced by one or more imported Products may be created atomically as part of the successful import;
- changing a Product row's category name reassigns that Product to the resolved/new category; it does **not** mean “globally rename the previous category”;
- global current-category name or short-code change remains an in-application catalogue action;
- resulting duplicate current category names remain blocking Errors.

For a **new Category** created by import:

- `Category short code` is optional;
- all Product rows resolving to the same new normalized Category name must have one consistent short-code meaning;
- blank plus one repeated non-blank value is acceptable and means that non-blank value is the proposed short code;
- conflicting different non-blank short codes are a blocking Error;
- the normal Category short-code normalization, length and uniqueness rules apply.

For an **existing Category**:

- blank `Category short code` means preserve the current value;
- a non-blank value equal to the current short code under normal Category normalization is valid consistency data;
- a different non-blank value is a blocking Error;
- if the current Category has no short code, any non-blank workbook value is likewise a blocking Error;
- import never clears, replaces or globally changes an existing Category short code;
- to change an existing Category short code, the operator uses the normal in-application Category manager and then re-exports if needed.

Catalogue export writes the current Category name and current Category short code on every Product row referencing that Category. Import validates repeated Category references as one shared Category meaning and never chooses the first conflicting row or guesses.

These semantics also apply in explicit add-only mode. Resolving a Product row to an existing Category by name does not authorize changing that Category's short code; a new Category may be created with its optional consistent short code.

This keeps the workbook understandable, preserves full Category business data for round-trip/first initialization, avoids exposing `category_id`, and avoids introducing a fourth required worksheet or a second global Category-edit workflow.

The controlling amendment is `docs/decisions/m10-category-short-code-workbook-semantics.md`.

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
- invalid/duplicate/conflicting Category short codes under section 9.2;
- attempted workbook change of an existing Category short code;
- contradictory repeated Product-row short-code meaning for one Category;
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
- Category `short_code` remains optional independent business data and is preserved by `.xlsx` round-trip through the visible Product-row Category short-code field;
- existing Category short codes are preserve/consistency data during import, not workbook-driven global edit commands;
- new Categories created by import may receive one optional consistent short code under normal validation;
- historical orders are independent snapshots;
- product deactivation is distinct from permanent deletion;
- current Product deletion never deletes historical order data;
- code/name search, category filter and status filter compose as the catalogue maintenance selection boundary;
- the complete current filtered result can be bulk activated/deactivated only after explicit impact confirmation;
- filtered bulk activation/deactivation skips already-target-state products, performs no write for zero effective changes and commits all required state changes atomically;
- filtered bulk state changes alter only Product active/inactive state and never provide bulk permanent deletion;
- category-based navigation remains practical with the optional independent operator-facing short code and full-name fallback;
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

This document is the **Approved — Phase 2 catalogue baseline**, incorporating the approved Phase 3 category-name-uniqueness decision, Phase 5 consistency clarification of category/workbook semantics, the approved 2026-08-30 filtered bulk activation/deactivation amendment, the approved M04 Category-short-code business-data amendment, and the approved 2026-09-17 M10 Category-short-code workbook semantics.

There are no remaining unresolved V1 catalogue business/data-flow questions for the behavior frozen here.

Exact screen layout, category navigation styling and technical helper-column/relationship representation may be selected during implementation only where they preserve every frozen semantic above, `acceptance-criteria.md`, `acceptance-criteria-amendment-filtered-catalogue-bulk-activation.md`, and `docs/decisions/m10-category-short-code-workbook-semantics.md`.
