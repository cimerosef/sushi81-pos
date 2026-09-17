# M10 Category short-code workbook semantics

**Status:** Approved  
**Decision date:** 2026-09-17  
**Approved by:** project owner  
**Applies to:** M10 — Catalogue `.xlsx` import/export  
**Amends:** `docs/catalogue-management.md`, M10 acceptance/readiness/implementation records  
**Does not authorize:** M10 implementation, Issue #4 opening, Codex execution, merge, or M11+

## Context

The Phase 2/5 Catalogue workbook baseline predates the later Approved M04 introduction of Category `short_code` as independent V1 business data.

The current model therefore has:

- opaque technical `category_id`;
- editable business-unique Category `name`;
- optional editable operator-facing Category `short_code` used for compact Caisse navigation.

The M04 decision explicitly requires future Catalogue `.xlsx` support to preserve `short_code`, while the existing three-sheet workbook contract intentionally has no operator-facing `Categories` worksheet and represents category assignment through the Product row's Category name.

M10 preparation identified the missing operator-visible workbook semantics and stopped for owner decision rather than inventing them during implementation.

## Decision

### 1. Workbook representation

The `Products` worksheet includes a visible business column for **Category short code** alongside the visible Category name.

There is still **no operator-facing `Categories` worksheet** in V1.

`category_id` remains technical identity and is not an operator-maintained workbook field.

### 2. New Category created by import

When one or more imported Product rows reference a new valid Category name:

- M10 may create that Category atomically as part of the successful import;
- the workbook may provide an optional Category short code for that new Category;
- all Product rows referring to the same new normalized Category name must provide one consistent short-code meaning;
- a mix of blank and the same non-blank short code is acceptable and means that non-blank short code is the proposed value;
- conflicting different non-blank short codes for the same new Category are a blocking Error;
- existing Category short-code length/normalization/uniqueness rules apply;
- the new Category receives its opaque `category_id` only through the normal successful commit path.

This supports first-catalogue initialization and later add-only batches without exposing database IDs.

### 3. Existing Category

For a Product row that resolves by normalized Category name to an existing current Category:

- blank Category short-code cell means **preserve the existing short code**;
- a non-blank value equal to the existing short code under the existing Category normalization rules is valid consistency data;
- a different non-blank value is a **blocking Error**;
- import must never clear, replace or globally rename an existing Category short code;
- the operator must use the normal in-application Category manager to change an existing Category short code, then re-export if another workbook round-trip is needed.

This prevents a Product-row edit from silently becoming a global Category mutation.

### 4. Existing Category without a short code

If an existing Category currently has no short code:

- a blank workbook cell preserves that state;
- a non-blank workbook value is treated as an attempted global Category short-code change and is therefore a blocking Error;
- the operator must first set the short code in the in-application Category manager and then re-export.

### 5. Repeated Category references

Because many Product rows can reference the same Category, importer validation must group category references by normalized Category name before commit.

For each Category, the workbook must resolve to exactly one safe interpretation under sections 2–4. Ambiguous or contradictory repeated values are Errors; the importer must not choose the first row or guess.

### 6. Export behavior

Catalogue export writes the current Category name and current Category short code on every Product row that references that Category.

Therefore a normal Sushi81-exported workbook round-trips Category short-code business data without exposing `category_id`.

### 7. Add-only mode

The same Category rules apply in explicit add-only mode:

- Product/OptionGroup/Option rows remain create-only;
- Category name may resolve to an existing current Category because Category assignment is name-based under the frozen workbook model;
- resolving to an existing Category does not authorize changing its short code;
- a new Category may be created with an optional consistent short code.

### 8. Error/preview behavior

Any Category short-code conflict described above is a blocking import Error and prevents the entire atomic commit.

Preview should identify the relevant `Products` worksheet rows/Category and make clear when the operator must change the existing Category in the application instead of through Excel.

## Rationale

This is the smallest safe extension of the existing three-sheet workbook contract:

- preserves the later M04 business field;
- supports full export/re-import fidelity;
- supports first catalogue initialization;
- avoids a fourth operator-facing worksheet;
- avoids exposing technical Category IDs;
- avoids inventing a second global Category-management workflow inside Product rows;
- prevents contradictory repeated Product rows from silently changing shared Category data;
- keeps normal Category edits centralized in the already accepted in-application Category manager.

## Consequences

- M10 readiness no longer has a material owner-decision blocker.
- `catalogue-management.md` and M10 implementation/acceptance records must align with this decision.
- M10 implementation remains separately unauthorized until the owner explicitly approves **M10 implementation**.
- Issue #4 remains CLOSED until the normal implementation authorization + mailbox prerequisites are completed.
- M11, M12 and M13 remain unauthorized.