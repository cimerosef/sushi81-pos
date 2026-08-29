# M03 implementation worklog

**Status:** Remediation complete; automated evidence green; manual WPF acceptance outstanding  
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
- Application, domain, infrastructure and desktop-seam tests using only synthetic data.

## Verification

- .NET SDK 10.0.400, Windows x64.
- Release restore/build passed (0 warnings, 0 errors).
- Release solution tests: **195 passed, 0 failed, 0 skipped**: Domain 10, Application 9, Infrastructure integration 30,
  Architecture/localization 22, OneDrive protocol 32, and GitHub wrapper/harness 92.
- Self-contained `win-x64` publish passed with `PublishSingleFile=false`.
- Post-fix implementation head `710d95b2dcb75995428ace25cc38081afd48127a` passed GitHub Actions Continuous integration run
  **#133** (success). The follow-up filter-state remediation head `e6fc0ddeb8077cab33758da9f62cb615300b2cb7` passed
  Continuous integration run **#137** (success; check URL is recorded in the PR completion evidence). The category
  binding remediation head `1053c9b210cac15343959aac8f9ffa2c13ccd9b8` passed local Release verification; its CI result
  is recorded in the matching PR completion evidence. The status binding remediation head
  `e56ec77b4d13898c40d57be600652309d77938df` passed local Release verification; its CI result is recorded in the matching
  PR completion evidence.

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
left the status ComboBox blank. The root cause was the localized `StatusFilters` collection being rebuilt while WPF could write a
transient null/invalid selected value back through the status binding. The narrow remediation exposes the stable semantic
`SelectedStatusKey` (with `All`, `Active` and `Inactive` keys), binds the ComboBox through `SelectedValuePath="Key"`, and
preserves the effective key while the collection is replaced. Null writes during an empty collection are ignored; once options
exist, invalid/null values deterministically resolve to All. The added desktop regressions cover fresh All state, zh-CN ↔ fr-FR
All round trips, Active and Inactive preservation across localization and refresh, and transient-null tolerance. Category binding,
status behavior and no-selection action guards remain covered. M03 remains Partial pending the operator rerun of the full WPF
checklist after this fix.

## Outstanding

- Operator must rerun the manual M03 Windows/WPF checklist from the contract against the newly published artifact, including
  visual confirmation that both category and status ComboBoxes show `Tous`/`全部` on first render and remain selected through
  either language-switch direction and refresh. This run did not claim interactive verification after the status binding fix.
- AC-CAT-003 historical-order independence and pricing-consumer cross-check in AC-ORD-011 remain intentionally
  deferred to M04, which is not started or authorized.

No M04 work is authorized in this branch. No real customer, order, payment, credential or other sensitive data was added.
