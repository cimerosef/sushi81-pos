# M07 parallel execution plan

**Status:** Prepared companion to `milestone-07-pairing-handoff-disaster-recovery.md` — NOT AUTHORIZED  
**Prepared:** 2026-09-07  
**Execution gate:** CLOSED

This companion supplies the mandatory parallel-execution section for the prepared M07 contract. It becomes executable only with the contract after separate owner authorization and the normal mailbox/gate sequence.

## 1. Integration ownership

The Codex main agent remains integration/safety owner for:

- canonical authority-state schema and migration;
- state-machine invariants and durable transition ordering;
- `IWriteAuthorityGuard` integration;
- CompositionRoot/startup/close integration;
- final merge of subagent work;
- cross-work-package tests/review;
- authority/recovery safety decisions.

Subagents must not independently redefine authority phases, product semantics, state JSON schema or normal/DR ordering.

## 2. Dependency seams

### Sequential spine

The following is the critical path and must be stabilized in order:

1. WP0 baseline/mutation audit;
2. WP1 canonical M07 authority model + M06 migration + durable state store;
3. shared immutable protocol DTO/value contracts for device/lineage/generation/transfer/recovery identities;
4. WP4 source protocol and WP5 target acquisition integration after transport/self-join contracts are available;
5. WP7 single-winner DR activation proof;
6. WP8 production DR and stale-generation/reinitialization;
7. final WP9/WP10 integration/closure.

Do not parallelize two agents editing the canonical authority-state schema or safety transition code at the same time.

## 3. Safe parallel lanes after WP1 seams stabilize

The main agent may delegate these independent lanes when interfaces/DTOs are frozen first:

- **Lane A — self-join/System metadata (WP2):** per-device registration, lineage/generation validation, read-only seed mechanics and isolated tests.
- **Lane B — GitHub transport/credential layer (WP3):** protected credential abstraction, strict Release Asset transport and fake-HTTP tests; no authority transitions inside transport.
- **Lane C — OneDrive recovery checkpoint publisher (WP6):** change scheduler/watermark, checkpoint metadata/snapshot/retention and isolated tests; no authority grant semantics.
- **Lane D — localization/diagnostic scaffolding:** resource keys, redacted event models and UI-neutral presentation contracts once state names are stable.

Each lane must return a bounded commit/set of commits and targeted tests. The main agent reviews/integrates before downstream state-machine work proceeds.

## 4. Work that must not be delegated independently

Keep under main-agent control or integrate immediately under main review:

- source irreversible relinquishment point;
- grant eligibility after relinquishment;
- target authority-activation ordering;
- DR create-once winner interpretation;
- generation advancement;
- stale-generation fencing semantics;
- any code path that changes `IWriteAuthorityGuard` writable state;
- any migration that can promote an existing device to Authoritative.

These are cross-cutting safety invariants, not isolated plumbing.

## 5. Later bounded parallelism

After WP4/WP5 behavior is stable:

- WPF close/target/status surfaces may be developed against frozen application services while protocol failure-injection tests are expanded in parallel;
- retention-specific tests may run separately from target acquisition tests;
- localization/layout/STA WPF tests may be expanded in parallel once resource/control contracts are stable.

After WP7 single-winner proof passes:

- DR candidate-validation test expansion and stale/reinitialization WPF presentation may proceed in parallel with main-agent WP8 integration, provided no subagent alters the activation/generation transition contract.

## 6. Integration gates

Before merging each parallel lane, main agent must verify:

- no competing authority truth/store was introduced;
- no remote call was placed inside a SQLite write transaction;
- no membership/checkpoint artifact acquired authority semantics;
- no token/customer data is logged/serialized remotely;
- all lane tests pass;
- cross-project build remains clean;
- public/internal interfaces match the prepared M07 contract.

A parallel lane that discovers a genuine material contradiction stops and reports it; it must not resolve it by inventing a new product/authority/recovery rule.

## 7. Expected execution topology evidence

Final `CODEX_DONE` must record:

- which work packages were delegated;
- subagent model/effort when controllable;
- commits/results returned by each lane;
- main-agent integration/review actions;
- any planned lane kept sequential and why;
- final cross-lane test/build evidence.

`POST_TASK_POWER_ACTION` defaults to `NONE` unless a separate current handoff explicitly says otherwise.
