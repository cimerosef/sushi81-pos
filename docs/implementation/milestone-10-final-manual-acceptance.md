# M10 — Catalogue `.xlsx` import/export — final owner manual acceptance

**Status:** CANDIDATE PREPARED / OWNER NOT YET EXECUTED

**Prepared/finalized:** 2026-09-17; WP5 final evidence/candidate regeneration completed 2026-09-18 pending controller review
**Milestone:** M10  
**Owner result:** NOT YET EXECUTED  
**Implementation authorization:** WP1–WP4 are controller-accepted; WP5 final evidence/candidate regeneration is complete on PR #22 pending controller review; owner manual acceptance remains unexecuted
**Candidate source head:** exact WP5-18 head recorded in its matching `CODEX_DONE`; WP5-17 candidate metadata is provisional/historical and superseded
**Candidate executable / ZIP:** exact WP5-18 published paths, sizes and hashes are recorded in its matching `CODEX_DONE`

> This checklist is for the project owner. Codex must not pre-check boxes or mark the result Passed.

## 0. Preconditions

Before execution record:

- [ ] implementation PR number and exact candidate source head;
- [ ] exact-head CI result;
- [ ] full Release test/build result;
- [ ] candidate EXE path, size and SHA-256;
- [ ] candidate ZIP path, size and SHA-256;
- [ ] current authority state/device role;
- [ ] test database is controlled/synthetic or an owner-approved disposable copy;
- [ ] Microsoft Excel or the intended real normal `.xlsx` operator application is available.

Do not use the live production database for destructive/corruption acceptance cases.

## A — Real export / Excel readability / protection

1. Seed a realistic current catalogue containing:
   - multiple Categories;
   - at least one Category with a short code and one Category without one;
   - Products with different VAT/prices/active/discount/options-enabled states;
   - SINGLE and MULTI OptionGroups;
   - active and inactive Options.
2. In the real Windows app, export Catalogue `.xlsx`.
3. Open the generated file in Excel.

Accept only if:

- [ ] Excel opens the file normally with no repair/corruption warning;
- [ ] visible logical sheets are exactly `Products`, `OptionGroups`, `Options`;
- [ ] no operator-facing `Categories` sheet appears;
- [ ] `Products` visibly exposes Category name and Category short code business columns;
- [ ] every Product row shows the current Category short code accurately, blank when that Category has none;
- [ ] ordinary business columns are understandable and practically editable;
- [ ] technical Product/OptionGroup/Option identity is not exposed as normal editable business data;
- [ ] `category_id` is not presented as an operator field;
- [ ] ordinary operator actions do not accidentally overwrite protected technical bindings;
- [ ] workbook remains usable for sorting/filtering/adding rows as designed;
- [ ] current business values match the application exactly.

Notes:

- Result: PASS / FAIL / NOT RUN
- Evidence/screenshots:

## B — No-op export/re-import round trip

1. Save a fresh exported workbook without business edits.
2. Import it in normal Update mode.
3. Inspect preview before Confirm.

Accept only if:

- [ ] preview reports no unintended Create/Modify/Activate/Deactivate operation;
- [ ] Category short-code consistency fields do not produce false changes;
- [ ] omitted/delete count does not exist;
- [ ] preview clearly states no database change has occurred yet;
- [ ] canceling leaves Catalogue unchanged;
- [ ] if a no-op Confirm path exists, it causes no unwanted durable changes or identity/order drift.

Afterward re-export and compare the business catalogue.

- [ ] Product/Group/Option identities and ordering remain stable;
- [ ] Category names and short codes round-trip unchanged;
- [ ] no duplicate Category/Product was created.

Result: PASS / FAIL / NOT RUN

## C — Normal Update mode — same-ID business edits and existing Category short-code safety

From a fresh export make controlled edits including as applicable:

- Product code;
- Product name;
- Category assignment by Category name;
- TTC price;
- VAT;
- Product active state;
- discount-eligible state;
- options-enabled state;
- OptionGroup name/mode/required/min/max/order where valid;
- Option name/price adjustment/active/order.

Also exercise existing Category short-code behavior:

1. leave one existing Category short-code cell blank;
2. leave/use the same existing short code on another Product row referencing that Category;
3. on a disposable copy, change an existing Category short code to a different non-blank value;
4. for an existing Category that currently has no short code, try entering a non-blank one through Excel.

Accept only if:

- [ ] blank existing Category short-code cell preserves the current value;
- [ ] same normalized existing short code is accepted as consistency data and does not create a Category mutation;
- [ ] a different non-blank short code for an existing Category is a blocking Error;
- [ ] adding a short code to an existing uncoded Category through the workbook is a blocking Error;
- [ ] the error clearly tells the operator that existing Category short-code changes belong in the in-app Category manager/re-export workflow;
- [ ] preview identifies exactly the intended Product/Group/Option modifications/state changes;
- [ ] no unrelated record is included;
- [ ] after Confirm, Product code change preserved the same internal Product identity;
- [ ] changed records persist after app restart;
- [ ] re-export shows intended final values only.

Result: PASS / FAIL / NOT RUN

## D — New Product / OptionGroup / Option rows in Update mode

1. In an exported workbook add:
   - one new Product;
   - one new OptionGroup for it;
   - multiple new Options;
   - one new valid Category name with a valid optional Category short code.
2. Add another Product row referencing that same new Category and use either the same short code or blank so the Category meaning remains consistent.
3. Use only the normal documented workbook parent-reference workflow; do not manually type database IDs.

Accept only if:

- [ ] parent relationships are understandable to the operator;
- [ ] preview shows creates, not accidental updates;
- [ ] exactly one new Category is proposed for the repeated normalized Category name;
- [ ] its intended short code is shown/understandable in preview;
- [ ] Confirm creates exactly one Category and one intended Product/Group/Option hierarchy set;
- [ ] Caisse can find/use the new active Product according to current Category short-code navigation rules;
- [ ] option selection behavior is correct;
- [ ] restart/re-export preserves hierarchy, IDs and the new Category short code.

Negative subcase:

4. On a disposable copy, make two Product rows for the same new Category contain different non-blank short codes.

- [ ] import blocks with a row-addressable Category short-code conflict;
- [ ] no partial Category/Product creation occurs.

Result: PASS / FAIL / NOT RUN

## E — Explicit Add-only / first catalogue initialization

Use a controlled empty catalogue database/application profile.

1. Obtain/create a valid no-existing-ID workbook/template.
2. Add at least two Products, one new Category name with optional short code, and a Product with options.
3. Select **Add-only** explicitly and preview.

Accept only if:

- [ ] preview clearly identifies Add-only/create-only mode;
- [ ] complete first catalogue can be created without pre-existing Product IDs or Category IDs;
- [ ] new Category short code is created when provided and valid;
- [ ] all created records work after restart/re-export;
- [ ] re-export repeats the created Category short code on Product rows.

Then on a non-empty catalogue:

4. Prepare an Add-only row using an already-existing Product code.

- [ ] import is blocked as an Error;
- [ ] it does not silently update the existing Product;
- [ ] zero partial changes occur.

5. Prepare an Add-only Product assigned by name to an existing Category and attempt to change that existing Category's short code in the row.

- [ ] Category resolution itself is allowed;
- [ ] the attempted existing Category short-code change is blocked;
- [ ] existing Category data remains unchanged.

Result: PASS / FAIL / NOT RUN

## F — Missing rows never delete

Using a fresh Update workbook copy:

1. remove one existing Product row;
2. remove one existing OptionGroup row;
3. remove one existing Option row;
4. keep unrelated explicit edits in other rows so the import has a real commit.

Accept only if:

- [ ] preview contains no Delete operation;
- [ ] preview/operator text clearly states omitted rows are not deleted;
- [ ] after Confirm, all three omitted existing records still exist unchanged;
- [ ] explicit unrelated edits still commit normally.

Result: PASS / FAIL / NOT RUN

## G — Technical-ID / relationship corruption fail-safe

On disposable workbook copies, deliberately break one condition at a time. It is acceptable to use advanced Excel visibility controls solely for this negative test.

Cases:

- malformed technical entity ID;
- duplicate existing ID;
- replace a Product/Group/Option ID with another syntactically valid current ID;
- break an existing parent binding;
- create an ambiguous/missing new child-parent reference.

Accept each only if:

- [ ] preview/import rejects it with blocking Error;
- [ ] error identifies the relevant sheet/row/field well enough to repair;
- [ ] importer never guesses another record/parent;
- [ ] database remains unchanged.

Result: PASS / FAIL / NOT RUN

## H — Validation and all-or-nothing behavior

Prepare one workbook containing several valid changes plus at least one invalid condition such as:

- negative Product price;
- invalid VAT;
- duplicate Product code;
- invalid SINGLE/MULTI/min/max structure;
- required group with no usable active option;
- duplicate/conflicting Category name or Category short code;
- attempted existing Category short-code change;
- contradictory short codes across Product rows for one new Category.

Accept only if:

- [ ] all blocking Errors are visible/actionable;
- [ ] Confirm is unavailable/rejected while any Error remains;
- [ ] none of the otherwise valid rows partially applies;
- [ ] after fixing the Error(s), one Confirm applies the whole intended batch;
- [ ] restart/re-export shows complete committed state.

Result: PASS / FAIL / NOT RUN

## I — Read-only/authority behavior

On a non-authoritative/read-only device using the existing M07 authority model:

- [ ] Catalogue export remains available as a read operation;
- [ ] export itself does not alter authority/recovery state;
- [ ] import parsing/preview, if UI allows it, performs no write;
- [ ] import Confirm cannot perform a Catalogue write;
- [ ] UI clearly reflects read-only authority state;
- [ ] there is no bypass through file/dialog action.

Then transfer/acquire authority through the already-approved M07 workflow, obtain a fresh preview of the same valid workbook, and verify:

- [ ] authorized Confirm now succeeds;
- [ ] no new M10-specific authority protocol exists.

Result: PASS / FAIL / NOT RUN

## J — Historical order independence

1. Before importing, create/commit an ordinary order using a Product with options.
2. Record its saved/reprinted Product code/name/category/price/option details and total.
3. Through M10 change current Catalogue code/name/category/price/options for that Product, and where appropriate use another Category whose current short code differs.
4. Reopen/reprint the historical order.

Accept only if:

- [ ] historical order remains readable;
- [ ] its snapshots remain exactly the original sale-time business values;
- [ ] current Category name/short-code changes in the live Catalogue do not retroactively reinterpret the historical snapshot;
- [ ] current Catalogue changes are not retroactively joined into history;
- [ ] historical total/tax/payment interpretation is unchanged.

Result: PASS / FAIL / NOT RUN

## K — FR / zh-CN and layout/operator flow

Exercise Export, Import, preview, Errors and confirmation in both French and Simplified Chinese.

Accept only if:

- [ ] action labels/messages are understandable in both languages;
- [ ] switching language does not alter parsed counts/business values, Category short-code meaning or selected file/import state unexpectedly;
- [ ] preview remains usable at normal/default window size;
- [ ] preview remains usable maximized/resized;
- [ ] buttons are visible/content-sized and issue text scrolls/wraps appropriately;
- [ ] Cancel/close does not persist transient preview changes.

Result: PASS / FAIL / NOT RUN

## L — Restart and final real round trip

After the accepted successful import set:

1. close the application normally;
2. restart;
3. inspect current Catalogue/Caisse behavior;
4. export again;
5. open the new `.xlsx` in Excel.

Accept only if:

- [ ] committed Catalogue persists;
- [ ] all intended identities/relationships/state remain correct;
- [ ] Category names/short codes remain correct and export consistently;
- [ ] new export opens cleanly;
- [ ] export matches the final current Catalogue;
- [ ] there are no duplicate/phantom rows or missing omitted-from-prior-import records.

Result: PASS / FAIL / NOT RUN

## Final owner disposition

Record only after A–L applicable checks are complete.

- Owner acceptance result: **NOT YET EXECUTED / PASSED / FAILED**
- Candidate source head:
- Candidate EXE SHA-256:
- Candidate ZIP SHA-256:
- Exact-head CI:
- Full Release tests/build:
- Owner notes / defects:

A PASSED owner acceptance does **not** itself authorize merge or M11. Merge remains a separate explicit project-owner action.
