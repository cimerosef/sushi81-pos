# M14 decision — PreProd environment isolation and immutable promotion

**Status:** Approved
**Date:** 2026-09-27
**Decision owner:** Sushi81 POS owner
**Applies to:** M14 and all later Sushi81 POS release cycles

## Context

Sushi81 POS V1 is now in production. Post-launch defects and small improvements must be developed and manually validated without risking the live production database, authority lineage, GitHub handoff transport, OneDrive Disaster Recovery metadata, device identities or credentials.

The existing application currently has one production deployment identity rooted at `%LOCALAPPDATA%\Sushi81 POS`. The approved V1 authority architecture uses local SQLite databases, target-directed normal handoff over a dedicated private GitHub Release Asset repository, OneDrive for System metadata and Disaster Recovery, and independent durable local authority state.

A safe permanent PreProd environment therefore cannot be only a copied executable. It must be a separate runtime system while remaining behaviorally identical to production.

## Decision

### 1. Two fixed deployment profiles

The application supports exactly two official deployment profiles:

- `prod`
- `preprod`

The profile is not a user-entered arbitrary path. It selects a fixed deployment identity.

Production durable data remains:

`%LOCALAPPDATA%\Sushi81 POS`

PreProd durable data uses:

`%LOCALAPPDATA%\Sushi81 POS PREPROD`

All Data, Recovery, Archive, Cache, Logs, Config and Temp paths are derived from the selected profile.

Unknown, missing when required, malformed or contradictory deployment-profile evidence fails closed before any business database or authority state is opened.

### 2. Installer coexistence

Production and PreProd use different permanent installer AppIds, installation directories, uninstall identities and shortcuts.

Production remains installed under:

`%LOCALAPPDATA%\Programs\Sushi81 POS`

PreProd uses:

`%LOCALAPPDATA%\Programs\Sushi81 POS PREPROD`

Uninstalling, repairing or reinstalling one environment must not remove or mutate the other environment's durable data.

### 3. Same application payload

For one release candidate, application binaries are published once.

The same immutable application payload is packaged into the PreProd installer and, after owner acceptance, promoted into the production installer without re-running `dotnet publish`.

Only packaging/deployment material may differ, including installer AppId, install path, shortcut/display name and the deployment-profile marker.

CI records a file-level SHA-256 payload manifest. Production promotion must verify that the packaged application payload is byte-for-byte identical to the owner-accepted PreProd payload.

### 4. Runtime isolation

PreProd is not a third production device.

PreProd has its own:

- lineage ID;
- generation;
- device IDs;
- local authority state;
- local recovery material;
- GitHub handoff transport configuration;
- Windows Credential Manager target;
- OneDrive shared root and System/DisasterRecovery tree.

Production and PreProd must never share a handoff repository, OneDrive root or Credential Manager target.

### 5. Remote transport isolation

The source/build repository remains `cimerosef/sushi81-pos`.

PreProd normal authority handoff uses a separate dedicated private runtime repository, recommended as:

`cimerosef/sushi81-pos-handoff-preprod`

The PreProd token is a separate fine-grained PAT scoped only to that private runtime repository with the minimum permissions required by the existing transport. It must not grant access to the production handoff repository or source repository.

The PreProd OneDrive root must be independent from the production root and must not be nested inside it or contain it.

### 6. Initial PROD-to-PREPROD seed

M14 provides a one-time initial bootstrap from the same computer's production `live.db` into a pristine PreProd profile.

The source is opened read-only through a SQLite-safe snapshot/backup mechanism. Raw `File.Copy` of a live production database is not accepted.

The seed may copy business database content, including catalogue, settings, orders, payments, Gestion export state and archive-proof metadata.

It must not copy production authority state, device identity, lineage/generation, pairing evidence, GitHub/OneDrive configuration, credentials, Recovery, Cache, Logs, Temp or bootstrap authority markers.

Canonical annual Archive databases are excluded from the M14 initial seed.

The source repository and CI artifacts must never contain a real production database.

### 7. Cross-environment collision guard

When PreProd technical configuration is saved on a computer that also has production installed, PreProd may inspect production non-secret local configuration read-only for collision detection.

PreProd must reject:

- identical OneDrive root;
- nested production/PreProd OneDrive roots;
- identical GitHub owner + repository;
- identical Windows Credential Manager target.

This is additional defence in depth; isolation does not rely only on operator discipline.

### 8. Permanent PreProd marking

PreProd must be visibly distinguishable from production without relying only on color.

At minimum:

- application/window identity includes `Sushi81 POS PREPROD`;
- major UI surfaces show `PREPROD — DONNÉES DE TEST` / `PREPROD — 测试数据`;
- kitchen/customer prints and reprints include `*** PREPROD ***`;
- default Gestion export filenames include a `PREPROD_` prefix.

Business/export schema semantics remain otherwise unchanged.

### 9. Candidate and promotion model

PreProd candidates are immutable GitHub pre-releases such as:

`v1.0.1-preprod-c01`

Candidate identity is separate from the application semantic product version. During one stabilization cycle the product version may remain `1.0.1` while candidates advance C01, C02, C03, etc.

After explicit owner acceptance of one candidate, production packaging downloads and verifies that exact accepted payload and packages it with the production deployment profile. It must not rebuild the application payload.

### 10. Scope boundary

M14 establishes the environment and release foundation only.

It does not implement post-launch business bug fixes, repeated automatic PROD-to-PREPROD refresh, automatic production deployment, production/PreProd data merge, multi-writer behavior, OneDrive live SQLite, or the deferred M12 populated annual archive verification.

## Approved owner amendment — 2026-09-29 single-PC PREPROD operation and acceptance waiver

The owner accepted the C01 PREPROD manual evidence on computer A and chose a **single-PC PREPROD operating model for now**. PREPROD is deployed only on A. Computer B remains Production-only; installing PREPROD on B merely to repeat the established M07 handoff behavior is not required for current M14 closure.

The original two-real-PC PREPROD pairing and A→B authority-handoff acceptance remains traceable but is **deferred under owner waiver**, not Passed. It becomes mandatory before PREPROD is expanded to a second computer, or before accepting a change that materially affects multi-device pairing, authority handoff, target acquisition, Disaster Recovery, OneDrive/GitHub cross-device coordination, or equivalent multi-device semantics.

Ordinary later business, UI, print and export bugfix candidates are validated first on PREPROD computer A. This amendment changes the present deployment and acceptance practice only. It does not change M07 runtime semantics, target-directed handoff, the approved Production A/B arrangement, or the application's ability to support more than one PREPROD device. The deferred two-PC verification must remain visible in M14 closure records until completed.
