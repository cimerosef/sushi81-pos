# Architecture and product decisions

This directory records approved decisions that materially constrain Sushi81 POS implementation, product behavior, business rules, architecture, data or operations.

## File naming

Use a **stable descriptive Markdown filename**, for example:

```text
advance-order-marker.md
export-eligibility.md
hiboutik-paste-simplification.md
order-modification-printing.md
```

Existing decision filenames are stable references and should not be renamed merely to introduce numeric prefixes.

## When to create a decision record

Create a separate record when a choice materially constrains later implementation or supersedes an earlier specification assumption and keeping the rationale/consequence visible is useful.

Do not create a decision record merely for a low-level implementation detail that can be chosen without changing approved business behavior or architecture.

## Recommended content

A decision record should state, as appropriate:

- status;
- date;
- scope / documents affected;
- context;
- decision;
- rationale;
- consequences or superseded behavior.

Approved decision records supplement the baseline documents. During a specification-freeze consistency pass, superseded behavior should also be folded into the affected baseline documents so implementation does not need to resolve contradictions by document chronology alone.

## Current M07 authority/recovery decisions

The M07 implementation must read these Approved records together:

- `target-directed-authority-handoff.md` — normal source-directed transfer to exactly one target;
- `github-handoff-transport.md` — dedicated private GitHub Release Asset normal handoff transport and acknowledgement;
- `m07-self-service-pairing-and-disaster-recovery.md` — concise canonical 2026-09-07 owner-decision record: self-service joining without write authority, exceptional DR quarantine/fencing, and freshest validated safe recovery source;
- `m07-self-join-disaster-recovery.md` — detailed Approved companion covering the same owner decisions plus UI/storage/governance consequences;
- `m07-recovery-candidate-ordering-clarification.md` — controlling technical consistency clarification: eligible M07 handoff/checkpoint recovery candidates carry a durable monotonic business-data revision, so freshness is mechanically ordered rather than guessed from timestamps/operator choice.

The two self-join/self-service records were produced during the same closed-gate preparation pass and are not competing product decisions. Where an older sentence in the detailed companion or prepared implementation material allows manual selection solely because two M07 candidates cannot be durably ordered, the recovery-candidate-ordering clarification supersedes that fallback. Otherwise the records are complementary.

## Current M09 Hiboutik paste decisions

M09 reads the original Phase 4 Hiboutik decisions together with the later owner-approved preparation amendment:

- `hiboutik-paste-simplification.md` — ordinary-order fallback rather than a separate emergency-order subsystem;
- `hiboutik-paste-option-confirmation.md` — exact product-code matching and ordinary option confirmation;
- `hiboutik-paste-total-calculation.md` — ordinary Sushi81 pricing remains authoritative;
- `m09-hiboutik-paste-operator-workflow-and-source-reference.md` — Approved 2026-09-14 V1 amendment controlling product-detail-block paste scope, explicit unresolved-line operator handoff, passive `Hiboutik` identification and nullable read-only `source_total_ttc` reconciliation reference.

Where older Phase 4 records require the source discriminator to be completely invisible or forbid retaining any Hiboutik source total, the 2026-09-14 M09 amendment supersedes those narrow clauses. It does **not** restore the former emergency-order UI/model, discrepancy workflow, dedicated Hiboutik reference field or Hiboutik-specific payment/reconciliation subsystem.

## Current M10 Catalogue workbook decision

M10 must read the Catalogue baseline together with:

- `m10-category-short-code-workbook-semantics.md` — **Approved 2026-09-17 V1 amendment** defining how the later M04 Category `short_code` business field is preserved in the three-sheet Catalogue `.xlsx` workflow.

The controlling M10 short-code result is:

- `Products` visibly carries Category name + Category short code;
- no operator-facing `Categories` worksheet is introduced;
- `category_id` stays technical/non-operator identity;
- new Categories created by import may receive one optional consistent short code;
- existing Category short code is preserve/consistency data only and cannot be cleared/replaced through workbook import;
- conflicting repeated Category short-code meaning is a blocking Error;
- existing Category short-code changes remain in the in-app Category manager.

The matching acceptance clarification is `../acceptance-criteria-amendment-m10-category-short-code-workbook.md`, and the consolidated operational baseline is `../catalogue-management.md`.

This specification decision closes M10 readiness but does **not** authorize M10 implementation.