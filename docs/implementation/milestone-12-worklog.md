# M12 — Annual archive and historical access — worklog

**Status:** Active implementation evidence ledger
**Milestone:** M12
**Implementation PR:** #25
**Branch:** `codex/m12-annual-archive-authorized`

## 2026-09-21 — Controller preparation / current-state reconciliation

Verified before M12 execution:

- authoritative main: `1a94f3400e0aa9fe9f878bbe98a8285112206ba9`;
- PR #24 M11: CLOSED/MERGED;
- M11 accepted runtime candidate: `77ccecf9d947462e96e74b8aa1d99ced30e3788e`;
- M11 final documentation head: `4eddf0a93c5a141326d76c6e67a9a9c5840e5068`;
- controller closure: PR #24 comment `5762784303`;
- merge completion: PR #24 comment `5762846107`;
- post-merge CI #821 / run `35617254203`: SUCCESS, 831/831 passed, 0 failed, 0 skipped, Release build 0 warnings / 0 errors;
- Issue #4 was CLOSED with no active executable handoff before M12 setup;
- M12 owner authorization was already granted for post-M11 start;
- M13 remains unauthorized.

Readiness review and implementation contract were created on PR #25. The review found no blocker to WP1, but preserved the known M02 OneDrive remote-publication acknowledgement limitation as a blocker to the later WP2 live-removal gate under `AC-STO-013`.

## Package ledger

| Package | State | Evidence |
|---|---|---|
| WP1 — archive core/eligibility/staging | READY pending exact handoff | First executable M12 package; no OneDrive publication/live deletion. |
| WP2 — export preservation/publication/live removal | BLOCKED at remote acknowledgement choice | Must not enable live deletion until conforming publication confirmation is explicitly approved. |
| WP3 — scheduler/authority/retry | Not started | No executable handoff. |
| WP4 — explicit archive selection/hydration/search | Not started | No executable handoff. |
| WP5 — archived reprint/hardening/owner candidate | Not started | No executable handoff. |


## 2026-09-21 — Owner amendment — local archive + user-selected export

Owner evidence: PR #25 comment `5765067664`.

Approved change:

- canonical annual archive databases are permanent application-managed local business data under the Sushi81 POS local application-data root;
- OneDrive is removed from annual archive publication, acknowledgement, retention, discovery and ordinary historical access;
- automatic February/late-start archiving remains non-interactive;
- completed archive export is a separate explicit copy action and the operator chooses its destination;
- export never moves/deletes the canonical local archive and failure cannot mutate canonical archive/live data;
- normal handoff does not automatically transfer annual archive files.

Control action:

- Issue #4 was CLOSED before reconciliation;
- former READY `M12-WP1-ARCHIVE-CORE-STAGING-01` / PR #25 comment `5764191282` was revoked before further execution;
- Approved decision and acceptance amendment were added and baseline/living docs aligned;
- the former OneDrive remote-publication acknowledgement blocker no longer applies to M12 annual archive completion;
- a new unique WP1 READY is required from the reconciled exact head.

Updated package plan:

| Package | State | Evidence |
|---|---|---|
| WP1 — archive core/eligibility/staging | READY after reconciliation | Local staging only; no canonical promotion/live deletion/export UI. |
| WP2 — export preservation/local canonical publication/live removal | Not started | Stage/validate -> durable local canonical promotion/reopen validation -> exact removal; preserve pending export payloads first. |
| WP3 — scheduler/authority/retry | Not started | Automatic February/late-start remains non-interactive. |
| WP4 — explicit local archive selection/search + user-selected export | Not started | Local Archive discovery/read-only access; explicit export copy destination selected by operator. |
| WP5 — archived reprint/hardening/owner candidate | Not started | No change to M08 snapshot/reprint semantics. |

## 2026-09-21 — WP1 evidence/governance closure package 03

Mailbox and scope record:

- source handoff: `CODEX_HANDOFF_READY: M12-WP1-EVIDENCE-CLOSURE-03`, PR #25 comment `5766940595`;
- handoff start head: `ef437f535c160224666269e50bb3060a858bf5b9`;
- historical premature/intermediate completion record `CODEX_DONE: M12-WP1-LOCAL-ARCHIVE-CORE-STAGING-02`, PR #25 comment `5766817946`, was preserved and not reused or edited for this closure;
- controller finding: PR #25 comment `5766933100` identified the focused evidence gap and required a new distinct closure record;
- production code changed: no; WP1 production implementation remains the start-head implementation. This package adds only focused integration evidence and this append-only ledger entry.

Focused evidence added and passing:

1. `ValidatorRejectsTruncatedArchiveAndPreservesLiveData` rejects a truncated SQLite archive and verifies live orders plus the M11 export ledger remain unchanged.
2. `ValidatorRejectsMissingRequiredSchemaAndCountMismatch` rejects a missing required table and an expected-order-count mismatch.
3. `ValidatorRejectsChildMismatchAndArchiveRetainsEquivalentSnapshotFactsWithoutCatalogue` rejects missing child rows and reconstructs archive-only scalar, item, adjustment, payment, and tax facts without current Catalogue/VAT/settings reads.
4. `RepeatedStagingIsIndependentAndM11LedgerStateRemainsByteEquivalent` proves two successful independent staging attempts, byte-equivalent `export_batches`/`export_batch_orders`/`export_emissions` state, service-level validation-failure cleanup, and retry success.

The previously retained WP1 policy/time, eligibility, authority, path, metadata, and builder-failure/retry tests remain passing. Focused M12 result: 19 passed, 0 failed, 0 skipped (9 application policy tests, 9 annual-archive integration tests, 1 archive-path test). Full `dotnet test Sushi81.Pos.sln -c Release --no-restore --nologo`: 850 passed, 0 failed, 0 skipped. Full Release build: 0 warnings, 0 errors. `git diff --check`: clean. Test implementation head: `66a592c`.

Final closure delivery remains bounded to WP1 evidence/governance. No WP2 behavior, canonical archive promotion, live deletion, scheduler, archive UI/search/export/reprint, or M13 work is included or authorized by this handoff. Issue #4 gate was OPEN during execution; PR #25 remains the active Draft/open implementation PR and is not merged.

## 2026-09-21 — WP2 local publication/export-preservation/live-removal package 04

Mailbox and scope record:

- source handoff: `CODEX_HANDOFF_READY: M12-WP2-LOCAL-PUBLISH-EXPORT-PRESERVATION-LIVE-REMOVAL-04`, PR #25 comment `5767278524`;
- handoff start head: `c0c16ac7e11a52f87d3a939a9d024f866c361a7e`;
- controller prerequisite: WP1 accepted by PR #25 comment `5767263231`;
- implementation/evidence commit: `2c68d4a` (`feat: finalize local annual archive removal`);
- Issue #4 was OPEN and PR #25 remained the active Draft/open implementation PR during execution.

Implemented only the authorized M12 WP2 boundary:

- migration 9 adds the durable `annual_archive_completions` ledger and upgrade/failure-rollback coverage;
- pending M11 CREATE/UPDATE/CANCEL actions are preserved as immutable PREPARED payloads using the exact action/payload/predecessor tuple, with retry reuse and stale-prepared separation;
- validated WP1 staging is copied through an incoming file, flushed, reopened/validated, atomically promoted without overwrite, and validated again before live removal;
- the exact validated order identity set is deleted in one SQLite transaction with the completion marker, automatic business-revision advancement, and post-commit durable recovery notification;
- repeated completion is idempotent, a missing canonical file is not silently recreated, corrupt canonical data fails closed, and copy/transaction failures leave live data retryable;
- expanded archive validation compares the full historical fact rows relevant to the target orders, not only identifiers and child counts.

Evidence recorded before final delivery:

- focused M12 WP2 real-SQLite/filesystem suite: 6 passed, 0 failed, 0 skipped;
- existing M12 WP1 staging/validation regression: 9 passed, 0 failed, 0 skipped;
- full `dotnet test Sushi81.Pos.sln -c Release --no-restore --nologo`: 856 passed, 0 failed, 0 skipped;
- full Release solution build: 0 warnings, 0 errors;
- `git diff --check`: clean before commit.

The package remains bounded to local annual archive publication, pending Gestion payload preservation, exact live removal and recovery notification. No scheduler, archive UI/search, user-selected archive export, historical reprint, OneDrive publication, M13 work or PR merge is included or authorized by this handoff. Exact-head CI and the durable matching `CODEX_DONE` remain the final delivery steps.

## 2026-09-21 — WP2 evidence/governance closure package 05

Mailbox and scope record:

- source handoff: `CODEX_HANDOFF_READY: M12-WP2-EVIDENCE-CLOSURE-05`, PR #25 comment `5767856589`;
- handoff start head: `6529b3014c772f8877b9ea2233ba69fb13c2c89f`;
- controller review: PR #25 comment `5767851546`;
- Issue #4 was OPEN and PR #25 remained the active Draft/open implementation PR during execution;
- production code changed: no; this closure package adds focused real-SQLite/filesystem integration evidence and this append-only ledger entry only.

Focused evidence added and passing:

1. unchanged already-successful UPDATE and CANCEL actions produce no new PREPARED preservation action;
2. exact PREPARED reuse does not duplicate, while a stale differing PREPARED payload does not satisfy the current action and the current action is preserved once; the exact/current PREPARED batches remain usable through `MarkBatchSucceededAsync` after live order deletion;
3. a zero-order year publishes a canonical archive, records count 0/year/hash, has no PREPARED action, and the second call is `AlreadyCompleted` without mutation;
4. exact closed/cancelled removal deletes child rows while open orders and closed/cancelled orders from another year remain;
5. `flush`, `incoming-validation`, `rename` and `post-rename-validation` publication failures leave live state and completion-marker state retryable;
6. a valid canonical archive without a completion marker is reused, while a source-mismatching canonical archive fails closed without replacement or live deletion;
7. an independently computed SHA-256 matches the finalization result and completion metadata;
8. pre-existing successful M11 export history is content-equivalent before and after archive finalization.

Validation evidence:

- focused WP2 real-SQLite/filesystem suite: 12 passed, 0 failed, 0 skipped;
- combined M12 WP1/WP2 regression filter: 22 passed, 0 failed, 0 skipped;
- full `dotnet test Sushi81.Pos.sln -c Release --no-restore --nologo`: 862 passed, 0 failed, 0 skipped;
- full Release solution build: 0 warnings, 0 errors;
- `git diff --check`: clean.

This closure remains bounded to WP2 evidence/governance. No WP3 scheduler/authority/retry work, archive UI/search/export/reprint, M13 work or PR merge is included or authorized. Exact-head CI and the durable matching `CODEX_DONE` remain the final delivery steps.

Exact-head CI for the pushed evidence commit `c2ae720f8a0c0aaa7c1989e7973b16898a4e9251`: Continuous integration run #853 / workflow run `35660059392` — SUCCESS. The subsequent documentation-only delivery commit retains the same tested implementation and requires its own exact-head CI before `CODEX_DONE`.
