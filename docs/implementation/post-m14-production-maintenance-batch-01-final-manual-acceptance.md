# Post-M14 Production Maintenance Batch 01 — owner PREPROD acceptance

**Status:** IN PROGRESS — 0, A, B and C PASS; D pending
**Environment:** computer A PREPROD only
**Candidate:** C02 / `v1.0.1-preprod-c02` — immutable Release `399541988` published; controller verification and owner acceptance pending

Do not use Production for candidate testing. Do not install PREPROD on computer B for this batch unless the controller explicitly expands scope because implementation touched a deferred M14 multi-device area.

## 0 — install and environment identity

After controller verification of the exact immutable C02 Release and installer hash, install C02 over the existing PREPROD installation on computer A. Confirm the prior PREPROD durable data/settings remain available, the PREPROD banner is visible, and the separate Production installation/data remain unchanged. Do not run this checklist against C01 or Production.

**Owner result:** PASS — C02 installed on computer A PREPROD. PREPROD identity/banner, prior PREPROD data/settings and normal launch were confirmed; Production remained separate/unmodified.

## A — duplicate-add regression

Using the normal Caisse product list:

- add several different simple products one by one;
- include both Add-button and double-click usage;
- use a normal operator rhythm and one deliberately faster sequence;
- repeat a deliberate add of the same product and confirm each distinct gesture remains its own line;
- confirm every deliberate add produces one and only one cart addition;
- confirm adding a later product never creates another copy of an earlier product;
- if practical, include one option-enabled product and confirm/cancel its dialog once.

**Owner result:** PASS — owner exercised mixed Add-button/double-click entry, several different products, a faster sequence and deliberate repeated adds. Every deliberate gesture added exactly once; no later add created an unintended duplicate of an earlier product.

## B — cart auto-reveal

Create enough cart lines to require the cart vertical scrollbar.

- add two more products at the bottom;
- confirm each newly added line becomes visible automatically;
- adjust quantity on an existing line and confirm the cart does not gratuitously jump to the bottom;
- edit/remove as convenient and confirm normal cart controls remain usable.

**Owner result:** PASS — with the cart overflowed, each newly added bottom line auto-revealed into view. Changing quantity on an existing line did not cause an unsolicited jump to the bottom; normal cart controls remained stable.

## C — natural code order

Inspect at least one category with a 1/2/.../10+ code family, such as the R family.

Expected shape includes `R1, R2, ... R9, R10, R11...`, not `R1, R10, ... R2`.

Also confirm letter-suffixed codes remain sensible, for example `R4, R4a, R4b, R4c, R5`.

**Owner result:** PASS — owner inspected representative natural-code families in the current product list. Numeric families sort naturally (`...R9, R10, R11...`) rather than lexically, and suffixed codes such as `R4, R4a, R4b, R4c, R5` appear in the expected sequence.

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

**Owner result:** IN PROGRESS — unpaid discounted Retrait customer ticket PASS. Visible negative `Remise`, discount amount, TVA/final Total coherence and `*** PREPROD ***` marking were confirmed. Paid explicit customer reprint, no-discount negative check and kitchen handwriting-space checks remain pending.

## E — completion

Batch owner acceptance may be marked PASS only after 0 and A–D pass against the exact controller-approved immutable C02 candidate. Checklist 0, A, B and C are now PASS. The owner-observed intermittent Production extra-add symptom has passed this C02 empirical regression round; D printing acceptance remains pending before Batch 01 final acceptance.

**C02 publication evidence:** [immutable PREPROD C02 Release](https://github.com/cimerosef/sushi81-pos/releases/tag/v1.0.1-preprod-c02), ID `399541988`, source `0aa3a0282a432da38bff9b35b38ffa03cf3bbada`, installer `Sushi81POS-PREPROD-Setup-1.0.1-0aa3a02.exe` SHA-256 `3ef1346e857914e9898dacafe522c9308d5d740a9db00413c19b9415daa44e4a`. Actions run `36629889943` built artifact `11062068062`; its GITHUB_TOKEN publish job failed before Release creation, then the tracked publisher completed publication with owner authentication without rebuilding. The Release is draft=false, prerelease=true, immutable=true and non-latest, with exactly five verified assets. Controller independent verification is complete. Owner testing is in progress: 0, A, B and C passed; D remains pending.

This checklist does not authorize:

- Production deployment;
- PR merge;
- M12 archive acceptance;
- M14 two-PC PREPROD acceptance.
