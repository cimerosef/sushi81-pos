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
- Release solution tests: **188 passed, 0 failed, 0 skipped**: Domain 10, Application 9, Infrastructure integration 30,
  Architecture/localization 15, OneDrive protocol 32, and GitHub wrapper/harness 92.
- Self-contained `win-x64` publish passed with `PublishSingleFile=false`.
- Final post-fix CI is required on the pushed head; the PR completion record names the resulting workflow run and check URL.

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

## Outstanding

- Operator must run the manual M03 Windows/WPF checklist from the contract against the published artifact. This run did
  not claim interactive verification.
- AC-CAT-003 historical-order independence and pricing-consumer cross-check in AC-ORD-011 remain intentionally
  deferred to M04, which is not started or authorized.

No M04 work is authorized in this branch. No real customer, order, payment, credential or other sensitive data was added.
