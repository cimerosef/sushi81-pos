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
