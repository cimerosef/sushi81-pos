# Architecture

**Status:** Approved — Phase 3 baseline, amended 2026-08-28  
**Last updated:** 2026-08-28  
**Product:** Sushi81 POS  
**Purpose:** Define the implementation architecture that preserves the approved product, lifecycle and data-model semantics while prioritizing reliability, simplicity and maintainability.

**Approved amendments:** `docs/decisions/target-directed-authority-handoff.md` replaces competitive/generic handoff acquisition with source-directed transfer to one target device. `docs/decisions/github-handoff-transport.md` replaces OneDrive desktop synchronization acknowledgement for normal handoff with a dedicated private GitHub Release Asset transport; OneDrive remains historical/DR/archive storage where separately approved.

## 1. Architecture priorities

The technical architecture is optimized in this order:

1. reliability;
2. simplicity;
3. maintainability;
4. operational clarity;
5. visual novelty only where it does not weaken the priorities above.

Sushi81 POS is a focused local Windows business application. It is not intended to become a distributed cloud platform, web application or general ERP.

## 2. Core platform — approved Phase 3 baseline

V1 uses:

- **.NET 10 LTS** as the application runtime/platform;
- **WPF** for the Windows desktop UI;
- **SQLite** for durable local business-data storage;
- **Microsoft.Data.Sqlite** as the primary .NET SQLite access layer unless implementation proves a concrete blocker;
- ordinary Windows printing infrastructure for local printer integration, with business-facing printing behavior defined separately in `printing.md`.

WPF is chosen deliberately because Sushi81 POS prioritizes mature Windows desktop behavior, data-heavy forms/tables, printer integration and maintainability over fashionable UI technology.

## 3. Application shape

V1 is implemented as a **single local desktop application with a modular internal structure**, not as multiple services.

Recommended internal separation:

- **Presentation/UI layer** — WPF views, navigation and user interaction;
- **Application layer** — order-entry, catalogue, payment, archive, import/export and handoff use cases;
- **Domain/business-rules layer** — approved pricing, validation, lifecycle and calculation rules;
- **Persistence layer** — SQLite repositories/queries, migrations and transactional persistence;
- **Infrastructure services** — printing, Excel import/export, local snapshot generation, GitHub Release Asset handoff transport and technical logging. Historical OneDrive diagnostics remain isolated from the normal authority gate.

These are code-organization boundaries, not separate processes or network services.

## 4. Local-first execution

Each paired Windows computer runs the full application locally and uses its own local working SQLite database.

The application must remain usable on the current authoritative/writable computer even if Internet/GitHub connectivity is temporarily unavailable, subject to the handoff ownership rules defined in `storage-strategy.md`.

The active SQLite working database must **not** be opened directly from a OneDrive-synchronized folder and must not be concurrently written by more than one paired device.

The local working database and local recovery snapshots are application-managed technical data. Normal users are not asked to choose, move, rename or directly manipulate those files.

Cross-device normal handoff is configured separately through a dedicated private GitHub repository and authenticated Release Asset API. Selecting/configuring that repository does not move or expose the local working database. OneDrive folder configuration remains separate for approved recovery/archive and historical diagnostics.

The architecture must not hard-code assumptions such as exactly two devices, `SHOP-PC` plus `HOME-PC`, or two fixed synchronization slots.

## 5. Monetary representation — approved Phase 3 baseline

Persisted business money uses **integer euro cents** rather than binary floating point.

Examples:

- €0.01 -> `1`;
- €18.50 -> `1850`;
- €125.37 -> `12537`.

Business calculations in application code use decimal arithmetic and the approved round-half-up semantics from `business-rules.md`; durable monetary values are converted to integer cents at persistence boundaries.

This applies to order totals, product prices, option adjustments, delivery fees, CB/Espèce amounts, VAT amounts and other persisted euro-denominated business amounts unless a later approved specification states otherwise.

## 6. Database and schema evolution

SQLite is the authoritative local operational datastore.

Requirements:

- schema changes are versioned through explicit migrations;
- migrations are testable and deterministic;
- migrations execute transactionally where SQLite permits and must fail safely without silently resetting production data;
- business data must never depend on current catalogue joins for historical interpretation;
- foreign-key behavior and delete rules must preserve the approved historical snapshot model;
- the physical schema must implement the approved `data-model.md` semantics rather than inventing new business entities during coding.

### 6.1 SQLite connection and durability settings — frozen technical choice

V1 uses a conservative SQLite configuration appropriate for one local application process with a single business writer:

- `PRAGMA journal_mode = WAL` for the live working database;
- `PRAGMA synchronous = FULL` for durability-first commit behavior;
- `PRAGMA foreign_keys = ON` on every opened connection;
- a finite busy timeout (target **5 seconds**) so short internal lock contention is retried rather than failing immediately;
- normal transactional writes for every multi-row business save;
- no reliance on raw filesystem copying of an open database for handoff/recovery.

Application code must keep transactions short and must not hold a write transaction while waiting for user input, printing or OneDrive synchronization.

Local recovery, handoff and disaster-recovery snapshots use SQLite's supported consistent backup/snapshot mechanism while the application controls writes, followed by the integrity/checksum validation required by `storage-strategy.md`.

No exotic SQLite tuning, custom page-size scheme, manual vacuum schedule or speculative performance pragma is part of the V1 baseline unless profiling later demonstrates a concrete need.

## 7. Multi-device principle — amended target-directed single-writer model

Sushi81 POS does **not** implement simultaneous multi-writer database access in V1.

The architecture supports **an arbitrary number of paired Windows devices by default**. The normal initial deployment may use two computers, but adding a third or later computer must not require redesigning the database, handoff protocol or application architecture.

All paired devices participate in the same authoritative database lineage through controlled handoff of complete, validated SQLite snapshots via a dedicated private GitHub Release Asset container. OneDrive is not the normal handoff acknowledgement mechanism.

At any moment:

- at most one paired device may hold authoritative write access;
- every other paired device is non-authoritative/read-only unless it is the exact designated target completing a valid handoff;
- the current authoritative device may close while **retaining** authority, or may explicitly transfer authority to one selected paired target device;
- a generic released handoff is never competed for through N-device claims/election;
- no automatic row-level database merge is performed.

GitHub Release Assets are therefore the **normal handoff transport/acknowledgement medium**, not the live database engine, not a real-time database synchronization service and not a distributed lock provider. OneDrive remains a recovery/archive medium and historical diagnostic boundary.

### 7.1 Source-directed authority token

The current authoritative device is the arbiter of every normal transfer.

A formal handoff carries immutable source and target `device_id` values. The source must durably relinquish business-write authority before the target-releasing ready/grant marker can be created. After that point, the former source is read-only/pending-transfer across restart and may only retry completion of the same already-fixed transfer.

Only the exact target device may acquire that handoff. Other paired devices remain read-only and do not attempt to win ownership through file claims, waiting periods or conflict-file behavior.

This design deliberately removes the cross-client atomic-claim requirement that M02 proved unavailable in the approved OneDrive/local-filesystem transport model.

### 7.2 Close semantics

Normal application close has two distinct intents governed in detail by `storage-strategy.md`:

- **Close and retain authority** — no formal release; this device remains authoritative for the next valid launch.
- **Transfer authority and close** — explicitly select/preselect one eligible target and run the target-directed formal handoff.

The application must not silently infer authority transfer merely because the current process exits.

### 7.3 Local reconstruction

Each participating device keeps its own local working database outside OneDrive and reconstructs/updates that local database only from:

- a formally completed handoff specifically targeted to that device; or
- an explicit approved Disaster Recovery action.

Each installation has its own opaque `device_id`; all devices in the same business data family share a stable `lineage_id`. Exact target selection, durable relinquishment, identity, generation, failure and acquisition rules are defined in `storage-strategy.md` and `docs/decisions/target-directed-authority-handoff.md`.

## 8. Maintainability principle

Prefer first-party .NET/Windows capabilities and small, mature dependencies.

Do not introduce:

- a web server merely to host the local UI;
- Docker/container infrastructure;
- microservices;
- a remote database server;
- distributed-database merge logic;
- Microsoft Graph/OAuth or a hosted coordinator merely to arbitrate normal authority transfer;
- a complex dependency-injection/framework stack unless it materially simplifies testing or maintenance;
- speculative extensibility that is not needed by approved requirements.

The codebase should remain understandable enough that Codex or a future maintainer can trace an order from UI action through business rule to SQLite persistence without crossing unnecessary infrastructure layers.

## 9. Deployment and local-data boundary — approved Phase 3 baseline

Application binaries are installer-managed and separate from business data.

Application-managed local business/technical data uses a fixed per-user application-data root under `%LOCALAPPDATA%\Sushi81 POS\` with logical subareas for:

- `Data\` — active `live.db`;
- `Recovery\` — local rolling recovery snapshots;
- `Cache\` — disposable local caches, including archive read-only hydration where needed;
- `Logs\` — technical logs;
- `Config\` — local machine/application configuration including device identity and durable authority/transfer state;
- `Temp\` — staging files used for safe snapshot/archive operations.

The working `live.db` and local Recovery path are not ordinary user-configurable locations.

The operator configures the dedicated private GitHub handoff repository/release for normal authority transfer. A shared OneDrive root remains separately configured for approved recovery/archive functions and historical diagnostics.

Application update/reinstall logic must not treat local business data, device identity or durable authority/transfer state as disposable program files.

### 9.1 Packaging and updates — frozen technical choice

V1 is published as a **self-contained Windows x64 WPF application** so the target PCs do not depend on a separately managed system-wide .NET runtime installation.

Packaging uses a simple conventional **Inno Setup 6** installer in per-user mode unless a concrete Windows compatibility blocker is discovered during packaging tests.

The installer installs application binaries under a per-user program location separate from `%LOCALAPPDATA%\Sushi81 POS\` business data and supports in-place upgrade over an existing installation.

V1 deliberately does **not** include a background/self-updating subsystem. Updates are distributed as a versioned installer and are launched explicitly. This avoids adding an update service, signing/distribution backend or another failure-prone network dependency to the POS.

Upgrade rules:

- the application must be closed before binaries are replaced;
- the installer must preserve local business data, local device identity, configuration and durable authority/transfer state;
- database migration runs under application control on first start of the new version, not by deleting/recreating `live.db`;
- a failed migration must leave the previous durable data recoverable rather than silently resetting it;
- rollback of application binaries must never silently downgrade an already-upgraded schema without an explicit compatible path.

Code signing may be added later for Windows trust/SmartScreen convenience, but V1 architecture does not depend on a paid signing service.

## 10. Excel `.xlsx` integration — frozen technical choice

V1 uses **ClosedXML** as the normal application-level dependency for reading and writing `.xlsx` workbooks.

Rationale:

- it keeps catalogue/export workbook code substantially simpler than using raw Open XML package structures directly;
- it does not require Microsoft Excel to be installed or automated through COM;
- it supports the worksheet, cell, formatting, hidden/protected technical-column and workbook-generation operations needed by the approved catalogue workflow;
- the dependency is isolated behind an application-owned workbook/import-export service so a future library replacement does not change business rules.

Implementation rules:

- pin an explicit tested package version rather than floating to latest automatically;
- review release/migration notes before dependency upgrades;
- do not use parallel workbook mutation because the library/workbook handling is not treated as thread-safe;
- validate all imported workbook business data independently of workbook-library parsing success;
- never let an Excel library exception partially commit catalogue or export business state.

Microsoft Excel COM automation is explicitly avoided for POS core operation.

## 11. Printing integration — frozen architecture boundary

V1 prints through the ordinary **Windows Print Spooler / configured Windows print queues** rather than coupling core business logic directly to printer-specific raw command languages.

The printing subsystem is separated into:

1. an application-owned **print-data/document model** generated from the persisted order snapshot;
2. ticket/receipt layout composition;
3. a Windows printer transport adapter using WPF/Windows printing APIs (`System.Printing` / `PrintQueue` and fixed-document/XPS-capable output where practical).

This boundary ensures that:

- order persistence never depends on printer success;
- kitchen and customer documents can be generated/tested without a physical printer;
- reprinting uses the same persisted business snapshot and print composition logic;
- printer names/settings remain configuration rather than business data;
- a later printer-specific adapter may be added only if a real hardware limitation requires it, without changing business rules or ticket content.

The exact ticket contents, automatic-print sequence, selective reprint behavior, stale read-only-device printing policy, printer configuration UX and failure/retry behavior belong to `printing.md`.

## 12. Phase 3 completion

All Phase 3 core technical architecture choices are frozen for V1 implementation, including the 2026-08-28 authority-handoff amendment:

- .NET 10 LTS + WPF;
- SQLite + Microsoft.Data.Sqlite;
- integer-cent persistence and decimal business calculation;
- local application-managed live database;
- N-device single-writer architecture with **target-directed source-arbitrated GitHub Release Asset handoff**;
- no generic competitive OneDrive acquisition/election;
- WAL + FULL synchronous durability profile with foreign-key enforcement;
- self-contained x64 deployment with a simple per-user Inno Setup installer and explicit/manual V1 updates;
- ClosedXML for `.xlsx` handling behind an application-owned service boundary;
- Windows Print Spooler / print-queue integration behind an application-owned print-document boundary.

No remaining Phase 3 item requires an unapproved business decision. M02 must re-verify the amended handoff protocol before M03 is authorized.

## 13. Approval

This document remains the **Approved — Phase 3 baseline**, amended on 2026-08-28 by `docs/decisions/target-directed-authority-handoff.md` and `docs/decisions/github-handoff-transport.md`.

Implementation must preserve the approved local-first WPF/SQLite architecture and may not silently replace it with a server, web application, live OneDrive database, simultaneous multi-writer design, fixed two-computer protocol or generic file-claim election.

Pure implementation details that do not change approved business behavior may continue to be selected during implementation according to the project priority order: reliability > simplicity > maintainability > operational clarity > novelty.
