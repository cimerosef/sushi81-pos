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

Each authorized Windows computer runs the full application locally and uses its own local working SQLite database.

The application must remain usable on the current working computer even if Internet/OneDrive connectivity is temporarily unavailable, subject to the handoff ownership rules defined in `storage-strategy.md`.

The active SQLite working database must **not** be opened directly from a OneDrive-synchronized folder and must not be concurrently written by two computers.

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

## 7. Multi-computer principle

Sushi81 POS does **not** implement simultaneous multi-writer database access in v1.

The two-computer workflow uses controlled handoff of complete, validated SQLite snapshots through OneDrive.

OneDrive is therefore a **handoff transport and backup medium**, not the live database engine and not a real-time database synchronization service.

Exact handoff, integrity, release/acquisition and failure behavior is defined in `storage-strategy.md`.

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

## 9. Decisions still to freeze

Before this document becomes the final approved architecture baseline, Phase 3 should still decide at least:

- Windows packaging/install model and application/data folder layout;
- exact local configuration/log locations;
- dependency choice for Excel `.xlsx` import/export;
- physical SQLite settings needed for durability/performance;
- printing integration details that materially affect architecture;
- update/distribution approach for future application versions.

## 10. Approval rule

This document remains **Draft — Phase 3 working design** until the remaining deployment/storage integration choices are reviewed.

Implementation may use the approved platform direction for planning, but Codex must not silently replace the local-first WPF/SQLite architecture with a server, web application, live OneDrive database or other distributed design.