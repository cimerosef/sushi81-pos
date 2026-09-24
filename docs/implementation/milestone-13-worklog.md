# M13 — Installer, localization completion and final V1 acceptance — worklog

**Status:** Preparation  
**Branch:** `codex/m13-installer-final-acceptance-authorized`  
**Draft PR:** TBD at creation  
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
