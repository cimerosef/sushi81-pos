# M07 recovery-candidate ordering clarification

**Status:** Approved technical consistency clarification — no new product decision  
**Date:** 2026-09-07  
**Applies to:** `m07-self-service-pairing-and-disaster-recovery.md`, `m07-self-join-disaster-recovery.md`, `../acceptance-criteria-amendment-m07-self-join-disaster-recovery.md`, and the prepared M07 implementation contract.

## Purpose

Two equivalent M07 decision records were created during the same closed-gate preparation pass. They agree on the three owner-approved material decisions. One of them (`m07-self-service-pairing-and-disaster-recovery.md`) additionally makes recovery freshness mechanically deterministic by requiring a durable monotonic business-data revision on both eligible recovery-candidate types.

This clarification adopts that stricter technical rule and removes the only meaningful ambiguity between the preparation records. It does not change the project owner's approved product choice that Disaster Recovery uses the freshest validated safe data.

## Controlling ordering rule

M07 production data must expose a durable monotonic `business_data_revision` (exact code name may differ) that advances with accepted durable business-data changes and is captured consistently in recovery/handoff evidence.

At minimum:

- every M07 normal GitHub handoff snapshot/grant unit records the snapshot's exact business-data revision;
- every M07 OneDrive Disaster Recovery checkpoint records the checkpoint snapshot's exact business-data revision;
- revision is part of integrity/protocol validation and is not derived from wall-clock time or filename;
- within one lineage/generation, a candidate with the greater validated revision is fresher;
- equal revision means equivalent business-data freshness for selection purposes even if packaging timestamps differ;
- timestamp remains operator/diagnostic information only.

An artifact missing a required trustworthy revision, carrying contradictory revision evidence, or otherwise not safely comparable under the M07 protocol is **not a validated safe candidate for automatic freshest-data recovery**. It must fail closed rather than being selected by timestamp.

The prepared wording in `m07-self-join-disaster-recovery.md`, the acceptance amendment, or implementation contract that allowed an operator to resolve an otherwise unorderable pair of M07 recovery candidates is superseded by this clarification. M07 must make normal eligible candidates mechanically orderable through durable revision metadata instead of delegating freshness guessing to the operator.

## Revision durability

Implementation may choose the simplest reliable persistence mechanism consistent with the existing architecture, but the revision must satisfy all of these invariants:

1. it cannot go backwards within a lineage/generation;
2. a committed business change must not be represented by a later snapshot/checkpoint with an older revision;
3. crash/restart cannot cause a previously committed revision to be reused for a distinct later business state;
4. snapshot/checkpoint creation observes a revision belonging to the exact captured SQLite state;
5. authority-only metadata transitions that do not change business data need not advance the business-data revision;
6. DR generation advancement preserves the restored business-data revision as the starting data watermark in the new generation, after which later business commits continue monotonic advancement according to the chosen durable scheme.

This may be implemented transactionally in SQLite/application persistence metadata if that is the safest/simple choice; it must not depend on OneDrive/GitHub availability for ordinary local commits.

## Relationship between the two M07 decision records

For implementation reading:

- `m07-self-service-pairing-and-disaster-recovery.md` is the concise canonical owner-decision record for the three material choices and its monotonic-revision rule is controlling;
- `m07-self-join-disaster-recovery.md` remains an Approved detailed companion for UI/storage/governance consequences where consistent;
- this clarification controls if either companion contains older fallback wording about unorderable candidate selection.

There is no product-semantic disagreement between the records after this clarification.
