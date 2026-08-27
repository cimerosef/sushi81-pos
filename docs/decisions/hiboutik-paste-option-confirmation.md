# Hiboutik paste-import product and option handling

**Status:** Approved — Phase 4 decision  
**Date:** 2026-08-27  
**Applies to:** Hiboutik paste-order import and normal order option workflow

## Context

Sushi81 POS and Hiboutik use strictly aligned product catalogues and the same operator-facing product codes. A pasted Hiboutik order can therefore identify its products reliably by product code.

However, the Hiboutik order text used for the fallback paste workflow does not contain the product-option selections required by some Sushi81 catalogue products.

## Decision

The paste importer uses the following rule:

1. Hiboutik product lines are matched to Sushi81 POS products by **exact product code**.
2. No fuzzy name-based product matching is required in the normal workflow.
3. If a pasted line does not contain a usable current product code or the code cannot be found, the importer must not silently guess another product; the operator must correct the order manually before confirmation.
4. For a matched product that has **no enabled option groups**, the product/quantity may be added to the normal cart directly.
5. For a matched product that has **one or more enabled option groups**, the importer must require the operator to confirm the option configuration before the order can be confirmed.
6. The option confirmation uses the **same normal option-selection UI, rules and validations** as manual POS order entry. No Hiboutik-specific option editor is created.
7. This manual confirmation is required for **all enabled option groups**, including groups that are normally optional, because an empty option in the pasted text cannot distinguish between “the customer selected nothing” and “the Hiboutik copied text omitted the selection.”
8. Where an option group is optional, the normal option UI must allow the operator to explicitly confirm no selection when that is the correct real-world result.
9. Required/single/multi-select rules, configured min/max selections, option availability, price adjustments and VAT treatment remain exactly those already approved for ordinary POS orders.
10. Once all required option confirmations are completed, the imported lines are ordinary editable cart lines and the order proceeds through the normal confirmation lifecycle.

## Rationale

The catalogue identity is deterministic, so product matching should remain simple. Option selections are genuinely missing from the source, so the application must not invent them.

Reusing the ordinary option workflow maximizes reliability while avoiding a second set of Hiboutik-specific controls or rules.
