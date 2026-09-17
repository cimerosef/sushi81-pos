# Post-M09 Hiboutik daily payment dashboard — implementation authorization

**Status:** AUTHORIZED  
**Authorized:** 2026-09-16  
**Project owner:** explicit instruction to implement the registered Dashboard enhancement and dispatch Codex immediately  
**Issue:** #18 — `Post-M09 enhancement — Hiboutik daily CB/Espèce dashboard`

## Authorized scope

Implementation is authorized only for the approved decision in:

- `docs/decisions/post-m09-hiboutik-daily-payment-dashboard.md`;
- `docs/acceptance-criteria-amendment-post-m09-hiboutik-daily-payment-dashboard.md`;
- `docs/implementation/post-m09-hiboutik-daily-payment-dashboard.md`.

The enhancement adds exactly two passive top-Caisse values for non-Cancelled `HIBOUTIK_PASTE` payment adjustments attributed by effective business date:

- Hiboutik CB today;
- Hiboutik Espèce today.

## Explicit non-authorization

This authorization does not authorize:

- M10 Catalogue `.xlsx` implementation;
- M11 Gestion export implementation;
- M12/M13;
- any additional Hiboutik metric/workflow;
- merge of the implementation PR;
- schema/data-model expansion beyond the already-approved facts;
- automatic progression after the matching `CODEX_DONE`.

Codex execution remains controlled by Issue #4 and the unique active PR handoff. Merge requires separate explicit owner approval after controller and owner acceptance.