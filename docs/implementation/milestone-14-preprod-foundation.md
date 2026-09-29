# M14 — PreProd Foundation and immutable release promotion — implementation contract

**Status:** OWNER-AUTHORIZED; WP1–WP6 controller-accepted; WP7 owner-accepted under waiver / closure-ready, controller closure pending
**Milestone:** M14
**Implementation branch:** `codex/m14-preprod-foundation-authorized`
**Start baseline:** `6ff2e04ce17e34addf58cf6dcfa756d4b7fae8aa`
**Target stabilization version:** `1.0.1`

## 1. Mission

Create a permanent PreProd deployment that can coexist with production on the same two Windows computers, run the real Sushi81 authority/handoff/DR architecture in isolation, be initially seeded from current production business data without importing production technical identity, and support immutable owner-testable GitHub candidates whose accepted application payload is promoted to production without rebuilding.

M14 does not fix the post-launch business defects that motivated creation of PreProd.

## 2. Non-negotiable invariants

1. Prod and PreProd never share local durable root, `live.db`, authority state, lineage, device identity, OneDrive System/DR root, GitHub handoff repository or Credential Manager target.
2. Unknown/malformed deployment identity fails closed before database/authority initialization.
3. Real production data never enters the source repository or CI artifact system.
4. Existing business calculations/lifecycle/printing/export semantics stay unchanged except explicit PreProd environment marking.
5. Existing target-directed handoff and DR implementations are reused, not replaced with a simplified test path.
6. Candidate application payload is published once.
7. Production promotion packages exactly the accepted payload; it must not run `dotnet publish`.
8. Production and PreProd installers may coexist and must not delete/mutate each other's durable data.
9. Codex never merges the M14 PR.

## 3. WP1 — Runtime deployment-profile isolation

Implement the environment seam before any current production path is resolved.

Required shape:

- accepted profiles exactly `prod` and `preprod`;
- production durable root remains `%LOCALAPPDATA%\Sushi81 POS`;
- PreProd durable root is `%LOCALAPPDATA%\Sushi81 POS PREPROD`;
- profile evidence is installed beside binaries through packaging, not user-configured as an arbitrary data path;
- malformed/unknown evidence blocks startup safely;
- tests prove all IAppPaths-derived directories are isolated;
- production existing-profile behavior remains compatible.

Add permanent PreProd identity:

- app/window title `Sushi81 POS PREPROD`;
- visible `PREPROD — DONNÉES DE TEST` / `PREPROD — 测试数据` on major operational surfaces;
- `*** PREPROD ***` marking on kitchen/customer prints and reprints;
- `PREPROD_` default prefix for Gestion export filenames.

Do not change stored business data or workbook schema.

## 4. WP2 — Dual installer coexistence

Generalize M13 packaging instead of creating unrelated scripts.

Required:

- preserve current production AppId as stable production identity;
- create one new permanent PreProd AppId;
- PreProd install directory `%LOCALAPPDATA%\Programs\Sushi81 POS PREPROD`;
- separate shortcut/uninstall display;
- installer writes exact deployment-profile evidence;
- installer lifecycle tests cover both installed together, repair/reinstall/uninstall isolation, and durable-data preservation;
- packaging verifies application files are unchanged except packaging/profile material.

No environment installer may delete durable app data.

## 5. WP3 — Initial PROD→PREPROD seed

Implement a PreProd-only one-time bootstrap.

Eligibility must fail closed unless the target PreProd profile is pristine with no established PreProd pairing/authority transfer/business test lineage.

Source:

`%LOCALAPPDATA%\Sushi81 POS\Data\live.db`

Rules:

- source production process must not be actively writing;
- open/read source safely and produce SQLite-safe snapshot/backup;
- validate integrity/schema;
- stage under PreProd;
- allow normal versioned application migration;
- install atomically only after validation;
- any failure leaves the prior target state unchanged/retryable;
- never mutate source production DB.

Seed copies business DB content only. It does not import:

- authority-state;
- production lineage/generation;
- device identity;
- pairing metadata;
- OneDrive/GitHub configuration;
- Credential Manager target/token;
- Recovery/Cache/Logs/Temp;
- authority/bootstrap markers;
- canonical annual Archive DB files.

Use real SQLite integration tests.

## 6. WP4 — Remote environment isolation

PreProd uses independent runtime infrastructure.

Expected operator configuration:

- dedicated private GitHub handoff repo, recommended `cimerosef/sushi81-pos-handoff-preprod`;
- PreProd release tag/name distinct from production;
- separate Credential Manager target and fine-grained PAT scoped only to PreProd handoff repo;
- independent OneDrive shared root, recommended leaf `Sushi81 POS PREPROD`.

Implement collision guard on a machine where production also exists.

Reject before persistence if PreProd proposes:

- same or nested OneDrive root;
- same GitHub owner+repository;
- same Credential target.

Reading production non-secret local settings for comparison is permitted read-only.

Pairing, target-directed handoff, acquisition, recovery checkpoint and DR services must operate unchanged through the selected PreProd paths/configuration. Add tests proving no production remote path/repository is used.

## 7. WP5 — Immutable GitHub candidate pipeline

Replace the M13-specific candidate packaging shape with a reusable release pipeline.

For one candidate exact source head:

1. restore/build/test exact head;
2. execute `dotnet publish` once for the application;
3. generate immutable application payload;
4. generate `payload-manifest.json` with source SHA and per-file SHA-256;
5. package PreProd installer from that payload;
6. verify installer lifecycle and forbidden-content rules;
7. publish owner-testable immutable GitHub pre-release assets.

Suggested candidate tag:

`v1.0.1-preprod-cNN`

Candidate identity must not require changing product semantic version for every iteration.

At minimum candidate assets/evidence include:

- PreProd installer;
- application payload archive;
- payload manifest;
- package summary;
- release provenance;
- exact source SHA / CI run / hashes.

Never overwrite an owner-tested candidate. Publish a new candidate ID.

## 8. WP6 — Production promotion pipeline

Promotion input is one explicitly accepted PreProd candidate.

Promotion must:

1. download candidate payload/manifest from durable GitHub evidence;
2. verify candidate/source/payload identity and all hashes;
3. refuse any mismatch or missing evidence;
4. not invoke application restore/build/publish;
5. package the existing payload under the production installer identity/profile;
6. verify all application files match the accepted payload manifest;
7. produce production installer + promotion provenance.

Promotion provenance records accepted candidate ID/tag, source SHA, payload SHA-256, packaging run and production installer SHA-256.

The pipeline existing does not authorize deployment. Owner release approval remains separate.

## 9. WP7 — Owner manual acceptance

The original unwaived acceptance sequence uses both real Windows computers:

Minimum owner sequence:

1. download controller-approved PreProd candidate from GitHub;
2. install beside existing production;
3. verify distinct app/shortcut/uninstall/data identities;
4. on the designated PreProd first computer, run initial safe seed from local production data;
5. inspect representative catalogue, business settings, order and Gestion data;
6. configure independent PreProd OneDrive root;
7. configure independent PreProd GitHub handoff repo and Credential target;
8. install/join PreProd on the second computer;
9. complete one real PREPROD-A→PREPROD-B target-directed authority handoff;
10. optionally return B→A only if controller needs additional evidence;
11. confirm production database/authority/runtime transport was not changed by PreProd actions;
12. verify visible PreProd print/export marking;
13. review CI evidence that production promotion packages the exact accepted payload.

**Approved 2026-09-29 owner waiver for current M14 closure:** PREPROD operates only on computer A for now; computer B remains Production-only. The owner accepted the applicable A-only manual evidence and the controller accepted WP6 promotion evidence. Steps 8–10 above are deferred, not Passed, and are mandatory before adding a second PREPROD computer or accepting changes that materially affect multi-device pairing, authority handoff, target acquisition, Disaster Recovery or OneDrive/GitHub cross-device coordination. This changes acceptance/deployment practice only; the implementation and M07 handoff semantics remain intact. See the Approved M14 decision amendment, acceptance amendment and final manual acceptance record for the `Accepted under waiver / closure-ready` status.

Do not repeat full M01–M13 business acceptance.

## 10. Automated acceptance minimum

Add deterministic automated coverage for:

- profile parsing/fail-closed behavior;
- prod/preprod path derivation;
- cross-root access prevention;
- coexisting installer identity and lifecycle;
- seed source read-only semantics;
- seed integrity/migration rollback;
- non-import of authority/device identity;
- OneDrive same/nested-root rejection;
- GitHub repository collision rejection;
- Credential target collision rejection;
- PreProd handoff/DR configuration isolation;
- UI/print/export PreProd marking;
- one-publish payload manifest;
- PreProd package payload hashes;
- production promotion payload hashes;
- explicit CI failure if production promotion attempts application rebuild.

Final gate:

- Release build 0 warnings / 0 errors;
- tests 0 failed / 0 unexpected skipped;
- exact-head CI success.

## 11. Explicit exclusions

M14 does not include:

- post-launch business bug fixes or feature improvements;
- repeated/automatic production-to-PreProd refresh;
- production/PreProd database merge;
- automatic production deployment/self-update;
- cloud/live shared database;
- multi-writer behavior;
- redesign of target-directed handoff;
- canonical annual Archive cloning;
- deferred M12 populated annual archive owner verification.

## 12. Parallel execution plan

After WP1 freezes runtime profile/path seams, WP2 installer work and WP3 seed internals may be partly parallelized if Codex judges the files/dependencies independent. WP4 depends on stable profile/path/config seams. WP5 and WP6 depend on stable installer/payload contracts. WP7 depends on WP1–WP6 controller acceptance.

Durable mailbox handoffs remain serial and controller-gated even if Codex internally parallelizes safe subtasks.
