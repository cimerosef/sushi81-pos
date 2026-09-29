# Post-M14 Production Maintenance Batch 01 — owner PREPROD acceptance

**Status:** NOT RUN
**Environment:** computer A PREPROD only
**Candidate:** pending controller-approved immutable candidate

Do not use Production for candidate testing. Do not install PREPROD on computer B for this batch unless the controller explicitly expands scope because implementation touched a deferred M14 multi-device area.

## A — duplicate-add regression

Using the normal Caisse product list:

- add several different simple products one by one;
- include both Add-button and double-click usage;
- use a normal operator rhythm and one deliberately faster sequence;
- confirm every deliberate add produces one and only one cart addition;
- confirm adding a later product never creates another copy of an earlier product;
- if practical, include one option-enabled product and confirm/cancel its dialog once.

**Owner result:** NOT RUN.

## B — cart auto-reveal

Create enough cart lines to require the cart vertical scrollbar.

- add another product at the bottom;
- confirm the newly added line becomes visible automatically;
- adjust quantity on an existing line and confirm the cart does not gratuitously jump to the bottom;
- edit/remove as convenient and confirm normal cart controls remain usable.

**Owner result:** NOT RUN.

## C — natural code order

Inspect at least one category with a 1/2/.../10+ code family, such as the R family.

Expected shape includes `R1, R2, ... R9, R10, R11...`, not `R1, R10, ... R2`.

Also confirm letter-suffixed codes remain sensible, for example `R4, R4a, R4b, R4c, R5`.

**Owner result:** NOT RUN.

## D — printing improvements

Create or use a PREPROD Retrait order where the Retrait discount is actually applied.

Before payment:

- inspect the customer ticket;
- confirm a visible negative `Remise` line is present;
- confirm amount, TVA and final Total are coherent;
- confirm `*** PREPROD ***` remains visible.

Then settle the same PREPROD test order and explicitly reprint the customer ticket:

- confirm the same discount amount remains;
- confirm payment information appears according to existing rules;
- confirm `DUPLICATA` and `*** PREPROD ***` remain visible.

A no-discount order must not display a false discount line.

Then inspect kitchen output:

- print an ordinary PREPROD kitchen ticket and confirm the area after the final `TOTAL` contains approximately three blank writable lines before the paper ends;
- explicitly reprint the kitchen ticket and confirm the same handwriting space remains together with `RÉIMPRESSION`;
- confirm `*** PREPROD ***` remains visible;
- confirm the customer ticket has not gained this kitchen-only blank writing area.

**Owner result:** NOT RUN.

## E — completion

Batch owner acceptance may be marked PASS only after A–D pass against the exact controller-approved immutable candidate.

This checklist does not authorize:

- Production deployment;
- PR merge;
- M12 archive acceptance;
- M14 two-PC PREPROD acceptance.
