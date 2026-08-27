# Category-name uniqueness

**Status:** Approved — Phase 3 decision  
**Date:** 2026-08-27  
**Applies to:** Sushi81 POS current catalogue categories

## Decision

Every current catalogue category must have a unique operator-facing name.

Therefore:

- two current `Category` records may not have the same visible category name, even if their internal `category_id` values are different;
- a category name is editable, but an edit that would create a duplicate current category name must be rejected;
- catalogue import must reject any result that would leave duplicate current category names;
- category identity remains based on the opaque internal `category_id`; name uniqueness is an additional business/operational constraint and is not used as the technical primary key;
- historical order snapshots are unaffected by later category renaming because historical lines retain their saved category-name snapshot.

The implementation must enforce business-visible uniqueness rather than allowing multiple categories that appear identical to the operator. Exact technical normalization rules may be chosen during implementation, but they must not permit visually equivalent duplicate category names merely because of technical differences such as surrounding whitespace or letter case.

## Consequences

This resolves the Phase 3 `Category duplicate-name policy` question in `data-model.md`.

It must also be reflected in catalogue validation:

- in-application category creation/editing must block duplicates;
- Excel catalogue import must treat a resulting duplicate category name as a blocking Error;
- no duplicate category name may be committed in an otherwise valid atomic import.
