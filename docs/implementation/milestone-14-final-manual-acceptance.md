# M14 — PreProd foundation — owner manual acceptance

**Status:** OWNER ACCEPTED UNDER WAIVER — M14 CLOSURE-READY; controller closure pending
**Milestone:** M14

This checklist is intentionally limited to the new environment-isolation and release-promotion foundation. It does not repeat the full V1 business acceptance.

## A — Candidate identity

- Download only the controller-approved immutable PreProd candidate from GitHub.
- Verify candidate ID, source SHA and installer/hash evidence match the controller record.
- Confirm installed application identifies itself as PreProd.

**Owner result:** PASS. The owner installed immutable C01 from `v1.0.1-preprod-c01` and verified the installer SHA-256 against `a895d9b464484c1de669aaf31484b9020a179583f9e35c9495d5d4d75bf691f6`. The separately launched application displayed persistent `Sushi81 POS PREPROD` / `PREPROD — DONNÉES DE TEST` identity.

## B — Prod/PreProd coexistence

On computer A:

- production Sushi81 POS still launches from its production identity;
- PreProd launches separately;
- shortcuts/uninstall entries are distinct;
- production data root and PreProd data root are distinct;
- uninstall/repair testing required by the controller does not cross-delete durable data.

**Owner result:** PASS for coexistence on computer A. Production and PREPROD launched as distinct installed applications; Production retained its existing data and normal launch. Their install and durable roots are separate. The WP2 hosted synthetic dual-installer lifecycle, rather than a real-A uninstall/repair, supplies the repair/uninstall isolation evidence. No real Production durable data was removed or changed.

## C — Initial production seed

With production safely closed:

- run the explicit PreProd initial seed;
- confirm representative catalogue, settings, orders and Gestion data are present;
- confirm PreProd has its own technical identity/authority lineage rather than production identity;
- confirm production remains unchanged.

**Owner result:** PASS. The explicit read-only SQLite PROD→PREPROD seed completed on A. The owner observed representative catalogue, order, business-settings and Gestion data in PREPROD, with Production unchanged. Direct inspection showed different DeviceId and LineageId values for the two environments; PREPROD established its own authoritative lineage rather than importing Production authority identity. The raw production database and authority identifiers are not reproduced in this repository record.

## D — Independent remote configuration

Configure:

- independent PreProd OneDrive root;
- independent private PreProd GitHub handoff repository;
- independent Credential Manager target/token.

Confirm collision guards reject any attempt to reuse production remote configuration.

**Owner result:** PASS. PREPROD saved a separate `Sushi81-POS-PREPROD` OneDrive root, a dedicated private `cimerosef/sushi81-pos-handoff-preprod` runtime repository and a distinct `Sushi81POS-PREPROD-GitHub-Handoff` Credential Manager target. The owner observed the expected pre-save French rejection when PREPROD was pointed at the source/build repository `cimerosef/sushi81-pos`. Correct configuration saved, restart completed, M07 services composed and the runtime GitHub connection-test action became available. No token value is recorded.

## E — Two-PC PreProd pairing and handoff

On computer B:

- install the same accepted PreProd candidate;
- join the PreProd system;
- confirm initial non-authoritative/read-only state as applicable;
- execute a real target-directed A→B authority transfer;
- acquire on B;
- confirm A becomes/remains non-authoritative and B becomes authoritative.

Production A/B authority state must not change because of this PreProd transfer.

**Owner result:** DEFERRED UNDER OWNER WAIVER — single-PC PREPROD operating model. On 2026-09-29 the owner chose to operate PREPROD only on computer A; computer B remains Production-only and was not given the PREPROD installer. No PREPROD A→B pairing, target-directed handoff or acquisition was performed or marked Passed. This remains a named verification obligation before a second PREPROD device is introduced or before accepting a change that materially alters multi-device pairing, authority handoff, target acquisition, Disaster Recovery, or OneDrive/GitHub cross-device coordination. Existing implementation and automated coverage remain unchanged.

## F — Environment marking

Verify at least:

- persistent PreProd UI/banner identity;
- one kitchen/customer print or safe reprint contains `*** PREPROD ***`;
- one Gestion export default filename contains `PREPROD_`.

**Owner result:** PASS. The persistent PREPROD banner was visible. A Gestion Save dialog suggested `PREPROD_Sushi81_POS_Export_20260929_141907.xlsx`. Safe physical kitchen and customer reprints contained `*** PREPROD ***`; the kitchen copy also showed `RÉIMPRESSION` and the customer copy `DUPLICATA`.

## G — Immutable promotion evidence

Controller reviews GitHub evidence showing:

- accepted candidate payload manifest;
- accepted payload hash;
- production packaging consumed that payload;
- production packaging did not run `dotnet publish`;
- packaged application files match accepted candidate hashes.

No production deployment is required merely to Pass this evidence item.

**Owner result:** PASS / accepted by controller — WP6 exact-head `1c73999c323dba593e3279f47c54a6174638b23d`; CI #963 / run `36546336521`; promotion artifact `11022832475`; controller comment `5887252324`. The production installer was built as payload-equivalence evidence only. No production deployment was performed.

## Completion rule

The original two-PC completion rule remains historical: full unwaived `Passed` requires A–G, including a real PREPROD A→B handoff. The owner's Approved 2026-09-29 single-PC waiver supersedes that requirement for **current M14 closure only**. A, B, C, D, F and G are accepted; E is explicitly deferred and is not Passed. With exact-head CI green, Release build/tests clean and documentation reconciled, M14 is **Accepted under waiver / closure-ready**, pending controller closure and any separate merge approval. M12 populated real annual-archive verification remains separately deferred and unchanged. The E obligation and its future trigger remain visible until real two-PC PREPROD verification is completed.
