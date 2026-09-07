# M07 WP7 isolated GitHub proof authorization

**Status:** Authorized proof environment  
**Authorized by:** project owner  
**Date:** 2026-09-07  
**Milestone:** M07 — Pairing, target-directed formal handoff and disaster recovery

## Purpose

This record authorizes the mandatory WP7 real GitHub single-winner proof against a disposable private repository that is isolated from both the Sushi81 POS source repository and the operational handoff repository.

## Authorized repository

Exactly this repository is authorized for the WP7 proof:

`cimerosef/sushi81-pos-handoff-m07-proof`

Verified properties at authorization time:

- private repository;
- project owner has administrative/write access;
- repository is intentionally disposable and dedicated to M07 WP7 proof work;
- no real Sushi81 order/customer/payment/business database data is authorized for use.

## Authorized test mutations

Within the repository above, an authorized WP7 proof handoff may:

- create a synthetic disposable Release or other isolated release container needed by the approved production GitHub transport;
- upload synthetic recovery-activation proof assets;
- intentionally run concurrent create/race/retry attempts using the deterministic next-generation activation name;
- inspect exact remote asset identity/state/size/digest and downloaded bytes;
- delete synthetic proof assets/releases created by the WP7 proof when cleanup is safe and unambiguous;
- repeat the synthetic race/retry drill as required to establish deterministic evidence.

All proof payloads must be synthetic and must contain no real Sushi81 business/customer/order/payment data or credentials.

## Explicitly not authorized

This authorization does **not** authorize:

- use of `cimerosef/sushi81-pos` as an operational/proof handoff repository;
- destructive testing against the operational `cimerosef/sushi81-pos-handoff` repository or its historical M02/M07 evidence;
- deletion of the `cimerosef/sushi81-pos-handoff-m07-proof` repository itself;
- exposing or committing PATs/tokens;
- broadening credential scope beyond the dedicated proof repository;
- production Disaster Recovery implementation (WP8) before the real WP7 proof passes;
- M08 or later milestone implementation;
- merge of PR #13.

## Credential boundary

The proof may use only a credential explicitly scoped to the dedicated proof repository with the minimum permissions required for private repository Release/Release Asset read/write/delete operations used by the test.

The credential must be supplied through a protected/ephemeral mechanism outside Git, logs, PR comments and ChatGPT conversation content. Environment-variable injection is acceptable for this disposable WP7 test process if the M07 contract permits it; production credential UX remains subject to the production Windows protected-storage requirement.

## Pass/fail gate

The real proof must demonstrate, using the production GitHub transport/activation primitive against the repository above, that concurrent contenders for the same `(lineage, next generation)` deterministic activation name result in **at most one accepted winner**, with loser/read-after-unknown-outcome behavior failing closed or observing the exact same immutable winner.

If the real GitHub behavior cannot establish that invariant, M07 remains `Blocked — architecture decision required` and WP8 must not begin.

If the proof passes, the evidence must be recorded durably in the M07 worklog/PR before WP8 is authorized to proceed under the existing M07 implementation authorization.
