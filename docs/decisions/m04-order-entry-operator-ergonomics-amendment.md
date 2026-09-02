# M04 order-entry operator ergonomics amendment

**Status:** Approved  
**Decision date:** 2026-08-31; amended 2026-09-01  
**Approved by:** project owner  
**Applies to:** M04 — First complete order-entry vertical slice / PR #6  
**Supersedes:** only the affected M04 operator-interaction/details described below; all other Approved M04 pricing, lifecycle, snapshot, persistence and scope rules remain unchanged.

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

The 2026-09-01 amendment in section E **supersedes** the earlier M04 restriction against restoring an operator-facing category shortcut field. A persisted Category short code is now explicitly approved business data for V1. Do not derive or invent codes from names during migration.

## B — Structured but ergonomic planned-time selection

The 2026-08-31 approved addendum below supersedes the original optional-time and all-day-slot wording for current M04 new-order confirmation. The nullable persisted field remains for historical compatibility only.

The persisted planned fulfilment time remains structured, exact time data. It is **not** converted to free text.

However, the operator must not be required to type a colon or remember an `HH:mm` text format.

Required interaction:

- planned time is required for every new POS order confirmation;
- the UI provides click-friendly structured selection for hour and minute;
- no manual `:` entry is required for the normal path;
- selectable hours are exactly `11`, `12`, `13`, `14`, `18`, `19`, `20`, `21`, and `22`;
- selectable minutes are exactly `00`, `05`, `10`, `15`, `20`, `25`, `30`, `35`, `40`, `45`, `50`, and `55`;
- a new order starts with no selected time, and the empty state cannot be confirmed;
- the selected result is shown clearly as a conventional 24-hour `HH:mm` value;
- mouse use must be practical for rapid counter operation; keyboard/type-to-select support may be added but is not a substitute for clickable selection;
- invalid free-text states such as `25:99` must no longer be part of the normal operator journey.

### Superseding addendum — required approved time slots

Status: Approved by the project owner on 2026-08-31 for M04 implementation.

For every new POS order, `PlannedFulfilmentTime` is required for confirmation for both Retrait and Livraison. The operator selects the hour and minute through two structured controls. Only hours `11`, `12`, `13`, `14`, `18`, `19`, `20`, `21`, and `22`, together with five-minute minutes `00` through `55`, are exposed. No free-form `HH:mm` input, colon entry, one-minute precision, or ordinary blank/no-time confirmation path is allowed.

The selected value remains exact `TimeOnly` data. Changing only the planned time is non-price-affecting and preserves an active manual total override. Starting a new order resets the time to unselected. French and Simplified Chinese language changes preserve the selected time. Existing historical snapshots with a null planned time remain readable; this addendum does not authorize a destructive schema migration or a `NOT NULL` change.

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

## E — Restored operator-facing Category short code

Status: Approved by the project owner on 2026-09-01 after the second M04 operator acceptance pass.

The former legacy concept of a concise operator-facing Category code is now explicitly restored as V1 business data.

Required semantics:

- Category retains opaque `category_id` as the technical identity;
- Category retains the full editable business-unique `name`;
- Category adds an editable operator-facing `short_code` used for fast Caisse navigation;
- `short_code` is business data, not a technical ID and not derived automatically from `name`;
- current non-blank short codes must be unique under ordinary trim/case-insensitive normalization, so two categories cannot look like the same code to the operator;
- the field is intended to be concise; implementation may impose a modest technical length limit suitable for compact navigation, but must not require exactly one character;
- changing a Category name does not implicitly change its short code and changing the short code does not rename the Category;
- no historical order is rewritten when the current Category short code changes. Historical order interpretation continues to use the persisted category-name snapshot; M04 does not require a new historical category-code snapshot.

Migration/compatibility:

- introduce the next additive SQLite migration after M04 migration 3;
- preserve every existing M03/M04 Category/Product/Order row;
- do **not** invent A/B/C codes from existing names during migration;
- pre-existing categories may therefore migrate with no code until the operator assigns one;
- Catalogue maintenance must expose the field clearly so missing legacy codes can be filled;
- new/edited normal category maintenance should validate the operator-entered short code according to the approved uniqueness rule;
- Caisse displays the short code as the primary Category navigation label when present; an uncoded migrated Category may temporarily fall back to its full name so the application remains operable before cleanup;
- coded categories use deterministic short-code ordering for navigation; uncoded migration fallbacks must remain deterministic and must not be silently assigned fabricated codes.

Future catalogue `.xlsx` support must eventually preserve this business field consistently, but this M04 amendment does **not** authorize implementing the later Excel import/export milestone now.

## F — Read-only dated committed-order browser

Status: Approved by the project owner on 2026-09-01 after the second M04 operator acceptance pass.

The prior exact-ID-only M04 reload proved technically correct but operationally insufficient: after more than one order was confirmed, the previous order could no longer be found through an ordinary visible workflow unless the operator had manually copied its GUID.

M04 therefore adds a deliberately narrow read-only order browser without importing the broader M05 modification/search workflow.

### F.1 Date meaning and default

The browser date is the order's business/operational **`planned_fulfilment_date`**, not its technical `created_at` date.

- default browser date = `IBusinessClock.BusinessDate`;
- default list therefore shows orders planned for today;
- operator may choose another past, current, or future date to view orders persisted for that planned date;
- unlike the new-order DatePicker, this read-only browser date picker must not prohibit past dates;
- the selected browse date is view state only and must never modify an Order.

### F.2 Data source and persistence

- list results come from persisted SQLite Order snapshots, not a session-only in-memory history;
- closing/restarting the application and returning to a date must rediscover the persisted orders for that date;
- use an indexed/narrow date query appropriate for normal daily order volumes;
- the query must remain snapshot-based and must not reconstruct historical values from current Catalogue.

### F.3 List presentation and selection

The daily list must make ordinary orders distinguishable without requiring the operator to memorize a GUID. At minimum each visible row exposes useful compact facts such as:

- planned time in 24-hour `HH:mm` form when present;
- Retrait/Livraison;
- current persisted status;
- authoritative Total TTC;
- telephone when present;
- enough stable identity internally to select the exact Order.

A stable sensible default ordering is required. Prefer operational planned-time ordering with deterministic tie-breaking; after a successful confirmation, if the browser is currently showing the same planned date, refresh the list and keep/select the newly created exact Order without removing older rows.

Selecting an order row loads/displays the existing committed snapshot read-only. Exact-ID reload may remain available as a diagnostic/precise fallback, but it is no longer the only normal retrieval path.

### F.4 Scope boundary

This dated browser is **not** authorization for general M05 search or modification. M04 still must not add:

- telephone or comment search;
- arbitrary full-text search;
- same-ID modification of a committed order;
- payment editing;
- Close/Reopen/Cancel actions;
- future/due-today/overdue dashboards;
- general reporting/export behavior.

The browser may display whatever controlled statuses already exist in persistence, but it is read-only.

## G — 24-hour committed-order time display

The second operator pass exposed a display defect where an entered/persisted evening time such as `18:25` was rendered as `06:25` in the committed-order summary.

Required correction:

- persisted `TimeOnly` value must round-trip exactly;
- every operator-facing planned-time display in M04 order summaries/browser/snapshot views uses unambiguous 24-hour `HH:mm` semantics;
- `18:25` must display as `18:25`, never `06:25` without an AM/PM marker;
- add regression coverage proving an evening value survives confirmation/persistence/reload and all relevant WPF summary formatting unchanged.

## Non-negotiable scope boundary

This amendment does not authorize M05+ behavior except for the explicit narrow read-only dated browser in section F and the restored Category short-code business field in section E.

In particular it still does not authorize same-ID committed modification, payments, Close/Reopen/Cancel, telephone/comment/full-text search, dashboards, final Windows printing, Hiboutik import, Excel export/import, archive or installer work.
