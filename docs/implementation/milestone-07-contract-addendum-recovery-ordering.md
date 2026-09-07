# M07 implementation-contract addendum — deterministic recovery ordering

**Status:** Prepared contract addendum — NOT YET AUTHORIZED  
**Prepared:** 2026-09-07  
**Parent contract:** `milestone-07-pairing-handoff-disaster-recovery.md`  
**Execution gate:** CLOSED

This file is part of the prepared M07 implementation contract and is non-executable until separate owner implementation authorization and the normal branch/PR/mailbox/gate sequence.

## 1. Reason for addendum

The final closed-gate specification consistency pass adopted `docs/decisions/m07-recovery-candidate-ordering-clarification.md` so that the project owner's “freshest validated safe recovery data” decision is mechanically deterministic.

Where the parent contract or its acceptance amendment says that two otherwise valid M07 recovery candidates may be left unordered and presented for operator freshness selection, **this addendum supersedes that fallback**.

The operator still confirms Disaster Recovery, quarantine and the displayed possible data-loss window. The operator does not guess which of two M07 artifacts is technically fresher.

## 2. Required durable business-data revision

Production M07 must add one canonical durable monotonic business-data revision/watermark, exact implementation name optional.

Required properties:

- revision advances transactionally with accepted durable business-data mutations;
- it remains local-first and does not require GitHub/OneDrive connectivity to advance;
- it cannot go backwards within the lineage/generation;
- crash/restart cannot reuse one committed revision for a distinct later business state;
- snapshot creation captures the exact revision belonging to the exact SQLite snapshot;
- M07 GitHub handoff snapshot/grant metadata carry that exact revision;
- OneDrive DR checkpoint metadata carry that exact revision;
- DR-restored generation starts from the restored revision and subsequent business writes continue monotonic advancement;
- authority-only metadata state changes need not advance this business-data revision.

The preferred technical shape is a transactional SQLite/application metadata row updated in the same durable transaction as business changes if that yields the simplest reliable invariant. Codex may choose the exact schema/API placement but not weaken the properties above.

## 3. Candidate comparison

For two eligible validated candidates from the same pre-recovery lineage/generation:

1. compare `business_data_revision`;
2. greater validated revision is fresher;
3. equal revision means equal business-data freshness;
4. handoff/checkpoint packaging time does not override revision ordering;
5. wall-clock timestamp and filename remain diagnostics/operator orientation only.

A candidate with missing, malformed, contradictory or untrusted required revision is not a validated safe M07 candidate and must fail closed.

GitHub handoff eligibility still additionally requires the valid matching grant. Revision metadata never makes a snapshot-only incomplete handoff eligible.

## 4. Required test additions

Add deterministic tests proving:

- one business commit advances revision exactly according to the chosen invariant;
- rollback/failed transaction does not falsely advance accepted business state;
- multiple sequential accepted mutations remain monotonic across restart;
- handoff snapshot/grant revision matches captured DB state;
- checkpoint revision matches captured DB state;
- checkpoint and handoff from the same revision compare equal in freshness;
- greater revision wins even when its wall-clock timestamp is earlier/skewed;
- missing/contradictory revision candidate is rejected;
- DR restores selected revision and new-generation later writes advance beyond it;
- offline authoritative writes still advance locally without remote services.

## 5. Parallel-execution companion

`milestone-07-parallel-execution-plan.md` is also a mandatory companion to the parent contract. Main-agent ownership of the canonical revision schema/update seam must be established before subagents implement handoff/checkpoint consumers of that value.

## 6. Precedence

For M07 implementation reading, use this order where wording differs:

1. Approved V1 decision/acceptance records, including `m07-recovery-candidate-ordering-clarification.md`;
2. this contract addendum;
3. parent M07 implementation contract;
4. historical preauthorization design review.

No part of this addendum authorizes Codex execution.
