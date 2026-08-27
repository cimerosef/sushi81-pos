# Customer ticket scope — no B2B invoicing

**Status:** Approved — Phase 4 decision  
**Date:** 2026-08-27  
**Applies to:** `docs/printing.md`, customer ticket/receipt scope

## Decision

Sushi81 POS V1 customer printing is limited to the ordinary customer-facing ticket / restaurant note used for daily Sushi 81 operations.

V1 does **not** provide a formal B2B invoice (`facture`) subsystem.

Accordingly V1 does not need invoice-specific workflow or data such as:

- customer company/legal-entity master data solely for invoicing;
- customer SIREN/SIRET/VAT-number capture solely for invoicing;
- a separate legal invoice-number sequence;
- invoice issue/correction/credit-note lifecycle;
- B2B invoice templates or accounting-document management.

The ordinary customer ticket must still contain the business/customer-facing information required by the approved Sushi 81 receipt/note format and applicable requirements.

If a formal business invoice is exceptionally required, it remains outside Sushi81 POS V1 and is handled through the existing separate process.

## Rationale

Formal B2B invoicing is not part of the POS's operational purpose and would introduce a separate accounting-document lifecycle with little daily value. Keeping it outside V1 preserves the project's priority order: reliability > simplicity > maintainability > operational clarity > novelty.
