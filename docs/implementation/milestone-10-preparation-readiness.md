# M10 — Catalogue `.xlsx` import/export — preparation/readiness

**Status:** Preparation — one project-owner workbook-semantics decision remains; implementation is **NOT AUTHORIZED**  
**Prepared:** 2026-09-17  
**Milestone:** M10 — Catalogue `.xlsx` import/export  
**Preparation baseline:** `main` at `861cfba1dfacbb3289395c0370f6d42765b6c223`  
**Implementation authorization:** **NOT GRANTED by this record**  
**Codex execution gate:** Issue #4 remains **CLOSED**; no executable M10 handoff exists.

## 1. GitHub facts re-established before preparation

This record was prepared from current GitHub facts rather than prior-chat memory.

Verified current state:

- `main` HEAD: `861cfba1dfacbb3289395c0370f6d42765b6c223`;
- PR #19 — `Post-M09: Hiboutik daily CB/Espèce dashboard` — **CLOSED / MERGED**;
- PR #19 merge commit: `861cfba1dfacbb3289395c0370f6d42765b6c223`;
- PR #19 final controller closure was accepted on closure head `d55e36a244327c55b81f6fb1040c0ef46dbe154a`;
- Issue #18 — post-M09 dashboard enhancement — **CLOSED / completed / merged**;
- Issue #4 — Codex execution gate — **CLOSED**, with no active Codex handoff;
- M10 implementation is not authorized;
- M11, M12 and M13 remain unauthorized.

Some living-status text merged in PR #19 was written before the merge and still says PR #19 is OPEN/unmerged. That wording is current-state drift only. GitHub merge metadata and current `main` control. Historical evidence is not to be rewritten.

## 2. Controlling M10 specification

Primary M10 ownership is confirmed from `docs/implementation-plan.md`:

- AC-CAT-008 — hierarchical `.xlsx` export;
- AC-CAT-009 — ID-preserving update-mode import;
- AC-CAT-010 — explicit add-only mode;
- AC-CAT-011 — validation, preview, warnings/errors and atomic import;
- catalogue portion of AC-ARCH-005 — ClosedXML application-service boundary and no Excel COM.

M10 must also preserve relevant existing acceptance and invariants, especially:

- AC-CAT-001 through AC-CAT-007 current-catalogue validation/identity semantics;
- AC-CAT-012 historical-order independence;
- AC-STO-010 authoritative/read-only write enforcement;
- AC-NFR-002 deterministic automated validation evidence;
- AC-NFR-004 actionable invalid-import feedback and no corruption.

Controlling sources reviewed:

- `docs/catalogue-management.md`;
- `docs/product-requirements.md`;
- `docs/business-rules.md`;
- `docs/data-model.md`;
- `docs/architecture.md`;
- `docs/storage-strategy.md`;
- `docs/acceptance-criteria.md`;
- `docs/v1-specification-freeze.md`;
- `docs/implementation-plan.md`;
- `docs/implementation-status.md`;
- `docs/decisions/category-name-uniqueness.md`;
- `docs/decisions/filtered-catalogue-bulk-activation.md`;
- `docs/decisions/m04-order-entry-operator-ergonomics-amendment.md` where Category `short_code` became approved V1 business data.

No separate Approved decision record currently defines a different M10 Excel identity/update model; the consolidated catalogue baseline controls except where the later M04 Category-short-code decision adds a requirement.

## 3. Frozen workbook semantics already complete

The existing frozen-and-amended specification already fixes the following behavior and it must not be reopened during implementation:

1. exactly three operator-facing logical worksheets: `Products`, `OptionGroups`, `Options`;
2. no separate operator-facing `Categories` worksheet;
3. Product/OptionGroup/Option opaque internal IDs may be carried only as protected technical update-matching data;
4. current Category identity remains opaque `category_id`, but normal workbook category assignment is by normalized operator-facing Category name rather than exposing `category_id`;
5. valid existing entity ID means update that exact entity; a Product code change does not create a new Product when Product ID remains the same;
6. blank entity ID creates a new entity in normal update mode;
7. explicit add-only mode never matches existing records by code/name to perform an implicit update;
8. add-only conflict with an existing normalized Product code is a blocking Error;
9. duplicate current Product codes and duplicate current Category names are blocking Errors;
10. malformed/unusable/corrupt IDs and inconsistent parent relationships are blocking Errors; importer must never guess;
11. Product row Category-name change means Product reassignment to another existing/new valid Category, not global Category rename;
12. row absence means “not included in this import”, never deletion;
13. no Excel permanent-delete mechanism is introduced;
14. Product and Option active state may be changed through explicit visible fields; OptionGroup has no active flag in the current model;
15. preview must at least count Create / Modify / Activate / Deactivate / Errors / Warnings and identify affected rows where practical;
16. any blocking Error means no business changes;
17. successful confirmed import is one atomic catalogue mutation;
18. successful batch import triggers the normal durable-change/recovery protection;
19. historical Order/Product/Option/category-name snapshots are never rewritten from current catalogue imports;
20. no permanent catalogue-import history entity is required.

## 4. One material owner decision still required — Category `short_code` workbook contract

The audit found one genuine post-freeze specification gap that affects operator-visible workbook meaning and therefore must not be invented by implementation.

The later Approved M04 decision makes Category `short_code` independent V1 business data and explicitly states:

> Future catalogue `.xlsx` support must eventually preserve this business field consistently.

The older `catalogue-management.md` workbook section predates that amendment and freezes only Category-name representation/resolution in `Products`, with no `Categories` sheet. It does not define how the later `short_code` field appears in or is changed through M10.

This leaves material questions that affect a real Excel workflow:

- whether `Category short code` is visible in `Products`;
- whether M10 may create a new Category with a short code during first/add-only import;
- whether an imported workbook may globally change an existing Category short code, or must instead require that existing Category short-code changes continue through the in-app Category manager;
- how repeated Product rows referring to the same Category must agree on a single Category short code.

### Recommended owner decision

The safest, smallest extension is:

1. add a visible `Category short code` business column to `Products`;
2. keep Category technical ID fully hidden/non-operator-facing and keep **no Categories worksheet**;
3. for a **new Category** created by import, allow one optional short code and require every workbook row naming that Category to agree after normalization; uniqueness/length rules remain the existing Category rules;
4. for an **existing Category**, the workbook short code is a consistency field, not a global rename command: it must be blank-as-preserve or equal to the current short code; a different non-blank value is a blocking Error instructing the operator to change the Category in the normal Category manager and re-export;
5. import never clears or silently replaces an existing Category short code;
6. this preserves first-catalogue initialization and round-trip fidelity without introducing a new global Category-edit pathway or a fourth operator sheet.

This recommendation requires explicit project-owner approval before M10 can be marked ready for implementation authorization.

## 5. Existing M03/M04 implementation seams available to M10

### 5.1 Domain catalogue model

Current production Domain records are already the correct business model:

- `Category`: opaque `Id`, unique normalized `Name`, optional unique normalized `ShortCode`, timestamps;
- `Product`: opaque `Id`, unique normalized `Code`, `Name`, `CategoryId`, TTC price, VAT rate, `IsActive`, `DiscountEligible`, `OptionsEnabled`, timestamps;
- `OptionGroup`: opaque `Id`, `ProductId`, name, SINGLE/MULTI mode, required/min/max, display order, timestamps;
- `ProductOption`: opaque `Id`, `OptionGroupId`, name, TTC adjustment, `IsActive`, display order, timestamps.

Domain validation already covers required fields, non-negative Product price, VAT range, option-group SINGLE/MULTI structure and required-active-choice validity.

### 5.2 Application catalogue boundary

`CatalogueService` and `ICatalogueQueries` already provide:

- read-only Category/Product listing;
- complete Product aggregate loading;
- centralized `IWriteAuthorityGuard` mutation protection;
- `IDurableChangeNotifier` after successful catalogue writes.

Those seams should be reused. M10 must not bypass the authority guard or recovery notifier.

### 5.3 SQLite persistence

`SqliteCatalogueStore` already proves:

- normalized Product/Category uniqueness;
- opaque-ID persistence;
- aggregate create/update;
- deterministic child ordering;
- foreign-key parent integrity;
- atomic single-aggregate and filtered-bulk writes through `SqliteTransactionRunner`.

Important implementation fact: current `UpdateProductAsync` treats the supplied Product aggregate as a complete replacement of child groups/options and physically deletes omitted children. Therefore M10 **must not** implement row-by-row import by calling this command with only workbook rows. The importer must overlay workbook rows onto a complete current catalogue snapshot so workbook absence remains unchanged, and the final whole-import persistence operation must use a dedicated atomic batch boundary.

### 5.4 Transaction and revision seam

`SqliteTransactionRunner` already provides one SQLite transaction and advances the canonical business-data revision only when the transaction changes business rows. M10 should add one batch-import persistence operation executed inside exactly one such transaction.

No new transaction framework is required.

### 5.5 WPF Catalogue seam

The existing M03 Catalogue view-model/UI already has:

- localized Catalogue maintenance;
- authority-aware write enablement;
- current Catalogue refresh;
- category/product dialogs and complete Product aggregate editing;
- operator-visible validation messages;
- established STA/WPF test patterns.

M10 should add a narrow Export / Import workflow to the existing Catalogue area rather than a second catalogue-management screen.

## 6. Workbook technical mapping — implementation choice

Subject to the Category-short-code owner decision above, the following is the recommended implementation architecture and is technical rather than a new business rule.

### 6.1 Visible business sheets

Keep the three fixed logical sheet names:

- `Products`;
- `OptionGroups`;
- `Options`.

Business columns map directly to the current Domain model. Exact localized visible header wording may be chosen during implementation, but import must not depend solely on translated display text.

### 6.2 Protected technical identity

Use hidden + locked technical columns for existing Product / OptionGroup / Option IDs and existing parent identity where required.

Additionally use a **VeryHidden technical metadata sheet** (for example `__Sushi81Meta`) that is not an operator-facing logical worksheet. It should contain at least:

- workbook contract/schema version;
- export identifier;
- invariant column/schema descriptors;
- immutable row-binding keys for exported existing records;
- exported ID/parent bindings and baseline values needed for corruption/stale-workbook detection.

Excel worksheet protection is only an accidental-edit barrier, not a security boundary. Import validation remains authoritative.

### 6.3 Corruption/staleness protection

For an exported update workbook, existing-row identity must be verified against its exported manifest/binding rather than trusting an edited hidden GUID blindly.

Required fail-safe behavior:

- malformed ID -> Error;
- ID not in current catalogue -> Error;
- valid ID bound to the wrong exported row/entity type -> Error;
- OptionGroup ID under another Product -> Error;
- Option ID under another OptionGroup -> Error;
- duplicated existing ID in workbook -> Error;
- edited/corrupted technical binding -> Error;
- ambiguous new parent resolution -> Error;
- current live record changed since the export baseline **and** the workbook also changes that record -> conflict Error rather than blind overwrite.

The importer may preserve a live record that changed after export when the workbook row itself is unchanged and therefore produces no write.

### 6.4 New child parent relationship

Existing child parent relationships stay bound by protected technical identity and cannot be silently re-parented by editing visible descriptive cells.

New OptionGroup / Option rows need an understandable workbook-local parent reference without exposing database IDs. The exact helper representation may be selected in implementation, but it must:

- resolve deterministically to exactly one Product/OptionGroup in the candidate workbook/current catalogue;
- support a new OptionGroup under a new Product and a new Option under a new OptionGroup;
- block ambiguous or missing parent references;
- never fall back to fuzzy name matching;
- never create a new business identifier in SQLite.

A workbook-local relationship key or similarly deterministic helper is acceptable; it is not a persisted Product/OptionGroup/Option identity.

## 7. Import mode separation

### Normal update mode

- intended for Sushi81-exported update workbooks;
- valid existing technical IDs update those exact records;
- blank entity IDs create new records;
- no current entity is selected by code/name as a substitute for a missing ID;
- any technical-ID corruption blocks;
- missing rows are ignored, not deleted.

### Explicit add-only mode

- must be an explicit operator-selected mode/action;
- entity technical IDs must be absent/blank;
- every entity row is a Create candidate only;
- no current Product/OptionGroup/Option is updated;
- no Product is matched by code/name to convert Create into Update;
- normalized Product-code conflict blocks;
- normalized Category names may resolve to existing Categories under the frozen Category rule, because Category assignment is name-based rather than a Product update match;
- supports an empty first catalogue and later all-new batches.

A workbook containing existing entity IDs must not be silently interpreted as add-only.

## 8. Mechanical guarantee that row absence is not deletion

The importer should use an **overlay plan**:

1. read the complete current catalogue snapshot;
2. parse workbook rows into explicit row operations only;
3. resolve/validate identities and relationships;
4. overlay only rows actually present in the workbook onto the current snapshot;
5. build the resulting candidate catalogue;
6. validate the candidate globally;
7. compute a write plan containing only explicit creates/updates/state changes;
8. never generate Delete operations from missing rows;
9. commit that plan atomically.

There is no Delete operation in the M10 import plan contract.

This is also why the current destructive child-replacement method cannot be used naïvely for partial workbook input.

## 9. Preview contract

Before any write, operator preview must show at least:

- import mode: Update or Add-only;
- file name;
- Products: create / modify / activate / deactivate counts;
- OptionGroups: create / modify counts;
- Options: create / modify / activate / deactivate counts;
- new Categories to be created;
- total Errors;
- total Warnings;
- a row-addressable problem list containing worksheet + Excel row + field + localized actionable message;
- a concise affected-row list for actual changes where practical;
- explicit statement that omitted rows are **not deleted**;
- explicit statement that no business change has occurred yet;
- Confirm enabled only when Errors = 0 and the device is authoritative.

Warnings never authorize bypass of Errors.

Do not invent an arbitrary “large import” threshold as a business warning rule during M10 preparation. The Warning channel may be used for concrete non-blocking conditions that are defined/tested without weakening any blocking validation.

## 10. Atomicity and authority boundary

### Read operations

Catalogue export is a pure read operation and may run on an authoritative or non-authoritative device. It must not acquire write authority and must not mutate SQLite.

The normal global read-only/non-authoritative presentation already tells the operator that local business data may be stale. M10 must not imply that an export grants or transfers authority.

### Import operations

File open/parse/validation/preview performs no business write.

The **commit** is a business-authoritative Catalogue mutation and must pass the existing centralized `IWriteAuthorityGuard` before the persistence call. The persistence call then runs the whole accepted import plan inside one SQLite transaction. Successful commit notifies `IDurableChangeNotifier` once, triggering normal recovery protection.

The WPF import Confirm action must be disabled/blocked in non-authoritative/read-only state, and the Application boundary must independently reject a bypass attempt.

No authority/recovery protocol change is needed.

## 11. Schema / dependency result

### SQLite schema

No new durable business entity or field is required for M10. Workbook contract version/manifest data belongs inside the `.xlsx`, not `live.db`.

**Recommended result: no SQLite migration.**

### ClosedXML

Current `Directory.Packages.props` and production project files contain **no ClosedXML package reference**.

This is not a specification blocker. The frozen Approved architecture explicitly selects ClosedXML for V1 `.xlsx` work and requires it to be isolated behind an application-owned workbook/import-export service, with a pinned tested version and no Excel COM dependency.

Therefore M10, once separately authorized, is allowed and expected to add one pinned ClosedXML dependency. The exact version is a technical implementation choice to be fixed/tested in the implementation PR; M10 must not add unrelated Excel libraries.

## 12. Automated evidence matrix

Implementation must add focused evidence at these levels.

### Domain / pure import-planning tests

- valid Product/OptionGroup/Option mapping;
- price, VAT, SINGLE/MULTI/min/max/required-active-choice validation;
- normalized duplicate Product codes;
- normalized duplicate Category names;
- Category short-code rules after owner decision;
- duplicate IDs / malformed IDs / wrong entity IDs;
- parent mismatch / missing parent / ambiguous new-parent helper;
- update-by-ID with Product code change preserves identity;
- blank-ID create;
- add-only never implicitly updates;
- add-only Product-code collision blocks;
- omitted Product/Group/Option produces no Delete operation;
- activation/deactivation counts;
- candidate-overlay result preserves unrelated current rows;
- conflict against changed live baseline fails closed;
- deterministic preview counts/issues.

### ClosedXML workbook contract tests

Using real in-memory/temp `.xlsx` files through ClosedXML:

- three visible logical worksheets have deterministic structure;
- technical ID columns hidden/locked;
- normal business cells remain editable;
- technical metadata sheet is VeryHidden/non-operator-facing;
- export -> reopen -> parse round-trip preserves all current entity values and IDs;
- formulas/cell types/blank cells are parsed deterministically;
- malformed/missing sheet/header/version/binding rejects safely;
- tampered hidden ID/binding rejects;
- workbook with no existing IDs supports explicit add-only validation;
- library exception/corrupt ZIP/XLSX yields Error with no business write;
- no Excel COM/Interop dependency.

### SQLite integration tests

- entire multi-Product/multi-child import commits in one transaction;
- injected mid-import write failure rolls back every category/product/group/option change;
- commit failure rolls back every change;
- stale/conflicting identity fails before partial write;
- unique/foreign-key constraints remain intact;
- no migration/schema change;
- one successful batch advances business revision once and notifies recovery once;
- rejected preview/commit advances nothing and notifies nothing;
- order snapshot rows remain byte/business-equivalent before/after current catalogue import;
- missing workbook child rows remain persisted;
- no import path invokes Product permanent deletion.

### Application / authority tests

- export does not request write authority;
- preview does not request write authority;
- authoritative confirm commits exactly once;
- non-authoritative confirm rejected before store mutation;
- RecoveryRequired/pending-transfer states also reject;
- notifier behavior matches existing durable-change contract;
- cancellation after commit cannot suppress post-commit recovery notification.

### Desktop / STA WPF tests

- Export and Import actions are visible/localized FR + zh-CN;
- file-dialog result/cancel path does not mutate business data;
- preview state displays counts/issues and blocks Confirm on Error;
- Confirm disabled/blocked read-only;
- language switch preserves loaded preview numeric/business meaning;
- modal/close/cancel discards transient preview only;
- successful import refreshes current Catalogue without corrupting filters/selection state;
- default and resized layouts remain usable.

### Full regression

- all existing M03 Catalogue tests;
- M04 order-snapshot/category-navigation regressions;
- M06/M07 authority/recovery regressions;
- full solution Release tests/build;
- architecture dependency boundary;
- `git diff --check`;
- privacy/safety audit with synthetic fixtures only.

## 13. Windows/WPF + real Excel manual acceptance design

Manual acceptance must exercise a real exported `.xlsx` in desktop Excel/compatible normal operator workflow, not merely a file-picker/UI smoke test.

Proposed owner checklist groups:

A. **Export/readability/protection** — export a seeded catalogue, open in Excel, verify three logical sheets, readable business fields, technical identities not normally editable, no Excel warning/corruption prompt.

B. **Safe round-trip no-op** — save exported workbook without business edits, import/preview, verify zero unintended changes and confirm no identity/order drift.

C. **Update-mode same-ID edit** — change Product code/name/price/category assignment/active flags and selected group/option fields; preview exact counts; commit; re-export and verify same identities and intended values only.

D. **New rows in update mode** — add Product + OptionGroup + Options using normal workbook relationship workflow; preview creates; commit; confirm normal Catalogue/Caisse visibility and options.

E. **Explicit add-only / first initialization** — on a controlled empty catalogue database, import a no-existing-ID workbook, verify create-only preview and complete usable hierarchy; on a non-empty catalogue, prove conflicting Product code blocks rather than updates.

F. **No implicit deletion** — remove an existing Product row, OptionGroup row and Option row from a copy, import it, verify preview contains no deletes and all omitted records remain after commit.

G. **Corruption fail-safe** — intentionally expose/edit a technical ID or break a parent binding in a test copy, verify blocking sheet/row Error and zero database change.

H. **Validation/atomic rollback** — combine several valid edits with one invalid VAT/price/parent/duplicate; verify all are blocked; after correction confirm all valid changes land together.

I. **Authority** — on non-authoritative device verify export works as read; import commit is impossible; transfer authority through existing M07 workflow and then prove the same valid import can be committed on authoritative device after fresh preview.

J. **Historical independence** — create an order before Catalogue update, change current code/name/category/price/options through M10, reopen/reprint historical order and confirm saved snapshot remains unchanged.

K. **FR / zh-CN and layout** — switch languages around export/import/preview and verify understandable labels/messages, unchanged workbook/business values, usable default/maximized layout.

L. **Restart/re-export** — restart application, verify committed Catalogue persists, re-export and confirm workbook reflects the final current catalogue.

Owner acceptance must never be pre-checked by Codex.

## 14. Automated versus owner evidence boundary

Automated evidence owns deterministic rules that are mechanically testable: schema/headers/protection flags, exact ID matching, validation, overlay/no-delete semantics, diff/preview counts, transaction rollback, authority rejection, persistence, historical snapshots, localization resource/state tests and dependency boundaries.

Project-owner manual acceptance owns actual Windows/WPF/Excel ergonomics and physical observation: opening the real generated file in Excel, practical editability/protection, comprehensibility of parent relationships, preview usability, file-dialog/operator journey, real-language layout, and real round-trip confidence.

Green automated tests do not replace owner Excel/WPF acceptance.

## 15. M11 / M12 boundaries

M10 may create only the generic seams needed by its own catalogue workbook work:

- an application-owned `.xlsx` adapter/service boundary;
- ClosedXML isolated in Infrastructure;
- reusable low-level workbook abstractions only where they arise naturally from M10.

M10 must **not** implement or pre-build:

- M11 `Gestion SUSHI 81` four-sheet export contract;
- M11 order eligibility/date-range/exported-at/correction semantics;
- M11 export filename/intermediate-save workflow beyond generic file-save mechanics required by M10;
- M12 annual archive creation/access;
- M12 archive hydration/browse UI;
- M13 installer/final handover.

M11 may later reuse the proven ClosedXML adapter boundary but owns its own workbook DTO/schema/validation/export logic.

## 16. Living-status reconciliation required by preparation

The following current-state documents contain stale pre-merge wording and should be reconciled in the docs-only M10 preparation PR while preserving historical evidence:

- root `README.md` — PR #19 still described as OPEN/unmerged;
- `docs/README.md` — same;
- `docs/implementation-status.md` — same in current-state sections;
- `docs/v1-specification-freeze.md` current exit/status paragraph — same;
- `docs/implementation-plan.md` current implementation-state tail still points at an earlier milestone;
- `docs/implementation/README.md`, `src/README.md`, `tests/README.md` have older current-status summaries from M06-M08 era.

This is accounting cleanup only. Historical milestone evidence, worklogs, acceptance records and old PR comments must not be rewritten.

## 17. Readiness conclusion

**GitHub baseline:** PASS  
**M09/post-M09 dependency:** PASS / merged  
**M10 core workbook specification:** substantially complete  
**Existing Domain/Application/SQLite/WPF seams:** PASS  
**ClosedXML architecture authorization:** PASS; dependency not yet installed  
**SQLite migration need:** none expected  
**Authority/recovery compatibility:** PASS  
**Historical-snapshot separation:** PASS  
**Material owner decisions:** **1 open — Category `short_code` workbook semantics**  
**Issue #4:** CLOSED  
**Executable Codex handoff:** none  
**Production implementation:** not started  
**M11+:** unauthorized

### Disposition

M10 is **not yet ready to be marked implementation-authorized** because the later Approved Category-short-code amendment requires one operator-visible workbook-semantic decision.

After the project owner explicitly approves one Category-short-code workbook contract, the preparation package can be finalized, the corresponding approved decision/baseline alignment recorded, and M10 can then be presented as ready for the separate statement **“批准 M10 implementation”**.

This record does not authorize implementation, opening Issue #4, creating an executable Codex handoff, or starting M11+.