# M10 — Catalogue `.xlsx` import/export — preparation/readiness

**Status:** Preparation complete — **READY FOR PROJECT-OWNER IMPLEMENTATION AUTHORIZATION**  
**Prepared/finalized:** 2026-09-17  
**Milestone:** M10 — Catalogue `.xlsx` import/export  
**Preparation baseline:** `main` at `861cfba1dfacbb3289395c0370f6d42765b6c223`  
**Implementation authorization:** **NOT GRANTED by this record**  
**Codex execution gate:** Issue #4 remains **CLOSED**; no executable M10 handoff exists.

## 1. GitHub facts re-established

Preparation was rebuilt from current GitHub facts rather than prior-chat memory.

Verified baseline:

- `main` HEAD: `861cfba1dfacbb3289395c0370f6d42765b6c223`;
- PR #19 — `Post-M09: Hiboutik daily CB/Espèce dashboard` — CLOSED / MERGED;
- PR #19 merge commit: `861cfba1dfacbb3289395c0370f6d42765b6c223`;
- PR #19 final closure head: `d55e36a244327c55b81f6fb1040c0ef46dbe154a`;
- Issue #18: CLOSED / completed / merged;
- Issue #4: CLOSED with no active executable handoff;
- M10 implementation: not authorized;
- M11/M12/M13: not authorized.

GitHub merge metadata/current `main` supersede stale pre-merge wording that remains in some historical/living text. Historical evidence is not rewritten.

## 2. Controlling M10 specification

Primary M10 acceptance ownership is:

- AC-CAT-008 — hierarchical `.xlsx` export;
- AC-CAT-009 — ID-preserving normal update import;
- AC-CAT-010 — explicit add-only mode;
- AC-CAT-011 — complete validation, preview and atomic commit;
- Catalogue portion of AC-ARCH-005 — ClosedXML behind an application-owned workbook boundary; no Excel COM.

Inherited invariants include AC-CAT-001 through AC-CAT-007, AC-CAT-012, AC-STO-010, AC-NFR-002 and AC-NFR-004.

Controlling sources include:

- `docs/catalogue-management.md`;
- `docs/product-requirements.md`;
- `docs/business-rules.md`;
- `docs/data-model.md`;
- `docs/architecture.md`;
- `docs/storage-strategy.md`;
- `docs/acceptance-criteria.md`;
- `docs/acceptance-criteria-amendment-m10-category-short-code-workbook.md`;
- `docs/v1-specification-freeze.md`;
- `docs/implementation-plan.md`;
- `docs/implementation-status.md`;
- `docs/decisions/category-name-uniqueness.md`;
- `docs/decisions/m04-order-entry-operator-ergonomics-amendment.md`;
- `docs/decisions/m10-category-short-code-workbook-semantics.md`.

## 3. Owner decision completed — Category `short_code`

During the audit, the only material operator-visible gap was the later M04 Category `short_code` business field: the old three-sheet workbook baseline predated that field.

On 2026-09-17 the project owner explicitly approved the recommended M10 semantics. They are now frozen in:

`docs/decisions/m10-category-short-code-workbook-semantics.md`

and aligned into `docs/catalogue-management.md` plus the M10 acceptance amendment.

Controlling behavior:

- `Products` contains visible Category name and visible Category short code business columns;
- there is no operator-facing `Categories` worksheet;
- `category_id` stays technical/non-operator identity;
- export repeats the current Category short code on every Product row using that Category;
- new Categories created by import may receive one optional consistent short code;
- repeated Product rows for one new Category must not disagree on non-blank short code;
- existing Category short code is preserve/consistency data only: blank preserves, same normalized value is valid, different non-blank value is a blocking Error;
- existing Category short code cannot be cleared or globally changed by workbook import;
- existing Category changes continue through the in-application Category manager;
- add-only mode follows the same Category rules.

No material owner decision remains open for M10.

## 4. Frozen workbook semantics now complete

M10 implementation must preserve all of the following:

1. exactly three operator-facing logical worksheets: `Products`, `OptionGroups`, `Options`;
2. no operator-facing `Categories` worksheet;
3. Product/OptionGroup/Option opaque IDs are protected technical update-matching data;
4. Category technical ID is not an operator workbook field; Category assignment remains name-based;
5. Product rows also preserve Category short-code business meaning under the approved rule above;
6. existing valid entity ID means update that exact entity;
7. Product code change with the same Product ID remains the same Product;
8. blank entity ID means create in normal update mode;
9. add-only is explicit create-only and never silently matches by code/name for update;
10. malformed/unknown/misbound IDs or parent relationships are blocking Errors;
11. Product Category-name change means Product reassignment, not global Category rename;
12. row absence never means deletion;
13. there is no Excel permanent-delete operation;
14. Product/Option active state changes require explicit visible fields; OptionGroup has no active flag;
15. preview includes Create/Modify/Activate/Deactivate/Errors/Warnings and row-addressable issues;
16. any Error blocks the whole commit;
17. confirmed import is one atomic catalogue mutation;
18. successful batch commit triggers normal durable-change/recovery protection once;
19. historical order snapshots are never rewritten;
20. no permanent import-history business entity is required.

## 5. Existing implementation seams

### Domain/current model

Current production model already contains the required M10 business data:

- Category: opaque ID, unique normalized name, optional unique normalized short code;
- Product: opaque ID, unique normalized code, name, CategoryId, TTC price, VAT, Active, DiscountEligible, OptionsEnabled;
- OptionGroup: opaque ID, ProductId, name, SINGLE/MULTI, required, min/max, display order;
- ProductOption: opaque ID, OptionGroupId, name, signed TTC adjustment, Active, display order.

### Application

Current Catalogue service/query boundaries already provide:

- current Category/Product reads;
- complete Product aggregate loading;
- centralized `IWriteAuthorityGuard` for business mutations;
- `IDurableChangeNotifier` after successful durable changes.

M10 should add a dedicated workbook/import orchestration boundary rather than bypassing these protections.

### SQLite

`SqliteCatalogueStore` already provides normalized uniqueness, opaque-ID persistence, foreign-key integrity and deterministic child ordering.

Important seam constraint: current `UpdateProductAsync` treats its Product aggregate as complete and deletes omitted child groups/options. Therefore M10 must **not** implement partial workbook rows by naïvely calling that command. The import path must overlay workbook rows onto a complete current snapshot and commit an explicit no-delete batch plan.

### Transaction/revision

`SqliteTransactionRunner` already supplies one SQLite transaction and business-data revision advancement. M10 should commit one complete validated import plan through exactly one transaction boundary.

### WPF

The existing Catalogue area already provides localization, authority-aware command state, refresh, Category/Product editing and established STA/WPF test patterns. M10 should extend that area with a narrow Export / Import / Preview workflow rather than creating a second Catalogue screen.

## 6. Implementation architecture — technical choices fixed

### Application-owned boundary

Recommended conceptual seams; exact class names may vary:

- `ICatalogueWorkbookGateway` — write/read `.xlsx` using application-owned DTOs only;
- `CatalogueImportPlanner` — pure deterministic normalization/validation/overlay/diff/preview;
- `ICatalogueImportStore` — current import baseline + one atomic commit method;
- `CatalogueWorkbookService` — export/preview/authorized commit orchestration.

ClosedXML types must not leak into Domain/Application public contracts.

### Infrastructure

- ClosedXML isolated behind workbook gateway;
- SQLite import persistence behind dedicated import store;
- reuse connection factory, transaction runner, normalization, ID generator, clock, constraints and business revision.

### Desktop

WPF owns file dialogs, mode selection, preview, localization, confirmation/cancel and post-success refresh only. No SQL/ClosedXML parsing belongs in presentation logic.

## 7. Technical identity / workbook safety mapping

Use hidden + locked technical columns for existing Product/OptionGroup/Option IDs and existing protected parent identity as needed.

Use one non-operator-facing VeryHidden technical metadata sheet such as `__Sushi81Meta` containing only safety metadata, including:

- workbook contract/schema version;
- export instance ID;
- invariant field/column descriptors;
- immutable exported row-binding keys;
- exported ID/parent bindings;
- export baseline values/fingerprint needed for corruption/stale-workbook detection.

Worksheet protection is an accidental-edit barrier, not the trust boundary. Import validation is authoritative.

Fail closed for:

- malformed ID;
- unknown current ID;
- duplicate existing ID;
- wrong entity type;
- ID bound to the wrong exported row;
- OptionGroup under wrong Product;
- Option under wrong OptionGroup;
- edited/corrupt technical binding;
- missing/ambiguous new parent;
- unsupported workbook version/structure;
- stale conflicting live edit.

Do not guess or fuzzy-match.

## 8. New-child relationship rule

New OptionGroup/Option rows need an understandable workbook-local relationship reference without database-ID entry.

Implementation may choose the helper representation, but it must:

- resolve deterministically to exactly one parent;
- support new child under new parent;
- survive normal Excel row insertion/sorting;
- block missing/ambiguous parent;
- never use fuzzy name matching or first-match behavior;
- remain workbook-scoped and not become a new persisted business identity.

## 9. Update vs add-only modes

### Normal Update

- existing valid protected ID -> update exact record;
- blank ID -> create;
- Product code/name never substitute for a missing existing ID;
- corrupt/misbound ID -> Error;
- missing rows remain untouched;
- Category name resolves assignment; Category short-code semantics follow the Approved M10 decision.

### Add-only

- explicit operator-selected mode;
- Product/OptionGroup/Option IDs must be blank/absent;
- all such entity rows are Creates only;
- no implicit current Product/Group/Option update;
- Product-code collision blocks;
- Category name may resolve to an existing Category because Category assignment is name-based;
- existing Category resolution does not authorize short-code change;
- new Category with optional consistent short code is allowed;
- supports empty-catalogue first initialization.

## 10. Mechanical no-delete guarantee

Importer uses a complete-current-state **overlay plan**:

1. read complete current Catalogue snapshot;
2. parse workbook rows into explicit row operations;
3. resolve/validate IDs, Category meaning and parent relationships;
4. overlay only present workbook rows onto current state;
5. build complete candidate Catalogue;
6. validate candidate globally;
7. compute only Creates/Updates/state changes;
8. generate no Delete operations from omissions;
9. commit the accepted plan atomically.

**There is no Delete operation in the M10 import plan contract.**

## 11. Preview contract

Before any write show at least:

- Update/Add-only mode;
- file name;
- Products: create / modify / activate / deactivate;
- OptionGroups: create / modify;
- Options: create / modify / activate / deactivate;
- new Categories;
- total Errors / Warnings;
- worksheet + Excel row + field + localized actionable problem list;
- concise affected-row summary where practical;
- explicit statement that omitted rows are not deleted;
- explicit statement that preview has not changed the database.

Confirm is possible only when Errors = 0 and current authority permits business writes.

Warnings must never weaken Errors. No arbitrary business-size warning threshold is invented by this preparation.

## 12. Atomicity / authority / recovery

### Read-only operations

Export, workbook parse and preview perform no business write and do not acquire write authority.

Catalogue export is allowed on authoritative and non-authoritative devices as a read of their local state. Existing global read-only/stale-state presentation remains controlling; export does not grant authority.

### Commit

Confirmed import is a business-authoritative mutation and must:

1. pass existing centralized `IWriteAuthorityGuard` at Application boundary;
2. send one immutable accepted plan + concurrency baseline to persistence;
3. execute inside one SQLite transaction;
4. revalidate identities/concurrency/uniqueness/parents before first business write;
5. allocate new IDs only in the commit path;
6. apply Categories/Products/Groups/Options without Deletes;
7. commit once;
8. notify `IDurableChangeNotifier` once after success using existing post-commit semantics;
9. refresh Catalogue UI.

Any failure before commit rolls back the full batch.

WPF Confirm must also be disabled/blocked in non-authoritative, transition/pending-transfer or RecoveryRequired states, but Application guard is the safety boundary.

## 13. Schema/dependency result

### SQLite

No new M10 durable business field/entity is needed. Workbook metadata belongs in `.xlsx`, not `live.db`.

**Decision: no M10 SQLite migration is expected.**

If implementation later proves a schema field is genuinely required, that is a controller blocker, not automatic authorization to migrate.

### ClosedXML

ClosedXML is not currently installed, but Approved architecture explicitly selects it for V1 `.xlsx` work. M10 implementation may therefore add one pinned/tested ClosedXML dependency, isolated to Infrastructure/tests as appropriate, with no Excel COM/Interop and no second XLSX library.

The exact version is a technical implementation choice to be fixed/tested in the implementation PR.

## 14. Automated evidence matrix

Implementation must add deterministic evidence for at least:

### Domain/pure planner

- valid Product/Group/Option mapping;
- Product price/VAT/group required/min/max validation;
- duplicate normalized Product codes;
- duplicate Category names/short codes;
- existing/new/repeated Category short-code rules;
- malformed/duplicate/wrong IDs;
- parent mismatch/missing/ambiguous new-parent helper;
- same-ID Product code change preserves identity;
- blank-ID create;
- add-only never implicitly updates;
- add-only code collision blocks;
- omitted Product/Group/Option produces no Delete;
- activation/deactivation counts;
- candidate overlay preserves unrelated current rows;
- stale conflicting baseline fails closed;
- deterministic preview counts/issues.

### ClosedXML

Using real temp `.xlsx` files:

- exactly three visible logical worksheets;
- technical columns hidden/locked;
- business cells editable;
- metadata sheet VeryHidden;
- export/reopen/parse round-trip preserves entity values/IDs/Category short codes;
- blank/cell-type/formula handling deterministic;
- malformed/missing sheet/header/version/binding rejects safely;
- tampered ID/binding rejects;
- no-ID workbook supports add-only;
- corrupt/non-XLSX file yields Error/no write;
- no COM/Interop dependency.

### SQLite integration

- multi-Product/multi-child import is one transaction;
- injected mid-import/commit failure rolls back every change;
- stale/conflicting identity blocks before partial write;
- constraints/FKs remain intact;
- no migration/schema change;
- successful batch advances business revision consistently and notifies recovery once;
- rejected/no-op path notifies nothing;
- historical order snapshots remain unchanged;
- missing child rows remain persisted;
- no import path invokes Product permanent deletion.

### Application/authority

- export/preview do not request write authority;
- authoritative Confirm commits once;
- NonAuthoritativeReadOnly/transition/RecoveryRequired commit rejected before store mutation;
- post-commit notifier semantics preserved;
- cancellation after commit cannot suppress recovery notification.

### Desktop/STA WPF

- Export/Import actions localized FR/zh-CN;
- file-dialog cancel no mutation;
- preview counts/issues/Confirm gating;
- read-only Confirm disabled;
- language switch preserves preview business meaning;
- Cancel/close discards transient preview;
- successful import refreshes Catalogue without corrupting filters/selection;
- normal/resized layout usable.

### Regression

- M03 Catalogue;
- M04 order snapshots/category navigation;
- M06/M07 authority/recovery;
- full Release solution tests/build;
- architecture boundary;
- `git diff --check`;
- synthetic/privacy audit.

## 15. Windows/WPF + real Excel owner acceptance

Owner acceptance must exercise a real exported `.xlsx`, not only UI mocks:

A. export/readability/protection in Excel;  
B. save/re-import no-op round trip;  
C. same-ID Product/category/price/state/group/option update;  
D. new Product + Group + Options in Update mode;  
E. explicit Add-only + empty first initialization;  
F. removed workbook rows do not delete Product/Group/Option;  
G. tampered technical ID / broken parent fails safely;  
H. mixed valid+invalid rows block completely, corrected workbook commits together;  
I. non-authoritative export works but commit is impossible; authoritative device commits after fresh preview;  
J. historical order/reprint snapshot unchanged;  
K. FR/zh-CN + practical layout;  
L. restart + re-export persistence verification.

Manual acceptance also verifies Category short-code behavior for new and existing Categories under the approved decision.

Codex must never pre-check owner acceptance boxes or claim manual PASSED.

## 16. M11/M12 boundary

M10 may create only generic workbook plumbing naturally required for Catalogue work:

- application-owned `.xlsx` adapter/service boundary;
- Infrastructure ClosedXML adapter;
- reusable low-level file-save/read mechanics where genuinely generic.

M10 must not implement/pre-build:

- M11 `Gestion SUSHI 81` four-sheet export contract;
- M11 order/date-range/eligibility/exported-at/correction semantics;
- M11 business filename/intermediate-file behavior beyond generic mechanics M10 itself needs;
- M12 annual archive/hydration/browser;
- M13 installer/final handover.

## 17. Work-package plan after separate authorization

No package is executable yet.

- WP1 — contract DTOs/interfaces + pinned ClosedXML + deterministic export/empty template/protection tests;
- WP2 — workbook parser + Update/Add-only import planner + identity/manifest/Category/parent/no-delete/preview tests;
- WP3 — dedicated atomic SQLite commit + authority/recovery + rollback/concurrency/historical-independence evidence;
- WP4 — WPF Export/Import/Preview workflow + FR/zh-CN + STA/WPF lifecycle/layout evidence;
- WP5 — cross-layer hardening, real `.xlsx` round-trip/tamper/corruption/stale cases, full regression, owner candidate + exact-head CI.

Completion of one work package never authorizes the next automatically; Issue #4 active handoff remains controlling.

## 18. Living-status reconciliation

Preparation identified stale wording written before PR #19 merge. Current-state reconciliation is part of this preparation branch; historical PR/worklog/manual evidence must not be rewritten.

The controlling fact is:

- PR #19 CLOSED / MERGED at `861cfba1dfacbb3289395c0370f6d42765b6c223`;
- Issue #18 CLOSED;
- Issue #4 CLOSED/no active handoff;
- M10 is Preparation/ready for separate implementation authorization;
- M11+ unauthorized.

## 19. Readiness conclusion

**GitHub baseline:** PASS  
**Post-M09 dependency:** PASS / merged  
**M10 workbook specification:** PASS / frozen-and-amended  
**Category `short_code` owner decision:** PASS / Approved 2026-09-17  
**Existing Domain/Application/SQLite/WPF seams:** PASS  
**ClosedXML architecture authorization:** PASS; dependency not yet installed  
**SQLite migration need:** none expected  
**Authority/recovery compatibility:** PASS  
**Historical-snapshot separation:** PASS  
**Material owner decisions:** none open  
**Issue #4:** CLOSED  
**Executable Codex handoff:** none  
**Production implementation:** not started  
**M11+:** unauthorized

### Final disposition

**M10 is READY FOR PROJECT-OWNER IMPLEMENTATION AUTHORIZATION.**

This readiness conclusion is not implementation authorization. Until the project owner separately states **“批准 M10 implementation”**:

- `milestone-10-authorization.md` remains NOT AUTHORIZED;
- no production implementation may start;
- ClosedXML must not yet be added to production projects;
- no executable `CODEX_HANDOFF_READY` may be published;
- Issue #4 remains CLOSED;
- M11/M12/M13 remain unauthorized.