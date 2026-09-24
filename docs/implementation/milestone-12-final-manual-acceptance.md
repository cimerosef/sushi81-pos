# M12 — Windows/WPF owner manual acceptance

**Status:** OWNER WAIVER RECORDED — M12 operational archive verification is partially deferred; the remaining populated-archive checks below are not claimed as passed.
**Candidate source:** see the matching WP5 `CODEX_DONE` on the active implementation PR for the exact final SHA and local publish path.
**Scope:** M12 annual archive and historical access only. This checklist does not authorize merge or M13.

Use only a test installation and synthetic/test archive data. Do not point this acceptance run at production business data. Record concise observations and any failure; do not mark a result Passed unless the owner personally performed it against the exact candidate.

## Checklist

- [ ] The Archive tab opens without automatically selecting an archive year.
- [ ] Available local archive year(s) are displayed clearly.
- [ ] Explicitly selecting a year loads only that archive.
- [ ] Search by reference/telephone and date/status controls are understandable.
- [ ] Archived detail is clearly read-only and displays the expected product, options, tax, total and payment facts.
- [ ] Switching French ↔ Chinese keeps the archive state usable and labels readable.
- [ ] Explicit archive copy opens the Windows destination dialog; cancel is harmless; successful copy appears at the chosen destination.
- [ ] Existing-destination overwrite confirmation is clear.
- [ ] Kitchen archived reprint visibly contains `RÉIMPRESSION`.
- [ ] Customer archived reprint visibly contains `DUPLICATA`.
- [ ] Cancelled archived reprint visibly contains `ANNULÉ` plus the appropriate reprint mark.
- [ ] A non-authoritative/read-only device can browse, export and reprint the archive without gaining write controls or authority.
- [ ] Normal `Commandes` live search remains separate and live-only.
- [ ] No unexpected archive editing or deletion controls exist.

Do not repeat automated failure-injection tests manually. Record the candidate SHA, Windows version, language used, each observed result, and any issue requiring follow-up.

## Evidence classification and owner disposition — 2026-09-24

The owner decision `M12-DEFER-REMAINING-ARCHIVE-MANUAL-VERIFICATION-20260924` is recorded on active PR #25 comment `5816797035`. It accepts proceeding with the practical risk of deferring the remaining populated-archive manual verification; it does not say that every item in the checklist passed. No checklist box above has been marked passed by this closure record.

### Owner-observed diagnostic outcomes (not acceptance passes)

- Some exact-head owner/diagnostic WPF runs showed only `2025 (0)` in archive discovery.
- Other same-head production and simultaneous internal/external runs showed both `2025 (0)` and the validated synthetic `2024 (3)` archive.
- These intermittent, conflicting observations did not establish a deterministic product-code defect or a safe repair. Further synthetic-fixture forensic cycles are stopped by the owner; no speculative repair is authorized.

These observations are the only owner-facing archive-discovery outcomes relied upon here. They do not establish successful populated-archive detail, search, copy/export, reprint, localization or read-only-device behavior.

### Automated and controller evidence

The accepted WP1–WP5 implementation/evidence candidate is source head `e62db0003f837297b848448a28a94e30c5db64a4`. Exact-head GitHub CI #860 / workflow run `35785073179` succeeded with 881 passed, 0 failed, 0 skipped; its Release build reported 0 warnings and 0 errors. The synthetic 2024 fixture itself was validated. The relevant TRACE-READ-17 / FORENSIC-18 / TRACE-19 diagnostics and controller dispositions are retained in PR #25. This automated/controller evidence does not substitute for the owner manual checks listed above.

### DEFERRED BY OWNER — real populated archive verification

Checks that require selecting a populated historical archive and exercising its real historical rows—detail, search, copy/export, reprint and read-only access—are deferred until a real prior-year archive containing business orders exists. At the current annual policy boundary, perform this operational verification at the first safe authoritative startup on or after **2027-02-01**, for calendar-year 2026 data. Verify real archive discovery/selection and historical detail/search/copy/reprint/read-only behavior. If that archive is missing, undiscoverable/unselectable, or an archive operation fails, reopen the finding as a product defect using real-data evidence.

### Explicit risk acceptance and fail-safe invariant

The owner accepts the practical risk that eligible historical orders may remain in `live.db` for an extended period while real-world archive verification is deferred; this is not a claim that database growth can never affect performance. The archive fail-safe remains mandatory: an archive failure must not silently delete eligible live orders. This deferred operational verification is not an M13 implementation blocker.

The documentation-only closure commit's exact-head CI result and final PR state are recorded in the matching `CODEX_DONE` comment on PR #25.
