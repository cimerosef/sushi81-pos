# M13 — Final V1 owner manual acceptance

**Status:** NOT STARTED  
**Milestone:** M13  
**Candidate head:** TBD  
**Installer artifact:** TBD  
**Acceptance date:** TBD

## 1. Acceptance principle

Do not repeat every historical manual test from M01–M12. M13 owner acceptance is the shortest high-value final release check that validates the new M13 behavior and proves the installed production artifact preserves previously accepted operational state.

Automated and exact-head CI evidence remain primary for exhaustive regression.

## 2. Preconditions

Before owner testing:

- all M13 implementation packages are controller-accepted;
- exact candidate head CI is green;
- Release build has 0 warnings / 0 errors;
- final installer artifact is produced from that exact head;
- installer SHA-256/source/version/run provenance is recorded;
- repository/security blockers are closed;
- a safe backup/recovery point exists before any test involving an existing operational profile.

## 3. Owner checklist

### A — production installer identity and launch

- Install the exact M13 per-user installer.
- Confirm Sushi81 POS launches without a separate .NET runtime install.
- Confirm installed version/provenance corresponds to the accepted candidate.
- Confirm no development/test data appears.

### B — upgrade/reinstall preservation

Using a controlled existing Sushi81 profile:

- verify the pre-upgrade `live.db` orders are still present;
- verify Archive years/files remain discoverable;
- verify Recovery state remains present;
- verify Config/device identity/pairing/authority state was not reset;
- verify configured printer/settings remain;
- perform same-version repair/reinstall and confirm the same preservation;
- where a safe acceptance environment is available, uninstall/reinstall and confirm durable application data still survives.

Any silent data reset, authority reset, lost archive or forced re-pair caused only by installer operations is FAIL.

### C — basic business smoke

On an authoritative safe test profile:

- launch Caisse;
- create/edit a small order;
- save, pay/close and retrieve it;
- perform one safe print/reprint smoke if printers are available;
- open Catalogue and Settings;
- open Gestion export and archive history surfaces.

This is a smoke test, not a repetition of all earlier milestone acceptance.

### D — French / Simplified Chinese

Switch FR -> zh-CN -> FR and inspect the major final surfaces:

- Caisse/order lifecycle;
- Catalogue/settings;
- Gestion export, including pending/history controls;
- authority/handoff/recovery controls;
- archive access/reprint controls;
- errors/confirmations relevant to the smoke flow.

Record any hard-coded wrong-language string, garbling or operationally significant clipping.

### E — retention/history observable behavior

The owner does not need to wait 30 real days. Automated deterministic-clock evidence proves the retention boundary.

Manual acceptance only confirms:

- retained successful batches can still be regenerated;
- pending PREPARED batches remain visible/retryable;
- legally pruned history is not presented as regenerable if a prepared acceptance fixture is supplied.

### F — final operating guide

Open the delivered operating guide and confirm it describes the actual final UI/workflows sufficiently for day-to-day Sushi 81 operation.

## 4. M12 deferred archive verification remains separate

Do not use M13 to relabel the deferred M12 populated-archive checks as Passed.

At the first safe authoritative startup on or after 2027-02-01 with real 2026 data, verify real archive discovery/selection/detail/search/copy/reprint/read-only behavior under the existing M12 waiver record.

## 5. Acceptance record

For each section record PASS / FAIL / DEFERRED-NOT-M13 with concise evidence.

Final V1 can close only when all M13-required sections pass and any remaining M12 deferred item is still represented accurately as deferred rather than silently waived.
