# M10 — Catalogue `.xlsx` import/export — implementation contract

**Status:** **WP1/WP2 CONTROLLER-ACCEPTED — WP3 ACTIVE UNDER ISSUE #4**
**Prepared/finalized:** 2026-09-17  
**Milestone:** M10  
**Primary acceptance ownership:** AC-CAT-008 through AC-CAT-011; catalogue portion of AC-ARCH-005  
**Inherited safety:** AC-CAT-012, AC-STO-010, AC-NFR-002, AC-NFR-004 and current Catalogue invariants  
**Category short-code decision:** Approved — `docs/decisions/m10-category-short-code-workbook-semantics.md`  
**M11+ scope:** explicitly excluded

> The frozen implementation contract is owner-authorized for the single active WP3 handoff recorded in PR #22 while Issue #4 is OPEN. WP1 and WP2 are controller-accepted; WP3 adds only the atomic Application/SQLite commit boundary, authority/recovery integration and regression evidence. This status does not authorize WP4, WP5, merge, M11, M12 or M13.

### WP3 implemented mechanics (current-state contract)

- `CatalogueImportService.CommitAsync` requires an error-free immutable preview, enters the centralized write-authority scope, calls the import store once, and invokes the durable-change notifier once only after a changed commit has returned successfully.
- `SqliteCatalogueStore.CommitAsync` reads the complete Catalogue baseline on the same SQLite transaction, compares the canonical order-independent baseline fingerprint before the first write, revalidates references/scalars/resulting hierarchy, allocates new opaque IDs only after validation, and applies one explicit INSERT/UPDATE batch.
- The commit boundary uses the Application-owned strict entity/action/reference/scalar validator; production commits consume only the real `CatalogueImportResult.PreviewBaseline`, while malformed plans fail closed before allocation or SQL writes.
- Changed Product codes and changed Group/Option display orders use collision-safe temporary staging only for affected rows; staging orders are outside the business `Int32` range. SQLite BUSY/LOCKED/BUSY_SNAPSHOT failures map to the stable blocking `concurrent-write-conflict` issue.
- Import persistence never interprets omitted workbook rows as deletion and never calls the aggregate save helpers that delete omitted children. Existing child parent bindings remain immutable; planned references resolve through local/entity maps only.
- The transaction runner remains the single revision/commit boundary. Rollback, stale-baseline, constraint, injected mid-batch and commit-failure paths return blocking issues without durable Catalogue mutation or notifier calls. Historical order tables are not touched.

## 1. Objective

Implement deterministic, safe, operator-usable Catalogue `.xlsx` export/import using ClosedXML and the existing Catalogue/authority/recovery architecture.

Required outcome:

- complete current Product / OptionGroup / Option export;
- three operator-facing logical worksheets only: `Products`, `OptionGroups`, `Options`;
- protected technical identity/relationship data;
- normal ID-preserving update/re-import;
- explicit create-only Add-only mode including first initialization;
- complete validation and preview;
- blocking Errors / non-blocking Warnings;
- one atomic confirmed commit;
- row absence never implies deletion;
- current Catalogue changes never rewrite historical orders;
- authoritative-device write enforcement;
- FR / zh-CN operator workflow;
- real Windows + Excel owner acceptance.

## 2. Non-negotiable business/data semantics

Implementation must preserve all of the following:

- no operator-facing `Categories` worksheet;
- Product / OptionGroup / Option opaque IDs are technical matching data, not business identifiers;
- Category assignment is by normalized Category name; `category_id` is not operator input;
- `Products` visibly carries Category name **and Category short code** as business data;
- existing Category short code is preserve/consistency data only: blank preserves, same normalized value is valid, different non-blank value blocks;
- import never clears/replaces/globally changes an existing Category short code;
- new Category created by import may receive one optional consistent short code under existing validation;
- repeated Product rows for one Category must not contradict that shared short-code meaning;
- valid existing entity ID updates that exact entity;
- changing Product code while Product ID is unchanged updates the same Product;
- blank entity ID creates;
- Add-only is explicit create-only and never silently turns a row into update by code/name;
- malformed/unknown/duplicated/misbound IDs or relationships are blocking Errors;
- Product Category-name change means Product reassignment, not global Category rename;
- row absence never means deletion;
- no permanent-delete column/flag/action exists in M10 workbook import;
- Product/Option activation changes occur only through explicit visible state fields;
- OptionGroup has no active flag;
- any Error blocks the entire commit;
- preview occurs before any business write;
- confirmed commit is all-or-nothing;
- no permanent import-history business entity is added.

The detailed Category contract is controlled by `docs/decisions/m10-category-short-code-workbook-semantics.md` and `docs/acceptance-criteria-amendment-m10-category-short-code-workbook.md`.

## 3. Architecture boundary

### 3.1 Application-owned contracts

Use a dedicated M10 boundary. Exact class names may vary, but responsibilities must remain equivalent:

- `ICatalogueWorkbookGateway`
  - export an application-owned immutable Catalogue workbook model to `.xlsx`;
  - parse `.xlsx` into application-owned raw workbook data;
  - no business writes.
- `CatalogueImportPlanner`
  - pure/deterministic normalization, identity/relationship/Category validation, complete-current-state overlay, diff and preview;
  - no SQLite, ClosedXML or WPF dependency.
- `ICatalogueImportStore`
  - read the import baseline required for planning/commit;
  - atomically commit one accepted plan;
  - no workbook parsing.
- `CatalogueWorkbookService`
  - orchestrate export, preview and authorized commit;
  - use centralized authority guard only for commit;
  - notify durable change only after successful business commit.

ClosedXML types must not appear in Domain/Application public contracts.

### 3.2 Infrastructure

ClosedXML stays in Infrastructure behind the workbook gateway.

SQLite import persistence stays in Infrastructure and reuses:

- `SqliteConnectionFactory`;
- `SqliteTransactionRunner`;
- current Catalogue normalization/storage conventions;
- current ID generator/business clock;
- current FK/unique constraints;
- business revision advancement.

### 3.3 Desktop

WPF owns only:

- file open/save dialogs;
- Update/Add-only mode selection;
- preview/dialog state;
- localization/presentation;
- confirmation/cancel;
- post-success Catalogue refresh.

No SQL or ClosedXML parsing belongs in presentation logic.

## 4. Workbook contract

### 4.1 Logical sheets

Exactly three operator-facing logical worksheets:

1. `Products`
2. `OptionGroups`
3. `Options`

Sheet names are invariant contract names. Visible labels may be localized, but parser identity must rely on deterministic invariant schema metadata, not translated-string guessing.

### 4.2 Product fields

At minimum:

- Product code;
- Product name;
- Category name;
- Category short code;
- Price TTC;
- VAT rate;
- Active;
- Discount eligible;
- Options enabled.

Technical Product identity/binding helpers remain hidden. Their row-local cells may
be unlocked so protected Excel sorting can move the complete row; the VeryHidden
manifest remains authoritative and import revalidates every helper.

### 4.3 OptionGroup fields

At minimum:

- understandable parent Product reference;
- group name;
- SINGLE/MULTI;
- required;
- min selections;
- max selections;
- display order.

Technical OptionGroup ID/existing parent binding helpers remain hidden and are
validated against the VeryHidden manifest after any workbook edit.

### 4.4 Option fields

At minimum:

- understandable parent OptionGroup reference;
- option name;
- TTC price adjustment;
- Active;
- display order.

Technical Option ID/existing parent binding helpers remain hidden and are validated
against the VeryHidden manifest after any workbook edit.

### 4.5 Technical metadata

Use one non-operator-facing **VeryHidden** technical sheet such as `__Sushi81Meta` containing only safety metadata, including:

- workbook contract version;
- export instance ID;
- invariant field/column descriptors;
- exported row-binding keys;
- existing entity ID / existing parent binding manifest;
- export-baseline values/fingerprint needed for tamper/stale-workbook detection.

It must not contain a duplicate hidden business Catalogue or deletion list.

WP1 uses contract version `M10-CATALOGUE-1`. `CatalogueWorkbookSchema` is the
Application-owned invariant descriptor source for the three worksheets; its metadata
rows record worksheet, field key, column index, business visibility/editability and
entity/relationship role. The manifest records the original row key, entity ID, parent
row key, worksheet/row location and a canonical typed baseline fingerprint. The visible
worksheet filter range includes hidden technical columns so ordinary sorting keeps each
binding with its business row. Business cells and row-local hidden helper cells are
unlocked where required by protected Excel sorting; helper columns remain hidden and
worksheet protection does not grant FormatColumns/unhide permission. A blank next-row
template remains editable, while the VeryHidden manifest is the authoritative identity
and relationship record. `CatalogueWorkbookService` requires the application-owned
`ICatalogueWorkbookSnapshotQueries` dependency; production SQLite export materializes
the complete model from one read connection/transaction and has no multi-read fallback.

### 4.6 Protection

- technical columns hidden;
- row-local helper cells participating in a sortable range may be unlocked for Excel
  compatibility, but remain hidden and are never trusted without manifest validation;
- worksheet protection does not allow normal column formatting/unhide;
- intended business cells editable;
- protection must preserve practical normal Excel editing/inserting/sorting/filtering where possible;
- metadata sheet VeryHidden/protected;
- no macro/VBA;
- protection password is not treated as security.

Importer revalidates every safety assumption even if the operator removes workbook protection externally.

## 5. Identity, Category and relationship contract

### 5.0 WP2 raw/parser and local-key contract

The WP2 parser exposes only library-neutral row records with worksheet name, Excel
row, invariant field values and opaque helper text; ClosedXML objects never leave
Infrastructure. `__Sushi81Meta` descriptors and the manifest are authoritative.
Existing rows carry the exported `product:`, `group:` or `option:` row key plus the
matching GUID and parent key. New rows carry blank identity/helper cells. The
manifest's original row number is diagnostic only, so sorting and insertion do not
change identity; every helper and parent GUID is revalidated against the manifest.

The pure planner assigns deterministic preview-only keys of the form
`<entity>:new:<excel-row>:<content-hash>` to new rows. New OptionGroups resolve a
parent by exact normalized Product Code against the complete resulting Product
candidate set. New Options resolve Product by that same exact code and then resolve
an OptionGroup by exact normalized name within that Product. Zero or multiple
candidates are blocking Errors; no fuzzy or first-match resolution is permitted.
These local keys are not durable IDs and are the only new-record references passed
to the future WP3 commit plan.

WP2 conformance adds a self-contained immutable plan reference shape. Every plan
operation carries an entity reference with the existing opaque Guid plus stable
local key, or a new-record local key with no Guid. Product operations carry an
explicit Category reference (existing Category Guid/local key or a planned-new
Category local key); OptionGroup operations carry the resolved parent Product
reference; Option operations carry the resolved parent OptionGroup reference.
Create operation `EntityId` is always null, and planned-new Categories are
represented by `CatalogueImportPlannedCategory` (local key, display name and
optional display short code), never by a preview-generated Guid. WP3 therefore
does not need to re-resolve names or codes. The plan operation set remains
Create/Modify/Activate/Deactivate only and has no Delete operation.

The manifest also carries an optional `ParentDisplayFingerprint` column. For an
OptionGroup it fingerprints the exported parent Product Code + Product Name; for
an Option it fingerprints the exported parent Product Code + Product Name +
OptionGroup Name; Product entries leave it blank. The value uses the same typed,
length-prefixed, culture-stable Application fingerprint encoding as entity
baselines. During preview, descriptive parent cells are accepted only when they
match the current/planned parent or this original-export fingerprint. Hidden
identity and parent helpers remain authoritative and are still checked for
tampering.

### 5.1 Existing Product/Group/Option records

An existing row is updateable only when:

- entity ID is structurally valid;
- row binding matches the exported manifest;
- the current live entity exists;
- entity type matches;
- protected existing parent relationship matches;
- workbook does not duplicate the existing ID.

Any failure is Error.

### 5.2 New records

Blank entity ID = Create candidate. New opaque IDs are allocated only in the successful commit path, not during preview.

### 5.3 Existing child parent relationships

M10 must not silently re-parent an existing OptionGroup to another Product or an existing Option to another OptionGroup through descriptive-cell edits. Protected existing parent identity is validated and retained.

### 5.4 New child parent relationship

New child rows need a workbook-local deterministic helper that:

- works with existing and new parents;
- resolves to exactly one candidate parent;
- survives ordinary row insertion/sorting;
- blocks missing/ambiguous parent;
- is not a new SQLite business identity.

No fuzzy Product/Group name matching and no first-match-wins behavior.

### 5.5 Category name and short code

Category name remains the workbook Category-resolution key.

For an existing Category:

- blank short-code cell = preserve current value;
- same normalized non-blank value = valid consistency data;
- different non-blank value = Error;
- existing blank current short code + workbook non-blank value = Error;
- workbook import never clears or globally changes it.

For a new Category:

- short code optional;
- blank plus one repeated non-blank value is one consistent proposal;
- different non-blank values across Product rows for the same normalized Category = Error;
- normal short-code uniqueness/length/normalization rules apply.

Existing Category short-code changes remain in the in-application Category manager.

## 6. Export behavior

Export is a pure read operation.

Required sequence:

1. read a consistent current Catalogue snapshot;
2. map to immutable application workbook DTOs;
3. create `.xlsx` with ClosedXML;
4. include all current Products, OptionGroups and Options, including inactive records;
5. repeat current Category name/short code on Product rows;
6. include existing technical IDs/parent bindings only in protected technical form;
7. include deterministic version/manifest/baseline metadata;
8. save to operator-selected path;
9. perform no SQLite mutation, write-authority acquisition, recovery notification or export-history write.

An empty Catalogue export still produces a valid usable template for first initialization.

Export is allowed on non-authoritative/read-only devices as a read of local state. Existing stale/read-only presentation remains controlling; export never grants authority.

## 7. Import workflow

### 7.1 Select mode/file

Operator explicitly selects Update or Add-only, then chooses the file. File-dialog cancel causes no state change.

### 7.2 Parse structure

Treat workbook as untrusted input. Validate before business planning:

- required logical sheets;
- supported contract/version/metadata;
- required business fields;
- supported value types;
- binding integrity;
- no duplicate technical bindings.

Corrupt/unreadable/non-XLSX input becomes a blocking Error with no business write.

### 7.3 Read current baseline

Read the complete current Catalogue needed for identity/uniqueness/Category/candidate validation.

For exported update workbooks compare current live values to export baseline so a stale workbook cannot blindly overwrite a newer live edit.

### 7.4 Overlay/plan

Overlay only explicit workbook rows onto the complete current Catalogue.

Missing rows generate **no operation**.

Build complete resulting aggregates before final validation.

### 7.5 Preview

Produce immutable preview + commit plan. No business write has occurred.

### 7.6 Confirm

Confirm available only when:

- Errors = 0;
- mode/plan is valid;
- authority state permits writes.

Cancel means no write.

### 7.7 Commit

Application service:

1. passes `IWriteAuthorityGuard`;
2. submits immutable plan + concurrency baseline;
3. store opens one SQLite transaction;
4. revalidates concurrency/identity/uniqueness/Category/parents before first business write;
5. allocates new IDs;
6. creates new Categories where planned;
7. applies Product/Group/Option creates/updates/state changes;
8. performs no Delete;
9. commits once;
10. notifies `IDurableChangeNotifier` once using existing non-cancellable post-commit semantics;
11. WPF refreshes Catalogue.

Any failure before commit rolls back the entire batch.

## 8. Update mode

- valid protected existing ID -> update exact record;
- blank ID -> create;
- Product code is editable but never an existing-record matcher;
- changing Product code with same ID preserves identity;
- current entity omitted from workbook remains untouched;
- current child omitted from workbook remains untouched;
- duplicate/malformed/misbound ID -> Error;
- stale concurrent conflict -> Error, not last-writer overwrite;
- Category resolution/short-code rules follow section 5.5.

An update workbook missing required protected manifest data for existing IDs fails closed instead of trusting arbitrary GUIDs.

## 9. Add-only mode

- explicit mode, never inferred as overwrite permission;
- Product/OptionGroup/Option technical IDs must be blank/absent;
- every such entity row is create-only;
- no current Product/Group/Option update is legal;
- normalized Product-code collision is Error;
- no matching by Product name;
- Category name may resolve to existing Category because Category assignment is name-based;
- that resolution does not authorize short-code change;
- valid new Category with optional consistent short code may be created atomically;
- first initialization into empty Catalogue is supported;
- any nonblank entity technical ID in Add-only blocks rather than being ignored.

## 10. Validation/issue model

### Blocking Errors include at least

- missing/unreadable required sheet;
- unsupported/corrupt workbook metadata;
- missing required business field;
- invalid numeric/boolean/enum value;
- invalid Product price/VAT;
- invalid OptionGroup SINGLE/MULTI/required/min/max structure;
- required group with no valid active choice in resulting aggregate;
- duplicate normalized Product code;
- duplicate normalized Category name;
- invalid/duplicate/conflicting Category short code;
- attempted existing-Category short-code change;
- contradictory repeated Product-row Category short-code meaning;
- malformed/duplicate/unknown/misbound entity ID;
- technical binding inconsistent with manifest;
- missing/ambiguous/wrong parent;
- Add-only existing Product-code collision;
- stale live conflict on workbook-edited existing record;
- persistence constraint/conflict discovered during commit revalidation.

Problems carry worksheet, Excel row, field and stable issue code suitable for localized presentation.

### Warnings

Warnings are informational/non-blocking only. They never downgrade an Error or authorize unsafe matching. Do not invent arbitrary business thresholds merely to create warnings.

### No guessing

Ambiguous input = Error. Never select nearest code/name, first row, first parent or reconstructed technical ID.

## 11. Preview

At minimum show:

- mode;
- selected file;
- Product create / modify / activate / deactivate;
- OptionGroup create / modify;
- Option create / modify / activate / deactivate;
- new Category count;
- Errors / Warnings;
- row-addressable issue list;
- affected-row summary where practical;
- explicit statement: omitted rows are not deleted;
- explicit statement: preview has not changed the database.

Add-only preview explicitly states existing Product/Group/Option records will not be updated.

## 12. Atomic persistence

Do not loop existing per-command CatalogueService mutation methods because they own separate transactions and cannot satisfy whole-workbook atomicity.

Add one dedicated import-store commit method using one `SqliteTransactionRunner.ExecuteAsync` call.

Import plan contains only explicit create/update/state-change operations. There is intentionally **no Delete operation**.

Before first write inside the transaction revalidate the complete accepted plan against current database/baseline. Any injected intermediate failure must roll back all statements.

## 13. Authority/recovery

- export: read-only, no write authority;
- parse: no write authority;
- preview: no write authority;
- commit: mandatory centralized authority guard;
- Application boundary rejects non-authoritative/read-only, transfer/transition and RecoveryRequired states according to existing guard contract;
- WPF reflects the same state but is not the safety boundary;
- successful business commit -> one durable-change notification;
- failed/rejected/no-op import -> no durable-change notification;
- no M06/M07 protocol changes.

## 14. SQLite/schema/dependency constraints

### SQLite

No M10 schema migration is expected. Current SQLite already stores all M10 business data. Workbook version/manifest stays in the workbook.

If implementation proves a new durable field is genuinely required, stop as a controller blocker rather than adding a migration automatically.

### ClosedXML

M10 is expected to add ClosedXML because Approved architecture selects it and it is currently absent.

Requirements:

- one pinned tested version;
- isolated to Infrastructure/tests as appropriate;
- no Office Interop / Excel COM;
- no second XLSX library;
- package/release notes reviewed before pinning;
- self-contained win-x64 publish remains healthy.

Exact version is a technical implementation choice.

## 15. Historical independence

M10 writes current Catalogue/Category data only. It must never rewrite historical order item/adjustment/category/price/VAT/discount/tax/print interpretation.

Automated and owner evidence must create an order before import, mutate current Catalogue via M10 and prove the committed historical order/reprint remains unchanged.

## 16. Work packages after separate authorization

No package is executable yet.

### WP1 — contracts + ClosedXML export

- pin ClosedXML;
- application workbook DTO/interfaces;
- Infrastructure writer;
- three visible sheets + protected metadata;
- empty-Catalogue template;
- export read-only path;
- workbook/protection/round-trip fixture tests;
- no import commit.

### WP2 — parser + planner + preview

- raw workbook parser;
- Update/Add-only validation;
- identity/binding validation;
- Category name/short-code resolution;
- new-parent helper;
- complete-current-state overlay/no-delete semantics;
- Errors/Warnings/preview counts/row diagnostics;
- no business write.

### WP3 — atomic SQLite commit + authority/recovery

- dedicated import-store transaction boundary;
- concurrency/identity revalidation before writes;
- new-ID allocation at commit;
- no Delete;
- authority guard + one durable-change notify;
- rollback/failure injection/historical-independence integration tests.

### WP4 — WPF workflow

- Catalogue Export/Import actions;
- file dialogs;
- explicit mode;
- preview/issues;
- authority-aware Confirm;
- success refresh;
- Cancel/close safety;
- FR/zh-CN;
- STA/WPF lifecycle/layout tests.

### WP5 — hardening + owner candidate

- real `.xlsx` round-trip;
- tampered/corrupt/stale cases;
- M03/M04/M06/M07 regressions;
- architecture checks;
- full Release tests/build/diff/privacy audit;
- self-contained win-x64 candidate/hashes;
- exact-head CI;
- manual-acceptance handoff only after controller review.

Work-package completion never authorizes the next package automatically.

## 17. Required automated evidence summary

At minimum prove:

- deterministic workbook structure/protection/versioning;
- export/re-import identity preservation;
- Category short-code round-trip and approved existing/new semantics;
- same-ID Product code change;
- blank-ID create;
- explicit Add-only create-only behavior;
- technical-ID tamper rejection;
- invalid parent rejection;
- missing-row no-delete across Product/Group/Option;
- deterministic preview counts/issues;
- complete rollback on invalid row or injected failure;
- non-authoritative commit rejection below UI;
- one durable-change notification only after successful commit;
- historical-order independence;
- FR/zh-CN state integrity;
- no COM and no unrelated dependency/schema change.

## 18. Manual acceptance

Owner checklist: `docs/implementation/milestone-10-final-manual-acceptance.md`.

Codex may prepare the candidate/evidence but must not check owner boxes, claim real Excel ergonomics Passed, merge, or start M11.

## 19. Scope exclusions

M10 does not authorize:

- M11 `Gestion SUSHI 81` four-sheet order export;
- M11 eligibility/date/exported-at/correction business semantics;
- M12 annual archive;
- M13 installer/final handover;
- Category permanent deletion/import deletion;
- workbook VBA/macros/Office automation;
- cloud Excel service;
- implicit Product matching by name/code in Update mode;
- schema migration without a newly reviewed blocker;
- import-history table;
- M07 authority/recovery protocol changes.

## 20. Authorization gate

Preparation conditions 1–3 are now complete:

1. Category short-code workbook semantics: **Approved**;
2. decision/baseline/acceptance alignment: **prepared/committed on this preparation branch**;
3. readiness/current-state reconciliation: **preparation-complete**.

Before any implementation can execute, all remaining conditions must still occur:

4. project owner explicitly says **“批准 M10 implementation”**;
5. `milestone-10-authorization.md` changes from NOT AUTHORIZED to AUTHORIZED with the exact final preparation head;
6. dedicated implementation branch/PR/mailbox is established according to current governance;
7. exactly one complete executable M10 handoff is published;
8. Issue #4 is updated to that unique pointer and only then opened.

Until condition 4 is explicitly granted, this contract is documentation only.
