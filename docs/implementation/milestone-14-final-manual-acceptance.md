# M14 — PreProd foundation — owner manual acceptance

**Status:** IN PROGRESS — WP7 OWNER ACCEPTANCE
**Milestone:** M14

This checklist is intentionally limited to the new environment-isolation and release-promotion foundation. It does not repeat the full V1 business acceptance.

## A — Candidate identity

- Download only the controller-approved immutable PreProd candidate from GitHub.
- Verify candidate ID, source SHA and installer/hash evidence match the controller record.
- Confirm installed application identifies itself as PreProd.

**Owner result:** pending

## B — Prod/PreProd coexistence

On computer A:

- production Sushi81 POS still launches from its production identity;
- PreProd launches separately;
- shortcuts/uninstall entries are distinct;
- production data root and PreProd data root are distinct;
- uninstall/repair testing required by the controller does not cross-delete durable data.

**Owner result:** pending

## C — Initial production seed

With production safely closed:

- run the explicit PreProd initial seed;
- confirm representative catalogue, settings, orders and Gestion data are present;
- confirm PreProd has its own technical identity/authority lineage rather than production identity;
- confirm production remains unchanged.

**Owner result:** pending

## D — Independent remote configuration

Configure:

- independent PreProd OneDrive root;
- independent private PreProd GitHub handoff repository;
- independent Credential Manager target/token.

Confirm collision guards reject any attempt to reuse production remote configuration.

**Owner result:** pending

## E — Two-PC PreProd pairing and handoff

On computer B:

- install the same accepted PreProd candidate;
- join the PreProd system;
- confirm initial non-authoritative/read-only state as applicable;
- execute a real target-directed A→B authority transfer;
- acquire on B;
- confirm A becomes/remains non-authoritative and B becomes authoritative.

Production A/B authority state must not change because of this PreProd transfer.

**Owner result:** pending

## F — Environment marking

Verify at least:

- persistent PreProd UI/banner identity;
- one kitchen/customer print or safe reprint contains `*** PREPROD ***`;
- one Gestion export default filename contains `PREPROD_`.

**Owner result:** pending

## G — Immutable promotion evidence

Controller reviews GitHub evidence showing:

- accepted candidate payload manifest;
- accepted payload hash;
- production packaging consumed that payload;
- production packaging did not run `dotnet publish`;
- packaged application files match accepted candidate hashes.

No production deployment is required merely to Pass this evidence item.

**Owner result:** accepted by controller — WP6 exact-head `1c73999c323dba593e3279f47c54a6174638b23d`; CI #963 / run `36546336521`; promotion artifact `11022832475`; controller comment `5887252324`. No production deployment was performed.

## Completion rule

M14 may be marked Passed only when A–G are accepted, exact-head CI is green, Release build/tests are clean, documentation/status is reconciled, and the M12 populated real annual archive verification remains separately deferred.
