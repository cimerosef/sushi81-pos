# Hiboutik paste-import total calculation

**Status:** Approved — Phase 4 decision  
**Date:** 2026-08-27  
**Applies to:** Hiboutik paste-order import pricing

## Decision

A Hiboutik paste-created order does **not** use the total copied from Hiboutik as its authoritative Sushi81 POS total.

Instead, after product-code matching and completion of all required option confirmations, Sushi81 POS calculates the order total using the same ordinary pricing engine used for a manually created order.

The calculation uses:

- current Sushi81 catalogue product prices;
- final reviewed quantities;
- operator-confirmed product options and option adjustments;
- ordinary discount eligibility and Retrait discount rules;
- ordinary Livraison minimum/fee rules;
- the same rounding and VAT behavior as every other POS order.

Any total text present in the pasted Hiboutik source is therefore informational source content only and must not automatically override the POS-calculated total.

After normal calculation, the operator retains the already-approved ability to manually edit the ordinary authoritative order-total field. Such an edit follows the same manual-total rules as every other Sushi81 POS order.

## Rationale

The Hiboutik and Sushi81 POS product catalogues and product codes are maintained as strictly aligned. The only important missing information in the pasted Hiboutik order text is product-option selection, which is completed manually before confirmation.

Recalculating from the final POS cart therefore keeps product lines, selected options and the authoritative total internally consistent and avoids introducing a second pricing authority or a Hiboutik-specific discrepancy workflow.

## Consequences

- no immutable Hiboutik-original-total field is required;
- no Hiboutik-vs-POS discrepancy alert is required;
- no special total reconciliation screen is required;
- ordinary manual total override remains available after the normal calculation;
- parser tests must verify that a pasted source total never silently replaces the POS-calculated total.
