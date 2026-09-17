# Acceptance criteria amendment — M10 Category short-code workbook semantics

**Status:** Approved  
**Approved:** 2026-09-17 by project owner  
**Applies to:** M10 — Catalogue `.xlsx` import/export  
**Amends/clarifies:** AC-CAT-008, AC-CAT-009, AC-CAT-010, AC-CAT-011  
**Decision source:** `docs/decisions/m10-category-short-code-workbook-semantics.md`

This amendment closes the operator-visible workbook gap created when Category `short_code` became approved V1 business data after the original three-sheet Catalogue workbook specification was frozen.

## AC-CAT-008 clarification — export workbook

In addition to the existing AC-CAT-008 requirements:

- the visible `Products` worksheet exports both Category name and Category short code as business data;
- no operator-facing `Categories` worksheet is added;
- `category_id` remains technical/non-operator identity;
- every exported Product row referencing a Category carries that Category's current short code, blank when none exists;
- the workbook remains sufficient to preserve Category short-code business meaning across a normal export/re-import round trip.

**Evidence:** workbook structure/round-trip tests + manual Excel inspection.

## AC-CAT-009 clarification — normal update import

In normal update mode:

- Category resolution remains by normalized Category name;
- for an existing Category, blank Category short code preserves the current value;
- a non-blank value equal to the current normalized short code is valid consistency data;
- a different non-blank value is a blocking Error;
- if the existing Category currently has no short code, a non-blank workbook value is also a blocking Error;
- import never clears, replaces or globally edits an existing Category short code;
- the operator changes an existing Category short code in the in-application Category manager and re-exports when needed;
- repeated Product rows resolving to the same Category must have one non-conflicting Category short-code meaning.

For a new Category created atomically by import:

- Category short code is optional;
- blank plus one repeated non-blank value is a single consistent proposal;
- different non-blank values for the same normalized new Category are a blocking Error;
- existing Category short-code length/normalization/uniqueness rules apply.

**Evidence:** import-planner/application/integration tests covering existing/new/repeated Category references and blocking conflicts.

## AC-CAT-010 clarification — add-only mode

Explicit add-only mode uses the same Category semantics:

- Product/OptionGroup/Option entities remain create-only;
- Category name may resolve to an existing current Category without turning the Product row into an update;
- such resolution does not authorize changing that existing Category's short code;
- a new Category may be created with an optional consistent short code;
- first-catalogue initialization therefore supports Category short codes without exposing `category_id`.

**Evidence:** empty-catalogue and non-empty-catalogue add-only tests.

## AC-CAT-011 clarification — validation/preview/atomicity

Category short-code conflicts are blocking Errors and therefore prevent the entire catalogue commit.

Preview/error feedback must identify the relevant `Products` worksheet row(s)/Category sufficiently for correction and, where an existing Category change was attempted, direct the operator to the normal in-application Category manager rather than silently changing shared Category data.

All accepted Category creation/short-code values commit inside the same one-transaction Catalogue import boundary as the Product/OptionGroup/Option changes.

**Evidence:** deterministic preview/error tests + transaction rollback/integration tests + owner Windows/Excel acceptance.

## Scope boundary

This amendment does not:

- add a fourth operator-facing worksheet;
- expose or make `category_id` editable;
- authorize global Category name/short-code editing through Product rows;
- add Category deletion through Excel;
- alter existing Product/OptionGroup/Option identity semantics;
- authorize M10 implementation by itself;
- authorize M11, M12 or M13.