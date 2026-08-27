# Export date-range selection

**Status:** Approved — Phase 4 decision  
**Date:** 2026-08-27  
**Applies to:** POS export into `Gestion SUSHI 81.xlsm`

## Decision

V1 does not impose the legacy fixed J-2 export cutoff.

The normal/default export action has **no mandatory date restriction**. It considers every order that:

- is an ordinary POS-originated order;
- is not `Cancelled`;
- is fully settled;
- is `Closed`;
- has not already been successfully exported under the applicable idempotency/correction rules.

In other words, the default operation is: **export all eligible settled orders that still need export**.

The operator may optionally narrow this set by selecting an inclusive start date and end date.

The date range is therefore an optional convenience filter, not a required export period and not a replacement for the eligibility rules.

For V1, when the optional date filter is used, it applies to the order's business/fulfilment date used for sales-period reporting. The UI must make the selected range visible before execution.

The UI may provide convenience presets such as today, yesterday, this week or another practical period, but custom date selection must remain available.

## Superseded legacy behavior

The old J-2 rule is not carried forward as a mandatory cutoff.

The earlier Phase 4 draft that required the operator to choose a date range before every export is also superseded by this decision. Date selection is optional.

## Rationale

The combination of two rules is sufficient for V1:

1. business eligibility determines what can be exported;
2. an optional user-selected date range can narrow that eligible set when the operator wants a specific period.

No additional automatic cutoff policy is required.
