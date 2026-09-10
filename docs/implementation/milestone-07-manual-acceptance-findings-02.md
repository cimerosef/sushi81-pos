# M07 manual-acceptance findings 02

**Finding ID:** `M07-MANUAL-ACCEPTANCE-REMEDIATION-20`  
**Date:** 2026-09-10  
**Status:** Remediated in code; project-owner retest required; not owner acceptance

## Owner observations carried forward

The owner review of the prior R19 production candidate (`e2fdcacdc2d1837ebe27478569ce3d66209e57b6`) found two M07 acceptance blockers:

1. Scenario H retention did not converge in a long-lived operational Release when legacy/incompatible grant evidence appeared before valid current-lineage units. The cleanup pass could stop on a `JsonException`, leaving otherwise valid older units untouched. Scenario H remains blocked on the old R19 candidate.
2. After a successful target acquisition, the durable database and authority state could be correct while already-running WPF catalogue, settings, order-entry, lifecycle and dashboard views still showed pre-acquisition data until process restart.

These observations do not constitute owner acceptance of the remediation candidate.

## Implemented remediation

### Retention evidence isolation

`NormalHandoffService.CleanupNewestThreeAsync` now isolates every candidate unit. Cancellation still propagates, while legacy, foreign, unsupported, malformed, partial and contradictory evidence is skipped as non-qualifying diagnostics. Only strictly validated, unambiguous, exact current-lineage snapshot+grant pairs are ranked by protocol generation/handoff version and removed as exact snapshot/grant pairs. Cleanup remains best-effort and cannot change authority.

Regression coverage adds a legacy incompatible grant before four valid current-lineage units and proves that cleanup continues, removes only the older exact pairs, retains the legacy pair, and leaves the released source non-authoritative.

### Database-replacement presentation refresh

Target acquisition, Disaster Recovery success and stale-generation reinitialization now use one ShellViewModel refresh seam after the durable data-first operation. The seam refreshes M03 catalogue/settings, M04 catalogue/order browser and M05 lifecycle/dashboard in the same process while preserving valid view context where possible.

A presentation refresh barrier is propagated to the shell and child view-models. It disables business writes and related M07 actions while the refresh is in flight. If post-authority refresh fails, the durable authority is not rolled back and all business surfaces remain fail-closed until a safe later refresh/restart.

STA regression coverage proves that new catalogue, settings, order-browser, lifecycle and dashboard data appears without process restart, that writes remain disabled during the refresh, and that a refresh failure leaves writable surfaces blocked.

## Owner rerun scope

Using the exact remediation-20 self-contained artifact, the owner must rerun:

- Scenario H with at least four complete alternating normal-handoff units plus legacy/stray evidence, confirming convergence to exactly the newest three validated units and no split deletion.
- Scenario D target acquisition, explicitly verifying that the final transferred catalogue/settings/order/lifecycle/dashboard data appears before any write is enabled and without restarting the target process.
- Scenario E round-trip acquisition for the same no-restart presentation condition.
- Scenario J/K only to the extent the successful DR restore or stale-generation reinitialization replaces the live database, confirming the same refresh and fail-closed behavior.
- Scenario A remains required on the remediation candidate because it was blocked on the earlier candidate; this remediation does not replace that owner retest.

The owner must record results only after ChatGPT review of the pushed exact head. Automated Release evidence below is not manual acceptance.

## Automated evidence identity

- Production/evidence implementation head before this documentation commit: `c74693c5f4a79c69878b59a9df88231ea083835c`.
- Full local Release solution: `539/539` Passed, `0` failed, `0` skipped.
- Release build: `0` warnings, `0` errors.
- Focused retention class: `NormalHandoffTests` `4/4` Passed.
- Focused WPF refresh class: `M07PresentationRefreshTests` `2/2` Passed.
- Fresh self-contained artifact: `artifacts/m07-manual-remediation-20-publish/Sushi81.Pos.Desktop.exe`.
- Artifact size: `162816` bytes.
- Artifact SHA-256: `DF4C312903B35B856830D4218A776E421AE7EE617BA717AFFA81779ED64DBE86`.
- Exact-head CI result for the final pushed docs/evidence head is recorded in the matching `CODEX_DONE` delivery comment.

WP7 proof repository/assets were not opened, rerun, changed or deleted. M08 and later milestones remain unauthorized; merge remains unauthorized.
