# M07 WP7 — real private GitHub single-winner proof

**Date:** 2026-09-08  
**Handoff:** `M07-WP7-REAL-PROOF-07`  
**Implementation commit:** `9023250`  
**Disposition:** **Passed — real private GitHub proof completed**

## Scope and safety boundary

This evidence covers only the authorized WP7 real private-repository proof. The proof uses the production `GitHubReleaseAssetTransport`, production `RecoveryActivationService`, production activation artifact construction, and strict receipt validation. It uses synthetic disposable data only; no production/customer data or credential is included in the repository or this report.

The proof repository is the private repository `cimerosef/sushi81-pos-handoff-m07-proof`, using private release `m07-wp7-proof-v1` (release id `384571289`). The ephemeral credential was read only from `SUSHI81_GITHUB_HANDOFF_TOKEN` and was not printed or persisted.

## Selected real proof evidence

### Concurrent same-lineage activation race

- Deterministic asset: `dr-9d1713f59d41430f87187b0aa5e19037-g-8.activation.json`
- Remote asset id: `550176057`
- Remote state: `uploaded`
- Remote asset count for the deterministic name: `1`
- Contender outcomes: `Won`, `BlockedNoWinner`
- Accepted count: `1`
- Same-winner retry: `ResumedSameWinner`
- Different-device retry: `LostToExistingWinner`
- Loser was not accepted: `true`
- Server SHA-256: `bdc5ef0e59a762560af654507c620c45b4d0dcb4006cefbb226b9823c1809926`
- Downloaded local SHA-256: `bdc5ef0e59a762560af654507c620c45b4d0dcb4006cefbb226b9823c1809926`
- Digest match: `true`
- Strict activation artifact validation: `true`

The `BlockedNoWinner` result is fail-closed handling of the losing concurrent create observation; the durable remote winner was then re-observed by the exact retry path. No competitive claim or generic takeover is used.

### Unknown create outcome after server commit

- Deterministic asset: `dr-7049af930fa443008153e992eba6b2e5-g-12.activation.json`
- Remote asset id: `550176156`
- Remote state: `uploaded`
- Remote asset count for the deterministic name: `1`
- First outcome after synthetic unknown transport result: `ResumedSameWinner`
- Exact same-device retry: `ResumedSameWinner`
- Different-device retry: `LostToExistingWinner`
- Server SHA-256: `59c58321ba95edf30c0cb09be023ae8badb1a9afe11008786c50fbd25e2e3d5e`
- Downloaded local SHA-256: `59c58321ba95edf30c0cb09be023ae8badb1a9afe11008786c50fbd25e2e3d5e`
- Digest match: `true`
- Strict activation artifact validation: `true`

## Automated verification

- `RecoveryActivationTests`: 4/4 Passed (deterministic concurrency, same-winner retry, loser read-only result, starter occupancy and unknown-outcome re-observation).
- Full solution Release test run with `--blame-hang-timeout 2m`: 444/444 Passed, 0 failed, 0 skipped.
  - Domain 33; Application 47; Infrastructure integration 134; test OneDrive feasibility 32; tool OneDrive feasibility 92; Architecture/WPF 106.
- Full solution Release build: 0 warnings, 0 errors.
- The proof assets are intentionally retained in the private proof release for review. Earlier calibration runs left additional unique synthetic assets; no duplicate deterministic asset name was used for the selected passing vectors.

## Governance disposition

WP7 real proof is complete. WP8 production disaster-recovery implementation remains not started and forbidden by the handoff. M08 and later milestones remain not started and unauthorized. Project-owner manual M07 acceptance, PR merge, and any next-milestone work remain pending separate authorization.
