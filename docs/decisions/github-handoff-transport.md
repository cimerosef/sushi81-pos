# GitHub private-repository handoff transport

**Status:** Approved — V1 specification amendment  
**Decision date:** 2026-08-28  
**Trigger:** Real M02 OneDrive transport evidence proved that a locally-created regular file can already be visible remotely while Windows Cloud Files still reports `CF_PLACEHOLDER_STATE_NO_STATES`, so the application cannot obtain a documented local-only per-artifact remote-upload acknowledgement from the OneDrive desktop sync boundary.  
**Supersedes for normal authority handoff:** OneDrive desktop-folder publication/synchronization as the transport/acknowledgement mechanism.  
**Preserves:** local SQLite working databases, target-directed single-writer authority semantics, local recovery, durable relinquishment-before-grant ordering, exact-target acquisition, generation/version protection, fail-closed behavior and Disaster Recovery separation.

## 1. Decision

V1 normal authority handoff will use a **dedicated private GitHub repository through the GitHub REST API** as the remote transport instead of relying on OneDrive desktop synchronization state.

The normal handoff repository is conceptually:

`cimerosef/sushi81-pos-handoff`

The repository name is deployment configuration rather than a protocol identity. Production code must not assume that the source-code repository itself is the handoff repository.

The handoff repository is a transport/coordination store only. The POS live SQLite database remains local and is never opened or edited in GitHub, OneDrive or another synchronized folder.

## 2. Release Assets, not Git history

Normal handoff binary snapshots must be stored as **GitHub Release Assets**, not as ordinary Git commits/blobs in repository history.

V1 uses one long-lived handoff release/container for normal handoff assets. Exact release/tag naming is a technical setup detail, but it must be stable and discoverable by API.

The application must use documented GitHub REST release/release-asset endpoints to:

- locate the configured handoff release;
- upload immutable assets;
- list/read immutable assets;
- download assets;
- delete old assets during retention cleanup.

A successful snapshot publication must be acknowledged directly by GitHub's server-side API response. Local filesystem synchronization state is not part of the GitHub transport protocol.

For a snapshot upload to count as remotely accepted, the implementation must require at least:

- successful GitHub API response for the upload;
- returned asset state indicating uploaded/complete rather than starter/incomplete;
- exact expected asset name;
- exact byte length;
- a server-reported SHA-256 digest when exposed by the documented API, matching the locally computed SHA-256.

Unknown, missing, contradictory or ambiguous remote evidence fails closed.

## 3. Snapshot filename

The user-facing snapshot filename is fixed as:

`YYYYMMDDHHMMSS.snapshot.db`

Example:

`20260827231152.snapshot.db`

The 14-digit prefix is derived from the source device's local wall-clock time at snapshot creation and is intended for human readability and natural filename sorting.

The timestamp filename is **not** the authority-ordering primitive. The existing immutable internal `handoff_version`, lineage, generation and transfer identity remain authoritative.

The implementation must never overwrite a same-named remote asset. If a collision exists, it must choose the next unused second-level timestamp candidate or fail safely; silent replacement is forbidden.

The matching target-bound handoff/grant metadata uses the same 14-digit basename, for example:

`20260827231152.grant.json`

Exact JSON field names are technical details, but the grant must bind at least protocol version, transfer ID, lineage ID, generation, handoff version, source device ID, target device ID, snapshot asset ID/name, snapshot byte length, snapshot SHA-256 digest and relevant timestamps.

## 4. Approved source ordering

A normal target-directed transfer keeps the existing safety ordering, with GitHub server acknowledgement replacing OneDrive sync-state observation:

1. operator explicitly selects **Transfer authority and close** and one paired target;
2. source blocks new business edits and finishes accepted writes;
3. source creates a SQLite-safe complete snapshot;
4. source validates SQLite integrity;
5. source assigns/validates the next immutable handoff version and target binding;
6. source computes snapshot SHA-256 and byte length;
7. source uploads the immutable `YYYYMMDDHHMMSS.snapshot.db` Release Asset;
8. source requires documented GitHub server-side upload acknowledgement and validates returned remote identity/name/size/digest;
9. source durably records relinquishment for this exact transfer and from then on remains business-read-only across restart;
10. only after durable relinquishment may source create and upload the matching immutable target-bound `YYYYMMDDHHMMSS.grant.json` asset;
11. source requires documented GitHub acknowledgement for the grant asset;
12. source durably records `Released`/completed publication and may complete the close flow;
13. retention cleanup may run only after the new handoff unit is complete.

A remotely uploaded snapshot without the matching valid target-bound grant never authorizes the target.

The target-releasing grant must never be uploaded before durable source relinquishment.

## 5. Failure boundaries

### Before durable relinquishment

If snapshot creation, integrity validation, GitHub authentication, network transport, upload response validation, remote digest/size validation or any other required pre-relinquishment step fails:

- no grant may exist;
- the source must not cross durable relinquishment;
- the prepared transfer may be safely cancelled according to the already-approved retain-authority semantics;
- retry must never overwrite a contradictory remote asset silently.

### After durable relinquishment

After the source has durably relinquished:

- the source stays business-read-only across restart;
- it may retry only the same immutable transfer;
- it may retry publication/verification of the same grant;
- it may not retarget, roll back to ordinary authority or create a replacement handoff version;
- if GitHub is temporarily unavailable, the safe state is zero writable devices until the same transfer completes;
- if normal completion becomes impossible, the exceptional remedy remains explicit Disaster Recovery.

## 6. Target acquisition

The designated target may become writable only after it has obtained the exact target-bound grant and referenced snapshot from GitHub and has validated all required facts.

At minimum it must verify:

- local immutable `device_id` equals `target_device_id`;
- source and target are valid/distinct paired devices;
- transfer ID, lineage, generation and handoff version are acceptable and non-stale;
- grant references one exact GitHub snapshot asset ID/name;
- downloaded snapshot byte length and SHA-256 match the immutable grant and GitHub asset metadata;
- SQLite integrity passes;
- required protocol metadata is supported;
- local durable acquisition is committed before business writes are enabled.

Wrong-target, missing asset, missing grant, partial upload, hash mismatch, size mismatch, malformed JSON, stale/replayed version, old generation, authentication failure, GitHub error or unknown state must remain read-only/fail-closed.

## 7. Retention — latest three complete handoffs

V1 retains the latest **three complete normal handoff snapshot units** in the GitHub handoff release.

One complete unit consists of the snapshot plus its matching immutable grant metadata.

Retention rules:

- cleanup occurs only after a newer handoff unit has been fully published and confirmed;
- while a new transfer is in progress, a temporary fourth snapshot/unit may exist so that an older valid recovery point is not deleted before its replacement is known good;
- after successful completion, delete complete units older than the newest three;
- delete snapshot and matching grant as one logical retention unit;
- never delete the current in-progress transfer or any of the newest three complete units;
- cleanup failure must not invalidate an otherwise completed authority transfer; record/retry cleanup later;
- abandoned pre-relinquishment snapshot-only assets may be cleaned only when durable local state proves they cannot be the committed active transfer;
- after relinquishment, assets belonging to the fixed transfer must not be garbage-collected merely because publication is incomplete.

The filename timestamp is not used alone to decide authority or safety. Retention must use validated handoff metadata/internal version and remote asset identity.

## 8. Authentication and credential boundary

The handoff repository must be private.

V1 uses least-privilege authenticated GitHub API access scoped only to the dedicated handoff repository. A fine-grained personal access token is an acceptable V1 credential mechanism when restricted to the required repository permissions.

Production credentials must:

- never be committed to Git;
- never be stored in the SQLite business database;
- never be uploaded as handoff metadata/assets;
- never appear in logs, exception messages, screenshots/evidence or test fixtures;
- be stored using an appropriate Windows protected credential mechanism for production use.

M02 feasibility tooling may accept a token through a process environment variable or other ephemeral test-only injection, provided it is never echoed/persisted.

## 9. Dedicated repository boundary

The source-code repository `cimerosef/sushi81-pos` must not be used as the production handoff data store.

A separate private repository is required for operational handoff assets so that:

- source history remains clean;
- operational snapshots are isolated from code and development permissions;
- transport credentials can be restricted to only the operational repository;
- retention/deletion of handoff assets cannot alter product source history.

The handoff repository must contain no real customer/order/payment information except the encrypted/opaque business snapshot itself as required for operation; no snapshot may ever be committed to the source repository or used as a test fixture.

## 10. Scope of this amendment

This amendment changes the **normal target-directed handoff transport and its remote acknowledgement mechanism**.

It does not by itself redesign:

- local working SQLite storage;
- local recovery snapshot retention;
- order/business semantics;
- printing/export/catalogue behavior;
- annual archive ownership;
- Disaster Recovery business semantics.

Where older approved documents say that normal handoff authority depends on OneDrive desktop synchronization confirmation, this decision supersedes that statement. Those baseline documents must be amended before production handoff implementation proceeds.

## 11. M02 gate consequence

The earlier OneDrive real-device evidence remains valid historical feasibility evidence and must remain in the M02 report.

M02 now reopens as a **GitHub transport feasibility revalidation**. It is not Passed until automated failure tests plus a real two-device private-repository upload/download/round-trip demonstrate the approved ordering and at-most-one-writer invariant.

M03 remains prohibited until M02 is explicitly accepted and closed.