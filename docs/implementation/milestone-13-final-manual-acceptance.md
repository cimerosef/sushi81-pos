# M13 — Final V1 owner manual acceptance

**Status:** Owner A–G PASS on the accepted M13 production candidate; final controller review and owner merge decision pending. M12 populated-archive verification remains DEFERRED-NOT-M13.

**Milestone:** M13

**Accepted production candidate:** source SHA `469c8761061b0ecf488e06386a97b9e10652a916`; exact-head CI #912 / run `36253271385` (932/932 tests); installer artifact ID `10909188508`, `Sushi81-POS-production-installer-win-x64-1.0.0-469c876`. Earlier candidates and failures remain historical in the worklog and PR #26.

**Acceptance evidence dates:** 2026-09-25 to 2026-09-26; see the owner evidence and controller records on PR #26.

## 1. Acceptance principle

Owner evidence now records A–G PASS across the accepted M13 candidate and the controlled A→B production cutover. The previous D language failure and F guide failure were repaired and later retested; their failure records remain in the worklog and PR #26. The catalogue VAT cutover defect was repaired under the approved fraction-compatibility amendment and handoff 19, with owner verification on B. Manual results come from owner evidence, not from hosted CI. PR #26 remains OPEN / Draft / unmerged; final controller review and a separate owner merge decision remain.

## 2. Final navigation and layout adjustment

The top-level order is **Catalogue | Paramètres | Données | Commandes | Caisse** in French and **商品目录 | 设置 | 数据 | 订单 | 收银台** in Simplified Chinese. **Données / 数据** contains the separate Gestion export and Archives historiques child tabs. Commandes and Caisse have distinct restrained blue operational header accents; high-contrast mode uses system brushes. This owner-requested presentation adjustment did not change business workflow; the owner subsequently accepted the repaired language switching and final layout.

**Owner result:** D language switching and final navigation/layout PASS on the repaired candidate. The prior failure remains historical.

## 3. Preconditions

Acceptance checks used these controls:

- WP1-WP6 controller reviews and exact-head evidence are available on PR #26.
- Review the controller review and matching language-repair and final-navigation completion comments; those document the superseded candidate history.
- Use only the accepted production installer artifact `10909188508` / `Sushi81-POS-production-installer-win-x64-1.0.0-469c876`. Verify its filename, byte size and SHA-256; verify installed `release-provenance.json` identifies source head `469c8761061b0ecf488e06386a97b9e10652a916`, version, runtime and AppId.
- Confirm exact-head CI, full Release build/tests, safety scans and hosted installer lifecycle are green in that completion evidence.
- For final review, use the accepted source/artifact identity above and the later owner evidence; do not substitute a superseded installer.

If any exact artifact identity or precondition is missing, stop and report it; do not substitute a different installer.

## 4. Owner checklist

### A — exact installer identity and launch

- Install the accepted per-user installer identified above.
- Confirm Sushi81 POS launches without a separate .NET runtime installation.
- Confirm the installed product/file version and `release-provenance.json` match the exact candidate source head.
- Confirm no development/test data appears in the installed tree.

**Owner result/date/evidence:** PASS; owner A/B evidence is on PR #26 (`OWNER_ACCEPTANCE_EVIDENCE: M13-FINAL-A-B-20260925`). The controlled production B install, profile preservation and subsequent authoritative A→B transfer provide later accepted-candidate evidence. Earlier candidate identity remains historical.

### B — existing-profile upgrade and reinstall preservation

Using a controlled existing Sushi81 profile, verify before and after upgrade/repair:

- `Data\live.db` and its existing orders are present;
- canonical `Archive` files remain discoverable;
- `Recovery` material remains present;
- `Config`, device identity, pairing and authority state have not reset;
- printer selections and application settings remain.

Perform same-version repair/reinstall. If a safe acceptance environment is available, also exercise uninstall/reinstall. Ordinary install, upgrade, repair and uninstall must not remove durable application data. **Any silent data reset, authority reset, lost archive or forced re-pair caused only by installer operations is FAIL.**

**Owner result/date/evidence:** PASS; owner A/B evidence is on PR #26 (`OWNER_ACCEPTANCE_EVIDENCE: M13-FINAL-A-B-20260925`). B is now authoritative/writable and A read-only after the normal target-directed handoff; installer operations preserved durable profile state.

### C — small authoritative business smoke

On a safe test profile where this device is confirmed authoritative:

- open Caisse; create a small order, edit it, save it, enter its actual CB / Espèce received amounts and close it when the exact total is settled;
- retrieve the saved order; make one safe print/reprint attempt if printers are available;
- open Catalogue, Paramètres, Gestion export and Archives historiques.

Do not perform refund execution or use Disaster Recovery as a routine smoke step. A non-authoritative device remains read-only for business writes.

**Owner result/date/evidence:** PASS on owner authoritative business smoke evidence (`OWNER_ACCEPTANCE_EVIDENCE: M13-FINAL-C-20260925`, PR #26). The later controlled production cutover and catalogue use are recorded separately.

### D — French / Simplified Chinese review

Switch **FR → zh-CN → FR** and inspect the major surfaces used in the smoke: Caisse/order lifecycle, Catalogue/Paramètres, Gestion export, authority/recovery controls, archive access/reprint, and relevant confirmations/errors. Record any wrong-language hard-coded text, garbling or operationally significant clipping. Confirm entered catalogue, customer and order data did not change when the display language changed.

**Owner result/date/evidence:** PASS on owner repaired-candidate FR/zh-CN status switching and final navigation/layout review (PR #26). The prior candidate FAIL (`OWNER_ACCEPTANCE_FAILURE: M13-FINAL-D-LANGUAGE-STATUS-20260925`) remains historical.

### E — retained Gestion export behavior

- Confirm a retained successful batch can still be regenerated from history.
- Confirm a pending `PREPARED` batch remains visible and can be retried.
- If a prepared acceptance fixture is available, confirm legally pruned history is not shown as regenerable. Do not wait 30 real days; the deterministic automated evidence covers the retention boundary.

Do not manually delete export history or database rows.

**Owner result/date/evidence:** PASS for the retained successful-batch behavior observed on the real owner profile (PR #26). PREPARED retry and 30-day pruning edges have deterministic automated evidence; they were not manually fabricated or relabelled as owner-observed.

### F — operating guide

Open both the primary Simplified Chinese guide at [../operating-guide.zh-CN.md](../operating-guide.zh-CN.md) and the equivalent English guide at [../operating-guide.md](../operating-guide.md). Follow the new-PC setup, protected GitHub credential boundary, read-only join, exact-target authority transfer, printer setup, settings verification and daily-operation procedures. Confirm that the procedures match the installed UI and supported workflows. Record any confusing, incorrect or incomplete operator instruction.

**Owner result/date/evidence:** PASS. The owner followed the revised Chinese guide through B new-PC formal installation/configuration, Credential Manager-backed GitHub connection test, OneDrive local availability, printer verification and normal exact-target A→B handoff (PR #26). The old-guide FAIL remains historical in PR #26 comment `5834968433` and the worklog; later corrections were retested.

## 5. M12 deferred archive verification remains separate

The first safe real populated-archive verification remains deferred under the existing M12 owner waiver. At the first safe authoritative startup on or after **2027-02-01** with real 2026 rows, separately verify real archive discovery, selection, detail/search, user-selected copy, reprint and read-only behavior. Record it as **DEFERRED-NOT-M13** until performed. Do not use M13 to relabel it Passed.

## 6. G — full business-data reset

The owner-approved, delivered in-app reset is documented in [`../decisions/m13-full-business-data-reset.md`](../decisions/m13-full-business-data-reset.md). Codex used synthetic data for automated tests; the owner executed the real profile operation. Owner evidence on PR #26 records preview counts, cancel with no mutation, typed `RESET` plus final confirmation, private `MaintenanceBackups` creation, cleared catalogue/orders/Gestion/archive views, preserved settings/printer/language, A authority and technical GitHub/OneDrive configuration, and a subsequent successful A→B handoff proving pairing and target reuse. **Owner result: PASS. Do not repeat the reset.**

## 7. Acceptance record

**Final owner record:** A–G PASS, including the accepted catalogue VAT repair on B. Genuine Excel percentage formatting is normalized to percentage points; the approved plain-numeric compatibility allowlist is exactly `0.055→5.5`, `0.1→10`, `0.2→20`. Other plain fractions stay literal. B's bound-record export/reimport changed the representative VAT values to 5.5/10/20; post-commit visual inspection passed, and a second export/reimport preview reported zero changes. Handoff 19 owner acceptance is PASS. B remains authoritative/writable; A is read-only. M12 real populated-archive verification remains DEFERRED-NOT-M13. PR #26 remains OPEN / Draft / unmerged pending final controller review and separate owner merge approval.
