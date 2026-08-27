# Architecture

**Status:** Draft — Phase 3 working design  
**Last updated:** 2026-08-27  
**Product:** Sushi81 POS  
**Purpose:** Define the implementation architecture that preserves the approved product, lifecycle and data-model semantics while prioritizing reliability, simplicity and maintainability.

## 1. Architecture priorities

The technical architecture is optimized in this order:

1. reliability;
2. simplicity;
3. maintainability;
4. operational clarity;
5. visual novelty only where it does not weaken the priorities above.

Sushi81 POS is a focused local Windows business application. It is not intended to become a distributed cloud platform, web application or general ERP.

## 2. Core platform — approved Phase 3 direction

V1 uses:

- **.NET 10 LTS** as the application runtime/platform;
- **WPF** for the Windows desktop UI;
- **SQLite** for durable local business-data storage;
- **Microsoft.Data.Sqlite** as the primary .NET SQLite access layer unless implementation proves a concrete blocker;
- ordinary Windows printing infrastructure for local printer integration, with exact printing behavior deferred to `printing.md`.

WPF is chosen deliberately because Sushi81 POS prioritizes mature Windows desktop behavior, data-heavy forms/tables, printer integration and maintainability over fashionable UI technology.

## 3. Application shape

V1 should be implemented as a **single local desktop application with a modular internal structure**, not as multiple services.

Recommended internal separation:

- **Presentation/UI layer** — WPF views, navigation and user interaction;
- **Application layer** — order-entry, catalogue, payment, archive and handoff use cases;
- **Domain/business-rules layer** — approved pricing, validation, lifecycle and calculation rules;
- **Persistence layer** — SQLite repositories/queries, migrations and transactional persistence;
- **Infrastructure services** — printing, Excel import/export, local snapshot generation, OneDrive handoff transport and technical logging.

These are code-organization boundaries, not separate processes or network services.

## 4. Local-first execution

Each paired Windows computer runs the full application locally and uses its own local working SQLite database.

The application must remain usable on the current authoritative/writable computer even if Internet/OneDrive connectivity is temporarily unavailable, subject to the handoff ownership rules defined in `storage-strategy.md`.

The active SQLite working database must **not** be opened directly from a OneDrive-synchronized folder and must not be concurrently written by more than one paired device.

The local working database and local recovery snapshots are application-managed technical data. Normal users are not asked to choose, move, rename or directly manipulate those files.

Cross-device handoff is configured separately through a user-selected OneDrive root folder. Selecting that folder does not move or expose the local working database.

The architecture must not hard-code assumptions such as exactly two devices, `SHOP-PC` plus `HOME-PC`, or two fixed synchronization slots.

## 5. Monetary representation — approved Phase 3 direction

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
- business data must never depend on current catalogue joins for historical interpretation;
- foreign-key behavior and delete rules must preserve the approved historical snapshot model;
- the physical schema must implement the approved `data-model.md` semantics rather than inventing new business entities during coding.

## 7. Multi-device principle — approved Phase 3 direction

Sushi81 POS does **not** implement simultaneous multi-writer database access in v1.

The architecture supports **an arbitrary number of paired Windows devices by default**. The normal initial deployment may use two computers, but adding a third or later computer must not require redesigning the database, handoff protocol or application architecture.

All paired devices participate in the same authoritative database lineage through controlled handoff of complete, validated SQLite snapshots via a user-configured OneDrive root folder.

At any moment:

- at most one paired device may hold authoritative write access;
- the current authoritative device may work locally and later publish a formal handoff;
- every other paired device is non-authoritative and may only use the approved read-only mode until it safely acquires a released formal handoff;
- no automatic row-level database merge is performed.

OneDrive is therefore a **handoff transport, recovery and archive medium**, not the live database engine and not a real-time database synchronization service.

Each participating device keeps its own local working database outside OneDrive and reconstructs/updates that local database only from a formally completed and validated handoff snapshot or an explicit approved disaster-recovery action.

Each installation has its own opaque `device_id`; all devices in the same business data family share a stable `lineage_id`. The exact identity and generation rules are defined in `storage-strategy.md`.

Exact handoff, integrity, release/acquisition, pairing, multi-device authority and failure behavior is defined in `storage-strategy.md`.

## 8. Maintainability principle

Prefer first-party .NET/Windows capabilities and small, mature dependencies.

Do not introduce:

- a web server merely to host the local UI;
- Docker/container infrastructure;
- microservices;
- a remote database server;
- distributed-database merge logic;
- a complex dependency-injection/framework stack unless it materially simplifies testing or maintenance;
- speculative extensibility that is not needed by approved requirements.

The codebase should remain understandable enough that Codex or a future maintainer can trace an order from UI action through business rule to SQLite persistence without crossing unnecessary infrastructure layers.

## 9. Deployment and local-data boundary — approved Phase 3 direction

Application binaries are installer-managed and separate from business data.

Application-managed local business/technical data uses a fixed per-user application-data root under `%LOCALAPPDATA%\Sushi81 POS\` with logical subareas for:

- `Data\` — active `live.db`;
- `Recovery\` — local rolling recovery snapshots;
- `Cache\` — disposable local caches, including archive read-only hydration where needed;
- `Logs\` — technical logs;
- `Config\` — local machine/application configuration including device identity;
- `Temp\` — staging files used for safe snapshot/archive operations.

The working `live.db` and local Recovery path are not ordinary user-configurable locations.

The operator configures only the shared OneDrive root used for handoff/recovery/archive functions; the application creates and manages its required subfolders beneath that root.

Application update/reinstall logic must not treat local business data as disposable program files.

## 10. Decisions still to freeze

Before this document becomes the final approved architecture baseline, Phase 3 should still decide at least:

- Windows packaging/install technology and update/distribution approach;
- dependency choice for Excel `.xlsx` import/export;
- physical SQLite settings needed for durability/performance;
- printing integration details that materially affect architecture.

The storage/data-location and multi-device principles are already constrained by the approved Phase 3 direction above and `storage-strategy.md`.

## 11. Approval rule

This document remains **Draft — Phase 3 working design** until the remaining deployment/integration choices are reviewed.

Implementation may use the approved platform direction for planning, but Codex must not silently replace the local-first WPF/SQLite architecture with a server, web application, live OneDrive database or other distributed design, and it must not implement device participation as a fixed two-computer model.