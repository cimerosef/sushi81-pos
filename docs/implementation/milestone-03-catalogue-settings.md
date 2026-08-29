# M03 — In-application catalogue and business settings

**Status:** Prepared detailed task definition — awaiting explicit user authorization to implement  
**Phase:** 6 — Implementation  
**Milestone:** M03  
**Scope type:** Production catalogue/settings persistence, validation and WPF maintenance workflows  
**Authoritative starting baseline:** `main` after M02 merge commit `54421c58a9fc6709ffdf29173b4f8c4791cd5ef4`

## 1. Codex mission

When, and only when, the user explicitly authorizes M03 implementation, implement the complete M03 scope in this file.

M03 turns the M01 foundation into the first real business-data feature: current Categories, Products, structured OptionGroups/Options, and the singleton BusinessSettings record must be durably stored in SQLite and maintainable from the WPF application.

M03 does **not** implement order entry, cart/pricing execution, payment/lifecycle, printing, Hiboutik parsing, catalogue Excel import/export, export, formal handoff UI, disaster recovery, annual archive or installer work.

GitHub `main` is the source of truth. Before coding, read the current versions of at least:

- `AGENTS.md`;
- `README.md`;
- `docs/README.md`;
- `docs/v1-specification-freeze.md`;
- `docs/acceptance-criteria.md`;
- `docs/product-requirements.md`;
- `docs/catalogue-management.md`;
- `docs/business-rules.md`;
- `docs/data-model.md`;
- `docs/architecture.md`;
- `docs/storage-strategy.md`;
- `docs/implementation-plan.md`;
- `docs/implementation-status.md`;
- `src/README.md`;
- `tests/README.md`;
- this file.

Also read all Approved records under `docs/decisions/`, with particular attention to `category-name-uniqueness.md` and `delivery-fee-vat.md`.

Do not use prior ChatGPT/Codex conversation memory or the legacy Excel workbook to invent target behavior.

## 2. Required repository workflow

After explicit M03 authorization:

1. fetch current `main` and verify the M02 merge is present;
2. create a fresh branch named `codex/m03-catalogue-settings` from current `main`;
3. create one implementation PR targeting `main`, title `M03: catalogue and business settings`;
4. keep the PR open and unmerged until explicit user approval;
5. do not begin M04 in the M03 PR;
6. do not force-push or rewrite already-reviewed shared history unless a concrete repository problem makes that unavoidable and it is reported first.

If `main` advances while M03 is being implemented, reconcile normally before final review.

## 3. Acceptance ownership and gate

M03 is responsible for the current-catalogue/settings portions of:

- `AC-CAT-001` — current product identity;
- `AC-CAT-002` — category uniqueness;
- `AC-CAT-003` — current product maintenance portion;
- `AC-CAT-004` — required product fields;
- `AC-CAT-005` — structured product options;
- `AC-ORD-011` — business-settings UI and persistence portion.

The historical-order independence sentence inside `AC-CAT-003` cannot be fully proven until M04 creates order snapshots. Record that cross-milestone dependency honestly; do not fabricate an order table or fake production order subsystem in M03 merely to claim full closure.

Likewise, M03 must expose durable BusinessSettings through a clean application read contract, but the pricing-consumer integration is re-exercised when the M04 pricing engine exists. Do not implement the M04 pricing engine in M03.

M03 may be accepted as a milestone when its authorized scope is complete, tested and manually verified even where the acceptance matrix explicitly retains a later cross-milestone regression.

## 4. Mandatory technical boundaries

Preserve the four production assemblies and dependency direction established in M01:

1. `Sushi81.Pos.Domain` — pure business types/validation; no WPF/SQLite.
2. `Sushi81.Pos.Application` — use cases/contracts; Domain only.
3. `Sushi81.Pos.Infrastructure` — SQLite persistence/migrations and concrete services.
4. `Sushi81.Pos.Desktop` — WPF presentation/composition root.

Do not add Entity Framework, Dapper, a generic repository framework, a mediator framework, an MVVM package, a validation package, ClosedXML, a UI toolkit or another dependency merely for M03.

Continue using:

- `Money` for persisted euro amounts;
- `IIdGenerator` for opaque IDs;
- `IBusinessClock` for application timestamps;
- `ITransactionRunner` / `IApplicationTransaction` for short multi-row writes;
- existing SQLite connection/migration infrastructure;
- existing resource-based French/Chinese localization.

No `double`/`float`/SQLite `REAL` may represent business money or percentage values used in business calculations.

## 5. Production logical model to implement

Create simple, explicit domain/application types for the approved current entities. Do not introduce speculative base classes or aggregate frameworks.

### 5.1 Category

Required data:

- opaque `category_id`;
- operator-visible `name`;
- `created_at`;
- `updated_at`.

Rules:

- name is required after trimming;
- category name uniqueness is business-visible and must reject names differing only by surrounding whitespace, case, or Unicode canonical composition;
- preserve a clean trimmed display name; do not translate catalogue data when UI culture changes;
- no operator-facing category deletion workflow in M03/V1;
- category rename is allowed and products retain the same category identity.

Use one application-owned normalization rule for category uniqueness:

1. trim surrounding whitespace;
2. Unicode normalize to Form C;
3. uppercase invariantly for the stored comparison key.

Persist both display `name` and a technical `normalized_name` and enforce a database unique constraint/index on `normalized_name` as a final concurrency/data-integrity backstop.

Do not strip accents, collapse internal spaces or perform fuzzy matching.

### 5.2 Product

Required data:

- opaque `product_id`;
- operator-facing `code`;
- `name`;
- `category_id`;
- base `price_ttc` as `Money` / integer cents;
- product `vat_rate`;
- `is_active`;
- `discount_eligible`;
- `options_enabled`;
- `created_at`;
- `updated_at`.

Validation:

- code required after trimming;
- name required after trimming;
- category must exist;
- price must be >= €0.00;
- VAT is entered/displayed as a decimal percentage and must be within 0% through 100% inclusive; do not hard-code a smaller list such as only 5.5/10/20 because the frozen specification does not make such a list authoritative;
- active/discount/options flags always persist explicitly.

Technical product-code uniqueness decision for M03: compare codes after trim + Unicode Form C + invariant uppercase, while preserving the trimmed entered display code. Persist a technical `normalized_code` and enforce it uniquely in SQLite. This prevents operational duplicates such as `F5` and `f5` without turning the visible code into a technical primary key.

A code may be edited. Permanent deletion releases it for later reuse.

### 5.3 OptionGroup

Required data:

- opaque `option_group_id`;
- parent `product_id`;
- required non-blank `name`;
- `selection_mode`: exactly `SINGLE` or `MULTI`;
- `is_required`;
- conditional `min_selections`;
- conditional `max_selections`;
- `display_order`;
- timestamps.

Rules:

- `display_order >= 0`;
- SINGLE: `min_selections` and `max_selections` persist as null; required means exactly one at order entry, optional means zero-or-one;
- MULTI: both min and max are explicit non-negative integers; `min <= max`; max must be at least 1; required MULTI requires min >= 1; optional MULTI may use min 0;
- no separate OptionGroup active flag;
- group names do not need to be unique unless a later approved specification says so;
- saved group order must be deterministic and must not be replaced by alphabetical sorting.

When `options_enabled` is true, a required group must have enough active choices to satisfy its minimum/required semantics. A save/deactivation that would make the enabled product's required group impossible to satisfy must be rejected clearly. When product-level `options_enabled` is false, groups/options may remain stored as dormant configuration, but their own structural min/max values must still be valid.

### 5.4 Option

Required data:

- opaque `option_id`;
- parent `option_group_id`;
- required non-blank `name`;
- signed `price_adjustment_ttc` as `Money`;
- `is_active`;
- `display_order`;
- timestamps.

Rules:

- positive, negative and exactly zero adjustments are valid;
- no additional arbitrary adjustment cap is introduced;
- `display_order >= 0`;
- option names are not required to be unique;
- active choices are the choices later visible in new-order entry;
- saved order is deterministic and never automatically alphabetical.

Do **not** persist option VAT as a current editable field. The approved later pricing rule determines adjustment VAT from sign/product VAT.

### 5.5 BusinessSettings

Implement exactly one current settings row containing at least:

- `pickup_discount_rate`, default 10%;
- `pickup_discount_min_total_ttc`, default €15.00;
- `delivery_min_merchandise_total_ttc`, default €30.00;
- `delivery_fee_enabled`, default false;
- `delivery_fee_amount_ttc`, default €0.00;
- `updated_at`.

Rules:

- discount rate accepts 0% through 100% inclusive;
- all money thresholds/fees are >= €0.00;
- the configured fee amount may remain non-zero while the enable flag is false; disabling the fee does not erase the configured amount;
- do not add a delivery-fee VAT setting; later pricing always uses fixed 10% for an enabled non-zero fee;
- no settings-history business feature.

Represent the percentage in application/domain code with `decimal`, never binary floating point. Persist it as a canonical invariant decimal text/fraction representation rather than SQLite `REAL`; for example `0.10` means 10%. Round-trip must be exact. The UI displays percent form (`10`, `12.5`, etc.) and converts explicitly.

## 6. SQLite migration and physical schema

Add the next production migration as version **2**. Keep migration 1 unchanged.

Prefer a new `M03Migrations` definition plus a small production migration aggregator (for example `ProductionMigrations.All`) so the composition root no longer has to know each milestone class individually. The aggregate order must be deterministic: M01 version 1 then M03 version 2.

Migration 2 must create only the current catalogue/settings schema needed by M03:

```text
categories
products
option_groups
options
business_settings
```

Use explicit SQL with these physical principles:

### `categories`

- `category_id TEXT PRIMARY KEY NOT NULL`
- `name TEXT NOT NULL`
- `normalized_name TEXT NOT NULL UNIQUE`
- `created_at_utc TEXT NOT NULL`
- `updated_at_utc TEXT NOT NULL`

### `products`

- `product_id TEXT PRIMARY KEY NOT NULL`
- `code TEXT NOT NULL`
- `normalized_code TEXT NOT NULL UNIQUE`
- `name TEXT NOT NULL`
- `category_id TEXT NOT NULL REFERENCES categories(category_id) ON DELETE RESTRICT`
- `price_ttc_cents INTEGER NOT NULL CHECK(price_ttc_cents >= 0)`
- `vat_rate TEXT NOT NULL`
- boolean columns as `INTEGER NOT NULL CHECK(... IN (0,1))`
- timestamps as non-null ISO-8601 UTC text

Add indexes useful for category filtering/current product listing. Do not add speculative full-text search.

### `option_groups`

- opaque text PK;
- required parent FK to products with `ON DELETE CASCADE` because permanent Product deletion removes its current option configuration;
- name, selection mode, required flag, nullable min/max, non-negative display order, timestamps;
- database CHECK for selection mode and basic min/max non-negativity where practical;
- unique `(product_id, display_order)`.

### `options`

- opaque text PK;
- required parent FK to option_groups with `ON DELETE CASCADE`;
- name;
- signed `price_adjustment_ttc_cents INTEGER NOT NULL`;
- active flag;
- non-negative display order;
- timestamps;
- unique `(option_group_id, display_order)`.

### `business_settings`

Use a singleton key constrained to one row, for example integer key `1` with `CHECK(singleton_id = 1)`.

Columns:

- canonical discount-rate text;
- pickup minimum cents >= 0;
- delivery merchandise minimum cents >= 0;
- fee enabled boolean;
- fee cents >= 0;
- updated UTC timestamp.

Migration 2 must insert the default singleton settings row exactly once. Re-running current-version startup must not duplicate or reset edited settings.

Do not seed demo categories/products/options.

M01 migration safety rules still apply: upgrading an existing database uses the existing pre-migration validated snapshot path; failure rolls back and never deletes/recreates `live.db`.

## 7. Persistence contracts and implementation

Create narrow Application-owned persistence contracts under logical `Catalogue` and `Settings` areas. Do not create a generic `IRepository<T>`.

Recommended responsibilities:

- catalogue query service: list/filter product summaries, load one complete product editor aggregate, list categories;
- catalogue mutation store: create/rename category; create/update product aggregate; activate/deactivate; permanent product delete;
- settings store: load and update singleton BusinessSettings.

All multi-row product saves must use the existing `ITransactionRunner` so Product + OptionGroups + Options either commit completely or roll back completely.

For a product aggregate update:

- preserve IDs of existing records;
- allocate new opaque IDs only for newly created records through `IIdGenerator`;
- preserve original `created_at` for existing records;
- set `updated_at` from `IBusinessClock.UtcNow` (or current equivalent) only on successful mutation;
- execute explicit deletions only for OptionGroups/Options the operator explicitly removed in the full editor;
- never infer a product permanent deletion from absence in a list/query;
- renumber group/option display orders to contiguous deterministic values when the operator reorders/deletes within the editor.

Convert SQLite unique/FK violations that can arise from a race or stale edit into stable application validation/conflict results; do not surface raw SQL to the operator.

Reads must use read-only connections where the existing infrastructure supports them.

## 8. Application use cases

Implement testable Application-layer use cases/services. WPF must not execute SQL directly.

At minimum provide operations equivalent to:

### Catalogue reads

- `ListCategories`
- `ListProducts(search, categoryFilter, activeFilter)`
- `GetProductForEdit(productId)`

For the maintenance list, support:

- code/name text search;
- category filter;
- All / Active / Inactive filter.

The catalogue is small; prioritize correct, simple deterministic behavior over speculative indexing/search infrastructure.

### Category mutations

- `CreateCategory(name)`
- `RenameCategory(categoryId, name)`

No category-delete use case.

### Product mutations

- `CreateProduct(complete editor draft)`
- `UpdateProduct(productId, complete editor draft)`
- `SetProductActive(productId, bool)`
- `DeleteProduct(productId)`

Create/update validates the entire Product + group/option structure before opening the write transaction. Recheck uniqueness/existence in the transaction where needed.

Permanent deletion is a real current-catalogue delete, not soft-delete. Inactive state remains the temporary-unavailability mechanism.

### Settings

- `GetBusinessSettings`
- `UpdateBusinessSettings`

The returned settings contract becomes the clean source M04 pricing will consume; do not make WPF resources/config JSON the business-settings authority.

### Error/result model

Use a small application-owned result/validation representation capable of returning field-level or operation-level messages without throwing for normal invalid operator input. Reserve exceptions for unexpected infrastructure/programming failures.

Do not leak Sqlite exception text, file paths or stack traces into normal UI messages.

## 9. Authority and recovery milestone boundary

Do not pull M06/M07 scope forward.

M03 must keep all durable mutations behind Application use cases and the existing transaction boundary so M06 can centrally attach authoritative/read-only enforcement and complete recovery scheduling without rewriting WPF screens.

Do **not** invent a fake persistent handoff/authority state or wire the M02 feasibility harness under `tools/` into production `src/` during M03.

Do **not** create an ad-hoc non-durable change-sequence counter merely to claim M06 recovery scheduling is complete. M06 owns the final connection of recovery/authority primitives to every business mutation.

M03 tests still must prove write transactions are atomic and migration failure-safe.

## 10. WPF shell/navigation result

Evolve the foundation shell into a small M03 administration shell without pretending the later Caisse/dashboard exists.

Use the existing MainWindow and localization system. Add exactly two M03 primary destinations:

- **Catalogue**;
- **Settings / Paramètres**.

A simple `TabControl`, sidebar or compact navigation is acceptable; choose the simplest reliable WPF structure. Do not add a third-party navigation framework.

Keep the existing UI language switch available.

Every new user-facing label/message must be resource-based in `fr-FR` and `zh-CN`. Catalogue names, product names/codes and other operator-entered data must remain byte/business-value equivalent when language changes.

Do not use `.Result`, `.Wait()` or other synchronous blocking on asynchronous persistence/UI operations.

## 11. Catalogue maintenance UI — exact interaction contract

### 11.1 Main catalogue view

Provide a dedicated catalogue screen with:

- search box for code/name;
- category filter including an All value;
- status filter: All / Active / Inactive;
- current product list showing at least Code, Name, Category, TTC Price, VAT, Active;
- New Product action;
- Edit selected product action (double-click may also edit);
- Activate/Deactivate explicit action;
- Permanent Delete explicit action;
- Manage Categories action.

Inactive rows must be visually identifiable without relying only on color.

An empty catalogue must show a clear empty state and still allow category/product creation.

### 11.2 Product editor

Use an inline editor panel or modal dialog. It must expose:

- Code;
- Name;
- Category selector;
- TTC Price;
- VAT %;
- Active;
- Retrait-discount eligible;
- Options enabled;
- structured OptionGroups/Options editor;
- Save;
- Cancel.

No keystroke persists business data. Save commits the complete validated edit; Cancel discards it.

Do not silently discard a dirty edit. If the operator tries to select another product, close the editor or navigate away while dirty, either block with a clear instruction or show Save/Discard/Cancel. Prefer the simpler implementation that is reliable and fully testable.

Validation errors remain visible next to/reasonably near the affected field and do not close the editor.

### 11.3 Category manager

Provide a compact manager that can:

- list current categories;
- create category;
- rename selected category;
- Save/Cancel the active category edit.

Do not show category delete.

Duplicate normalized name must be rejected with a business-friendly message.

When a category is renamed, product lists/selectors refresh to the new name without changing product identity.

### 11.4 Product activation/deactivation

Activation/deactivation is an explicit durable action. It does not delete the product or its options.

If invoked from the product editor it may be included in Save. If invoked from the list toolbar, the explicit button/action itself counts as the save intent; refresh after success.

### 11.5 Product permanent deletion

Deletion requires explicit confirmation showing enough identity to avoid deleting the wrong product (at least product code + name).

On confirm:

- delete the current Product and its current OptionGroups/Options transactionally;
- leave Category intact;
- release the product code for reuse;
- refresh the list.

M03 has no historical order tables; do not create them. M04 will prove later snapshots survive catalogue deletion.

### 11.6 OptionGroups editor

Within the Product editor allow:

- Add Group;
- Edit Group;
- Delete Group;
- Move Up;
- Move Down.

Group fields:

- Name;
- Single/Multi;
- Required/Optional;
- Min and Max only when Multi.

Switching to SINGLE clears/drops min/max in the saved draft.

Display group order visibly and save it deterministically.

Deletion of a group removes its current child options only when the Product Save is confirmed. Cancelling the Product editor restores the last persisted state.

### 11.7 Options editor

For the selected group allow:

- Add Option;
- Edit Option;
- Delete Option;
- Active/Inactive toggle;
- Move Up;
- Move Down.

Fields:

- Name;
- signed TTC adjustment, including negative and €0.00;
- Active.

Display/save option order deterministically.

If an edit would leave an enabled required group with insufficient active choices, Product Save is blocked with a clear validation error.

## 12. Settings UI — exact interaction contract

Provide one Settings screen with these editable fields:

- Retrait discount rate (%);
- Minimum total after Retrait discount (€);
- Livraison merchandise minimum (€);
- Delivery fee enabled;
- Fixed delivery fee amount (€).

Show the fixed delivery-fee VAT rule as read-only explanatory text if useful, but **do not** provide an editable delivery-fee VAT control.

Provide Save and Cancel/Reload behavior. No field persists while typing.

On first database creation the screen must show the frozen defaults: 10%, €15.00, €30.00, disabled, €0.00.

After Save + application restart, edited settings must reload exactly.

## 13. Localization requirements

Add resource keys for all new M03 UI labels, buttons, validation summaries and confirmation messages in French and Simplified Chinese.

Use concise operational translations. At minimum cover concepts equivalent to:

- Catalogue / 商品目录;
- Paramètres / 设置;
- Rechercher / 搜索;
- Catégorie / 分类;
- Tous / 全部;
- Actifs / 启用;
- Inactifs / 停用;
- Nouveau produit / 新建商品;
- Modifier / 编辑;
- Enregistrer / 保存;
- Annuler / 取消;
- Activer / 启用;
- Désactiver / 停用;
- Supprimer définitivement / 永久删除;
- Gérer les catégories / 管理分类;
- Code / 编码;
- Nom / 名称;
- Prix TTC / 含税价;
- TVA / 增值税;
- Éligible remise Retrait / 可享自取折扣;
- Options activées / 启用商品选项;
- Groupes d’options / 选项组;
- Choix / 选项;
- Obligatoire / 必选;
- Facultatif / 可选;
- Sélection unique / 单选;
- Sélection multiple / 多选;
- Minimum / 最少;
- Maximum / 最多;
- Ajustement TTC / 含税调整;
- Monter / 上移;
- Descendre / 下移;
- Remise Retrait / 自取折扣;
- Minimum après remise / 折扣后最低金额;
- Minimum Livraison / 配送最低金额;
- Frais de livraison / 配送费.

Exact punctuation/capitalization may follow the existing resource convention. Do not localize or mutate operator-entered catalogue data.

## 14. Validation behavior and messages

Normal invalid input must be corrected before commit and must not partially write.

Cover at least:

- blank category name;
- duplicate normalized category name on create/rename;
- blank product code/name;
- duplicate normalized product code on create/update;
- missing category;
- negative product price;
- VAT outside 0..100 or unparsable;
- blank option-group name;
- invalid SINGLE persisted min/max;
- MULTI min/max structural errors;
- required MULTI min 0;
- blank option name;
- invalid display ordering/duplicate positions at persistence boundary;
- enabled required group with insufficient active options;
- settings percentage outside 0..100;
- negative settings money;
- stale/nonexistent IDs on update/delete.

SQLite constraints remain the last defensive layer; Application validation is the operator-facing layer.

## 15. Concurrency/stale-edit behavior

V1 is single-writer, but stale in-memory editor state can still occur within one process.

Do not silently recreate a Product/Category that was deleted between load and save. Update/delete of a missing expected ID returns a conflict/not-found result and requires refresh.

For M03, optimistic row-version columns are not required. Use ID existence + database uniqueness/FK constraints and transactional revalidation. Do not add a generalized concurrency subsystem.

## 16. Test implementation — mandatory inventory

Use existing MSTest projects. Do not create a test project unless existing dependency boundaries make a concrete case unavoidable.

### Domain tests

Add deterministic tests for:

- Category normalization/display trimming;
- Product required fields;
- Product price non-negative;
- VAT accepted/rejected boundaries;
- SINGLE required/optional structural semantics;
- MULTI required/optional min/max boundaries;
- signed/zero Option adjustment;
- active-choice satisfiability for required enabled groups;
- BusinessSettings defaults;
- BusinessSettings rate/money validation.

### Application tests

Use fakes, not a mocking package. Cover:

- create category normalization/duplicate result;
- rename category;
- create product command validation before transaction;
- complete product aggregate create/update mapping;
- new IDs allocated only to new records;
- explicit option/group delete intent;
- deterministic reordering;
- activation/deactivation;
- permanent delete confirmation/use-case boundary does not imply soft-delete;
- settings read/update contract;
- normal invalid input returns validation result instead of raw infrastructure exception.

### Infrastructure integration tests

Use isolated temp databases only. Cover at least:

1. migration 1 -> 2 upgrade preserves existing M01 sentinel/foundation data;
2. fresh migration creates all five M03 tables and one default settings row;
3. rerun migration is idempotent and does not reset edited settings;
4. category normalized unique constraint including case/outer-space equivalence through application path;
5. product normalized code uniqueness;
6. FK prevents Product with missing Category;
7. create full Product + two groups + multiple options and reload exact identities/order/amounts;
8. update product code/name/category/price/VAT/flags without changing product ID;
9. category rename preserves category ID/product association;
10. deactivate/reactivate product preserves row/options;
11. permanent delete Product cascades current groups/options but leaves Category;
12. released product code can be reused by a new product ID;
13. option/group reorder persists exactly;
14. settings default load;
15. settings edit survives store recreation/process-like reopen;
16. injected failure in a full product aggregate save rolls back every row;
17. duplicate/conflict failure leaves pre-existing catalogue unchanged;
18. failed migration does not delete/recreate database and pre-migration recovery behavior remains intact.

Query actual persisted cents/text/booleans/foreign keys rather than only testing in-memory DTOs.

### Desktop/localization tests

Keep view-model/business behavior testable without requiring automated mouse control.

Cover at least:

- French and Chinese resource keys exist for all M03 visible strings;
- language switch changes M03 labels but does not change bound catalogue/settings values;
- Product editor Cancel leaves persistence untouched;
- Settings Cancel leaves persistence untouched;
- dirty-editor navigation is not silently discarded;
- validation errors are represented for UI binding;
- async save command cannot double-submit while already executing.

Do not add a brittle pixel/UI-automation suite in M03.

### Architecture regressions

Existing dependency-boundary tests must continue to pass. Add checks if necessary so:

- Domain/Application still expose no WPF/Sqlite types;
- Desktop does not become the persistence layer;
- no new forbidden package/reference breaks the four-layer architecture.

## 17. Manual Windows acceptance checklist

Codex must produce a self-contained `win-x64` publish suitable for manual M03 testing. The user must not need the .NET SDK/Git/source on the test machine for the final practical UI check.

Manual acceptance should be driven one small step at a time after automated review. The required scenario is:

1. launch clean/isolated M03 data environment and confirm French UI starts;
2. switch to Chinese and back; no hang, catalogue data unchanged;
3. verify empty catalogue state;
4. create categories `Entrées` and `Plats`;
5. attempt ` plats ` / case-equivalent duplicate and confirm rejection;
6. create one ordinary active product with code/name/category/price/VAT and no options;
7. create one product with options enabled, at least one SINGLE group and one MULTI group, positive/negative/zero choices and explicit ordering;
8. edit the first product's code and verify same internal record persists while the old visible code becomes reusable only after appropriate edit/delete;
9. deactivate/reactivate a product and verify it remains present in maintenance list/filter;
10. reorder option groups/options, restart app and verify order persists;
11. cancel an unsaved edit and verify prior saved state returns;
12. rename a Category and verify Products display the new category name;
13. permanently delete a Product through confirmation and verify its current options disappear and its code can be reused for a new Product;
14. inspect default settings;
15. edit all five settings, Save, restart and verify exact persistence;
16. confirm no editable delivery-fee VAT setting exists;
17. confirm no Caisse/order/payment/Excel-import/export/printing/Hiboutik features were accidentally introduced.

Use synthetic catalogue values only.

## 18. Startup/composition requirements

Update `CompositionRoot` so startup:

1. initializes M01 paths/config/logging as today;
2. runs the complete production migration list including M03 version 2;
3. constructs the M03 SQLite stores/application services/view models;
4. shows the M03 shell only after successful initialization;
5. preserves the existing visible startup-failure behavior;
6. preserves clean async localization switching;
7. disposes any owned async resources cleanly.

Do not hide migration/catalogue startup failure by silently switching to an in-memory catalogue.

For manual/test isolation, add a test harness/path seam only if needed; production `%LOCALAPPDATA%\Sushi81 POS\Data\live.db` rules remain unchanged.

## 19. Logging/privacy

M03 diagnostics may log technical operation type, opaque IDs, counts and exception categories.

Do not log full catalogue payloads unnecessarily. In particular do not introduce logging of future customer/order-sensitive fields. No credentials/business database/build output are committed.

Validation messages presented to the operator must not include local DB paths, raw SQL or stack traces.

## 20. Explicitly out of scope

Do not implement or scaffold production behavior for:

- product browsing/add-to-cart/Caisse;
- option-selection order-entry dialog;
- custom per-order adjustment entry;
- discount/VAT/delivery pricing engine;
- Order/OrderItem/Tax/Payment tables;
- order history/snapshot regression production entities;
- customer/telephone/order search;
- payments/close/cancel/dashboard;
- printer adapters/layouts;
- Hiboutik paste parser;
- ClosedXML or catalogue `.xlsx` import/export;
- Gestion export;
- production pairing/handoff/DR UI;
- archive databases;
- installer/update subsystem;
- roles/login/accounts;
- legacy Hiboutik emergency-order model.

M10 owns `.xlsx` catalogue work. M04 owns order entry/pricing/snapshots. M06/M07 own final production authority/recovery/handoff enforcement.

## 21. Prohibited shortcuts

M03 fails review if implementation:

- uses product code/category name as technical primary key;
- stores business money/percentages as floating-point `REAL`/`double`;
- persists editor fields character-by-character;
- omits explicit Product delete confirmation;
- implements category deletion despite V1 not requiring it;
- treats Product deactivation as deletion;
- automatically alphabetizes option group/choice order;
- allows invalid required option configuration to commit;
- hard-codes the five business settings in pricing-facing code instead of persisting the singleton;
- exposes delivery-fee VAT as editable;
- introduces Excel import/export early;
- writes SQL directly from WPF;
- adds EF/Dapper/MVVM/framework dependencies without a reviewed concrete need;
- silently resets/recreates `live.db` on failure;
- adds sample/demo production catalogue data;
- bypasses localization resources for new visible UI;
- weakens M01 architecture/build warnings/tests;
- modifies M02 handoff harness or approved handoff semantics as part of unrelated M03 work.

## 22. Required verification commands

Before reporting completion run from repository root:

```powershell
dotnet --info
dotnet restore Sushi81.Pos.sln
dotnet build Sushi81.Pos.sln -c Release --no-restore
dotnet test Sushi81.Pos.sln -c Release --no-build
dotnet publish src/Sushi81.Pos.Desktop/Sushi81.Pos.Desktop.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=false
```

Requirements:

- Release build: 0 warnings / 0 errors;
- all prior M01/M02 solution tests remain green;
- all new M03 tests green;
- self-contained win-x64 publish succeeds;
- GitHub Actions CI succeeds on final PR head.

If the implementation environment cannot perform an interactive WPF check, do not claim one; produce the publish artifact/evidence and leave the specific manual checklist for ChatGPT/user execution.

## 23. Documentation/evidence updates in the M03 PR

During implementation update:

- `docs/implementation-status.md` with concrete M03 evidence/status;
- `src/README.md` to reflect implemented M03 business feature;
- `tests/README.md` with M03 test coverage;
- this contract only for factual execution notes that do not rewrite its requirements.

Do not rewrite Approved Phase 1–5 specifications merely to match an implementation shortcut. A genuine contradiction stops that path for review.

At completion, status should distinguish:

- fully evidenced M03 current-catalogue/settings behaviors;
- `AC-CAT-003` historical-order independence portion deferred to M04 snapshot regression;
- AC-ORD-011 pricing-consumer cross-check repeated in M04;
- M04 remains Not started/unauthorized.

## 24. Codex completion report contract

Post one durable PR comment beginning exactly with the handoff ID supplied by ChatGPT, for example:

`CODEX_DONE: M03-IMPLEMENT-01`

Include:

- final branch/head SHA;
- PR number;
- exact migration version/name;
- concise production files/areas changed;
- final test totals by project;
- Release build result;
- self-contained publish result;
- CI run number/conclusion;
- M03 acceptance mapping and any intentionally deferred cross-milestone evidence;
- manual WPF verification status (performed/not performed, never guessed);
- confirmation no real business/customer/payment/credential data was committed;
- confirmation M04 was not started;
- blockers/unresolved findings, or `none`;
- mandatory `browserNotification: succeeded|unavailable|failed` line after the notification attempt.

Do not merge the M03 PR. Stop and wait for ChatGPT review and explicit user merge approval.

## 25. M03 implementation quality bar

The intended outcome is not merely “CRUD screens that work.” M03 must leave a durable, migration-safe, layered current-catalogue/settings subsystem that M04 can consume directly without replacing its data model or undoing UI shortcuts.

Prefer small explicit code over abstractions that save little. Prefer database constraints plus application validation over trusting either layer alone. Prefer one complete product editor save transaction over many incidental writes. Preserve opaque identities and saved option order from the beginning.

If a pure implementation choice is not specified here and does not alter business behavior, choose according to the standing order:

**reliability > simplicity > maintainability > operational clarity > novelty.**
