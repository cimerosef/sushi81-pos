# M13 — Final V1 owner manual acceptance

**Status:** CANDIDATE PREPARED / OWNER ACCEPTANCE PENDING

**Milestone:** M13

**Candidate source SHA and installer artifact:** See the matching top-level PR #26 comment `CODEX_DONE: M13-WP6-FINAL-CANDIDATE-OPERATING-GUIDE-06` for the exact source head and artifact identity. This source file intentionally does not contain its own not-yet-built commit SHA.

**Acceptance date:** Pending owner execution

## 1. Acceptance principle

This is the short final owner check for the exact WP6 production candidate. Do not repeat every historical M01–M12 manual test. Exact-head automated evidence is recorded in the matching WP6 completion comment; this checklist covers the installed production artifact, preservation of an existing profile, a small business smoke, language switching, retained export behavior and the operator guide.

WP6 prepares a candidate. It does not claim owner acceptance, M13 Passed, merge or release. Record PASS / FAIL / DEFERRED-NOT-M13 only after the owner performs the relevant check.

## 2. Preconditions

Before testing:

- WP1–WP5 are controller-accepted and their exact heads/CI are recorded in `implementation-status.md`.
- Review the controller's WP6 review and the matching `CODEX_DONE` comment on PR #26.
- Download only the exact installer artifact named in that completion comment. Verify the listed installer filename, byte size and SHA-256; verify installed `release-provenance.json` identifies the listed source head and version.
- Confirm exact-head CI, full Release build/tests, safety scans and hosted installer lifecycle are green in that completion evidence.
- Create and verify a safe recovery point before testing any existing operational profile. Do not use real business data for a disposable test.

If any exact artifact identity or precondition is missing, stop and report it; do not substitute a different installer.

## 3. Owner checklist

### A — exact installer identity and launch

- Install the exact per-user installer identified in the WP6 `CODEX_DONE` comment.
- Confirm Sushi81 POS launches without a separate .NET runtime installation.
- Confirm the installed product/file version and `release-provenance.json` match the exact candidate source head.
- Confirm no development/test data appears in the installed tree.

**Owner result/date/evidence:** Pending owner execution.

### B — existing-profile upgrade and reinstall preservation

Using a controlled existing Sushi81 profile, verify before and after upgrade/repair:

- `Data\live.db` and its existing orders are present;
- canonical `Archive` files remain discoverable;
- `Recovery` material remains present;
- `Config`, device identity, pairing and authority state have not reset;
- printer selections and application settings remain.

Perform same-version repair/reinstall. If a safe acceptance environment is available, also exercise uninstall/reinstall. Ordinary install, upgrade, repair and uninstall must not remove durable application data. **Any silent data reset, authority reset, lost archive or forced re-pair caused only by installer operations is FAIL.**

**Owner result/date/evidence:** Pending owner execution.

### C — small authoritative business smoke

On a safe test profile where this device is confirmed authoritative:

- open Caisse; create a small order, edit it, save it, enter its actual CB / Espèce received amounts and close it when the exact total is settled;
- retrieve the saved order; make one safe print/reprint attempt if printers are available;
- open Catalogue, Paramètres, Gestion export and Archives historiques.

Do not perform refund execution or use Disaster Recovery as a routine smoke step. A non-authoritative device remains read-only for business writes.

**Owner result/date/evidence:** Pending owner execution.

### D — French / Simplified Chinese review

Switch **FR → zh-CN → FR** and inspect the major surfaces used in the smoke: Caisse/order lifecycle, Catalogue/Paramètres, Gestion export, authority/recovery controls, archive access/reprint, and relevant confirmations/errors. Record any wrong-language hard-coded text, garbling or operationally significant clipping. Confirm entered catalogue, customer and order data did not change when the display language changed.

**Owner result/date/evidence:** Pending owner execution.

### E — retained Gestion export behavior

- Confirm a retained successful batch can still be regenerated from history.
- Confirm a pending `PREPARED` batch remains visible and can be retried.
- If a prepared acceptance fixture is available, confirm legally pruned history is not shown as regenerable. Do not wait 30 real days; the deterministic automated evidence covers the retention boundary.

Do not manually delete export history or database rows.

**Owner result/date/evidence:** Pending owner execution.

### F — operating guide

Open [`../operating-guide.md`](../operating-guide.md) and confirm it matches the installed UI, labels and supported day-to-day workflows.

**Owner result/date/evidence:** Pending owner execution.

## 4. M12 deferred archive verification remains separate

The first safe real populated-archive verification remains deferred under the existing M12 owner waiver. At the first safe authoritative startup on or after **2027-02-01** with real 2026 rows, separately verify real archive discovery, selection, detail/search, user-selected copy, reprint and read-only behavior. Record it as **DEFERRED-NOT-M13** until performed. Do not use M13 to relabel it Passed.

## 5. Acceptance record

After performing the checks, the owner records PASS / FAIL / DEFERRED-NOT-M13 and concise evidence for A–F, with date and the exact candidate SHA/artifact. Do not pre-fill or infer owner results from hosted CI. Final V1 acceptance and any merge decision remain separate explicit owner/controller actions.
