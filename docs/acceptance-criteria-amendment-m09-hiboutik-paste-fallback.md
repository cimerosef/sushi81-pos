# Acceptance criteria amendment — M09 Hiboutik paste fallback

**Status:** Approved — V1 acceptance amendment  
**Date:** 2026-09-14  
**Applies to:** AC-HIB-001 through AC-HIB-009  
**Decision source:** `docs/decisions/m09-hiboutik-paste-operator-workflow-and-source-reference.md`

This amendment preserves AC-HIB numbering but supersedes the clauses below where the original consolidated `acceptance-criteria.md` differs. Unmentioned AC-HIB requirements remain unchanged.

## AC-HIB-001 — Paste import remains only an order-creation aid

Pasting/parsing a Hiboutik product-detail block must not itself create a durable order.

Successful parsing may populate ordinary cart product/quantity data and transient unresolved-line review state only. Order-level fields such as fulfilment mode, planned date/time, telephone, address and comment remain ordinary operator-entered fields under the M09 product-block input contract.

Abandoning the draft after parsing leaves the business database unchanged.

**Evidence:** parser/UI integration test + no-write failure/abandon tests.

## AC-HIB-004 — Exact product-code matching plus explicit unresolved-line disposition

Automatic product resolution uses only the current active Sushi81 catalogue exact product code. Product names/source prices must not be used for fuzzy automatic substitution.

Every material pasted line must be classified as:

- resolved product;
- specifically known ignored source line; or
- unresolved line.

Unknown text must not be silently discarded merely because it does not match the expected product syntax.

An unresolved line blocks final order confirmation until the operator explicitly either:

- selects a current active catalogue product; or
- marks the line as not a product / intentionally ignored.

Reliably parsed quantity is retained as the default quantity after manual product selection. Source-line/unresolved review state remains transient and is not persisted after confirmation/abandon.

**Evidence:** parser tests + WPF operator-resolution tests.

## AC-HIB-006 — POS pricing remains authoritative; optional source total is reference-only

After product resolution and required option confirmation, the ordinary Sushi81 pricing engine calculates the authoritative order total from current catalogue/business data and final reviewed cart.

Per-item `Total : ...`, final `TOTAL ...`, source display prices and retained `source_total_ttc` must never silently override POS pricing.

The ordinary manual authoritative-total override remains available afterwards.

A nullable `source_total_ttc` may be retained only when the pasted Hiboutik source amount is reliably determined. It is read-only reference data and must not affect discount, VAT, payment/Close arithmetic, printed selling prices, reporting, export or lifecycle.

**Evidence:** parser/pricing regression tests where source amount differs from POS calculated/discounted total.

## AC-HIB-007 — Product-block paste scope; future fulfilment remains ordinary structured data

M09 does not require parsing fulfilment/date/time/customer fields from the Hiboutik email. The operator pastes the product-detail block and enters Retrait/Livraison, planned fulfilment date/time and other order-level data through the ordinary POS fields.

If the operator enters a future planned fulfilment date/time, all existing ordinary future-order/advance-marker/reminder/printing rules apply without source-specific behavior.

This criterion supersedes the earlier parser requirement to preserve a future date/time found elsewhere in a full pasted email.

**Evidence:** UI/manual test + ordinary future-order regression.

## AC-HIB-008 — System-controlled source discriminator with passive operator visibility

`HIBOUTIK_PASTE` remains a system-controlled, non-editable source discriminator and continues to enforce anti-double-counting exclusions.

The ordinary order list/detail may expose a compact passive read-only `Hiboutik` source label and, when available, the read-only `source_total_ttc` reference amount. This presentation must not create an emergency-order screen, status, counter, special lifecycle or reconciliation subsystem.

A paste-created order is automatically excluded from:

- ordinary POS-originated operational turnover;
- ordinary POS-originated received-payment summaries;
- ordinary POS CB amount that must newly be represented in Hiboutik;
- export to `Gestion SUSHI 81`.

M11 retains final export-exclusion cross-check ownership.

**Evidence:** schema/reporting/UI tests; export cross-check in M11.

## AC-HIB-009 — Minimal retained Hiboutik source metadata

V1 permits exactly these source-specific durable facts for this fallback:

- system-controlled `source_type = HIBOUTIK_PASTE`;
- nullable, read-only `source_total_ttc`, captured only when the pasted source total can be determined reliably.

`source_total_ttc` is preserved across later ordinary edits as the original source-reference amount.

V1 still must not require or persist:

- `EmergencyImportDetail`;
- raw pasted-email/source-line retention after conversion;
- dedicated Hiboutik order/reference field;
- parser fingerprint;
- duplicate-management subsystem;
- discrepancy/reconciliation state;
- Hiboutik-specific payment workflow.

If desired, the Hiboutik order reference remains ordinary manually entered comment text.

**Evidence:** schema review + persistence/edit regression tests.

## Additional parser acceptance requirements

The M09 parser fixture/test set must include at least:

- multi-product blocks with positive quantities and exact product codes;
- per-item `Total : ...` lines interleaved with products;
- final `TOTAL ...` present and absent;
- source total different from normal POS total after Retrait discount;
- known `1 x Livraison (0)` service/technical line;
- missing/unknown product code;
- unknown non-product text that must become unresolved rather than disappear;
- manual unresolved-product selection;
- explicit operator ignore of a non-product unresolved line;
- unresolved state blocking confirmation;
- products with enabled option groups, including explicit no-selection confirmation for optional groups;
- malformed/unsupported source producing no business write;
- synthetic/sanitized fixtures only.
