# M13 — Installer, localization completion and final V1 acceptance — worklog

**Status:** Preparation  
**Branch:** `codex/m13-installer-final-acceptance-authorized`  
**Draft PR:** #26 — M13: Installer, localization completion and final V1 acceptance  
**Start baseline:** `f59663c6b47ab21114c24360544e4e25094f4722`

This file is append-only for M13 package/evidence history. Historical failures are not rewritten away.

## 2026-09-24 — controller preparation and baseline reconstruction

Verified current GitHub baseline:

- PR #25 closed after M12 merge;
- `main` = `f59663c6b47ab21114c24360544e4e25094f4722`;
- M12 final pre-merge head = `49a0fe69e23e68c5591ef36ba5c56ef09a9d88b3`;
- CI #862 / run `36021889765`: success on the final pre-merge head;
- CI #863 / run `36023054757`: post-merge build-and-test success;
- Issue #4 closed; active executable handoff = none;
- M12 populated real-archive manual verification remains explicitly deferred under owner waiver;
- M13 Gestion export retention/compaction owner decision = PR #25 comment `5792796519`.

Controller prepared:

- Approved decision record for M13 export retention/compaction;
- matching acceptance amendment;
- M13 readiness, implementation contract, authorization, worklog and final owner acceptance checklist;
- work-package sequence WP1–WP6.

Security finding:

- GitHub currently reports `cimerosef/sushi81-pos` visibility = `public`, conflicting with the project's stated private-repository expectation.
- Preparation may continue, but Issue #4 remains CLOSED and no executable READY is published until owner/admin resolves that discrepancy.

Next intended executable package after resolution: WP1 — Gestion export retention/compaction core only.

## 2026-09-24 — durable M13 mailbox established

- Draft PR #26 was opened from `codex/m13-installer-final-acceptance-authorized` to `main`.
- PR start baseline remains `f59663c6b47ab21114c24360544e4e25094f4722`.
- Issue #4 remains CLOSED; active executable handoff = none.
- No READY was published because the source-repository visibility discrepancy remains unresolved.


## 2026-09-24 — owner repository-visibility disposition

The project owner explicitly instructed the controller not to block M13 because `cimerosef/sushi81-pos` is public and stated that the public state is intentional.

Controller disposition:

- repository visibility is no longer an M13 blocker;
- no visibility change is required for M13;
- preparation/control package remains valid;
- the next executable handoff is WP1 Gestion export retention/compaction core only;
- Issue #4 may be reopened only after the exact WP1 READY is durably published on PR #26.


## 2026-09-24 — controller WP1 technical decision

For safe compaction, absence from `orders` is not accepted as proof of M12 archival.

WP1 is directed to add/use compact durable per-order archive proof in `live.db`, populated atomically with M12 live-order deletion/completion. This proof follows normal live-database handoff and avoids depending on local Archive files that do not transfer with authority. Missing/legacy proof fails closed and retains export history. The proof itself may remain long-term; only obsolete full M11 payload/history is subject to the approved compaction policy.
