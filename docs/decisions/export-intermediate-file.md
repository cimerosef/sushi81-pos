# Export through an intermediate file

**Status:** Approved — Phase 4 decision  
**Date:** 2026-08-27  
**Applies to:** Sushi81 POS export into the `Gestion SUSHI 81` management workflow

## Decision

V1 does **not** write directly into `Gestion SUSHI 81.xlsm`.

Instead, the approved workflow is:

**Sushi81 POS -> controlled intermediate export file -> separate/manual import into Gestion SUSHI 81**

The POS is responsible for producing a complete, validated export package containing the eligible order-level and sales-detail data. The downstream import into `Gestion SUSHI 81.xlsm` remains a separate step outside the POS export transaction.

## Rationale

Introducing the new POS may also lead to later changes in the downstream management workflow. Keeping a stable intermediate boundary provides more flexibility than coupling the POS directly to the internal structure of `Gestion SUSHI 81.xlsm`.

This separation means that future changes to:

- the management workbook;
- the import procedure;
- the downstream analysis workflow;
- or even replacement of `Gestion SUSHI 81.xlsm`

can be handled without necessarily changing the POS business database or export-selection logic.

## Operational consequences

- A POS export is considered successful when the intermediate file has been generated and validated successfully.
- The POS does not need `Gestion SUSHI 81.xlsm` to be open, present or writable in order to generate an export.
- Importing the generated file into Gestion is a separate operator action/workflow.
- The POS export ledger tracks generation of the intermediate file, not completion of the later Gestion import.
- The generated file must carry enough stable identity information, especially Sushi81 POS order IDs, for the downstream import process to prevent duplicate insertion and support reconciliation.

## Technical boundary

The exact workbook layout, file naming and schema-version marker are implementation details, but V1 should use a controlled tabular format compatible with the existing Windows/Excel workflow. The export contract should be explicit and versionable so that a future downstream process can consume the same data without tightly coupling itself to POS internals.
