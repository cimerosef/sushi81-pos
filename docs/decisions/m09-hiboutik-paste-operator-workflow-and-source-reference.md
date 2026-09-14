# M09 Hiboutik paste operator workflow and source reference

**Status:** Approved — V1 specification amendment  
**Date:** 2026-09-14  
**Applies to:** Hiboutik paste-order fallback, order-entry UI, order data model, operational reconciliation and acceptance criteria  
**Supersedes where inconsistent:** `hiboutik-paste-simplification.md`, `hiboutik-paste-option-confirmation.md`, `hiboutik-paste-total-calculation.md`, `paste-order-import.md`, `data-model.md` section 6 source-visibility wording, and AC-HIB-004/006/007/008/009 in `acceptance-criteria.md`.

## Context

Real Hiboutik automatic-order emails show a product-detail block in a stable practical form such as:

```text
1 x AA1 Produit Exemple Alpha (5.50)
Total : 5.5
2 x BB2 Produit Exemple Beta (5.00)
Total : 10
1 x Livraison (0)
Total : 0
TOTAL 15.5
```

The Sushi81 operator's fastest fallback workflow is to copy the whole product-detail block directly, including the per-line `Total : ...` lines and, when included in the selection, the final `TOTAL ...` line. Requiring the operator to clean the text before pasting would defeat the speed objective of the fallback.

The operator also prefers to enter fulfilment mode, planned date/time, telephone, address and ordinary comment manually in the normal POS order screen. Therefore M09 does not need to parse the entire email or reproduce order-level email metadata.

During preparation, two further operational needs were identified:

1. unknown or changed product lines must be handed to the operator rather than guessed or silently lost;
2. a pasted Retrait order may receive the normal Sushi81 pickup discount, so its authoritative POS total can differ from the amount originally shown by Hiboutik. A small retained source amount and passive source indication materially improve end-of-day matching without reintroducing the old emergency-order subsystem.

## Decision

### 1. Paste-input scope

The M09 paste contract is the **Hiboutik product-detail text block**, not the whole email.

The operator may paste the block without deleting:

- per-item `Total : ...` lines;
- a final `TOTAL ...` line when present;
- the known Hiboutik technical/service line `1 x Livraison (0)` and its corresponding total line when it is included in the copied region.

Order-level facts are entered through the ordinary POS fields after paste import. M09 does not auto-populate from the email:

- Retrait/Livraison;
- planned fulfilment date/time;
- customer name;
- telephone;
- address;
- comment/instructions;
- Hiboutik order reference.

A Hiboutik reference may still be typed manually into the ordinary order comment.

This amendment supersedes the earlier AC-HIB-007/parser expectation that a future date/time present elsewhere in the pasted email must be parsed. Future-order behavior itself is unchanged: if the operator enters a future planned fulfilment date/time in the ordinary POS fields, the normal advance-order, reminder and printing rules apply.

### 2. Automatic product resolution remains exact-code only

For a product-like line, the parser may reliably extract the positive quantity and candidate product code from the source structure.

Automatic catalogue resolution uses the **current active Sushi81 catalogue exact product code only**. Source display name and source price are never fuzzy-matched to select a product.

If exact current-code resolution succeeds, the current Sushi81 product definition becomes the ordinary cart product. The source description and source unit/line price do not become pricing authority.

### 3. Line classification and fail-safe unresolved workflow

Every non-blank relevant source line must end in one of three explicit parser outcomes:

1. `RESOLVED_PRODUCT` — product syntax is reliable and the exact current active code resolves;
2. `KNOWN_IGNORED_LINE` — a specifically supported non-product source line such as per-item `Total : ...`, final `TOTAL ...`, or the known Hiboutik `Livraison (0)` technical/service line;
3. `UNRESOLVED_LINE` — product-like or otherwise material pasted text that cannot safely be classified or resolved.

Unknown text must not be silently discarded merely because it fails a product regular expression.

For an unresolved line the transient import state retains, for operator review:

- the source line text;
- parsed quantity when reliable;
- parsed candidate code when reliable.

The operator must explicitly resolve every unresolved line by either:

- selecting a current active catalogue product; or
- explicitly marking the line as not a product / intentionally ignored.

Operator product selection is a deliberate human decision and is not fuzzy automatic matching. A reliably parsed quantity is carried into the selected product by default; if quantity was not reliable, the operator must supply/correct it.

Final order confirmation is blocked while any unresolved line remains undecided.

Transient unresolved/source-line text is not persisted after the ordinary order is confirmed or the draft is abandoned.

### 4. Ordinary option confirmation remains mandatory

A product resolved automatically or selected manually that has enabled option groups must use the same ordinary option-selection UI and rules as manual order entry.

Every enabled group requires explicit operator confirmation for a pasted product, including explicit confirmation of no selection for an optional group when no option is correct.

No Hiboutik-specific option editor is introduced.

### 5. Known source totals are tolerated and may provide one reference amount

Per-item `Total : ...` and final `TOTAL ...` lines are accepted primarily so the operator can paste the whole detail block quickly.

They never provide POS pricing authority.

M09 may capture one nullable retained source-reference amount:

`Order.source_total_ttc`

Its meaning is strictly:

> the reliably determined total shown by the pasted Hiboutik source, retained only as a read-only reconciliation/reference aid.

Reliability rule:

- if a parseable final `TOTAL ...` line is present, it is the preferred source-reference amount;
- if no final total is present, the importer may derive the source-reference amount from the per-item/source `Total : ...` lines only when the copied block is complete and the derivation is unambiguous;
- otherwise `source_total_ttc` remains null;
- the POS must never substitute its own calculated pre-discount amount for a missing/ambiguous source amount.

The source-reference amount is captured at ordinary order creation, remains read-only, and is preserved across later ordinary edits so it continues to identify the original Hiboutik order amount.

### 6. POS total remains the only authoritative order total

The ordinary Sushi81 pricing engine remains the sole automatic pricing authority.

It uses current Sushi81 catalogue data, reviewed quantities, operator-confirmed options/adjustments, ordinary Retrait discount rules, ordinary Livraison rules and current configured business settings.

`source_total_ttc`:

- does not override `Order.total_ttc`;
- does not affect discount calculation;
- does not affect VAT/tax snapshots;
- does not affect CB/Espèce arithmetic or Close;
- does not affect printing prices;
- does not contribute separately to turnover or received-payment reporting;
- does not affect export eligibility/payload;
- does not create a discrepancy/reconciliation status.

The ordinary manual authoritative-total override remains available under the same rules as every other order.

### 7. Passive operator-visible source identification

The durable source discriminator remains system-controlled and non-editable:

- `POS`;
- `HIBOUTIK_PASTE`.

However, the previous requirement that the source be completely invisible is amended.

For a Hiboutik paste-created order the ordinary order list/detail may show a compact passive read-only `Hiboutik` source indication and, when available, the read-only Hiboutik source-reference total.

This is an identification/reconciliation aid only. It must not introduce:

- a dedicated emergency-order screen;
- special emergency lifecycle/status;
- emergency dashboard counter;
- special emergency colour/style requirement;
- discrepancy panel/status;
- Hiboutik-specific payment/reconciliation workflow.

### 8. Anti-double-counting boundary remains unchanged

A `HIBOUTIK_PASTE` order remains an ordinary order for lifecycle, editing, payment, search, future/due/overdue operation, printing and reprinting.

It remains automatically excluded from:

- ordinary POS-originated operational turnover;
- ordinary POS-originated received-payment summaries;
- ordinary POS card amount that must newly be represented in Hiboutik;
- export to `Gestion SUSHI 81`.

M11 still owns the final production export-exclusion cross-check.

### 9. No return to the old emergency metadata model

Apart from the existing source discriminator and the newly approved nullable `source_total_ttc`, V1 still does not add:

- `EmergencyImportDetail`;
- raw pasted-email retention;
- dedicated Hiboutik order/reference field;
- parser fingerprint;
- duplicate-management subsystem;
- discrepancy/reconciliation state;
- Hiboutik-specific payment model.

## Fixture and privacy rule

Repository parser fixtures must be synthetic or sanitized and preserve structural characteristics only. Real customer names, telephone numbers, addresses, Hiboutik order references or other sensitive production content must not be committed.

## Rationale

This amendment keeps the fallback fast and operationally useful while remaining fail-safe:

- the operator can paste the whole product-detail block with no cleanup;
- exact-code automatic matching stays deterministic;
- unknown lines cannot disappear silently;
- human correction reuses the ordinary catalogue and option workflows;
- the original Hiboutik amount remains available for evening matching when it can be determined reliably;
- POS pricing/accounting authority remains singular;
- the superseded complex emergency-order subsystem is not restored.
