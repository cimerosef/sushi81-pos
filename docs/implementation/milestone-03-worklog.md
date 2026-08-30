# M03 implementation worklog

**Status:** Remediation complete through FIX-11; automated evidence green; manual WPF acceptance outstanding
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
- Release solution tests: **202 passed, 0 failed, 0 skipped**: Domain 10, Application 9, Infrastructure integration 30,
  Architecture/localization 29, OneDrive protocol 32, and GitHub wrapper/harness 92.
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
  Continuous integration run **#183** (success).

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

## Outstanding

- Operator must rerun the manual M03 Windows/WPF checklist from the contract against the newly published artifact, including
  visual confirmation that all six Catalogue headers are populated (`Code`, `Nom`, `Catégorie`, `Prix TTC`, `TVA`, `Actifs` in
  French and the corresponding zh-CN labels), switch live in both directions, and remain readable at default/resized/maximized
  sizes. The operator should also confirm both category and status ComboBoxes show `Tous`/`全部` on first render and remain
  selected through either language-switch direction and refresh, plus the category-manager resize/maximize layout and readable
  French/Chinese action buttons, and the Product Editor's SelectionMode, Required, min/max, group-action and option-action labels
  remain readable at normal and larger sizes in both languages. Product creation passed in the latest run; acceptance stopped at
  the blank-header observation.
- AC-CAT-003 historical-order independence and pricing-consumer cross-check in AC-ORD-011 remain intentionally
  deferred to M04, which is not started or authorized.

No M04 work is authorized in this branch. No real customer, order, payment, credential or other sensitive data was added.
