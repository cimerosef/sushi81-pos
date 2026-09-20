# M10 — Catalogue `.xlsx` import/export — final owner manual acceptance

**Status:** OWNER A–L FUNCTIONAL ACCEPTANCE PASS; FINAL-DELIVERY USABILITY PASS; CLOSURE-READY PENDING CONTROLLER RECORD

**Prepared/finalized:** 2026-09-17; owner A–L and final-delivery usability evidence recorded 2026-09-19/20; documentation closure recorded 2026-09-20
**Milestone:** M10  
**Owner result:** A–L functional acceptance PASS. Scenario I is accepted by prior/manual evidence reuse rather than a fresh computer-B replay. Final-delivery Excel bulk-paste ergonomics and responsive Preview layout are also PASS; no further owner manual testing is required for M10.
**Implementation authorization:** WP1–WP5, owner A–L acceptance and polish-25 are controller-recorded. The closure-26 handoff is documentation/governance only on unmerged PR #22; no owner result authorizes merge or M11+
**Candidate source head:** `34e61c67785aa6c8c0ca84a545e31de30b17ac39`
**Candidate executable / ZIP:** `artifacts/m10-win-x64-final-polish-p25/Sushi81.Pos.Desktop.exe` and `artifacts/m10-win-x64-final-polish-p25.zip`; sizes and SHA-256 values are recorded below and in the matching `CODEX_DONE`

> This checklist records the project owner's completed A–L acceptance and the targeted polish-25 retest. The owner evidence is durable in PR #22 comment `5750016695` and Issue #4 closure handoff `5750020351`; Codex did not substitute automated evidence for owner acceptance. Historical candidate failures and their repairs remain preserved as evidence. M10 is closure-ready, but final controller closure and separate explicit merge approval remain outstanding.

## Accepted candidate and evidence record

- PR/branch: #22 / `codex/m10-catalogue-xlsx-authorized` (OPEN / unmerged);
- source head: `34e61c67785aa6c8c0ca84a545e31de30b17ac39`;
- exact-head CI run `35512075546`, job `106081601962`: success;
- focused workbook protection/paste evidence: 20/20 passed;
- focused STA/WPF Preview render evidence: 1/1 passed across 640×480, 940×700 and 1280×900 in fr-FR and zh-CN;
- full Release solution: 787/787 passed; Release build: 0 warnings / 0 errors;
- EXE: `artifacts/m10-win-x64-final-polish-p25/Sushi81.Pos.Desktop.exe`, 162,816 bytes, SHA-256 `FD49066E245837F8664C76B48D8978405E5A2457BC859FB6C8C734B456192A82`;
- ZIP: `artifacts/m10-win-x64-final-polish-p25.zip`, 69,962,650 bytes, SHA-256 `A53C639C84709274F56019F4B11FCE3AE58F10258174614FE7030912A3676AA2`;
- forbidden-data scan: no database, workbook, CSV, business-data, credential, token or secret files; artifacts remain ignored/untracked.

## 0. Preconditions

Before execution record:

- [x] implementation PR number and exact candidate source head;
- [x] exact-head CI result;
- [x] full Release test/build result;
- [x] candidate EXE path, size and SHA-256;
- [x] candidate ZIP path, size and SHA-256;
- [x] current authority state/device role;
- [x] test database is controlled/synthetic or an owner-approved disposable copy;
- [x] Microsoft Excel or the intended real normal `.xlsx` operator application is available.

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

- [x] Excel opens the file normally with no repair/corruption warning;
- [x] visible logical sheets are exactly `Products`, `OptionGroups`, `Options`;
- [x] no operator-facing `Categories` sheet appears;
- [x] `Products` visibly exposes Category name and Category short code business columns;
- [x] every Product row shows the current Category short code accurately, blank when that Category has none;
- [x] ordinary business columns are understandable and practically editable;
- [x] technical Product/OptionGroup/Option identity is not exposed as normal editable business data;
- [x] `category_id` is not presented as an operator field;
- [x] ordinary operator actions do not accidentally overwrite protected technical bindings;
- [x] workbook remains usable for sorting/filtering/adding rows as designed;
- [x] current business values match the application exactly.

Notes:

- Result: PASS
- Evidence/screenshots:

## B — No-op export/re-import round trip

1. Save a fresh exported workbook without business edits.
2. Import it in normal Update mode.
3. Inspect preview before Confirm.

Accept only if:

- [x] preview reports no unintended Create/Modify/Activate/Deactivate operation;
- [x] Category short-code consistency fields do not produce false changes;
- [x] omitted/delete count does not exist;
- [x] preview clearly states no database change has occurred yet;
- [x] canceling leaves Catalogue unchanged;
- [x] if a no-op Confirm path exists, it causes no unwanted durable changes or identity/order drift.

Afterward re-export and compare the business catalogue.

- [x] Product/Group/Option identities and ordering remain stable;
- [x] Category names and short codes round-trip unchanged;
- [x] no duplicate Category/Product was created.

Result: PASS

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

- [x] blank existing Category short-code cell preserves the current value;
- [x] same normalized existing short code is accepted as consistency data and does not create a Category mutation;
- [x] a different non-blank short code for an existing Category is a blocking Error;
- [x] adding a short code to an existing uncoded Category through the workbook is a blocking Error;
- [x] the error clearly tells the operator that existing Category short-code changes belong in the in-app Category manager/re-export workflow;
- [x] preview identifies exactly the intended Product/Group/Option modifications/state changes;
- [x] no unrelated record is included;
- [x] after Confirm, Product code change preserved the same internal Product identity;
- [x] changed records persist after app restart;
- [x] re-export shows intended final values only.

Result: PASS

## D — New Product / OptionGroup / Option rows in Update mode

1. In an exported workbook add:
   - one new Product;
   - one new OptionGroup for it;
   - multiple new Options;
   - one new valid Category name with a valid optional Category short code.
2. Add another Product row referencing that same new Category and use either the same short code or blank so the Category meaning remains consistent.
3. Use only the normal documented workbook parent-reference workflow; do not manually type database IDs.

Accept only if:

- [x] parent relationships are understandable to the operator;
- [x] preview shows creates, not accidental updates;
- [x] exactly one new Category is proposed for the repeated normalized Category name;
- [x] its intended short code is shown/understandable in preview;
- [x] Confirm creates exactly one Category and one intended Product/Group/Option hierarchy set;
- [x] Caisse can find/use the new active Product according to current Category short-code navigation rules;
- [x] option selection behavior is correct;
- [x] restart/re-export preserves hierarchy, IDs and the new Category short code.

Negative subcase:

4. On a disposable copy, make two Product rows for the same new Category contain different non-blank short codes.

- [x] import blocks with a row-addressable Category short-code conflict;
- [x] no partial Category/Product creation occurs.

Result: PASS

## E — Explicit Add-only / first catalogue initialization

Use a controlled empty catalogue database/application profile.

1. Obtain/create a valid no-existing-ID workbook/template.
2. Add at least two Products, one new Category name with optional short code, and a Product with options.
3. Select **Add-only** explicitly and preview.

Accept only if:

- [x] preview clearly identifies Add-only/create-only mode;
- [x] complete first catalogue can be created without pre-existing Product IDs or Category IDs;
- [x] new Category short code is created when provided and valid;
- [x] all created records work after restart/re-export;
- [x] re-export repeats the created Category short code on Product rows.

Then on a non-empty catalogue:

4. Prepare an Add-only row using an already-existing Product code.

- [x] import is blocked as an Error;
- [x] it does not silently update the existing Product;
- [x] zero partial changes occur.

5. Prepare an Add-only Product assigned by name to an existing Category and attempt to change that existing Category's short code in the row.

- [x] Category resolution itself is allowed;
- [x] the attempted existing Category short-code change is blocked;
- [x] existing Category data remains unchanged.

Result: PASS

## F — Missing rows never delete

Using a fresh Update workbook copy:

1. remove one existing Product row;
2. remove one existing OptionGroup row;
3. remove one existing Option row;
4. keep unrelated explicit edits in other rows so the import has a real commit.

Accept only if:

- [x] preview contains no Delete operation;
- [x] preview/operator text clearly states omitted rows are not deleted;
- [x] after Confirm, all three omitted existing records still exist unchanged;
- [x] explicit unrelated edits still commit normally.

Result: PASS

## G — Technical-ID / relationship corruption fail-safe

On disposable workbook copies, deliberately break one condition at a time. It is acceptable to use advanced Excel visibility controls solely for this negative test.

Cases:

- malformed technical entity ID;
- duplicate existing ID;
- replace a Product/Group/Option ID with another syntactically valid current ID;
- break an existing parent binding;
- create an ambiguous/missing new child-parent reference.

Accept each only if:

- [x] preview/import rejects it with blocking Error;
- [x] error identifies the relevant sheet/row/field well enough to repair;
- [x] importer never guesses another record/parent;
- [x] database remains unchanged.

Result: PASS

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

- [x] all blocking Errors are visible/actionable;
- [x] Confirm is unavailable/rejected while any Error remains;
- [x] none of the otherwise valid rows partially applies;
- [x] after fixing the Error(s), one Confirm applies the whole intended batch;
- [x] restart/re-export shows complete committed state.

Result: PASS

## I — Read-only/authority behavior

On a non-authoritative/read-only device using the existing M07 authority model:

- [x] Catalogue export remains available as a read operation;
- [x] export itself does not alter authority/recovery state;
- [x] import parsing/preview, if UI allows it, performs no write;
- [x] import Confirm cannot perform a Catalogue write;
- [x] UI clearly reflects read-only authority state;
- [x] there is no bypass through file/dialog action.

Then transfer/acquire authority through the already-approved M07 workflow, obtain a fresh preview of the same valid workbook, and verify:

- [x] authorized Confirm now succeeds;
- [x] no new M10-specific authority protocol exists.

Result: PASS

## J — Historical order independence

1. Before importing, create/commit an ordinary order using a Product with options.
2. Record its saved/reprinted Product code/name/category/price/option details and total.
3. Through M10 change current Catalogue code/name/category/price/options for that Product, and where appropriate use another Category whose current short code differs.
4. Reopen/reprint the historical order.

Accept only if:

- [x] historical order remains readable;
- [x] its snapshots remain exactly the original sale-time business values;
- [x] current Category name/short-code changes in the live Catalogue do not retroactively reinterpret the historical snapshot;
- [x] current Catalogue changes are not retroactively joined into history;
- [x] historical total/tax/payment interpretation is unchanged.

Result: PASS

## K — FR / zh-CN and layout/operator flow

Exercise Export, Import, preview, Errors and confirmation in both French and Simplified Chinese.

Accept only if:

- [x] action labels/messages are understandable in both languages;
- [x] switching language does not alter parsed counts/business values, Category short-code meaning or selected file/import state unexpectedly;
- [x] preview remains usable at normal/default window size;
- [x] preview remains usable maximized/resized;
- [x] buttons are visible/content-sized and issue text scrolls/wraps appropriately;
- [x] Cancel/close does not persist transient preview changes.

Result: PASS

## L — Restart and final real round trip

After the accepted successful import set:

1. close the application normally;
2. restart;
3. inspect current Catalogue/Caisse behavior;
4. export again;
5. open the new `.xlsx` in Excel.

Accept only if:

- [x] committed Catalogue persists;
- [x] all intended identities/relationships/state remain correct;
- [x] Category names/short codes remain correct and export consistently;
- [x] new export opens cleanly;
- [x] export matches the final current Catalogue;
- [x] there are no duplicate/phantom rows or missing omitted-from-prior-import records.

Result: PASS

## Final owner disposition

Record only after A–L applicable checks are complete.

- Owner acceptance result: **PASSED — closure-ready pending controller record**
- Candidate source head: `34e61c67785aa6c8c0ca84a545e31de30b17ac39`
- Candidate EXE SHA-256: `FD49066E245837F8664C76B48D8978405E5A2457BC859FB6C8C734B456192A82`
- Candidate ZIP SHA-256: `A53C639C84709274F56019F4B11FCE3AE58F10258174614FE7030912A3676AA2`
- Exact-head CI: run `35512075546`, job `106081601962` — success
- Full Release tests/build: 787/787 passed; 0 warnings / 0 errors
- Owner notes / defects: A–L and final-delivery usability PASS; no further owner manual testing required; final controller closure and explicit merge approval remain separate.

A PASSED owner acceptance does **not** itself authorize merge or M11. Merge remains a separate explicit project-owner action.
