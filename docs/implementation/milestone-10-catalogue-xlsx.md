# M10 — Catalogue `.xlsx` import/export — implementation contract

**Status:** **DRAFT / PREPARATION ONLY — NOT AUTHORIZED**  
**Prepared:** 2026-09-17  
**Milestone:** M10  
**Primary acceptance ownership:** AC-CAT-008 through AC-CAT-011; catalogue portion of AC-ARCH-005  
**Inherited safety:** AC-CAT-012, AC-STO-010, AC-NFR-002, AC-NFR-004 and current Catalogue invariants  
**Open owner decision:** Category `short_code` workbook contract in `milestone-10-preparation-readiness.md`  
**M11+ scope:** explicitly excluded

> This file is a prepared implementation contract, not an executable handoff. Issue #4 must remain CLOSED and Codex must not modify production code until a separate project-owner M10 implementation authorization exists.

## 1. Objective

Implement deterministic, safe, operator-usable Catalogue `.xlsx` export and import using ClosedXML and the existing current Catalogue/authority/recovery architecture.

The feature must support:

- complete current Product / OptionGroup / Option export;
- safe normal update/re-import;
- explicit add-only import including first catalogue initialization;
- protected technical identity/relationship data;
- complete deterministic validation and preview;
- blocking Errors and non-blocking Warnings;
- one atomic confirmed commit;
- no implicit deletion from row absence;
- no historical-order rewrite;
- authoritative-device write enforcement;
- FR / zh-CN operator workflow;
- real Windows + Excel owner acceptance.

## 2. Non-negotiable business/data semantics

Implementation must preserve all of the following:

- visible logical worksheets are `Products`, `OptionGroups`, `Options`;
- Category has no separate operator worksheet;
- Product / OptionGroup / Option opaque IDs are technical matching data, not editable business identifiers;
- Category assignment is by normalized Category name and never exposes `category_id` as an operator input;
- valid existing entity ID updates that exact entity;
- changing Product code while Product ID is unchanged updates the same Product;
- blank entity ID creates;
- add-only is explicitly create-only and never silently converts a row into update by code/name;
- current Product-code conflicts and Category-name conflicts remain blocking under existing normalization;
- malformed/unknown/inconsistent IDs or relationships are Errors, never guessed;
- Category-name change in a Product row reassigns the Product; it is not a global Category rename;
- row absence never means deletion;
- no permanent-delete column/flag/action exists in M10 workbook import;
- Product/Option activation changes occur only through explicit visible state fields;
- OptionGroup has no active/inactive field unless a future separately approved specification changes the model;
- any Error blocks the whole commit;
- preview occurs before any business write;
- commit is all-or-nothing;
- current catalogue changes never rewrite historical order snapshots;
- no permanent import-history entity is added.

Category `short_code` behavior remains blocked on the one explicit owner decision in the readiness record. Implementation must not invent it.

## 3. Architecture boundary

### 3.1 Application-owned contract

Create a dedicated M10 application boundary rather than embedding ClosedXML or SQLite details in WPF.

Recommended conceptual contracts (exact class names may vary):

- `ICatalogueWorkbookGateway`
  - export an application-owned immutable catalogue workbook model to `.xlsx`;
  - parse `.xlsx` into an application-owned raw workbook model;
  - no business writes.
- `CatalogueImportPlanner`
  - pure/deterministic normalization, identity/relationship validation, candidate overlay, diff and preview production;
  - no SQLite/ClosedXML/WPF dependency.
- `ICatalogueImportStore`
  - read the current import baseline needed by the planner/commit;
  - atomically commit one already validated import plan;
  - no workbook parsing.
- `CatalogueWorkbookService`
  - orchestrate export, import preview and authorized commit;
  - acquire centralized write authority only for commit;
  - notify durable change only after successful business commit.

Do not make ClosedXML types part of Domain/Application public contracts.

### 3.2 Infrastructure

ClosedXML belongs in Infrastructure behind the workbook gateway.

SQLite import persistence belongs in Infrastructure and must reuse:

- `SqliteConnectionFactory`;
- `SqliteTransactionRunner`;
- existing Catalogue normalization/storage conventions;
- existing ID generator and business clock;
- foreign keys/unique constraints;
- business revision advancement.

### 3.3 Desktop

WPF owns only:

- file selection/save dialogs;
- mode selection;
- preview/dialog state;
- localization/presentation;
- confirmation/cancel;
- post-success Catalogue refresh.

No Excel parsing or SQL is allowed in code-behind/view-model presentation logic.

## 4. Workbook contract

### 4.1 Logical sheets

Three operator-visible logical worksheets only:

1. `Products`
2. `OptionGroups`
3. `Options`

Sheet names are invariant contract names. Visible business-column labels may be localized/presented for FR/zh-CN as implementation permits, but machine parsing must use deterministic invariant schema metadata rather than fragile translated-string guessing.

### 4.2 Product business fields

At minimum export/import the existing Product business model:

- Product code;
- Product name;
- Category name;
- Category short-code field only after owner semantics are approved;
- Price TTC;
- VAT rate;
- Active;
- Discount eligible;
- Options enabled.

Technical matching/binding data is hidden/locked.

### 4.3 OptionGroup business fields

At minimum:

- understandable parent Product reference;
- group name;
- selection mode SINGLE/MULTI;
- required flag;
- min selections;
- max selections;
- display order/ordering semantics.

Technical OptionGroup ID and existing parent identity are hidden/locked.

### 4.4 Option business fields

At minimum:

- understandable parent OptionGroup reference;
- option name;
- TTC price adjustment;
- active flag;
- display order/ordering semantics.

Technical Option ID and existing parent identity are hidden/locked.

### 4.5 Hidden technical metadata

Use one VeryHidden technical workbook sheet, with an invariant internal name such as `__Sushi81Meta`, as a non-operator logical helper.

It should carry only workbook safety metadata such as:

- contract version;
- export instance identifier;
- invariant field/column mapping;
- exported row-binding key;
- existing entity ID and parent-binding manifest;
- export-baseline values/fingerprint needed to distinguish operator edits from stale live changes.

It must not contain a duplicate business catalogue or a hidden deletion list.

The three required logical worksheets remain the only operator-facing sheets.

### 4.6 Protection

- technical columns hidden;
- technical cells locked;
- business cells intended for maintenance unlocked;
- sheet protection configured to preserve ordinary edit/sort/filter/insert-row workflow where practical;
- metadata sheet VeryHidden/protected;
- no macro/VBA dependency;
- no password/protection scheme is treated as a security boundary.

All safety assumptions are revalidated on import even if workbook protection has been removed.

## 5. Identity and relationship contract

### 5.1 Existing records

An existing row is updateable only when:

- its technical entity ID is structurally valid;
- its row binding matches the exported manifest;
- the current live entity with that ID exists;
- entity type matches;
- protected existing parent relationship matches;
- the workbook does not duplicate that existing ID.

Failure of any point is a blocking Error.

### 5.2 New records

A blank entity ID is a Create candidate.

New records receive opaque database IDs only during the successful commit path. Preview does not allocate/persist final IDs.

### 5.3 Existing parent relationship

M10 must not silently re-parent an existing OptionGroup to another Product or an existing Option to another OptionGroup through edited descriptive text. Existing technical parent binding is validated and retained.

### 5.4 New parent relationship

The workbook must offer an understandable deterministic relationship mechanism for newly added child rows without exposing database IDs.

The selected helper design must:

- work for existing and newly created parent rows;
- resolve to exactly one candidate parent;
- survive ordinary Excel row insertion/sorting;
- produce a blocking Error for missing/ambiguous parent;
- be workbook-scoped only, not a new persisted business identity.

Do not use fuzzy Product/Group name matching or “first match wins”.

## 6. Export behavior

Export is read-only.

Required sequence:

1. read a consistent current Catalogue snapshot;
2. map it to immutable application workbook DTOs;
3. create `.xlsx` with ClosedXML;
4. include all current Products, OptionGroups and Options, including inactive records;
5. include technical update IDs/parent bindings only in protected form;
6. include deterministic metadata/version/baseline bindings;
7. save to operator-selected path;
8. perform no SQLite mutation, authority acquisition, recovery notification or export-history write.

An empty catalogue export must still produce a valid usable template for first initialization.

Export on a non-authoritative/read-only device is permitted as a read of its local state and does not grant authority. The existing global stale/read-only presentation remains controlling.

## 7. Import workflow

### 7.1 Stage 1 — select mode and file

Operator explicitly chooses:

- normal Update import; or
- Add-only import.

Canceling file selection ends the workflow with no state change.

### 7.2 Stage 2 — parse workbook

ClosedXML reads untrusted workbook input into the application raw workbook model.

Parsing must verify structural requirements before business planning:

- required logical worksheets;
- supported contract/version where metadata exists;
- required fields/columns;
- supported value types;
- hidden identity/binding integrity for update workbooks;
- no duplicate technical row/entity bindings.

A corrupt/non-XLSX/unreadable workbook becomes a blocking Error, never an exception-driven partial business action.

### 7.3 Stage 3 — read current catalogue baseline

Read the complete current catalogue needed for deterministic identity, uniqueness and candidate validation.

For exported update workbooks, compare current live values to export-baseline manifest data so stale operator edits cannot blindly overwrite newer current changes.

### 7.4 Stage 4 — plan/overlay

Overlay only explicit workbook rows onto the complete current catalogue.

Missing rows generate **no operation**.

Build the complete resulting candidate aggregates before final validation so Product/OptionGroup/Option structural rules are checked against what would exist after commit, not against an incomplete worksheet fragment.

### 7.5 Stage 5 — preview

Produce immutable preview + commit plan.

No business write has occurred.

### 7.6 Stage 6 — confirm

Confirm is available only when:

- Errors = 0;
- there is an effective change or explicitly valid no-op handling;
- current authority state permits business writes.

Cancel closes preview with no write.

### 7.7 Stage 7 — commit

Application service:

1. acquires `IWriteAuthorityGuard` write scope;
2. sends the immutable accepted plan + baseline concurrency token to the import store;
3. SQLite store opens one transaction;
4. revalidates concurrency/identity/uniqueness/parent assumptions inside that transaction before first business write;
5. allocates new IDs;
6. applies new Categories if authorized by approved Category semantics;
7. applies Product/OptionGroup/Option creates/updates/state changes;
8. performs no Deletes;
9. commits once;
10. application notifies `IDurableChangeNotifier` once with the existing non-cancellable post-commit semantics;
11. WPF refreshes current Catalogue.

Any failure before commit rolls back the complete batch.

## 8. Normal update mode

Normal Update import accepts existing rows only by protected identity.

Rules:

- existing valid ID -> update exact record;
- blank ID -> create;
- Product code is editable but never an existing-record matcher;
- changing Product code with same ID preserves identity;
- an existing current ID absent from workbook remains untouched;
- an existing child row absent from workbook remains untouched;
- duplicated/malformed/misbound ID -> Error;
- stale concurrent edit conflict -> Error rather than last-writer overwrite;
- Category name resolution follows the frozen Category-name rules.

A normal Update workbook that lacks the required protected binding metadata for existing IDs must fail closed rather than trusting arbitrary GUIDs.

## 9. Add-only mode

Add-only is an explicit mode, not inferred as permission to overwrite.

Rules:

- Product/OptionGroup/Option technical IDs must be blank/absent;
- every entity row is create-only;
- no existing Product/Group/Option update path is legal;
- normalized Product-code conflict is Error;
- no matching by Product name;
- Category name may resolve to an existing Category because Category assignment is name-based under the frozen specification;
- valid new Category names may be created atomically according to the approved Category/short-code contract;
- new parent relationships must resolve entirely within the candidate workbook/current Category/Product context without database-ID entry;
- first initialization into an empty current catalogue is supported.

If an add-only workbook contains a nonblank existing technical entity ID, import blocks rather than stripping/ignoring it.

## 10. Validation and issue model

### 10.1 Blocking Errors

At minimum:

- missing/unreadable required logical sheet;
- unsupported/corrupt workbook contract metadata for update mode;
- missing required business field;
- invalid numeric/boolean/enum value;
- invalid Product price/VAT;
- invalid OptionGroup SINGLE/MULTI/required/min/max structure;
- required group with no valid active choice in resulting aggregate;
- duplicate normalized Product code in resultant current catalogue;
- duplicate normalized Category name/short-code conflict under approved Category rules;
- malformed/duplicate/unknown/misbound entity ID;
- edited technical binding inconsistent with manifest;
- missing/ambiguous/wrong parent relationship;
- add-only existing Product-code conflict;
- stale live change conflict on a workbook-edited existing record;
- any persistence-level constraint/conflict discovered during commit revalidation.

Problems must carry at least worksheet, Excel row, field and stable issue code suitable for localized presentation.

### 10.2 Warnings

Warnings are non-blocking informational conditions only. They must never downgrade an Error or authorize unsafe matching.

Do not add arbitrary business thresholds merely to populate the Warning count.

### 10.3 No guessing

When input could map to more than one business/technical interpretation, return Error. Never choose nearest code/name, first row, first parent or reconstructed technical ID.

## 11. Preview

Preview must show operator-useful information at minimum:

- mode;
- selected file;
- Product create / modify / activate / deactivate;
- OptionGroup create / modify;
- Option create / modify / activate / deactivate;
- new Category count;
- Errors count;
- Warnings count;
- row-addressable issue list;
- actual affected rows/summary where practical;
- clear statement: omitted rows are not deleted;
- clear statement: preview has not changed the database.

For Add-only preview, explicitly state that existing Product/Group/Option records will not be updated.

## 12. Atomic persistence design

Do **not** loop through existing `CatalogueService.Create/Update...` calls because those calls each own their own transaction and cannot satisfy whole-workbook atomicity.

Add one dedicated import commit store method backed by one `SqliteTransactionRunner.ExecuteAsync` call.

The method may reuse/refactor internal SQL helpers from `SqliteCatalogueStore`, but must preserve public M03 behavior and tests.

The import plan should be explicit operation data, for example conceptually:

- `CreateCategory`;
- `CreateProduct`;
- `UpdateProductFields`;
- `CreateOptionGroup`;
- `UpdateOptionGroupFields`;
- `CreateOption`;
- `UpdateOptionFields`;
- Product/Option state changes as ordinary field updates.

There is intentionally **no Delete operation**.

Before any write inside the transaction, revalidate the complete plan against the current database/baseline token. An injected failure after any intermediate statement must roll back all statements.

## 13. Authority/recovery

- export: read-only, no authority scope;
- parse: no authority scope;
- preview: no authority scope;
- confirmed commit: mandatory centralized authority scope;
- Application boundary must reject NonAuthoritativeReadOnly, PendingTransfer/transition and RecoveryRequired states according to the existing guard contract;
- WPF Confirm must reflect the same state but is not the security/safety boundary;
- successful business commit -> one durable-change notification;
- failed/no-op import -> no durable-change notification;
- no M06/M07 protocol change.

## 14. SQLite/schema/dependency constraints

### No schema migration expected

M10 workbook metadata remains in the workbook. Current SQLite model already stores every M10 business field.

Do not add a migration unless implementation discovers a genuine persisted-data requirement that cannot be met by the existing schema; such a discovery is a blocker requiring controller review rather than an automatic schema expansion.

### ClosedXML

M10 is expected to add ClosedXML because Approved architecture already selects it and it is currently absent.

Requirements:

- one pinned tested version;
- package reference isolated to Infrastructure/tests as appropriate;
- no Office Interop / Excel COM;
- no second XLSX library;
- release notes reviewed before pinning;
- full self-contained Windows publish remains healthy.

## 15. Historical independence

M10 may read/write only current Catalogue tables and current Category data.

It must never update historical:

- `order_items` product code/name/category/base price/VAT/discount snapshots;
- `order_item_adjustments` option/group/price/VAT snapshots;
- order totals/tax snapshots;
- print history/business interpretation.

Automated and owner evidence must create an order before Catalogue import, mutate current catalogue through M10, then prove the committed historical order/reprint remains unchanged.

## 16. Work packages after authorization

No work package below is executable until separate owner authorization and a valid Issue #4 handoff.

### WP1 — Contract types + ClosedXML export + deterministic workbook fixture tests

- add pinned ClosedXML dependency;
- application workbook DTO/interfaces;
- Infrastructure workbook writer;
- three visible sheets + protected technical metadata;
- empty-catalogue template;
- export read-only path;
- contract/protection/round-trip fixture tests;
- no import commit yet.

### WP2 — Parser + import planner + preview model

- read workbook into raw DTO;
- Update/Add-only mode validation;
- identity/binding manifest validation;
- candidate overlay/no-delete semantics;
- Category resolution including owner-approved short-code rule;
- parent relationship helper;
- Errors/Warnings/preview counts and row diagnostics;
- pure deterministic tests;
- no business write yet.

### WP3 — Atomic SQLite import commit + authority/recovery

- dedicated `ICatalogueImportStore` commit boundary;
- one transaction for whole plan;
- concurrency/identity revalidation inside transaction;
- no Delete operations;
- new-ID allocation at commit;
- authority guard + one durable-change notify;
- rollback/failure injection and historical-independence integration tests.

### WP4 — WPF Catalogue export/import/preview workflow

- existing Catalogue area buttons/commands;
- file dialogs;
- explicit mode workflow;
- preview dialog/panel;
- issue navigation/readability;
- authority-aware Confirm;
- success refresh;
- Cancel/close safety;
- FR + zh-CN;
- STA/WPF lifecycle/layout tests under interactive quality gate.

### WP5 — Cross-layer hardening + owner candidate

- real `.xlsx` round-trip integration tests;
- tampered workbook/corrupt file/stale baseline cases;
- full M03/M04/M06/M07 regressions;
- architecture checks;
- Release build/full tests/diff check/privacy audit;
- self-contained win-x64 owner candidate and hashes;
- exact-head CI;
- manual-acceptance handoff only after controller review.

Work-package completion never automatically authorizes the next package; Issue #4 active handoff controls execution.

## 17. Required automated evidence summary

The detailed matrix in `milestone-10-preparation-readiness.md` is controlling. At minimum implementation completion requires proof of:

- deterministic workbook structure/protection/versioning;
- export/re-import identity preservation;
- safe Product-code change by same ID;
- blank-ID create;
- explicit add-only create-only semantics;
- technical-ID tamper rejection;
- invalid parent rejection;
- Category normalization/short-code approved contract;
- missing-row no-delete across Product/Group/Option;
- exact preview counts/issues;
- full rollback on any invalid row or injected persistence failure;
- non-authoritative commit rejection below UI;
- one durable-change notification only after successful commit;
- historical-order independence;
- FR/zh-CN UI state integrity;
- no COM and no unrelated dependency/schema change.

## 18. Manual acceptance

The prepared owner checklist is `milestone-10-final-manual-acceptance.md`.

Codex may prepare a candidate and automated evidence but must not:

- check owner acceptance boxes;
- claim Excel ergonomics are Passed without owner execution;
- merge the PR;
- start M11.

## 19. Explicit scope exclusions

M10 does not authorize:

- order/export workbook implementation from `docs/export.md` / M11;
- `Gestion SUSHI 81` four-sheet export;
- annual archive creation/access (M12);
- installer/final handover (M13);
- Category permanent-delete/import-delete mechanism;
- workbook macro/VBA/Office automation;
- external Excel service/cloud API;
- implicit Product matching by name/code in Update mode;
- schema migration without a newly surfaced reviewed blocker;
- live import-history table;
- M07 authority/recovery protocol changes.

## 20. Authorization gate

Before this contract becomes executable, all must be true:

1. owner resolves/approves Category `short_code` workbook semantics;
2. approved decision/baseline alignment is committed;
3. preparation/status reconciliation is complete;
4. project owner explicitly says **“批准 M10 implementation”**;
5. `milestone-10-authorization.md` is changed from NOT AUTHORIZED to AUTHORIZED with exact preparation head;
6. dedicated implementation branch/PR/mailbox is established according to current governance;
7. exactly one complete executable M10 handoff is published;
8. Issue #4 is then opened only for that handoff.

Until then, this file is documentation only.