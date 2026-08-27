# Export date-range selection

**Status:** Approved — Phase 4 decision  
**Date:** 2026-08-27  
**Applies to:** POS export into `Gestion SUSHI 81.xlsm`

## Decision

V1 does not impose the legacy fixed J-2 export cutoff.

Instead, the operator chooses the export period explicitly by providing a start date and an end date.

The exporter then considers only orders that:

- fall within the selected date period under the final approved export-date basis;
- are ordinary POS-originated orders;
- are not Cancelled;
- are fully settled;
- are `Closed`;
- have not already been successfully exported under the applicable idempotency/correction rules.

The date range is inclusive at both ends.

The UI may provide convenience presets such as today, yesterday, this week or a previous period, but such presets are shortcuts only. The operator remains able to choose a custom period.

## Superseded legacy behavior

The old J-2 rule is not carried forward as a mandatory cutoff.

That rule existed mainly as an operational buffer in the workbook-centered system. SQLite retention and the new `Closed`-only eligibility rule remove the need for a fixed delay.

## Remaining point

A separate business decision must still define which business date controls inclusion in the selected period (for example order/fulfilment date versus settlement date). This decision record freezes only that the **operator controls the export period** rather than the application imposing a fixed J-2 delay.
