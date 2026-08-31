# M03 implementation worklog

**Status:** Remediation complete through FIX-16; automated evidence green; full operator Windows/WPF acceptance passed on the final head
**Milestone:** M03 — In-application catalogue and business settings

This worklog records the implementation/evidence handoff for the active M03 PR. The production scope is limited to
current catalogue and singleton business settings persistence, validation and localized maintenance UI.

Authoritative contract: `milestone-03-catalogue-settings.md`.  
Authorization: `milestone-03-authorization.md`.

## Delivered

- Domain/application contracts for Category, Product, OptionGroup, Option and BusinessSettings.
- SQLite migration 2 (`create-catalogue-and-business-settings`) with exactly the five M03 tables and one default
  settings singleton; no demo rows.
- Transactional product aggregate persistence with opaque IDs, explicit child deletion and deterministic order.
- Localized French/zh-CN Catalogue and Settings WPF destinations with category/product/group/option maintenance,
  activation/deactivation, permanent-delete confirmation, filters and settings round-trip editing.
- Catalogue column headers use a direct presentation seam so all six labels remain visible and localized during live
  French/zh-CN switching; fixed Code/Price/VAT/Active widths and flexible Name/Category widths keep the grid readable
  at the minimum, resized and maximized window sizes.
- Application, domain, infrastructure and desktop-seam tests using only synthetic data.

## Verification

- .NET SDK 10.0.400, Windows x64.
- Release restore/build passed (0 warnings, 0 errors).
- Release solution tests: **215 passed, 0 failed, 0 skipped**: Domain 10, Application 11, Infrastructure integration 33,
  Architecture/localization 37, OneDrive protocol 32, and GitHub wrapper/harness 92.
- Self-contained `win-x64` publish passed with `PublishSingleFile=false`.
- Post-fix implementation head `710d95b2dcb75995428ace25cc38081afd48127a` passed GitHub Actions Continuous integration run
  **#133** (success). The follow-up filter-state remediation head `e6fc0ddeb8077cab33758da9f62cb615300b2cb7` passed
  Continuous integration run **#137** (success; check URL is recorded in the PR completion evidence). The category
  binding remediation head `1053c9b210cac15343959aac8f9ffa2c13ccd9b8` passed local Release verification; its CI result
  is recorded in the matching PR completion evidence. The status binding remediation head
  `e56ec77b4d13898c40d57be600652309d77938df` passed local Release verification; its CI result is recorded in the matching
  PR completion evidence. The status lifecycle remediation head `a3bab3de2b43e14a415f4251ab1b6dd77cf61aa0` passed local
  Release verification; its CI result is recorded in the matching PR completion evidence. The category-manager layout
  remediation in the final evidence head also passed local Release verification; its final CI result is recorded in the
  matching PR completion evidence. The `M03-MANUAL-UI-CATALOGUE-HEADER-FIX-09` final head
  `9bd4e057400abe0110ae95fdeb84efed43eb80a4` passed GitHub Actions Continuous integration run **#157** (success). The
  `M03-MANUAL-UI-OPTION-GROUP-ADD-CRASH-FIX-10` implementation head `64dc110bd2d6c6b9a623d4c3ba487210e014c178` passed
  GitHub Actions Continuous integration run **#173** (success). The `M03-MANUAL-UI-OPTION-GROUP-LAYOUT-FIX-11`
  implementation/evidence head `2ad30a7d1a4d12c256fc274b808873ef1ff8278e` passed GitHub Actions Continuous integration run
  **#179** (success). The FIX-11 addendum alignment head `96f9330ceae21c0fc224c0eb9a6083ecfcebcc10` passed GitHub Actions
  Continuous integration run **#183** (success). The `M03-MANUAL-UI-LIVE-FILTER-FIX-12` implementation/evidence head
  `b3e34d0d79a4efd39dd3817d19cd107a0f7af50c` passed GitHub Actions Continuous integration run **#189** (success). The
  final evidence-documentation head `47a21237f7296c002986807918c733fbc58e5858` passed GitHub Actions Continuous integration
  run **#193** (success).

## Remediation handoff `M03-REVIEW-FIX-02`

The review correction pass is intentionally limited to M03 catalogue/settings behavior and its evidence:

- **A — Read safety:** every M03 read path uses the existing read-only SQLite connection helper; missing-database reads do not
  create a database.
- **B — Numeric integrity:** money, VAT, MULTI bounds, option adjustments and settings fields reject invalid input without
  coercion; Save is blocked and state remains unchanged.
- **C — Validation UX:** validation carries stable codes and is rendered with localized, field-visible French/zh-CN messages;
  normal validation does not expose SQL, paths or stack traces.
- **D — Product close safety:** dirty product edits cannot be silently discarded; title-bar close is blocked with localized
  guidance and explicit Cancel leaves persistence unchanged.
- **E — Category edits:** Create/Rename use an explicit edit buffer with Save/Cancel; typing alone never persists and duplicate
  names are localized.
- **F — Activation:** the toolbar action reflects active/inactive state (`Deactivate`/`Activate`) and is disabled with no
  selection.
- **G — Localization refresh:** language switching immediately refreshes the All filter and visible M03 labels while business
  values remain unchanged; option fields are visibly labeled.
- **H — Evidence inventory:** deterministic domain, application, infrastructure (1–18) and desktop/localization coverage is
  recorded in the M03 test projects; the solution total is 188.
- **I — Documentation:** this worklog and `docs/implementation-status.md` retain the automated/interactive acceptance boundary
  and explicitly defer M04.

## Manual-test remediation handoff `M03-MANUAL-UI-FILTER-FIX-03`

The operator's first M03 Windows/WPF pass found that an empty catalogue displayed blank category/status filters, and language
switching could clear their visible selection. The same pass found Edit and permanent Delete remained enabled with no selected
product. The follow-up fix preserves semantic filter keys (`all`, `Active`, `Inactive`) while replacing localized labels,
initializes both filters to All, restores valid category selection after refresh, deterministically falls back to All when a
category disappears, and binds Edit/Delete to the same selected-product capability guard as activation. The added desktop
regressions cover French/zh-CN round trips, Active/Inactive preservation, category identity preservation, refresh/fallback and
no-selection action state. M03 remains Partial pending resumption and completion of the full operator checklist.

## Manual-test remediation handoff `M03-MANUAL-UI-CATEGORY-BINDING-FIX-04`

The operator's resumed Windows/WPF pass confirmed that the persisted Chinese language and status filter were correct, but the
category ComboBox still rendered blank on first display of an empty catalogue. The root cause was binding the rebuilt
`CategoryFilters` collection through object-instance `SelectedItem` identity while WPF transiently cleared selection during
collection replacement. The narrow remediation exposes the stable semantic `SelectedCategoryId` key (with `Guid.Empty` for the
localized All item), binds the ComboBox through `SelectedValuePath="Id"`, and preserves the key through localization, refresh and
missing-category fallback. The setter tolerates transient null writes while the collection is empty and deterministically falls
back to All once the rebuilt collection is available. Two desktop regressions exercise this binding-facing property shape for
fresh empty state, FR/zh-CN All-label replacement, real-category persistence and removal fallback; existing status-filter and
no-selection action tests remain green. M03 remains Partial pending the operator rerun of the full WPF checklist after this fix.

## Manual-test remediation handoff `M03-MANUAL-UI-STATUS-BINDING-FIX-05`

The subsequent operator pass confirmed the category binding fix in the real UI, but switching from persisted zh-CN to French
left the status ComboBox blank. The first status-key remediation exposed `SelectedStatusKey` and a semantic
`SelectedValuePath="Key"` binding, but the operator's next pass showed that rebuilding the `StatusFilters` collection could still
race with WPF's asynchronous selection lifecycle. That intermediate attempt is superseded by the lifecycle remediation below.

## Manual-test remediation handoff `M03-MANUAL-UI-STATUS-LIFECYCLE-FIX-06`

The operator reproduced the blank status selection in the opposite French → zh-CN direction after the first status-key fix. The
root cause was collection replacement itself: `StatusFilters.Clear()` followed by new option instances allowed WPF to process a
transient null/invalid selection after localization returned. The final narrow remediation initializes exactly three bindable
status options once (`All`, `Active`, `Inactive`) and updates only their mutable localized labels in place. Their immutable keys,
object identities and collection positions therefore remain stable for the view-model lifetime while the ComboBox continues to
bind `SelectedValuePath="Key"` to `SelectedStatusKey`. Invalid values safely resolve to All, and no transient null can be
introduced by a collection rebuild because localization no longer rebuilds that collection. The added regression captures all
three option objects, verifies identity/position and labels across fr/zh localization and refresh, and proves All/Active/Inactive
semantic selection preservation plus invalid/null fallback. M03 remains Partial pending the operator rerun of the full WPF
checklist after this fix.

## Manual-test remediation handoff `M03-MANUAL-UI-CATEGORY-LAYOUT-FIX-07`

The operator's post-lifecycle WPF pass confirmed the filter round-trip, then exposed a category-manager layout defect: the
previous `DockPanel` made its last child (the horizontal action area) fill all remaining height, and the default-stretching
buttons became tall narrow blocks when the dialog was resized or maximized. The narrow remediation replaces that root with a
five-row `Grid`: the category list alone uses the resizable star row, while the editor heading, text box, validation message and
action area use content-sized `Auto` rows. The action area is a top-aligned `WrapPanel` with explicit minimum button sizes so
French and Chinese labels remain readable and wrap naturally at the default dialog width or when resized. Category Create,
Rename, Save, Cancel, close-safety and localization semantics are unchanged. A deterministic desktop structural regression
asserts the star/Auto row contract, content-sized action panel, non-stretching alignment and removal of the old DockPanel root.
M03 remains Partial pending the operator rerun of the category-manager checklist and the remaining manual WPF checks.

## Manual-test remediation handoff `M03-MANUAL-UI-CATEGORY-CREATE-ACTION-FIX-08`

The operator's next WPF checklist step showed no visible reaction when clicking `Créer`, even though the prior handler only
flipped the edit buffer and toggled Save/Cancel. The interaction was not operationally clear because the editable name field
did not receive focus, the edit field remained enabled outside edit mode, and Create/Rename stayed available to compete with an
active edit. The narrow remediation now exposes actionable state on the pure `CategoryEditBuffer` (`CanBeginEdit`, `CanSave`,
`CanCancel`, and explicit `CompleteSave`), disables the name field until Create/Rename begins an edit, focuses the field with a
caret (or selects the existing name for Rename), enables Save/Cancel immediately, and disables Create/Rename while editing.
Selection updates re-enable Rename only when a category is selected; Cancel and successful Save restore the non-edit state,
refresh the catalogue, clear the field and return focus to the list. No keystroke persists data, no category-delete action was
added, and close still abandons unpersisted edits. Deterministic presentation tests cover the create/rename/action-state
lifecycle; the FIX-07 layout structural regression and existing business tests remain green. M03 remains Partial pending the
operator rerun of category creation/rename and the remaining WPF checklist.

## Manual-test remediation handoff `M03-MANUAL-UI-CATALOGUE-HEADER-FIX-09`

The operator's next WPF pass successfully created and displayed a synthetic product (`TST001`, `Produit test simple`,
`Entrées`, TTC `8.50`, VAT `10`, Active). Acceptance then stopped because every Catalogue DataGrid header was blank: the
rows were visible, but there were no Code/Name/Category/TTC/VAT/Active labels. The root cause was binding each
`DataGridColumn.Header` through `RelativeSource AncestorType=Window`; DataGridColumn is not in the Window visual/logical tree,
so those bindings cannot resolve.

The narrow fix removes those unsupported column bindings, gives the grid a named presentation seam, and applies a pure
`CatalogueHeaderSet` from `ShellViewModel.Localized` directly to all six columns during construction, load and every completed
language change. French and zh-CN values therefore update live without reopening the window or changing product data. Code,
TTC, VAT and Active columns have fixed readable widths while Name and Category share the remaining space with star sizing,
including at the minimum, resized and maximized window sizes. A deterministic desktop regression exercises the real French and
zh-CN resource dictionaries through the seam, round-trips back to French, and verifies the six-column sizing/presentation
contract without relying on a screenshot or source text alone. M03 remains Partial pending the operator rerun of the full WPF
checklist with the header labels now visible.

## Manual-test remediation handoff `M03-MANUAL-UI-OPTION-GROUP-ADD-CRASH-FIX-10`

The operator's next WPF pass reproduced a user-visible crash twice after `New Product` → enable Options → `+ Groupes
d'options`; manual acceptance stopped at that point. The defect-escape retrospective found that the prior implementation
created a `Border`, assigned its `StackPanel` child, and then inserted that child directly into the parent panel. The first
dynamic add therefore attempted to attach an already-owned WPF element and raised the logical-parent exception during the
button click. The existing source/pure tests did not construct a shown dialog and click the dynamic control, so this was an
insufficient regression seam.

The narrow fix keeps the intended `Border` as the `GroupEditor.Container` and inserts/removes/reorders that container as the
single visual child. The dialog captures the shell localization dictionary explicitly so child group/option controls do not
fall back to English when their `DataContext` is not inherited. The mode label now uses a dedicated `SelectionMode` resource.
No persistence path changed: Add Group/Add Option, mode changes, remove and reorder update only the in-memory editor until
Product Save; Cancel closes without a create call. SINGLE clears and disables min/max, while MULTI enables their existing
validation path.

The regression is an actual STA WPF lifecycle test: it shows an owner and modal product dialog, raises the real Add Group,
Add Option, mode, Move Up, Remove and Cancel events, inspects the visual container and localized labels, and asserts that
the synthetic store received no create call. This also audits adjacent event wiring and initialization order without adding
business-feature scaffolding. M03 remains Partial pending the operator rerun of the complete Windows/WPF checklist against the
fixed artifact.

## Manual-test remediation handoff `M03-MANUAL-UI-OPTION-GROUP-LAYOUT-FIX-11`

The next operator pass reached the first rendered option group, but the French `Mode de sélection` label was clipped to
`Mode de sélectio` at the normal Product Editor size. The defect-escape retrospective identified the fixed `Width = 90`
selection-mode label as the direct cause and expanded the audit to every fixed-width or action-row element in the group and
option editors across French and zh-CN at normal and larger supported widths.

The narrow layout fix removes the hardcoded selection-mode label width and uses an Auto / gap / Star Grid so the localized
label receives its natural width while the ComboBox remains usable. Minimum/maximum fields and group actions now use
content-sized WrapPanels with a consistent peer-button margin/padding contract; OptionEditor action buttons use the same
horizontal padding and margin alignment. Existing numeric TextBox widths and all editor semantics remain unchanged. No
persistence, validation, localization-resource, or FIX-10 lifecycle/container behavior was changed.

The regression is a real STA WPF modal-dialog test. It adds a group and option after rendering, verifies visible TextBlock
natural widths against `ActualWidth` (with WPF margins accounted for), enabled-button desired widths, and peer-button height
spread in French and zh-CN, then resizes the dialog and repeats the assertions. It closes through the existing safe test seam so no synthetic product write
occurs. The test covers SelectionMode, Required, min/max, group actions and option labels/actions in the actual visual tree.
M03 remains Partial pending the operator rerun of the complete Windows/WPF checklist against the fixed artifact.

## Manual-test remediation handoff `M03-MANUAL-UI-LIVE-FILTER-FIX-12`

The operator's next catalogue pass found that the visible search, category and status controls changed their bound values but
did not update the product list until the explicit refresh button was clicked. The root cause was that the original setters
only changed presentation state; the only query trigger was the manual `RefreshAsync` command. This was misleading in the live
UI because the controls looked like immediately applied filters.

The narrow fix adds automatic product requery for status and category changes, plus a 250 ms search debounce so a text edit
does not issue one database query per keystroke. Every automatic and manual reload now uses an immutable filter snapshot,
monotonic request version and an owned cancellation source. A newer request cancels its predecessor, stale stores that ignore
cancellation cannot commit products or categories, and only the current full refresh may clear `IsBusy`. Category collection
rebuilds suppress transient WPF binding callbacks, while localization updates semantic labels in place without duplicate
filter queries. The existing refresh button remains an explicit force reload. If an automatic query fails, the desktop shell
reports the existing localized generic error rather than exposing storage details.

Four architecture regressions cover mixed All/Active/Inactive transitions, automatic category filtering, selected-product
clearing after a filter excludes it, debounced search, latest-wins behavior when an older query ignores cancellation, semantic
French → zh-CN → French preservation, no duplicate query on language-only changes, manual force reload, and stale overlapping
full-refresh protection. The test store and all values are synthetic. M03 remains Partial pending the operator rerun of the
complete Windows/WPF checklist against the fixed artifact; M04 is not started.

## Extension handoff `M03-FILTERED-BULK-ACTIVATION-EXT-13`

The operator approved the filtered catalogue bulk activation/deactivation amendment after FIX-12 acceptance. The documentation-first
step consolidated `AC-CAT-013` into `docs/acceptance-criteria.md` (amendment metadata 2026-08-30) while retaining the standalone
approval record. The current M03 branch first incorporated main `4c971afda3ff5f2bc047f083bf962ee46d74f1f7` and preserved all prior
FIX-12 evidence.

Final extension head: `37ab7c5d156a7aa0b7aec90ffa3523fb6c75551f`; GitHub Actions Continuous integration run **#208** passed.
The follow-up evidence-documentation head `4b67bc7e0787008deb5a5033ea941f265817c967` also passed CI **#210**.

The implementation is deliberately narrow: `CatalogueService` validates an immutable Product-ID/expected-state request and
delegates even all-no-op captures to the store's read-only preflight; `SqliteCatalogueStore` revalidates every target inside one transaction, skips already-target
products, applies one operation timestamp to changed rows and rolls back on missing/stale targets or injected failures. Only
`is_active` and `updated_at_utc` of changed Products are written; aggregate fields and option hierarchies remain unchanged. The
view-model waits for the latest live/debounced filter task before capturing the complete filtered result, and MainWindow presents
localized FR/zh-CN confirmation counts, cancellation/no-op behavior, automatic refresh and preserved filter semantics through exactly
two bulk action buttons. No bulk Delete, migration or future-milestone UI was added.

The operator journey was mapped as: settle composed filters → capture IDs/states → inspect localized target/matched/effective counts
→ cancel or confirm → one atomic mutation → refresh without resetting filters. Automated Application, SQLite integration and STA/presentation
seams cover request validation, exact capture, no-op behavior, stale/latest synchronization, all-or-nothing writes, timestamp/aggregate
preservation, localization and no-bulk-delete structure. Final local Release verification for this extension is **215 passed, 0 failed,
0 skipped** across Domain 10, Application 11, Infrastructure integration 33, Architecture/localization 37, OneDrive protocol 32 and
GitHub wrapper/harness 92. Manual Windows/WPF acceptance remains outstanding; this automation run did not perform an interactive desktop
session. M03 remains Partial and M04 remains not started/unauthorized. `POST_TASK_POWER_ACTION: NONE`.

## Review remediation handoff `M03-FILTERED-BULK-ACTIVATION-REVIEW-FIX-14`

The review of EXT-13 identified four narrow evidence gaps. This remediation adds a focused SQLite integration regression for successful
multi-product **Activate**, including an already-active no-op Product, one operation timestamp for changed rows, unchanged Product
aggregate/option values and unchanged schema version. Existing deactivation, stale/missing-target and injected-failure rollback tests
remain intact.

The WPF evidence now constructs and renders the actual `MainWindow` on an STA thread in both `fr-FR` and `zh-CN`. The two localized bulk
buttons are checked for visibility, hit testing, natural desired-width fit and action-row bounds at normal `980x680`, minimum `760x520`
and larger `1400x900` sizes; no bulk Delete control is present. The production path continues to use the native modal Yes/No
`MessageBox.Show`.

The smallest workflow seam is `M03ShellViewModel.ExecuteBulkActiveStateWorkflowAsync`: it waits for the latest composed/debounced filter,
captures immutable IDs/states, short-circuits no-op and Cancel without mutation, performs one Application bulk call on confirmation, and
refreshes successfully while preserving search/category/status semantics. Architecture tests cover Cancel/no-op zero-write behavior,
confirmed Deactivate and Activate filter-preserving refreshes, selected-product clearing when a row leaves the result, and latest-filter
capture before confirmation.

The prior status-document publish wording was corrected: the required default self-contained win-x64 output directory was occupied by the
running Sushi81 POS process, so verification used an ignored alternate publish directory with the same settings and did not terminate
the operator process. Final local remediation verification is 221 passed, 0 failed, 0 skipped; interactive Windows/WPF acceptance remains
an operator gate, M03 remains Partial and M04 remains not started/unauthorized.

## Failure-path refresh remediation `M03-FILTERED-BULK-FAILURE-REFRESH-FIX-15`

The follow-up review identified that an unexpected exception from a confirmed bulk mutation reached the safe WPF error path without
refreshing the catalogue. The narrow workflow seam now centralizes one post-attempt refresh for successful and normal result-based
outcomes, while unexpected non-cancellation mutation exceptions perform a best-effort refresh before the original exception is
re-raised. The native MessageBox remains the production confirmation surface, and MainWindow no longer duplicates result-failure
refresh work.

Synthetic architecture regressions prove result failure refresh/filter preservation, unexpected exception refresh before propagation,
and refresh-failure handling that preserves the original exception without claiming success. Final local verification is **224 passed,
0 failed, 0 skipped**; M03 and AC-CAT-013 remain Partial pending operator Windows/WPF acceptance, and M04 remains not started/unauthorized.

## Language selector height remediation `M03-LANGUAGE-COMBO-HEIGHT-FIX-16`

The remaining M03 UI polish defect was limited to the top-right language ComboBox rendering too tall. The XAML now sets a compact
single-line height with centered alignment/content alignment and minimal padding while retaining the existing 150px width. No global
style, dependency, business logic, Catalogue/Settings layout or main-window close behavior was changed.

A narrow structural architecture regression protects the selector dimensions/alignment; the existing FR/zh-CN switching and persisted
language tests remain green. Final local verification is **225 passed, 0 failed, 0 skipped**; the required self-contained win-x64 publish
uses the current M03 artifact. Manual Windows/WPF verification of this selector was subsequently completed and passed; M03 and
AC-CAT-013 are now Passed for their authorized M03 scope. M04 remains not started/unauthorized.

## Final operator acceptance closure — `M03-FINAL-ACCEPTANCE-DOCS-17`

The operator completed the full M03 Windows/WPF checklist against head
`40f6e3884af488e4dd496f26b29bf9f6ca97bece`; every authorized M03 path passed:

- AC-CAT-013 three-filter intersection, precise bulk mutation and filter-preservation path;
- single-product activation/deactivation regression and unchanged option hierarchy through bulk state changes;
- active/inactive restart persistence;
- category rename, save/cancel and restored state;
- permanent Product delete confirmation, cascade behavior and code reuse;
- all five BusinessSettings defaults and edited Save/restart persistence;
- no editable delivery-fee VAT control and fixed 10% explanatory boundary;
- M03 scope boundary with no future-feature controls exposed;
- FIX-16 language selector compact/single-line visual presentation in both zh-CN and French;
- existing main-window X-to-exit behavior intentionally unchanged, per the operator's explicit withdrawal of the proposed change.

M03 and AC-CAT-001/AC-CAT-013 are therefore Passed for the authorized milestone scope. AC-CAT-003 historical-order independence
and the AC-ORD-011 pricing-consumer cross-check remain explicitly deferred to M04; no M04 work is started or authorized.

## Outstanding

- AC-CAT-003 historical-order independence and the pricing-consumer cross-check in AC-ORD-011 remain intentionally deferred to
  M04, which is not started or authorized.

No M04 work is authorized in this branch. No real customer, order, payment, credential or other sensitive data was added.
