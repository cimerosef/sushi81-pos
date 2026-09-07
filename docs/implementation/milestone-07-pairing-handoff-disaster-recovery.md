# M07 implementation contract — pairing, target-directed handoff and disaster recovery

**Status:** Prepared for project-owner implementation authorization — **NOT YET AUTHORIZED**  
**Prepared:** 2026-09-07  
**Milestone:** M07 — Pairing, target-directed formal handoff and disaster recovery  
**Execution gate:** CLOSED  
**Codex instruction status:** This document is a contract draft/prepared implementation specification. **Codex MUST NOT execute it until a separate M07 authorization record exists and GitHub issue #4 has been opened by the governance controller after all preparation prerequisites are verified.**

## 1. Authority and entry baseline

Implementation must begin from the then-current `main`, not from conversation memory.

Current accepted production baseline at preparation time:

- M06 Passed and merged via PR #11 at `2c5eb52740d0c12e3e837579ecceac6d0600b59e`;
- accepted M06 production repair head `4a0c1ca9e44a6c48899e6ef8dc211172371e4d20`;
- final M06 docs/PR head `86326d81551aa4cb5cdcbc6826b8c740317b34c4`;
- Release tests 364/364 Passed;
- build 0 warnings / 0 errors;
- exact-head CI `34091370109` successful;
- project-owner Windows/WPF manual acceptance Passed.

M07 must extend, not replace, the M06 production safety spine:

- `%LOCALAPPDATA%\Sushi81 POS\Data\live.db` remains the live local SQLite database;
- `Config/authority-state.json` remains the one canonical production authority-state path;
- the independent M06 bootstrap marker/anchor remain part of fail-closed established-installation validation;
- `IWriteAuthorityGuard` remains the final Application-layer business-write gate;
- missing/corrupt/contradictory established state remains fail closed;
- M06 local Recovery remains independent and continues newest-five validated local snapshots;
- existing localized persistent read-only/transition/recovery-required presentation remains the foundation M07 extends.

M02 feasibility code under `tools/` is evidence/reference only. Production M07 may port/reuse proven semantics but must not make production depend on the feasibility executable or introduce a second independent authority cursor.

## 2. Controlling specifications

Codex/review must apply all current Approved baseline/decisions, especially:

- `docs/decisions/target-directed-authority-handoff.md`;
- `docs/decisions/github-handoff-transport.md`;
- `docs/decisions/m07-self-join-disaster-recovery.md`;
- `docs/acceptance-criteria-amendment-m07-self-join-disaster-recovery.md`;
- `docs/storage-strategy.md` except older OneDrive-normal-handoff wording explicitly superseded by Approved decisions;
- `docs/acceptance-criteria.md` plus the M07 amendment;
- `docs/architecture.md`;
- `docs/v1-specification-freeze.md`;
- `docs/implementation/interactive-quality-gate.md`;
- `docs/implementation/control-state-preservation.md`;
- `docs/implementation/post-task-power-policy.md`.

No implementation choice may reinterpret self-join as authority, turn DR into ordinary takeover, add a renewable remote lease, or weaken source-relinquish-before-grant ordering.

## 3. Hard scope

M07 must deliver production implementation and evidence for:

1. durable device/lineage/generation identity;
2. N-device self-join membership with no current-authority approval requirement;
3. read-only initialization/paired-uninitialized behavior;
4. authoritative close choice: retain vs explicit transfer;
5. normal exact-target GitHub Release Asset handoff;
6. source irreversible relinquishment and same-transfer retry;
7. exact-target acquisition and durable local activation;
8. newest-three complete normal-handoff retention;
9. changed-only OneDrive DR checkpoints, maximum normal frequency once per 15 minutes, newest five;
10. explicit generation-advancing DR using validated safe candidates;
11. online create-once/single-winner DR activation;
12. stale-generation detection/read-only enforcement/reinitialization path;
13. production GitHub credential/configuration boundary;
14. localized WPF operational UI/status/failure behavior;
15. deterministic automated evidence and final real Windows multi-device owner acceptance.

Out of M07 scope:

- physical printer integration/reprint completion (M08);
- Hiboutik paste flow (M09);
- catalogue `.xlsx` (M10);
- Gestion export (M11);
- annual archive execution/historical archive UI (M12);
- installer/final V1 acceptance (M13);
- hosted backend/remote SQL/database merge;
- automatic reconciliation/merge of independently divergent generations;
- employee accounts/permissions;
- automatic authority election/claiming.

## 4. Canonical durable authority model

### 4.1 One source of production truth

Evolve the M06 `Config/authority-state.json` schema in place through a versioned migration. Do not add a second production authority-state file/cursor whose truth can diverge.

The canonical document must durably carry at least:

- schema version/revision;
- immutable local `device_id`;
- editable device display name reference/value as technically appropriate;
- `lineage_id` when joined/initialized;
- current `generation`;
- current/high-water `handoff_version`;
- detailed authority phase;
- current immutable transfer identity/evidence when applicable;
- current immutable DR activation identity/evidence when applicable;
- last safely acquired/restored business-data identity/watermark where needed for stale/candidate ordering;
- update timestamp for diagnostics only, never as authority ordering.

Required detailed phases or exact equivalents with identical semantics:

- `Uninitialized`;
- `PairedUninitializedReadOnly`;
- `Authoritative`;
- `ClosedRetainedAuthority` (may be represented by Authoritative with durable close intent if simpler, but writable semantics on next valid launch are unchanged);
- `TransferPreparing`;
- `RelinquishedPendingGrant` — irreversible;
- `ReleasedNonAuthoritative`;
- `TargetAcquisitionPending`;
- `NonAuthoritativeReadOnly`;
- `StaleGeneration`;
- `DisasterRecoveryPending`;
- `RecoveryRequired`.

The existing M06 coarse `WriteAuthorityState` is derived from the detailed phase. It must not be separately persisted as another independent authority truth.

All phases except valid current `Authoritative`/retained-authority semantics derive a non-writable business guard.

### 4.2 Safety-critical state durability

For authority-sensitive state transitions use a durable replacement pattern at least as strong as the proven M02 shape:

1. serialize to a same-volume temporary file;
2. flush/write-through such that the bytes are durable according to the platform abstraction;
3. atomically replace/move the canonical file;
4. reopen/reparse/revalidate the canonical file;
5. only then perform any later step whose safety depends on that transition.

Corrupt/unsupported/contradictory established state must not default to writable.

### 4.3 M06 → M07 migration

Migration rules:

- valid M06 `Authoritative` may one-time initialize a new immutable `device_id`, new lineage identity if none exists, initial generation and handoff high-water state while retaining authority;
- existing M06 `NonAuthoritativeReadOnly`, `Transitioning` or `RecoveryRequired` must not be promoted merely by upgrading;
- missing marker/anchor/live DB/state on an established install remains fail closed under the M06 evidence rules;
- identity creation must be idempotent across crash/restart;
- migration must not silently recreate/delete customer/order/payment data.

Tests must start from exact serialized M06 state shapes, not only newly constructed M07 objects.

## 5. Shared System metadata and self-join

### 5.1 OneDrive structure

M07 treats the configured OneDrive Sushi81 root as containing active semantics for:

```text
<root>\
    System\
    DisasterRecovery\
    Archive\
```

Any legacy `Handoff\` folder may remain untouched for historical compatibility/diagnostics but carries **no normal authority-transfer semantics**. Normal handoff uses the configured dedicated private GitHub repository.

Suggested current-generation System layout may be technically adjusted if equivalent:

```text
System\
  Lineage\
    lineage.json
  Devices\
    <generation>\
      <device-id>.device.json
```

Prefer immutable per-device registration artifacts so two new computers can self-join without contending on one mutable paired-set file.

### 5.2 Device self-join state machine

A fresh installation joining existing data must perform mechanically:

1. generate/persist immutable random local `device_id` before publishing membership;
2. obtain operator display name;
3. select/validate configured existing OneDrive root;
4. discover and validate lineage/current-generation metadata;
5. reject contradictory lineage or unsupported protocol metadata;
6. create the exact current-generation per-device registration artifact using non-overwriting semantics;
7. if the exact same valid registration already exists for the same device ID, treat retry as idempotent;
8. if a contradictory artifact exists under the same identity, fail closed to RecoveryRequired/explicit diagnostic state;
9. persist local membership/current generation while business write guard remains false;
10. attempt safe read-only hydration only from an artifact type explicitly allowed for onboarding; never consume a recovery-only checkpoint as ordinary live refresh merely because it is newer;
11. if safe seed/hydration data is unavailable, persist `PairedUninitializedReadOnly` rather than inventing authority;
12. expose normal-handoff eligibility when current-generation membership is valid;
13. expose DR entry when normal authority is genuinely unavailable, even if the device has no ordinary seed/live copy.

There is no request/approval round trip to the old authoritative device.

### 5.3 Pairing/seed snapshot

When a healthy authoritative device can provide a fresh read-only initialization seed, M07 may create/publish a validated non-authority seed through the OneDrive System area. This is a technical convenience, not an approval gate and not an authority grant.

Seed metadata must include lineage/generation/source, identity/change watermark, size/SHA-256, schema/protocol and creation time. It must be SQLite-safe and integrity checked before installation.

A missing, delayed or corrupt seed leaves the joining device read-only and must not prevent legitimate DR.

### 5.4 Generation change and membership

After DR generation advance:

- old-generation registration evidence does not make a device an eligible current-generation handoff target;
- old devices must explicitly reinitialize/join current-generation membership;
- reinitialization installs current validated data before any later authority grant can produce writes;
- old `device_id` may remain the same if its durable identity survived; lost identity/reinstall receives a new ID.

## 6. Normal close and source handoff

### 6.1 Authoritative close UI

Closing an authoritative app presents exactly three user intents:

1. **Close and retain authority** — safe/default; no network required; flush local recovery and close while durable authority remains with this computer.
2. **Transfer authority and close** — explicit target selector from current-generation eligible joined devices excluding self.
3. **Cancel** — return to application without authority change.

One eligible target may be preselected but transfer remains explicit. Multiple targets show display name plus short stable identity to disambiguate duplicates.

A non-authoritative device closes normally and must not be offered an authority-transfer action it cannot perform.

### 6.2 Source protocol

For one normal transfer, execute in this exact safety order:

1. operator explicitly chooses transfer + exact target;
2. switch central business write guard to non-writable/Transitioning before remote work;
3. finish accepted application writes and flush required local recovery;
4. allocate/persist immutable transfer identity: new `transfer_id`, lineage, generation, monotonic new handoff version, source ID, target ID;
5. create SQLite-safe complete snapshot;
6. run SQLite integrity/schema validation;
7. compute byte size and SHA-256;
8. reserve non-overwriting timestamp-based snapshot filename and matching grant filename;
9. upload snapshot to configured private GitHub Release Asset container;
10. require/revalidate strict GitHub receipt: success creation status, uploaded state, exact name/size, asset ID and server digest matching local SHA-256;
11. **irreversible durability point:** persist/reopen/revalidate `RelinquishedPendingGrant` carrying exact immutable transfer + exact accepted snapshot receipt;
12. reconstruct/re-evaluate write guard from durable state and prove business writes are rejected;
13. only now construct matching target-bound grant from the persisted relinquished state;
14. upload grant and require/revalidate strict receipt for its exact immutable bytes/identity;
15. persist/reopen/revalidate `ReleasedNonAuthoritative` with grant receipt;
16. run newest-three retention cleanup;
17. complete transfer-and-close UI.

No grant-generation code path may be callable from pre-relinquishment state.

### 6.3 Source failure matrix

Before durable step 11:

- network/auth/upload/integrity/cancel failure may safely abort back to Authoritative **only if no target-releasing grant exists**;
- already allocated transfer IDs/versions should not be reused; monotonic gaps are allowed;
- cleanup of an incomplete snapshot asset is technical/non-authority-critical and must use exact asset ID.

At/after durable step 11:

- no transition back to writable source exists;
- no retarget exists;
- no replacement transfer exists;
- restart reconstructs read-only pending state;
- allowed work is same-transfer idempotent technical retry/status/close only;
- target permanently lost/unusable routes to explicit DR.

Crash injection is required before and after every durable/remote boundary above.

## 7. GitHub normal handoff transport

Production must preserve the M02-proven GitHub transport contract:

- dedicated private handoff repository, not source-code repository;
- one configured long-lived Release container;
- immutable `.snapshot.db` + `.grant.json` unit;
- timestamp filename is uniqueness/operator orientation only, not ordering authority;
- protocol lineage/generation/handoff version/transfer ID provide ordering/identity;
- uploaded snapshot/grant receipts are persisted by exact asset identity;
- malformed/contradictory/starter/partial assets never grant authority;
- newest three complete validated units retained after a newer Released unit exists;
- temporary fourth complete unit permitted;
- cleanup failure does not roll authority back and is retryable.

Network/HTTP failure tests must cover at least 401, 403, 404, 409/422 as applicable, 5xx/502, timeout, truncated response, `starter`, duplicate name, contradictory size/digest/state and stale listing.

## 8. Target discovery and acquisition

A non-authoritative joined device may acquire a normal handoff only for an exact grant targeted to its local immutable `device_id` in its current lineage/generation.

Exact sequence:

1. discover candidate complete grant/snapshot unit;
2. reject wrong target, self-as-source, wrong lineage/generation, stale/replayed version/transfer, non-member target or malformed protocol;
3. fetch exact snapshot asset referenced by the grant;
4. validate remote asset ID/name/size/digest against grant and durable source evidence;
5. download to staging;
6. compute local bytes/SHA-256 and compare;
7. validate SQLite integrity and expected application schema;
8. persist/reopen/revalidate `TargetAcquisitionPending` while guard remains non-writable;
9. atomically install/replace local `Data/live.db` from validated staging;
10. reopen/revalidate installed live DB;
11. durably persist/reopen/revalidate local `Authoritative` acquisition tied to the exact transfer and high-water version;
12. only after step 11 may the central guard become writable.

Crash after DB installation but before authority commit remains read-only on restart and resumes/revalidates the same acquisition idempotently. Never commit authority first and data second.

A third/non-target device seeing the same valid unit remains read-only.

## 9. Recovery-only OneDrive checkpoints

### 9.1 Scheduling

Use the existing M06 post-commit durable-change seam, with a separate M07 checkpoint scheduler/watermark rather than coupling cloud publication to the local Recovery debounce state.

Rules:

- only the current authoritative device publishes ordinary DR checkpoints;
- durable business data must have changed since the previous completed checkpoint;
- maximum normal publication frequency: one completed checkpoint per 15 minutes;
- multiple changes within the window coalesce to the latest safe snapshot;
- no change => no redundant checkpoint required;
- OneDrive/network failure delays/retries publication but does not revoke local authority or block ordinary local POS writes;
- process shutdown may perform a bounded safe flush if one is already due/appropriate, but must not turn normal close into a fragile network authority gate.

Use injectable time and deterministic scheduler tests.

### 9.2 Checkpoint unit

Each completed checkpoint must have immutable metadata sufficient to validate/order candidates, including at least:

- protocol/schema version;
- checkpoint ID;
- lineage ID;
- generation;
- source device ID;
- durable business-change watermark/high-water information;
- relevant handoff high-water information where needed for cross-artifact comparison;
- creation timestamp for orientation;
- exact DB filename;
- exact byte size;
- SHA-256.

Snapshot must be SQLite-safe and integrity/schema validated before final publication.

Only complete validated units count toward retention; newest five are retained. Older unit deletion occurs only after a newer valid replacement exists. Incomplete/corrupt units do not count.

Checkpoint is never an authority release/grant, membership approval or ordinary non-authoritative refresh source.

## 10. Disaster Recovery

### 10.1 Entry conditions

DR is a separate explicit workflow, not a button that bypasses normal target selection.

Before activation UI must show:

- known lineage/current/pre-recovery generation;
- known prior authoritative/designated target where available;
- reason normal path is unavailable;
- validated recovery candidates;
- candidate type/source/version/watermark/time;
- possible data-loss window;
- explicit quarantine warning/confirmation.

Operator must explicitly confirm that the old authoritative/designated-target machine is genuinely unavailable and will remain stopped/quarantined from Sushi81 POS writes until reinitialized.

If online GitHub coordination is unavailable, DR cannot complete and the device remains read-only. Do not substitute an offline force-authority switch.

### 10.2 Safe candidate discovery

Eligible candidate types in the pre-recovery generation:

- complete validated GitHub handoff snapshot + its valid matching grant;
- complete validated OneDrive DR checkpoint.

Never eligible:

- snapshot-only GitHub handoff without valid grant;
- starter/partial/corrupt assets;
- wrong lineage/generation;
- invalid size/hash/SQLite/schema;
- stale candidate that protocol ordering proves is older than another safely ordered candidate when policy selects freshest.

Ordering uses durable protocol high-water/change metadata, not filenames or wall-clock timestamps alone.

If two valid candidates cannot be safely ordered from durable protocol metadata, do not silently guess. Present the ambiguity and explicit data-loss warning for operator selection while both remain read-only candidates.

### 10.3 GitHub single-winner recovery activation

Use a deterministic generation-bound asset name in the configured private handoff Release, for example:

```text
dr-<lineage-id>-g-<next-generation>.activation.json
```

Exact safe naming may normalize IDs but must map one lineage + next generation to exactly one activation asset name.

Immutable activation payload contains at least:

- protocol/schema version;
- lineage ID;
- prior generation;
- next generation;
- unique `recovery_id`;
- exact recovery device ID;
- selected candidate type/identity/hash/high-water evidence;
- creation timestamp for diagnostics;
- payload size/hash validation fields as technically appropriate.

Protocol:

1. generate/persist local immutable recovery identity while guard false;
2. construct exact activation bytes deterministically for that recovery attempt;
3. attempt create/upload under the deterministic next-generation asset name;
4. only a strict successful creation receipt with uploaded state, exact name/size/asset ID and matching digest can provisionally win;
5. if duplicate-name/conflict response occurs, fetch/revalidate the exact existing asset;
6. if existing uploaded valid activation matches the same `recovery_id`, device ID and exact payload, treat as idempotent resume;
7. if existing uploaded valid activation belongs to another recovery identity/device, this contender loses and remains read-only;
8. a `starter`/incomplete asset is not a winner and grants nobody authority; safe cleanup may delete only its exact asset ID, after which contenders may retry the create-once operation;
9. a valid uploaded activation is authority-history/fencing evidence and must not be deleted/replaced by ordinary retention cleanup.

Before broad DR implementation, prove this primitive with deterministic concurrency tests and a disposable real private-repository race/retry drill using synthetic data only. If one-winner behavior cannot be demonstrated, stop and record `Blocked — architecture decision required`.

### 10.4 Local DR completion order

After this device owns/resumes the valid next-generation activation:

1. persist/reopen/revalidate `DisasterRecoveryPending` with exact activation receipt; guard false;
2. download/copy selected safe candidate to staging;
3. verify exact bytes/hash/integrity/schema again;
4. atomically install local `live.db`;
5. reopen/revalidate installed DB;
6. establish current-generation System membership/metadata for the recovery device without creating a second writer;
7. durably persist/reopen/revalidate local `Authoritative` at next generation bound to the exact recovery activation/candidate;
8. only then make `IWriteAuthorityGuard` writable;
9. publish/refresh current-generation non-authority device metadata/seed as appropriate asynchronously.

Crash after remote activation but before step 7 means zero recovery writers. Restart may resume only the same recovery identity on the same exact activation.

## 11. Stale-generation behavior and reinitialization

When a device with generation `G` observes/proves current generation `G+1`:

- persist stale-generation/read-only state before allowing any coordinated action;
- central business write guard must reject writes;
- old normal handoff grants are ineligible;
- old registration does not qualify as current-generation target membership;
- UI explains that DR occurred and this computer must be reinitialized;
- reinitialization installs validated current-generation data and current membership while remaining non-authoritative unless a later valid normal handoff targets it.

Do not merge stale local writes automatically. Do not silently discard stale local files without preserving diagnostics/recovery evidence according to existing safe-storage policy.

The disconnected-old-writer physical limitation must be documented truthfully: if the operator violated the DR quarantine rule and continued writing while that machine was isolated, another computer cannot have remotely stopped those writes. On reconnection/observation the old generation is still fenced from further accepted operation; reconciliation is not automated in V1.

## 12. Credential and configuration boundary

Normal/DR GitHub transport configuration:

- repository owner/name and long-lived release identifier are non-secret configuration;
- use a dedicated private handoff repository, never the source-code repository;
- production GitHub token uses least privilege limited to the handoff repository;
- production secret storage uses a Windows protected credential mechanism behind an application-owned interface (Windows Credential Manager or equivalently appropriate first-party Windows protected secret store selected during implementation);
- environment-variable token support is allowed only in isolated tests/tools, not as production credential persistence;
- token/Auth headers must never be written to SQLite, authority JSON, OneDrive metadata, GitHub asset payloads, logs, screenshots or operator error text;
- logs use existing sensitive-data redaction discipline.

Provide a non-authority-mutating **Test connection/configuration** action so operator setup can verify repository/release/credentials without releasing/acquiring authority.

## 13. Idempotency and retry invariants

Every remote/durable operation must have an explicit idempotency identity.

- self-join retry => same device registration or fail closed on contradiction;
- source transfer retry before relinquishment => same prepared identity while active; abandoned transfer identity never becomes a later unrelated transfer;
- source retry after relinquishment => exact same transfer/target/snapshot/grant only;
- target acquisition retry => exact same target grant/transfer only;
- checkpoint retry => exact checkpoint identity until finalized; failed partial units not counted;
- DR activation retry => exact same `recovery_id`/device/payload; another valid recovery activation wins and this device remains read-only;
- retention cleanup => exact remote asset IDs only; never filename-only broad deletion.

No retry path may convert an ambiguous result into write authority merely because it “probably succeeded”. Re-read/revalidate authoritative local/remote evidence.

## 14. Observability

Add structured, redacted diagnostics sufficient to reconstruct M07 safety state without exposing customer data or credentials.

Record at least:

- local device short ID/lineage short ID/generation/phase;
- transfer ID/version/source/target short identities;
- remote snapshot/grant asset IDs and validation status, not token;
- irreversible relinquishment reached/not reached;
- target acquisition pending/completed;
- checkpoint ID/watermark/publication/retention outcomes;
- DR recovery ID/prior→next generation/candidate identity/activation asset ID/outcome;
- stale-generation observation/reinitialization;
- retries and failure classification.

Do not log full customer/order payloads merely to troubleshoot authority transfer.

## 15. WPF UX requirements

All safety-critical M07 UI is available in French and Simplified Chinese.

Required surfaces include:

- device identity/display name and current authority status;
- join-existing-lineage setup flow;
- paired/current-generation device list;
- close-retain / transfer-and-close / cancel dialog;
- target selector;
- pre-relinquishment progress/cancel behavior;
- post-relinquishment irreversible pending state with exact retry/status, no writable cancel/retarget;
- non-authoritative/read-only banner/state;
- paired-uninitialized read-only state;
- target acquisition progress/failure;
- DR candidate list and data-loss information;
- explicit old-device quarantine confirmation;
- DR online-coordination failure;
- stale-generation/reinitialize presentation;
- GitHub/OneDrive configuration and non-mutating connection test.

Preserve unrelated screen/control state during localized refresh/status changes under `control-state-preservation.md`. Long network/file operations must not freeze the WPF dispatcher.

## 16. Automated verification contract

M07 may not rely on manual testing for protocol correctness.

### 16.1 Authority state/migration

Cover:

- every valid detailed phase → derived `WriteAuthorityState`;
- exact M06→M07 serialized migration for Authoritative/RO/Transitioning/RecoveryRequired;
- missing/corrupt/unsupported/contradictory authority JSON;
- marker/anchor/live-DB mismatch;
- crash/durability injection around every safety-sensitive state replacement;
- reconstructing guard from disk, not only in-memory transition;
- all M03–M05 business mutation services still call central guard before SQL mutation.

### 16.2 Self-join/N-device

At least:

- B joins while authoritative A is healthy with **no A approval action**;
- B remains read-only after join;
- A can later target B normally;
- C joins independently; N-device list works;
- simultaneous independent device registration artifacts do not overwrite each other;
- duplicate retry same device is idempotent;
- contradictory registration fails closed;
- wrong lineage/stale generation fails closed;
- missing/corrupt seed keeps PairedUninitializedReadOnly;
- fresh replacement joins after A is unavailable and can enter DR without a seed;
- reinstall/lost device ID creates new identity;
- post-DR old-gen membership is not current target eligibility.

### 16.3 Normal transfer

Deterministic 3+ device state/interleaving tests for:

- close-retain offline and restart;
- exact target selection;
- wrong-target/non-target/replayed/stale generation/version rejection;
- snapshot hash/size/truncation/SQLite/schema corruption;
- grant metadata mismatch;
- crash before/after snapshot creation/upload/receipt;
- cancel before relinquishment;
- crash immediately before/after durable relinquishment;
- reconstructed source guard false after relinquishment;
- grant cannot exist before relinquishment;
- source restart after relinquishment only resumes same transfer;
- no retarget/no writable rollback;
- target crash before staging/after staging/after live DB replace/before authority commit/after authority commit;
- idempotent acquisition;
- newest-three retention/temporary fourth/cleanup failure/retry.

### 16.4 GitHub transport failures

Use deterministic fake HTTP/server layers for status/result permutations including:

- authentication/permission failure;
- not-found/misconfiguration;
- duplicate-name conflict;
- 502/5xx and starter assets;
- timeout before/after server-side create;
- contradictory uploaded state/name/size/ID/digest;
- stale listings;
- exact same-response retry vs conflicting asset.

Unknown outcome must re-observe remote state and fail closed until proven.

### 16.5 OneDrive checkpoint

Cover:

- changed vs unchanged durable data;
- fake clock exactly around 15-minute boundary;
- multiple coalesced changes;
- newest-five retention;
- incomplete/corrupt metadata/DB;
- offline/unavailable OneDrive;
- shutdown/restart scheduler watermark;
- checkpoint failure never changes authority;
- checkpoint cannot be consumed as normal handoff/read-only auto-refresh.

### 16.6 DR

Must include:

- complete handoff+grant eligible;
- snapshot-without-grant ineligible;
- OneDrive checkpoint eligible;
- corrupt/wrong lineage/generation/hash/schema rejected;
- freshest safely ordered selection;
- ambiguous ordering produces explicit selection, not silent clock guess;
- quarantine confirmation required;
- offline DR cannot activate;
- two/three concurrent next-generation contenders produce at most one valid activation;
- strict successful create winner;
- duplicate existing same recovery identity = idempotent resume;
- duplicate existing other recovery identity = loser/read-only;
- starter does not win; safe exact-ID cleanup/retry;
- timeout unknown outcome reconciled by exact remote observation;
- crash after remote activation before local pending/DB install/authority commit;
- same recovery resumes; another device cannot steal it;
- new generation becomes authoritative only data-first + durable-authority-last;
- old generation refuses writes/old grants after observation;
- old device reinitializes then remains RO until later handoff;
- no automatic generation data merge.

### 16.7 Real WPF STA tests

Exercise actual controls/dialogs, not only view-model string tests:

- close default=retain;
- target selector one/multiple targets;
- cancel before relinquishment;
- post-relinquishment button/action set removes writable cancel/retarget;
- self-join does not present source-approval dependency;
- PairedUninitializedReadOnly presentation;
- DR warning/checkbox/confirmation affordance;
- safe candidate rows;
- stale generation/reinitialize;
- FR↔zh-CN runtime switch;
- representative window sizes/DPI where existing quality gate requires;
- unrelated selection/filter/edit control state survives status/localization refresh.

## 17. Real GitHub DR activation proof gate

Before broad production DR completion, execute an isolated proof against a disposable/private test Release/repository configuration using only synthetic files/identities.

Required evidence:

1. two independent contenders target the exact same deterministic lineage+next-generation activation asset name;
2. exactly one obtains the accepted create/upload receipt or the test proves an equivalent unique accepted remote object;
3. the losing contender reads the existing activation and remains non-authoritative;
4. same-winner retry resumes idempotently;
5. simulated/observed starter handling grants nobody authority and exact-ID cleanup/retry does not create two valid activations;
6. remote final state contains one valid immutable activation for that generation;
7. no production credentials/data are committed to the repository or test logs.

Failure of this proof blocks M07 architecture; do not compensate with timing sleeps or “last writer wins”.

## 18. Project-owner Windows/WPF acceptance contract

Final M07 acceptance must use the exact accepted production head and real Windows/WPF artifacts, with at least two physical Windows PCs A/B and a third logical or real device C where needed. A VM/Windows Sandbox may supplement the third-device case; it must not replace all real cross-device evidence.

Use:

- synthetic Sushi81 data only;
- actual configured OneDrive shared root;
- actual dedicated private GitHub handoff repository/release;
- production credential path rather than test environment variable for the production-app scenarios.

The prepared durable checklist is `docs/implementation/milestone-07-final-manual-acceptance.md`.

## 19. Performance/responsiveness

Network/checkpoint/hash/SQLite snapshot operations run asynchronously off the WPF dispatcher as appropriate.

Do not hold SQLite write transactions while waiting for user input, GitHub, OneDrive or long hashing/upload work.

Normal order/payment/catalogue operations on the authoritative device must not acquire a remote coordinator lease or wait for cloud checkpoint completion.

## 20. Work-package order for Codex

When and only when M07 is explicitly authorized, execute in this order unless a proven dependency requires a smaller local reordering:

### WP0 — baseline and guard audit
- rebuild from current `main`;
- confirm no stale competing authority architecture;
- enumerate all mutation paths and preserve M06 guard coverage;
- run baseline Release tests/build before changes.

### WP1 — canonical M07 authority state
- schema/model/migration;
- durable atomic state store;
- derived write states;
- phase/restart/failure tests.

### WP2 — device identity/self-join/System metadata
- immutable device identity;
- current-generation registration;
- PairedUninitializedReadOnly;
- optional seed hydration;
- N-device tests/WPF setup.

### WP3 — production GitHub configuration/credential/transport
- protected credential interface;
- connection test;
- production Release Asset client using M02-proven strict receipts;
- transport fake tests.

### WP4 — normal source handoff/close
- WPF close choices;
- snapshot/receipt/relinquishment/grant/Released sequence;
- same-transfer retry;
- retention;
- failure injection.

### WP5 — target discovery/acquisition
- exact-target validation;
- staging/install/durable activation;
- crash/idempotency/stale/wrong-target tests;
- WPF state.

### WP6 — OneDrive DR checkpoints
- change seam + independent scheduler/watermark;
- metadata/safe snapshot;
- newest-five retention;
- offline/failure behavior.

### WP7 — prove DR activation primitive
- deterministic race model;
- disposable real private-repository race/retry drill;
- stop for architecture amendment if single-winner is unproven.

### WP8 — production DR
- safe-candidate discovery/order;
- quarantine UI;
- online activation;
- local restore/new-generation commit;
- stale-generation/reinitialization;
- complete failure/crash tests.

### WP9 — integration/WPF/localization/observability
- full cross-feature guard regression;
- STA/WPF tests;
- FR/zh-CN completeness;
- responsive async UI;
- redacted structured diagnostics.

### WP10 — closure evidence
- complete Release suite;
- Release build 0 warnings/errors;
- isolated self-contained `win-x64` publish;
- exact-head CI;
- update worklog/status evidence without claiming manual Passed;
- prepare exact artifact/head for project-owner manual acceptance.

Codex must not perform the project-owner manual acceptance itself and must not merge the PR.

## 21. Required build/test evidence

At implementation completion before owner manual acceptance, record:

- exact production-code head SHA;
- `dotnet test` Release result including total passed/failed/skipped;
- Release build result and warning/error counts;
- self-contained `win-x64` publish result;
- exact-head CI run ID/status;
- targeted protocol/failure test names/coverage;
- real DR activation proof repository/run evidence without credentials;
- any known limitations explicitly owned by M08+ only.

Any failed test, ambiguous authority invariant, real-repo race proof failure or safety-related manual blocker keeps M07 open.

## 22. Exit criteria

M07 implementation can be proposed for project-owner acceptance only when:

- all authorized scope is implemented in production code;
- all automated tests pass;
- normal source/target handoff and retention are proven;
- self-join replacement-PC path does not require dead source approval;
- pairing/self-join never grants authority;
- OneDrive checkpoints satisfy change/frequency/retention/no-authority rules;
- single-winner DR activation is proven deterministically and against a real disposable private GitHub test configuration;
- DR uses only validated safe candidates and enforces quarantine confirmation/new generation;
- stale generation remains read-only/reinitializable;
- production credential path is protected/redacted;
- FR/zh-CN safety UI is complete;
- exact-head Release/CI/publish evidence is green;
- project-owner manual checklist is ready but remains Pending until executed by owner.

M07 becomes Passed only after the owner executes and passes the exact-head Windows/WPF checklist and the final evidence/status closure records that result.

## 23. Governance boundary

This prepared contract does **not** authorize implementation.

Before Codex execution, the governance controller must, after explicit owner implementation approval:

1. create a separate durable M07 authorization record;
2. ensure worklog/manual checklist point to the authorized contract;
3. create dedicated M07 implementation branch and PR;
4. publish the single active mailbox pointer/handoff;
5. verify no conflicting active milestone/handoff exists;
6. only then open GitHub issue #4 execution gate;
7. give Codex one complete executable handoff.

Until those events occur, Issue #4 stays CLOSED, no executable `CODEX_HANDOFF_READY` exists, and M08 remains not started.
