# M14 — PreProd foundation — preparation readiness

**Status:** READY FOR IMPLEMENTATION AFTER EXECUTION GATE REOPEN
**Milestone:** M14
**Start baseline:** `6ff2e04ce17e34addf58cf6dcfa756d4b7fae8aa`
**Authorized branch:** `codex/m14-preprod-foundation-authorized`

## 1. Owner authorization

On 2026-09-27 the owner approved and froze the M14 design for:

- permanent Prod/PreProd runtime separation;
- independent local data/authority identities;
- independent GitHub runtime handoff and OneDrive DR/System roots;
- initial safe production database seed into pristine PreProd;
- distinct coexisting installers;
- immutable GitHub PreProd candidates;
- one-publish application payload;
- production promotion from the exact owner-accepted payload without rebuilding the application.

This authorization does not include any post-launch business bug fix.

## 2. Controlling sources

Codex must treat these as controlling for M14:

- `docs/decisions/m14-preprod-environment-isolation-and-promotion.md`
- `docs/acceptance-criteria-amendment-m14-preprod-foundation.md`
- `docs/implementation/milestone-14-preprod-foundation.md`
- existing approved authority/handoff/DR/installer/storage architecture
- `AGENTS.md`

Where M14 changes deployment/storage/release mechanics, the M14 decision/amendment governs. Existing business behavior remains frozen unless explicitly changed here.

## 3. Starting facts

At authorization:

- `main` = `6ff2e04ce17e34addf58cf6dcfa756d4b7fae8aa`;
- M13 is merged;
- no active implementation PR existed before M14 preparation;
- Issue #4 execution gate is CLOSED;
- production application data root is `%LOCALAPPDATA%\Sushi81 POS`;
- current production installer has one fixed AppId/install identity;
- normal authority handoff uses dedicated GitHub Release Assets;
- OneDrive System/DisasterRecovery remains separate from normal handoff;
- current CI already creates exact-head self-contained win-x64 installer evidence with source/SHA provenance.

## 4. Implementation sequencing

Packages are controller-gated:

1. WP1 — runtime deployment-profile isolation;
2. WP2 — dual installer coexistence;
3. WP3 — initial PROD→PREPROD seed;
4. WP4 — remote environment isolation;
5. WP5 — immutable GitHub candidate pipeline;
6. WP6 — production promotion pipeline;
7. WP7 — owner two-PC acceptance and closure.

Codex must not skip ahead unless the controller publishes an explicit handoff.

## 5. Safety rules

- Never place real production database/customer/order/payment data in source Git, source-repo Releases, CI artifacts, tests or samples.
- Never copy production authority/device/lineage/pairing state into PreProd.
- Never use the production handoff repository or production Credential target for PreProd.
- Never perform a production application rebuild during candidate promotion.
- Never change business rules as part of environment work.
- Never merge the M14 PR.
- Do not perform the deferred M12 real populated annual archive acceptance.

## 6. Readiness conclusion

The specification seams required to start WP1 are frozen. WP1 can proceed once the controller publishes the exact executable handoff and the owner reopens Issue #4.
