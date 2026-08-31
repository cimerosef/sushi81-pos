# M04 order-entry operator ergonomics amendment

**Status:** Approved  
**Decision date:** 2026-08-31  
**Approved by:** project owner  
**Applies to:** M04 — First complete order-entry vertical slice / PR #6  
**Supersedes:** only the affected M04 operator-interaction details described below; all other Approved M04 pricing, lifecycle, snapshot, persistence and scope rules remain unchanged.

## Context

The first Windows/WPF operator acceptance attempt on M04 exposed usability problems that block efficient restaurant order entry even though the underlying pricing/persistence tests are green.

The project owner approved the following operator-workflow corrections. These are binding for the remainder of M04.

## A — Category-first two-pane order-entry navigation

The normal Caisse product-selection experience must follow the fast legacy interaction pattern rather than using a category drop-down as the primary navigation control.

Required interaction:

1. Categories are always visible in a dedicated navigation area.
2. Selecting one category immediately displays that category's active Products in the adjacent Product area.
3. The Product area supports direct double-click add as the primary fast path.
4. Code/name search remains available as a useful secondary path, but category selection is the normal first entry point.
5. The operator must not repeatedly open a drop-down merely to move between ordinary categories.
6. Category selection must not mutate the cart or other order state.
7. Category/Product areas must remain readable at supported window sizes and in both French and Simplified Chinese.

The legacy workbook used operator-facing letter shortcuts/order for categories. Current Approved V1 Catalogue data intentionally does not persist a dedicated `RaccourciCat` / category-shortcut field. This amendment therefore changes the M04 **navigation layout/workflow only** and does not silently reintroduce a new persisted Category field during M04. Use current Category names with deterministic ordering. If literal legacy shortcut codes must later become persisted business data, surface a separate specification amendment rather than inventing it inside this remediation.

## B — Structured but ergonomic planned-time selection

The persisted planned fulfilment time remains structured, exact time data. It is **not** converted to free text.

However, the operator must not be required to type a colon or remember an `HH:mm` text format.

Required interaction:

- planned time remains optional;
- the UI provides click-friendly structured selection for hour and minute;
- no manual `:` entry is required for the normal path;
- all hours 00–23 and minutes 00–59 remain representable;
- empty/no-time state remains easy to select;
- the selected result is shown clearly as a conventional `HH:mm` value;
- mouse use must be practical for rapid counter operation; keyboard/type-to-select support may be added but is not a substitute for clickable selection;
- invalid free-text states such as `25:99` must no longer be part of the normal operator journey.

A compact two-control hour/minute picker or an equally simple native WPF interaction is acceptable. Do not add a heavy UI framework solely for this control.

## C1 — No new order may be confirmed for a past business date

For **new-order confirmation**, `planned_fulfilment_date` must be greater than or equal to `IBusinessClock.BusinessDate`.

Rules:

- date earlier than BusinessDate => confirmation blocked with localized operator feedback;
- date equal to BusinessDate => allowed;
- future date => allowed and existing advance-order marker behavior remains unchanged;
- the WPF date picker should prevent or discourage selection before BusinessDate in addition to Application/Domain validation;
- historical committed orders with an old planned date remain reloadable/read-only. Do not reinterpret history as invalid.

This is a new M04 validation rule and must be enforced below the UI so a non-WPF caller cannot bypass it.

## D1 — Direct add for simple Products; option dialog only when options are enabled

The existing Approved Catalogue rule is reaffirmed and must be visible in actual WPF behavior:

- Product with `options_enabled = false` / no enabled option workflow => explicit Add and Product-row double-click add directly to cart with quantity 1 and **no option dialog**;
- Product with options enabled => normal option-selection dialog appears automatically before the configured line is added;
- option required/optional/SINGLE/MULTI/min/max rules remain unchanged;
- cart quantity can then be changed with cart controls;
- an existing configured cart line can still be reopened for option/custom-adjustment editing.

Do not simplify option-enabled Products into an invalid unconfigured cart line merely to avoid the dialog.

## Non-negotiable scope boundary

This amendment does not authorize M05+ behavior. In particular it does not authorize general historical-order search, same-ID committed modification, payments, Close/Reopen/Cancel, dashboards, final Windows printing, Hiboutik import, Excel export/import, archive or installer work.

M04 may improve the visibility of the **just-confirmed / exact-ID reloaded** committed snapshot, but general live order search remains M05.
