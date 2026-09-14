# Hiboutik paste-order import

**Status:** Approved — Phase 4 baseline, amended 2026-09-14  
**Last updated:** 2026-09-14  
**Product:** Sushi81 POS  
**Purpose:** Specify the simple fallback workflow that converts a pasted Hiboutik product-detail block into an ordinary Sushi81 POS order when Hiboutik server-side printing is unavailable.

## 1. Scope

V1 provides a **Hiboutik paste-order import** as a fast order-creation aid.

Its purpose is deliberately narrow:

**paste the Hiboutik product-detail block -> resolve/import ordinary cart lines -> operator resolves any unknown lines -> operator completes normal order fields/options -> ordinary POS pricing/validation -> confirm -> print normally.**

This function is not a Hiboutik synchronization, duplicate-management or reconciliation subsystem.

The controlling 2026-09-14 amendment is `docs/decisions/m09-hiboutik-paste-operator-workflow-and-source-reference.md`.

## 2. Ordinary-order model remains authoritative

A Hiboutik paste-created order remains an ordinary Sushi81 order for:

- the order-entry screen;
- cart editing;
- product options;
- Retrait/Livraison rules;
- planned date/time;
- telephone/address/comment;
- pricing and manual total override;
- confirmation;
- modification/cancellation;
- payment/Close;
- search/future/due/overdue views;
- printing/reprinting.

V1 still has no dedicated emergency-order screen, lifecycle/status, dashboard counter, discrepancy panel or Hiboutik-specific payment/reconciliation workflow.

## 3. Operator entry point and paste scope

The application provides a simple action such as **Paste Hiboutik order** from the normal order-creation workflow.

The intended fast operation is:

1. in the Hiboutik automatic order email, copy the complete product-detail block;
2. paste it directly into Sushi81 POS without cleaning individual source-total lines;
3. parse/import recognized products;
4. resolve any remaining unknown lines explicitly;
5. complete ordinary POS order fields and option confirmations;
6. confirm through the normal order action.

No durable order is created merely by pasting or parsing.

The M09 parser contract is intentionally limited to the **product-detail block**. The operator enters the following manually in the ordinary POS fields rather than relying on email parsing:

- Retrait/Livraison;
- planned fulfilment date;
- planned fulfilment time;
- telephone;
- delivery address;
- comment/instructions;
- Hiboutik order reference, if useful.

If the Hiboutik order reference is useful, it remains ordinary manually typed comment text; no dedicated reference field exists.

## 4. Real source structure and tolerated block content

The real Hiboutik product-detail structure supports product lines in the practical form:

```text
<quantity> x <product code> <source display description> (<source price>)
Total : <source line total>
```

A copied block may also contain:

```text
TOTAL <source order total>
```

and, for delivery, the known Hiboutik technical/service line:

```text
1 x Livraison (0)
Total : 0
```

The operator is not required to remove these lines before paste. This tolerance exists to maximize live-store speed.

Repository fixtures must use synthetic examples rather than real customer/order data.

## 5. Source boundary and normalization

The pasted source is untrusted plain text.

V1 does not:

- connect automatically to Gmail/Outlook;
- monitor a mailbox;
- scrape Hiboutik;
- call the Hiboutik API;
- monitor the clipboard in the background;
- execute HTML, scripts or embedded pasted content.

The parser is deterministic and independently testable.

Safe normalization may include:

- CRLF/LF normalization;
- repeated blank-line cleanup;
- non-breaking-space normalization;
- trimming surrounding whitespace;
- harmless Unicode punctuation/spacing normalization;
- removal of copy/paste formatting artifacts that do not carry business meaning.

Normalization must not silently alter business values such as product code, quantity or source monetary text.

A parser exception or unsupported format performs no business write.

## 6. Line classification

Every non-blank relevant pasted line must end in one of three explicit outcomes.

### 6.1 `RESOLVED_PRODUCT`

A product-like source line has a reliably parsed positive quantity and candidate product code, and that code resolves to the current active Sushi81 catalogue by exact code.

The source display name and source price are not product identity or pricing authority.

### 6.2 `KNOWN_IGNORED_LINE`

The parser may specifically recognize non-product source lines that are expected in the copied block, including:

- per-item `Total : ...` lines;
- final `TOTAL ...` line;
- the known Hiboutik `Livraison (0)` technical/service line and its source total.

Known ignored lines do not create order items and are not parser errors.

### 6.3 `UNRESOLVED_LINE`

Any product-like or otherwise material pasted line that cannot safely be classified/resolved becomes unresolved.

Unknown text must not be silently discarded merely because it fails expected product syntax.

The transient unresolved state may retain:

- the source line text;
- parsed quantity when reliable;
- parsed candidate code when reliable.

This transient source text is not persisted after confirmation or draft abandonment.

## 7. Exact product-code resolution and manual handoff

Automatic product resolution uses the **current active Sushi81 catalogue exact product code only**.

There is no fuzzy automatic matching by product name.

If the code is missing, malformed or not found in the current active catalogue, the importer must not guess another product.

For each unresolved line the operator must explicitly choose one of two dispositions:

1. **Select product** — choose a current active Sushi81 catalogue product through the ordinary product-selection/search capability;
2. **Not a product / ignore** — explicitly decide that the source line should not produce a cart item.

Human selection is an explicit operator decision and is not fuzzy automatic matching.

If quantity was parsed reliably, it becomes the default quantity for the manually selected product. If quantity was not reliable, the operator must enter/correct quantity.

Final order confirmation is blocked while any unresolved line remains undecided.

## 8. Option completion

The pasted source does not provide trusted Sushi81 option selections.

If a resolved or manually selected current product has enabled option groups, the operator must confirm options using the **same ordinary option-selection UI and validation rules** as manual POS order entry.

This confirmation is required for all enabled groups, including optional groups.

For an optional group, the ordinary option UI must permit explicit confirmation of no selection when that is correct.

Required/optional, single/multi, min/max, active choice, display-order, option-adjustment, discount and VAT rules remain ordinary catalogue/order rules.

No Hiboutik-specific option editor exists.

## 9. POS pricing is authoritative

The authoritative total of a Hiboutik paste-created order is calculated by **Sushi81 POS from current Sushi81 catalogue/business data and the final reviewed cart**, exactly as for a manually created ordinary order.

The normal pricing engine uses:

- current product base prices;
- final reviewed quantities;
- operator-confirmed options/adjustments;
- ordinary Retrait discount rules;
- ordinary Livraison minimum/fee rules;
- ordinary VAT and rounding rules.

Source display prices, per-line source totals and final Hiboutik source total never override this calculation.

After normal calculation, the operator retains the approved ordinary manual authoritative-total override under `business-rules.md`.

## 10. Nullable Hiboutik source-reference total

M09 may persist one additional minimal source-specific fact:

`Order.source_total_ttc`

This is a nullable, read-only **reference amount only** representing the total reliably determined from the pasted Hiboutik source.

Reliability rule:

- a parseable final `TOTAL ...` line is preferred when present;
- when no final total is present, per-item/source `Total : ...` lines may be summed only when the copied block is complete and the result is unambiguous;
- otherwise `source_total_ttc` remains null;
- the POS must never substitute its own calculated pre-discount amount for a missing/ambiguous Hiboutik source amount.

`source_total_ttc` is captured at creation and remains read-only/preserved through later ordinary edits so that evening reconciliation can still identify the amount originally shown by Hiboutik.

It has **no pricing/accounting authority**. It does not affect:

- `Order.total_ttc`;
- discount calculation;
- tax snapshots;
- CB/Espèce or Close arithmetic;
- printed selling prices;
- operational turnover or received-payment reporting;
- export eligibility/payload;
- order lifecycle/status.

No Hiboutik-vs-POS discrepancy state or comparison workflow is required.

## 11. Source discriminator and passive visibility

A confirmed paste-created order persists the system-controlled source discriminator:

`HIBOUTIK_PASTE`

A manual ordinary POS order persists:

`POS`

The source field is not operator-editable.

The previous requirement that it be completely invisible is amended. The ordinary order list/detail may show a compact passive read-only **Hiboutik** source indication and, when available, `source_total_ttc` for reconciliation/identification.

This passive visibility must not become a separate emergency order type, screen, style, counter or workflow.

## 12. Anti-double-counting boundary

A `HIBOUTIK_PASTE` order already represents an order that exists in Hiboutik and therefore remains automatically excluded from:

- ordinary POS-originated operational turnover;
- ordinary POS-originated received-payment totals;
- the ordinary POS CB amount that must newly be represented/entered in Hiboutik;
- export to `Gestion SUSHI 81`.

The order remains otherwise ordinary for search, lifecycle, editing, payment, future/due/overdue operation, printing and reprinting.

M11 owns the final production export-exclusion cross-check.

## 13. Future fulfilment after the M09 amendment

M09 no longer parses planned fulfilment date/time from the full email.

The operator enters fulfilment/date/time through the ordinary order fields.

If a future date/time is entered, all normal structured future-order behavior remains unchanged, including `advance_order_marker`, reminders and M08 printing prominence.

The amendment changes parser scope only; it does not weaken ordinary future-order semantics.

## 14. Persistence and printing boundary

The final sequence remains:

1. operator finishes ordinary order review and resolves all import issues/options;
2. normal validation succeeds;
3. order is durably committed to SQLite;
4. normal M08 printing workflow is invoked;
5. print failure does not roll back/delete the order;
6. normal reprint remains available.

Printing is never the event that creates the business order.

## 15. No return to the old emergency metadata model

Apart from:

- system-controlled `source_type`; and
- nullable read-only `source_total_ttc`;

V1 does not require:

- `EmergencyImportDetail`;
- dedicated Hiboutik reference-number field;
- raw pasted-email/source-line retention after successful conversion;
- parser fingerprint;
- dedicated duplicate-management subsystem;
- discrepancy/reconciliation status;
- Hiboutik-specific payment/reconciliation model.

## 16. Validation and failure behavior

If the pasted source cannot produce any useful safely classified content, the application reports the problem and leaves the business database unchanged.

Ambiguous business facts are never invented.

Known source-total/service lines may be ignored according to this specification; unknown material lines become unresolved and require explicit operator disposition.

Final confirmation uses the existing ordinary validation/pricing/lifecycle rules plus:

- zero unresolved import lines;
- required explicit option confirmation for pasted products with enabled option groups.

## 17. Duplicate handling

V1 does not add a dedicated Hiboutik duplicate-detection subsystem.

If operationally useful, the operator may type the Hiboutik order reference into ordinary comment text.

## 18. Testing and sanitized fixtures

Automated parser/application/integration tests must use synthetic or sanitized source blocks and cover at least:

- multiple products and quantities;
- per-item `Total : ...` lines interleaved with products;
- final `TOTAL ...` present and absent;
- exact current product-code resolution;
- missing/unknown product code;
- unknown text becoming unresolved rather than disappearing;
- manual product selection for unresolved lines;
- explicit operator ignore for non-product unresolved text;
- unresolved state blocking confirmation;
- known `Livraison (0)` technical/service line;
- products without options;
- products requiring option confirmation;
- optional option groups explicitly confirmed with no selection;
- source total differing from current POS-calculated/discounted total;
- malformed/unsupported input producing no business write;
- abandon-after-parse producing no durable order;
- `HIBOUTIK_PASTE` persistence and anti-double-counting reporting behavior.

No real customer name, telephone, address, Hiboutik order reference or other sensitive production information may be committed to Git.

## 19. Frozen M09 business semantics

The controlling V1 behavior is now:

1. paste import is only an ordinary-order creation aid;
2. the operator pastes the Hiboutik product-detail block directly, including source-total lines;
3. order-level fulfilment/customer/schedule data is entered manually in ordinary POS fields;
4. automatic product matching is exact current active product code only;
5. unknown material lines become explicit unresolved state and cannot disappear silently;
6. each unresolved line requires operator product selection or explicit ignore before confirmation;
7. every imported product with enabled options requires explicit ordinary option confirmation;
8. current Sushi81 pricing remains the sole automatic authoritative total;
9. nullable `source_total_ttc` may retain a reliably determined Hiboutik source amount only as a read-only reconciliation aid;
10. a compact passive `Hiboutik` source indication may be visible in ordinary order list/detail;
11. anti-double-counting exclusions remain unchanged;
12. no dedicated emergency-order lifecycle, discrepancy, duplicate or payment/reconciliation subsystem is introduced.

## 20. Approval

This document is **Approved — Phase 4 baseline, amended 2026-09-14**.

The detailed amendment rationale and supersession rules are recorded in `docs/decisions/m09-hiboutik-paste-operator-workflow-and-source-reference.md` and the amended acceptance mapping is recorded in `docs/acceptance-criteria-amendment-m09-hiboutik-paste-fallback.md`.
