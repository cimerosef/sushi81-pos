# Post-export correction

**Status:** Approved — Phase 4 decision  
**Date:** 2026-08-27  
**Applies to:** POS intermediate export workflow

## Decision

An order that has already been successfully exported must not be exported again as a new positive sale if it is later modified or cancelled.

Instead, the POS creates a pending correction against the same stable Sushi81 POS order ID.

The next applicable export package carries an explicit action type:

- `UPDATE` — the previously exported order has been modified and the downstream system must replace/update the previously imported business state for that same order ID;
- `CANCEL` — the previously exported order has subsequently been cancelled and the downstream system must reverse/remove the previously imported sale for that same order ID.

The correction action does not allocate a new order ID and does not represent a second sale.

## Behaviour

- A modification made before the first successful export simply changes the normal future export content; no correction action is needed because no previous export exists.
- A modification made after successful export creates `UPDATE` pending state for that order.
- A cancellation made after successful export creates `CANCEL` pending state for that order.
- The correction is included in a later intermediate export package even though the original order ID has already appeared in a previous package.
- Ordinary duplicate protection must distinguish a legitimate pending correction from an accidental duplicate export.
- Once the correction package itself has been generated and validated successfully, the corresponding pending correction may be marked exported in the technical export ledger.
- The downstream import process is responsible for applying `UPDATE`/`CANCEL` idempotently by stable order ID.

## Rationale

This keeps Sushi81 POS and downstream management data reconcilable after legitimate post-export changes without generating duplicate sales or silently leaving stale management history.
