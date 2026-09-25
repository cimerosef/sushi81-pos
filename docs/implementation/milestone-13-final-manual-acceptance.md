# M13 — Final V1 owner manual acceptance

**Status:** SECTIONS A-C PASS EVIDENCE ON PRIOR CANDIDATE; SECTION D FAIL; REPAIRED-CANDIDATE D RE-TEST PENDING

**Milestone:** M13

**Prior candidate that received owner checks:** source SHA `33145cf796e939741255f7599dc8174df9d7d083`, artifact ID `10860083727`. This candidate is superseded for acceptance because Section D failed. The repaired candidate SHA and artifact identity will be recorded in the matching top-level PR #26 completion comment after exact-head CI.

**Acceptance evidence date:** 2026-09-25 for the prior candidate; repaired-candidate Section D re-test pending

## 1. Acceptance principle

This record tracks owner acceptance after testing the exact WP6 candidate `33145cf796e939741255f7599dc8174df9d7d083` / artifact `10860083727`. The owner recorded Sections A-C as PASS on that candidate and Section D as FAIL because transient operator statuses remained in Chinese after switching to French. The old artifact is superseded. A-C remain recorded as prior-candidate evidence only; they do not establish acceptance of the repaired candidate. After controller review of the combined navigation-adjusted exact-head artifact, the owner must re-run Section D and inspect the final navigation/visual layout on that candidate. Sections E-F remain paused pending the owner re-test and controller review.

Automated evidence is recorded in the matching language-repair and final-navigation completion comments; it does not convert manual checks to PASS. Do not claim M13 Passed, merge or release here.

## 2. Final navigation and layout adjustment

The top-level order is **Catalogue | Paramètres | Données | Commandes | Caisse** in French and **商品目录 | 设置 | 数据 | 订单 | 收银台** in Simplified Chinese. **Données / 数据** contains the separate Gestion export and Archives historiques child tabs. Commandes and Caisse have distinct restrained blue operational header accents; high-contrast mode uses system brushes. This owner-requested presentation adjustment does not change any workflow or the carried-forward A-C, D, E or F result.

**Owner re-test pending:** Section D and the final navigation/visual layout must be reviewed together on the exact combined candidate after controller review. Do not mark D, E or F PASS here.

## 3. Preconditions

Before testing:

- WP1-WP6 controller reviews and exact-head evidence are available on PR #26.
- Review the controller review and matching language-repair and final-navigation completion comments before downloading the combined candidate.
- Download only the exact combined final-candidate installer artifact named in the final-navigation completion comment. Verify its filename, byte size and SHA-256; verify installed `release-provenance.json` identifies the listed source head, version, runtime and AppId.
- Confirm exact-head CI, full Release build/tests, safety scans and hosted installer lifecycle are green in that completion evidence.
- The current outstanding owner action is Section D plus final navigation/visual-layout review on that exact combined candidate.

If any exact artifact identity or precondition is missing, stop and report it; do not substitute a different installer.

## 4. Owner checklist

### A — exact installer identity and launch

- Install the exact per-user installer identified in the WP6 `CODEX_DONE` comment.
- Confirm Sushi81 POS launches without a separate .NET runtime installation.
- Confirm the installed product/file version and `release-provenance.json` match the exact candidate source head.
- Confirm no development/test data appears in the installed tree.

**Owner result/date/evidence:** PASS on prior candidate `33145cf796e939741255f7599dc8174df9d7d083` / artifact `10860083727`; evidence comment `OWNER_ACCEPTANCE_EVIDENCE: M13-FINAL-A-B-20260925`. This does not certify the repaired candidate.

### B — existing-profile upgrade and reinstall preservation

Using a controlled existing Sushi81 profile, verify before and after upgrade/repair:

- `Data\live.db` and its existing orders are present;
- canonical `Archive` files remain discoverable;
- `Recovery` material remains present;
- `Config`, device identity, pairing and authority state have not reset;
- printer selections and application settings remain.

Perform same-version repair/reinstall. If a safe acceptance environment is available, also exercise uninstall/reinstall. Ordinary install, upgrade, repair and uninstall must not remove durable application data. **Any silent data reset, authority reset, lost archive or forced re-pair caused only by installer operations is FAIL.**

**Owner result/date/evidence:** PASS on prior candidate `33145cf796e939741255f7599dc8174df9d7d083` / artifact `10860083727`; evidence comment `OWNER_ACCEPTANCE_EVIDENCE: M13-FINAL-A-B-20260925`. This does not certify the repaired candidate.

### C — small authoritative business smoke

On a safe test profile where this device is confirmed authoritative:

- open Caisse; create a small order, edit it, save it, enter its actual CB / Espèce received amounts and close it when the exact total is settled;
- retrieve the saved order; make one safe print/reprint attempt if printers are available;
- open Catalogue, Paramètres, Gestion export and Archives historiques.

Do not perform refund execution or use Disaster Recovery as a routine smoke step. A non-authoritative device remains read-only for business writes.

**Owner result/date/evidence:** PASS on prior candidate `33145cf796e939741255f7599dc8174df9d7d083` / artifact `10860083727`; evidence comment `OWNER_ACCEPTANCE_EVIDENCE: M13-FINAL-C-20260925`. This does not certify the repaired candidate.

### D — French / Simplified Chinese review

Switch **FR → zh-CN → FR** and inspect the major surfaces used in the smoke: Caisse/order lifecycle, Catalogue/Paramètres, Gestion export, authority/recovery controls, archive access/reprint, and relevant confirmations/errors. Record any wrong-language hard-coded text, garbling or operationally significant clipping. Confirm entered catalogue, customer and order data did not change when the display language changed.

**Owner result/date/evidence:** FAIL on prior candidate `33145cf796e939741255f7599dc8174df9d7d083` / artifact `10860083727`; evidence comment `OWNER_ACCEPTANCE_FAILURE: M13-FINAL-D-LANGUAGE-STATUS-20260925`. The repaired exact-candidate re-test is pending; do not mark PASS until the owner performs it.

### E — retained Gestion export behavior

- Confirm a retained successful batch can still be regenerated from history.
- Confirm a pending `PREPARED` batch remains visible and can be retried.
- If a prepared acceptance fixture is available, confirm legally pruned history is not shown as regenerable. Do not wait 30 real days; the deterministic automated evidence covers the retention boundary.

Do not manually delete export history or database rows.

**Owner result/date/evidence:** Paused pending controller review and the repaired-candidate Section D re-test.

### F — operating guide

Open [`../operating-guide.md`](../operating-guide.md) and confirm it matches the installed UI, labels and supported day-to-day workflows.

**Owner result/date/evidence:** Paused pending controller review and the repaired-candidate Section D re-test.

## 5. M12 deferred archive verification remains separate

The first safe real populated-archive verification remains deferred under the existing M12 owner waiver. At the first safe authoritative startup on or after **2027-02-01** with real 2026 rows, separately verify real archive discovery, selection, detail/search, user-selected copy, reprint and read-only behavior. Record it as **DEFERRED-NOT-M13** until performed. Do not use M13 to relabel it Passed.

## 6. Acceptance record

After performing the checks, the owner records PASS / FAIL / DEFERRED-NOT-M13 and concise evidence for A–F, with date and the exact candidate SHA/artifact. Do not pre-fill or infer owner results from hosted CI. Final V1 acceptance and any merge decision remain separate explicit owner/controller actions.
