# Export through an intermediate file

**Status:** Approved — Phase 4 decision  
**Date:** 2026-08-27  
**Applies to:** Sushi81 POS export into the `Gestion SUSHI 81` management workflow

## Decision

V1 does **not** write directly into `Gestion SUSHI 81.xlsm`.

The approved workflow is:

**Sushi81 POS -> controlled intermediate export file -> separate/manual import into Gestion SUSHI 81**

The POS is responsible for producing a complete, validated export package containing the eligible order-level and sales-detail data. Downstream import into `Gestion SUSHI 81.xlsm` is a separate step outside the POS export transaction.

## Rationale

The management workbook/process may evolve independently from the POS.

Keeping a stable intermediate boundary prevents the POS business database and export-selection logic from being tightly coupled to the internal layout of the current management workbook.

Future changes to:

- the management workbook;
- its import procedure;
- downstream analysis;
- or eventual replacement of the workbook

can therefore be handled without redesigning the POS order model merely because the downstream consumer changed.

## Operational consequences

- A POS export is successful when the intermediate file has been generated and validated successfully.
- The POS does not need `Gestion SUSHI 81.xlsm` open, present or writable merely to generate an export.
- Importing the file into Gestion is a separate operator action/workflow.
- The POS export ledger tracks successful intermediate-file generation, not completion of the later Gestion import.
- Stable Sushi81 POS order identity is carried so the downstream importer can prevent duplicate insertion and apply later corrections.

## Frozen V1 contract

This decision originally established the integration boundary. The later Approved `docs/export.md` now freezes the concrete V1 intermediate workbook contract, including:

- `.xlsx` format;
- versioned schema;
- `Meta`, `Orders`, `OrderLines` and `TaxBreakdown` sheets;
- controlled `CREATE` / `UPDATE` / `CANCEL` actions;
- required columns/data representations;
- safe generation, idempotency and exact successful-batch regeneration.

Those concrete contract details are therefore **not open implementation choices in V1**. Pure presentation/filename mechanics explicitly left flexible by `export.md` may still be selected during implementation.

## Phase 5 incorporation

This boundary and its concrete V1 contract are incorporated into:

- `docs/product-requirements.md`;
- `docs/export.md`;
- `docs/acceptance-criteria.md`.

The decision no longer represents a pending contract-design task.
