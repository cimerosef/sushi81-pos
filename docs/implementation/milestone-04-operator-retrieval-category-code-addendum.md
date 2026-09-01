# M04 operator retrieval and Category-code addendum

**Status:** Approved implementation addendum  
**Approved by:** project owner  
**Approval date:** 2026-09-01  
**Applies to:** M04 / PR #6 / `codex/m04-order-entry`  
**Parent contract:** `docs/implementation/milestone-04-order-entry.md`  
**Decision authority:** `docs/decisions/m04-order-entry-operator-ergonomics-amendment.md`, sections E–G  
**POST_TASK_POWER_ACTION:** `NONE`

## 1. Trigger

The second manual Windows/WPF acceptance pass found that the broad M04 ergonomics remediation now works, but exposed three remaining operator issues:

1. an evening planned time entered as `18:25` could be rendered as `06:25` in the committed-order summary;
2. the Category navigator still used full Category names rather than the concise operator codes used in the restaurant workflow;
3. exact-ID reload alone was not discoverable enough because after confirming multiple orders the operator had no ordinary visible way to find earlier committed orders without having copied their GUIDs.

The project owner explicitly approved restoring a Category short-code field and adding a date-selectable read-only committed-order browser whose default date is today.

## 2. Required implementation scope

Implement only the following M04 addendum on the existing PR #6 branch.

### 2.1 Category short code

Add an operator-facing `short_code` to the current Category model and persistence.

Requirements:

- keep opaque Category ID unchanged;
- keep full Category name unchanged and business-unique;
- short code is independently editable operator business data;
- do not derive/guess short code from the name;
- non-blank current short codes are trim/case-insensitive unique;
- do not require exactly one character;
- add a modest practical technical length limit suitable for compact Caisse navigation;
- preserve current Category/Product/Order data through an additive next migration after schema v3;
- migrated existing categories with no code remain valid and editable; do not fabricate values;
- Catalogue maintenance exposes short-code edit/validation in FR and zh-CN;
- normal Caisse Category navigation uses the short code as the visible primary label when present;
- uncoded migrated categories temporarily fall back to full Category name;
- coded categories use deterministic short-code ordering;
- current short-code changes never rewrite historical Order snapshots;
- do not implement future `.xlsx` import/export now.

### 2.2 Dated read-only order browser

Add one narrow persisted Order-list query and WPF browser.

Date semantics are binding:

- filter field = persisted `planned_fulfilment_date`;
- default selected browse date = `IBusinessClock.BusinessDate`;
- allow operator to choose past/today/future dates for **viewing**;
- do not reuse the new-order past-date restriction on the browser date picker.

Data behavior:

- query SQLite, not session-only history;
- application restart must still rediscover orders for the selected date;
- keep query narrow and indexed/practical for normal daily volumes;
- return stable exact Order identity and compact persisted summary facts;
- do not reconstruct historical values from current Catalogue;
- do not silently exclude a controlled persisted status merely because M04 currently creates only Open orders.

UI behavior:

- default view shows today's planned orders;
- prior orders remain visible after confirming another order;
- row should expose at least planned time, fulfilment mode, status, authoritative Total TTC and telephone when present;
- stable sensible default ordering, preferably planned time with deterministic tie-break;
- selecting one row displays the existing committed snapshot read-only;
- if a newly confirmed order belongs to the currently browsed date, refresh and select it without removing older rows;
- exact-ID reload may remain as fallback but is not the only ordinary retrieval path;
- FR/zh-CN switching preserves selected browser date, list selection and loaded snapshot.

Explicitly prohibited:

- same-ID committed modification;
- telephone/comment/full-text search;
- payments;
- Close/Reopen/Cancel;
- future/due-today/overdue dashboard;
- reporting/export features.

### 2.3 24-hour planned-time presentation bug

Fix all M04 operator-facing planned-time formatting so structured `TimeOnly` values use unambiguous 24-hour display.

Required proof:

- confirm an order at exact `18:25`;
- persistence/reload remains exactly `18:25`;
- just-confirmed snapshot shows `18:25`;
- dated browser row/details show `18:25`;
- exact-ID reload shows `18:25`;
- FR/zh-CN switch does not change the time value.

Do not change the approved time-slot business rule.

## 3. Migration and compatibility expectations

Use the next additive schema migration after v3. It must:

- preserve all existing schema/data;
- add Category short-code persistence safely;
- add only order-browser indexing needed for the date query if the existing schema lacks an adequate index;
- avoid destructive resets or invented Category codes;
- pass upgrade tests from a populated v3 database;
- keep historical null-time compatibility unchanged.

## 4. Automated evidence required

At minimum add/adjust tests for:

### Category

- v3 -> new migration preserves populated Category/Product/Order data;
- existing Category without code remains readable;
- create/edit short code persists/reloads;
- duplicate normalized short code is rejected atomically;
- name rename preserves short code;
- short-code edit preserves name and Product assignment;
- Caisse category labels prefer short code and fall back to name only for uncoded migrated row;
- deterministic short-code ordering;
- FR/zh-CN localization does not mutate business short codes.

### Order browser

- list by planned date returns only matching date rows;
- past/current/future dates all query successfully;
- restart/new store instance returns the same persisted date list;
- multiple orders on one date remain simultaneously discoverable after later confirmations;
- selected list row resolves exact stable snapshot;
- exact-ID fallback remains valid;
- no list query depends on current Catalogue;
- stable ordering with tie-break;
- browser-date changes are read-only and never mutate Orders.

### 24-hour time

- `18:25` persists and reloads exactly;
- every relevant formatter outputs `18:25`, never `06:25`;
- actual STA/WPF path confirms the just-saved summary and browser display use 24-hour time.

## 5. Manual acceptance target after delivery

Manual rerun must first verify:

1. Category left pane shows concise operator codes after codes are assigned;
2. category selection still filters products correctly;
3. order at `18:25` displays `18:25` everywhere;
4. confirm at least two orders for today and prove both remain visible in today's list;
5. choose another date and view that date's persisted orders;
6. restart application and verify today's/selected-date orders remain discoverable;
7. row selection loads the exact read-only snapshot;
8. no M05 modification/search/payment/lifecycle actions appeared.

The existing broader M04 manual checklist remains required before final merge approval.

## 6. Completion/governance

- implement on existing branch `codex/m04-order-entry` and PR #6 only;
- do not create another M04 PR;
- do not merge PR #6;
- do not start M05;
- run full Release restore/build/test/self-contained win-x64 publish and final PR CI;
- update M04 worklog/status with exact final evidence;
- manual Windows/WPF acceptance remains unclaimed until the project owner performs it;
- completion requires a distinct top-level mailbox record matching the handoff ID that authorizes this addendum.
