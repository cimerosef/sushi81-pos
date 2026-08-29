# M02 — GitHub transport feasibility revalidation

**Status:** Authorized implementation/revalidation contract  
**Date:** 2026-08-28  
**Milestone:** M02 — remote handoff transport feasibility gate  
**Implementation branch:** continue `codex/m02-directed-handoff-revalidation` / PR #3  
**Approved decision:** `docs/decisions/github-handoff-transport.md`  
**Do not merge without explicit user approval. Do not start M03.**

## 1. Purpose

Replace the failed OneDrive-desktop synchronization acknowledgement path for **normal target-directed authority handoff** with a dedicated private GitHub repository using documented GitHub REST API Release Assets.

This is still an M02 feasibility/revalidation task. Do not implement the full production M07 UI/pairing/Disaster Recovery workflow.

The goal is to prove that GitHub can provide the missing server-side per-artifact acknowledgement while preserving all existing target-directed single-writer safety invariants.

## 2. Mandatory repository synchronization before coding

PR #3 already contains the directed-handoff durability/safety work and real OneDrive blocker evidence. Do not throw that work away.

Before changing code:

1. fetch current `main`;
2. incorporate current `main` into `codex/m02-directed-handoff-revalidation` using the safest normal Git workflow for the existing PR;
3. verify that `docs/decisions/github-handoff-transport.md` and this contract are present;
4. resolve documentation conflicts in favor of the new approved GitHub transport decision;
5. do not rewrite or delete historical OneDrive evidence.

Do not create a new implementation PR unless the existing PR becomes technically unusable. Prefer continuing PR #3 so the M02 proof history remains contiguous.

## 3. Specification amendment pass must happen first

Before implementing GitHub transport behavior, amend the affected V1 baseline documents on the implementation branch so code never intentionally implements against contradictory approved text.

Review at least:

- `docs/architecture.md`
- `docs/storage-strategy.md`
- `docs/acceptance-criteria.md`
- `docs/v1-specification-freeze.md`
- `docs/implementation-plan.md`
- `docs/implementation-status.md`
- `docs/README.md`
- `README.md`
- `src/README.md`
- `tests/README.md`
- all Approved records under `docs/decisions/`

Search the complete repository documentation for `OneDrive`, `Handoff`, `sync`, `synchronization`, `snapshot`, `ready`, `grant`, `five`, and related wording.

Rules:

- normal handoff transport/acknowledgement becomes GitHub REST + private handoff repository + Release Assets;
- target-directed authority semantics remain unchanged unless explicitly amended by `github-handoff-transport.md`;
- OneDrive real-device evidence remains historical M02 evidence, not current transport design;
- do not casually remove OneDrive references that belong only to still-approved annual archive/Disaster Recovery behavior outside this amendment's scope;
- if an existing baseline makes GitHub handoff impossible without changing a business/user-visible rule not covered by the Approved decision, stop and report the exact conflict rather than inventing behavior.

Also fix the existing documentation consistency defects discovered during review:

- do not claim that all M02 evidence is synthetic when sanitized real Home Device A evidence is present;
- stop embedding a self-invalidating phrase such as “latest branch head” in durable status prose. Record a named verification/evidence head and CI run instead;
- update the current status to the GitHub transport revalidation gate, not the superseded OneDrive local-confirmation gate.

## 4. Preserve historical OneDrive evidence

The following real evidence remains important and must stay in the M02 report:

- locally-created snapshot uploaded and visible in OneDrive web;
- local Cloud Files state remained `CF_PLACEHOLDER_STATE_NO_STATES / raw=0`;
- source stopped safely at `TransferPrepared`;
- `snapshotEvidence=null` and `markerEvidence=null`;
- no ready/grant marker existed;
- irreversible relinquishment was not crossed.

The OneDrive `transport-probe` command may remain as a historical diagnostic. It must no longer participate in the normal GitHub handoff write-authority gate.

Do not delete the user's existing Home Device A prepared v1 state or OneDrive test artifact from code or instructions. The transport change must allow an exact pre-relinquishment prepared transfer to continue safely when identity/target/version still match.

## 5. Approved GitHub transport container

Implement the M02 GitHub transport against a **configurable dedicated private repository**. The expected production deployment is conceptually:

`cimerosef/sushi81-pos-handoff`

Do not hard-code the source repository `cimerosef/sushi81-pos` as the operational transport repository.

Use one long-lived GitHub Release as the Release Asset container. Use these M02 defaults unless a concrete GitHub API constraint requires a documented technical adjustment:

- release tag: `sushi81-handoff-v1`
- release name: `Sushi81 POS Handoff Transport`
- `make_latest=false`

The handoff repository must have an initialized default branch/commit before release bootstrap. The M02 tool does **not** need to create the GitHub repository itself.

Add a non-destructive setup/check command that can:

- authenticate;
- verify repository exists;
- verify repository is private;
- obtain repository identity/default branch;
- locate the configured release by tag;
- create the one long-lived release if explicitly requested and missing;
- return release ID/upload URL/technical status without exposing the token.

Do not use GitHub Actions, Git LFS, Packages, repository commits, cloned working trees or filesystem sync as the handoff transport.

## 6. GitHub API implementation boundary

Use GitHub's documented REST API directly through .NET `HttpClient`; do not add an unnecessary third-party GitHub SDK/NuGet dependency for M02.

Centralize:

- API base URI;
- upload base URI handling from the release `upload_url`;
- required `User-Agent`;
- `Accept: application/vnd.github+json` where applicable;
- a supported explicit `X-GitHub-Api-Version` value;
- authentication header construction;
- request timeout/cancellation;
- JSON serialization/deserialization;
- rate-limit/error diagnostics without secrets.

Create a narrow transport abstraction so the directed authority coordinator depends on semantic transport operations rather than GitHub HTTP details. A suitable shape is conceptually:

- ensure/get transport container;
- upload immutable asset and return server receipt;
- list immutable assets;
- get asset metadata;
- download asset by ID;
- delete asset by ID for retention cleanup.

Exact type names are technical choices, but keep the boundary small and testable.

## 7. Authentication

M02 live feasibility tooling must read the token only from:

`SUSHI81_GITHUB_HANDOFF_TOKEN`

or an equivalently explicit test-only environment-variable name if existing conventions require it.

Requirements:

- never accept the token as a normal positional CLI value that will appear in shell history;
- never print it;
- never serialize it to state JSON;
- never include it in exception messages/logging/test snapshots;
- never commit it;
- never place it in the business SQLite database;
- redact `Authorization` headers from any HTTP diagnostics.

Document the required fine-grained token permissions for the dedicated private handoff repository. Use the least permission necessary for list/read/upload/delete Release Assets and release bootstrap. Do not request organization-wide or source-repository access.

Production credential storage belongs to M07, but the architecture/spec should state that production uses a Windows protected credential mechanism rather than plaintext config.

## 8. Snapshot naming — exact rule

Implement exactly:

`YYYYMMDDHHMMSS.snapshot.db`

Example:

`20260827231152.snapshot.db`

Use a 14-digit `yyyyMMddHHmmss` local wall-clock prefix for human readability.

Matching grant metadata:

`YYYYMMDDHHMMSS.grant.json`

The snapshot/grant basename must match exactly.

Important invariants:

- filename timestamp is not authority ordering;
- `handoff_version` remains the monotonic protocol ordering primitive;
- lineage/generation/transfer IDs remain opaque protocol identities;
- never overwrite an existing same-named GitHub asset;
- if a collision is detected, choose the next unused second-level timestamp candidate deterministically or fail safely;
- record actual UTC creation/publication timestamps and source timezone/offset in grant metadata where useful for diagnostics;
- filename parsing must be strict: exactly 14 ASCII digits plus the required suffix.

Add isolated tests for formatting, parsing, natural lexicographic ordering, collision handling, malformed names and the fact that filename order cannot override internal version/generation rules.

## 9. GitHub server receipt — snapshot

A local snapshot does **not** count as remotely published merely because an HTTP request was sent.

The successful M02 GitHub snapshot receipt must require all applicable documented evidence:

- HTTP upload succeeded with the documented successful status (`201 Created` for Release Asset upload);
- returned asset state is `uploaded`/complete, not `starter` or another incomplete state;
- returned asset name equals the requested `YYYYMMDDHHMMSS.snapshot.db` exactly;
- returned asset byte size equals the local snapshot size;
- returned asset ID is valid and persisted as immutable remote identity;
- returned GitHub `digest`, when documented/exposed, is present as `sha256:<hex>` and equals the locally computed SHA-256.

For this feasibility gate, if GitHub does not expose a usable digest for the real private-repository asset, do not silently weaken the rule. Report the real result for architecture review.

Persist a narrow immutable server receipt/evidence structure containing at least release ID, asset ID, exact name, byte length, SHA-256/digest and server-created timestamp if available.

A `502`/upstream error may leave a `starter` asset according to GitHub documentation. Treat it as incomplete. A pre-relinquishment empty starter asset may be safely deleted/retried only when its identity is proven and local durable state proves relinquishment was not crossed.

Duplicate-name `422`, authentication errors, permission errors, timeouts, cancellation, network errors, rate-limit errors, malformed API responses, missing digest, mismatched digest/size/name/state or unknown status all fail closed before relinquishment.

## 10. Source transfer ordering — GitHub

Refactor/adapt the existing directed source flow to exactly this order:

1. validate exact source/target/lineage/generation/handoff-version/transfer identity;
2. block new business writes according to existing `TransferPrepared` semantics;
3. complete/flush accepted writes;
4. create a SQLite-safe complete snapshot;
5. run SQLite integrity validation;
6. compute local SHA-256 and byte length;
7. assign/reserve a unique timestamp basename;
8. upload `YYYYMMDDHHMMSS.snapshot.db` to the configured GitHub release;
9. validate the complete GitHub server receipt from section 9;
10. only then durably persist source relinquishment with exact transfer + snapshot checksum/size + GitHub release/asset identity;
11. immediately verify the centralized write gate is false and remains false after reconstructed restart;
12. create immutable `YYYYMMDDHHMMSS.grant.json` from the persisted relinquishment facts;
13. upload the grant Release Asset;
14. require GitHub server acknowledgement for the grant upload (status/state/name/size/digest with the same strictness applicable to a small JSON asset);
15. only then persist source `Released`/completed publication;
16. perform retention cleanup as a post-completion technical action;
17. complete transfer-and-close.

The grant must never be uploaded before durable relinquishment.

Do not keep the old OneDrive `IN_SYNC` observation between any of these steps.

## 11. Existing prepared Home Device A v1 must be resumable

The user's current real synthetic M02 state is pre-relinquishment `TransferPrepared` for:

- source: `device-a`
- target: `device-b`
- lineage: `ca9dfdd4-fa48-4002-b993-23ce5c52a141`
- generation: `7`
- handoff version: `1`
- transfer ID: `fc235936-64a6-460b-bcaf-f2f0b212790e`

No snapshot evidence/grant/relinquishment was committed.

The new GitHub source command must be able to load this exact prepared state and continue the **same transfer identity** using the new GitHub transport.

Because no immutable snapshot evidence was committed under the old transport, it is acceptable to generate the new timestamp-named GitHub snapshot during this exact pre-relinquishment resume. Do not require deleting or editing the old OneDrive test snapshot.

Reject any attempt to use this prepared state with a different transfer ID, target, lineage, generation or handoff version.

Do not add a general migration mechanism for arbitrary production transfers; this is a safe M02 pre-relinquishment compatibility path.

## 12. Grant schema

Create one immutable JSON grant asset per completed transfer using the same timestamp basename.

The schema must include at least:

- protocol format/version;
- transfer ID;
- lineage ID;
- generation;
- handoff version;
- source device ID;
- target device ID;
- snapshot release ID;
- snapshot asset ID;
- snapshot asset exact filename;
- snapshot byte length;
- snapshot SHA-256;
- source snapshot-created UTC time;
- source durable relinquishment UTC time;
- grant publication timestamp when available/appropriate.

Canonicalize/serialize deterministically enough for stable hashing/tests. Compute the grant's local SHA-256 before upload and validate the GitHub asset receipt.

The grant is an authorization artifact only because source durable state was committed before it could be created. A grant whose referenced snapshot is missing or invalid must never enable the target.

## 13. Target acquisition — GitHub

Add/adapt an M02 target command that:

1. authenticates and gets the configured private handoff release;
2. lists/discovers candidate `.grant.json` assets;
3. downloads/parses grants without trusting filename order alone;
4. filters to exact local target device and current lineage/generation;
5. selects only a valid non-stale successor according to durable local high-water/version rules;
6. obtains the exact referenced snapshot by GitHub asset ID and exact name;
7. validates GitHub asset metadata and grant metadata agree;
8. downloads the snapshot through the authenticated API;
9. computes SHA-256 and byte length locally;
10. compares local hash/size with both grant and GitHub receipt/metadata;
11. runs SQLite integrity validation;
12. persists `AcquisitionPending` / target evidence / final local cursor using the already-proved crash-safe ordering;
13. reconstructs a fresh coordinator/write gate from disk;
14. returns success only when `mayBusinessWrite=true` for the exact target.

A wrong/non-target device must never become writable and must not mutate durable acquisition state merely by listing/downloading transport assets.

## 14. Retention — exactly newest three complete units after cleanup

Implement approved retention of the latest **three complete handoff units**.

A complete unit is:

- one `YYYYMMDDHHMMSS.snapshot.db` asset;
- its matching `YYYYMMDDHHMMSS.grant.json` asset;
- internally valid metadata binding the pair.

Retention algorithm:

1. never run destructive cleanup before the new handoff is fully uploaded and source is durably `Released`;
2. enumerate/validate complete handoff units;
3. order them by validated internal lineage/generation/handoff version, not filename timestamp alone;
4. retain newest three complete units for the active lineage/generation history as specified;
5. delete older unit assets by exact GitHub asset ID;
6. treat snapshot+grant as one logical deletion unit;
7. cleanup failure is non-authority-critical: transfer stays complete and cleanup is recorded/retried later;
8. never delete an asset referenced by current pending/relinquished durable state;
9. do not count incomplete/starter/stray assets as one of the three complete retained versions;
10. permit a temporary fourth complete/in-progress unit until the new unit is confirmed, because deleting an old known-good unit first is forbidden.

Add tests proving:

- first three complete transfers delete nothing;
- fourth complete transfer deletes only v1 pair after v4 is complete;
- failure uploading v4 deletes nothing;
- failure before v4 grant deletes nothing;
- cleanup API failure does not roll authority backward;
- retry cleanup is idempotent;
- wrong asset IDs/names are never deleted;
- an active relinquished-but-incomplete transfer is never garbage-collected.

## 15. HTTP/API failure test matrix

Use a deterministic fake/stub `HttpMessageHandler` or equivalent test seam. Do not make CI depend on live GitHub.

At minimum cover:

- repository missing;
- repository public -> reject;
- authentication 401;
- authorization 403;
- rate limit 403/429 where applicable;
- release missing;
- bootstrap create-release success/failure;
- snapshot upload 201 valid;
- snapshot 201 but state not uploaded;
- snapshot name mismatch;
- snapshot size mismatch;
- snapshot digest missing;
- snapshot digest mismatch;
- duplicate-name 422;
- upstream 502/starter asset;
- timeout/cancellation/network exception;
- crash after valid snapshot server receipt but before durable relinquishment;
- crash immediately after durable relinquishment before grant upload;
- grant upload failure after relinquishment;
- exact same-transfer resume succeeds;
- retarget/change-version/change-transfer after relinquishment rejected;
- grant upload 201 but metadata/digest mismatch;
- target list/download happy path;
- target missing grant/snapshot;
- target wrong device;
- target stale/replay/old generation;
- download truncation/hash mismatch/size mismatch;
- SQLite corruption;
- crash between target evidence and final cursor;
- retention cases from section 14;
- token never appears in serialized result/log/error text.

Existing directed lifecycle safety tests (`A -> B v1 -> B -> A v2 -> A -> B v3`) must remain green with transport-independent safety logic.

## 16. Fix the previously noted test helper defect

In `DirectedTargetAcquisitionTests`, inspect the helper previously noted as conceptually:

`Fixture.CreateReleasedTransferAsync(long version)`

The helper previously used a hard-coded transfer version `1` internally instead of its `version` argument in one place. Correct this so test setup cannot misrepresent v2/v3 behavior.

Do not merely change the assertion; fix the fixture semantics and keep the existing continuous lifecycle regression valid.

## 17. CLI contract for M02 real testing

Expose clear non-production feasibility commands. Exact executable remains the M02 tool project.

Provide commands equivalent to:

### Repository/release check

`github-transport-check --owner <owner> --repo <repo> --release-tag sushi81-handoff-v1 [--create-release] --json`

### Source run/resume

`github-directed-source-run --state-dir <local-synthetic-dir> --device <source> --target <target> --lineage <id> --generation <n> --version <n> --transfer-id <id> --owner <owner> --repo <repo> --release-tag sushi81-handoff-v1 --json`

The command must support the exact pre-existing `TransferPrepared` state described above.

### Target acquire

`github-directed-target-acquire --state-dir <local-synthetic-dir> --device <local-target> --source <source> --target <target> --lineage <id> --generation <n> --version <n> --transfer-id <id> --owner <owner> --repo <repo> --release-tag sushi81-handoff-v1 --json`

### Promote for reverse transfer

Reuse/adapt the existing local `directed-target-promote` behavior; promotion must remain transport-independent.

### Read-only remote inspection

Provide a command that lists sanitized transport units/asset IDs/names/sizes/digests/target/version without downloading or changing authority state.

All commands must refuse application/live DB paths and remain synthetic M02 tooling.

## 18. Real-device revalidation plan to document, not execute without operator

After automated verification, update the report with exact operator commands for the following sequential two-device test. The devices do **not** need to be simultaneously online.

Round 1:

- Device A resumes current prepared A -> B v1 using GitHub;
- GitHub returns valid snapshot and grant receipts;
- A becomes `Released`, `mayBusinessWrite=false`;
- later Device B downloads/acquires exact v1 and becomes the only writable device.

Round 2:

- B promotes locally;
- B -> A v2 uploads a new timestamp snapshot/grant through GitHub;
- B becomes non-writable;
- later A downloads/acquires exact v2 and becomes the only writable device.

Optional stronger proof:

- A -> B v3;
- B re-acquires using its historical local state/high-water advancement.

For real evidence capture, record only sanitized repository/asset IDs/names/digests/sizes, technical states and timestamps. Never record token or business snapshot contents.

M02 cannot be marked Passed solely from mocked HTTP tests. Real private-repository server receipts and at least A -> B v1 plus B -> A v2 must be completed before final acceptance.

## 19. GitHub repository setup documentation

Add a short operator setup section to the M02 report/README for later real testing:

- create a dedicated **private** repository (expected name `sushi81-pos-handoff`);
- initialize it with a harmless README/default branch;
- create a fine-grained PAT restricted to only that repository and only the required Contents read/write permission;
- set it temporarily in the M02 test process environment as `SUSHI81_GITHUB_HANDOFF_TOKEN`;
- run the transport-check/bootstrap command;
- never paste the token into screenshots/chat/repository files.

Do not require Git CLI on the operator's Windows machines. The M02 tool talks directly to GitHub REST API.

## 20. Documentation and evidence update

Update:

- `docs/implementation/milestone-02-directed-handoff-revalidation-report.md`
- `docs/implementation-status.md`
- PR #3 body
- `tools/Sushi81.Pos.OneDriveFeasibility/README.md` (rename the tool/project only if doing so is clearly worth the churn; otherwise document that it now contains historical OneDrive diagnostics plus current GitHub feasibility transport)

The report must clearly separate:

1. original competitive OneDrive blocker;
2. approved target-directed safety amendment;
3. real OneDrive local-acknowledgement blocker;
4. approved GitHub transport amendment;
5. automated GitHub transport evidence;
6. real GitHub private-repository evidence when later supplied.

Do not rewrite history to make earlier blockers look like passes.

## 21. Build, test and publish verification

Run exactly:

```powershell
dotnet restore Sushi81.Pos.sln
dotnet build Sushi81.Pos.sln -c Release --no-restore
dotnet test Sushi81.Pos.sln -c Release --no-build
dotnet publish src/Sushi81.Pos.Desktop/Sushi81.Pos.Desktop.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=false
```

Requirements:

- 0 build warnings;
- 0 build errors;
- all existing 119 tests remain green unless the baseline count is legitimately increased by synchronized main changes;
- all new GitHub transport tests green;
- self-contained win-x64 publish passes;
- CI green for the final pushed implementation/evidence head;
- no credentials, real databases, real snapshots, logs or business data committed.

## 22. M02 gate outcomes

After implementation but before the user's real two-device run, report:

`PARTIAL — GitHub transport implementation ready; real two-device evidence required`

only if automated proof and GitHub API design are conforming.

After real evidence, the only acceptable final conclusions are:

### FEASIBLE / candidate for M02 Passed

Only if all of the following are evidenced:

- private GitHub repository and release access succeeds with least-privilege credentials;
- real snapshot upload returns a valid server receipt including usable digest/size/asset identity;
- real grant upload returns a valid server receipt;
- target can authenticated-download and validate exact assets;
- A -> B v1 and B -> A v2 complete sequentially;
- at every tested state writable-device-count <= 1;
- source restart/retry and target restart reconstruction remain correct;
- retention logic is verified;
- no unresolved architecture contradiction remains.

### BLOCKED

If the real GitHub API/private-repository behavior cannot provide the required remote acknowledgement/download/retention semantics without another material architecture amendment.

Do not downgrade security or single-writer safety merely to obtain a pass.

## 23. Scope exclusions

Do not start or implement:

- M03 Catalogue/settings;
- production M07 pairing UI;
- production credential UI;
- Disaster Recovery implementation;
- annual archive implementation;
- order/cart/payment/catalogue/printing/export/Hiboutik features;
- migration of real business data;
- automatic creation of the user's GitHub repository/account/token.

This task is the narrow M02 GitHub transport feasibility revalidation only.

## 24. Final Codex response contract

When implementation is complete, report exactly:

1. branch name;
2. final pushed head SHA;
3. PR #3 current head/status;
4. baseline documents amended;
5. GitHub transport classes/files added/changed;
6. exact Release Asset server-receipt rule;
7. exact snapshot/grant filename rule;
8. credential source/permissions and secret-redaction protections;
9. source ordering before/after relinquishment;
10. target acquisition ordering;
11. retention algorithm and proof that only newest three complete units remain after cleanup;
12. confirmation that existing prepared Home Device A v1 is supported without changing transfer identity;
13. historical OneDrive diagnostic/evidence preservation;
14. test total/passed/failed/skipped;
15. build warnings/errors;
16. publish result;
17. CI run number/result;
18. current M02 gate conclusion;
19. explicit confirmation that PR #3 was not merged;
20. explicit confirmation that M03 was not started.

Do not merge PR #3.
