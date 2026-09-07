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
- `m07-self-service-pairing-and-disaster-recovery.md` — self-service device joining without write authority, exceptional Disaster Recovery quarantine/fencing, and freshest validated safe recovery-source selection.
