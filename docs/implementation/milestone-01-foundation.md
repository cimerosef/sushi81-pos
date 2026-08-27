# M01 — Executable foundation and safe persistence spine

**Status:** Approved task definition — implementation not started  
**Phase:** 6 — Implementation  
**Milestone:** M01  
**Scope type:** Production foundation and automated tests; no business-feature implementation

## 1. Codex mission

Implement only the technical foundation described in this file.

The result must be a buildable, testable .NET 10/WPF solution with safe SQLite bootstrap/migration/snapshot primitives. Do not implement Catalogue, Order, Payment, Hiboutik, printing, export, OneDrive handoff, annual archive or installer business workflows.

Read the entire repository instruction/specification set before editing, with particular attention to:

- `AGENTS.md`;
- `docs/v1-specification-freeze.md`;
- `docs/implementation-plan.md`;
- `docs/implementation-status.md`;
- `docs/acceptance-criteria.md`;
- `docs/architecture.md`;
- `docs/data-model.md`;
- `docs/storage-strategy.md`;
- `src/README.md`;
- `tests/README.md`.

GitHub is authoritative. Do not use prior chat history or the legacy Excel workbook to invent target behavior.

## 2. Required repository result

Create this logical structure. Generated files such as `obj/` must remain ignored and uncommitted.

```text
Sushi81.Pos.sln
Directory.Build.props
Directory.Packages.props
global.json
.editorconfig
src/
    Sushi81.Pos.Domain/
    Sushi81.Pos.Application/
    Sushi81.Pos.Infrastructure/
    Sushi81.Pos.Desktop/
tests/
    Sushi81.Pos.Domain.Tests/
    Sushi81.Pos.Application.Tests/
    Sushi81.Pos.Infrastructure.IntegrationTests/
    Sushi81.Pos.ArchitectureTests/
```

Keep production assemblies limited to four:

1. `Sushi81.Pos.Domain` — `net10.0`; no project references.
2. `Sushi81.Pos.Application` — `net10.0`; references Domain only.
3. `Sushi81.Pos.Infrastructure` — `net10.0-windows`; references Application and Domain.
4. `Sushi81.Pos.Desktop` — `net10.0-windows`, WPF executable; references Application, Domain and Infrastructure and acts as composition root.

Dependency direction is mandatory. Domain/Application must not reference WPF, Microsoft.Data.Sqlite, ClosedXML, Windows printing or concrete infrastructure.

Use central package version management in `Directory.Packages.props`. Pin every NuGet dependency to an exact stable version compatible with the installed .NET 10 SDK. Do not use floating versions. Record the versions in the completion report; do not add a dependency that is not used by M01.

Use the highest installed stable .NET 10 SDK and pin it in `global.json` with a reasonable feature-band roll-forward policy. If no .NET 10 SDK is available, stop and report the environment blocker; do not silently target .NET 8/9 or another UI stack.

## 3. Repository-wide build rules

Configure at least:

- nullable reference types enabled;
- implicit usings enabled;
- deterministic builds;
- .NET analyzers enabled at the recommended level;
- warnings treated as errors for solution-owned code;
- explicit UTF-8 source files;
- no generated build output committed.

Use MSTest for new test projects unless the installed .NET 10 templates create a concrete incompatibility. Use simple fakes/test doubles; do not add a mocking framework in M01.

Create a Windows CI workflow that restores, builds Release and runs all tests. Do not add deployment, public publishing or secret-dependent jobs.

## 4. Domain primitives

### 4.1 Money

Implement one shared immutable Money value type in Domain.

Requirements:

- canonical value is signed 64-bit integer euro cents;
- no `double` or `float` participates in business-money calculation;
- conversion from decimal euros explicitly rounds to cents using the approved round-half-up behavior (`MidpointRounding.AwayFromZero` for signed midpoint handling);
- conversion to decimal euros is exact (`cents / 100m`);
- addition, subtraction and multiplication paths use checked arithmetic where overflow is possible;
- equality and ordering are value based;
- formatting is presentation responsibility and does not become the persisted representation.

Implement one shared business-rounding helper rather than duplicating rounding rules.

Minimum tests:

- €0.01 ↔ 1 cent;
- €18.50 ↔ 1850 cents;
- €13.635 → €13.64;
- corresponding signed midpoint behavior is explicit and tested;
- zero, positive and negative adjustment values;
- equality/comparison;
- checked overflow;
- round-trip decimal/cents cases.

Do not implement discount, VAT, delivery-fee or order pricing formulas in M01.

### 4.2 Time and business-date seams

Use .NET `TimeProvider` or a small application-owned abstraction backed by it so all later date-sensitive behavior can be tested deterministically.

Provide explicit access for:

- current UTC technical timestamp;
- current local/business date in the configured local timezone context;
- test-controlled time.

Do not call `DateTime.Now`, `DateTime.UtcNow` or equivalent directly throughout application/domain code outside the approved time provider implementation.

M01 does not invent a non-midnight restaurant trading-day cutoff.

### 4.3 IDs

Provide an application-owned ID generator abstraction using random/time-ordered UUID-compatible values available in .NET 10; prefer UUID/GUID version 7 when supported.

IDs must be opaque and collision-safe. Do not reproduce the legacy second-resolution `YYYYMMDD_HHMMSS` identifier as the technical primary key.

The final operator-facing order-ID rendering is not part of M01.

## 5. Application data paths

Implement an `IAppPaths` abstraction and a Windows production implementation rooted exactly under:

`%LOCALAPPDATA%\Sushi81 POS\`

Expose and create application-managed subdirectories:

- `Data`;
- `Recovery`;
- `Cache`;
- `Logs`;
- `Config`;
- `Temp`.

The production live path is:

`%LOCALAPPDATA%\Sushi81 POS\Data\live.db`

Tests must use an isolated randomly named temporary root, never the real user application-data folder.

Do not offer a UI/configuration option to relocate `live.db` or `Recovery`. Do not place `live.db` beside the executable or inside OneDrive.

Path initialization must be idempotent and fail clearly rather than silently falling back to a disposable directory.

## 6. Local configuration

Implement a small local configuration service using `System.Text.Json` and an application-managed file under `Config`.

M01 configuration contains only foundation/local values needed now, such as selected UI culture. Device/lineage/OneDrive/printer settings belong to later milestones.

Writes must use a temp-file plus atomic replace/move pattern so interruption does not normally leave a partial JSON file.

Malformed configuration must produce an actionable error and must not delete unrelated business data. Do not store credentials or secrets.

## 7. Logging and diagnostics

Use `Microsoft.Extensions.Logging` abstractions with one small mature rolling-file provider or an application-owned equivalent. Pin any external provider to an exact stable version.

Production logs belong under `Logs` and must be bounded. Use these M01 defaults unless a concrete platform blocker is found:

- daily rolling files;
- roll at approximately 10 MB;
- retain at most 14 log files;
- UTF-8 text;
- include timestamp, severity, stable event ID/category and exception type/message where appropriate.

Never log telephone numbers, addresses, order comments, pasted Hiboutik text, full business payloads, credentials/tokens or database contents.

Use technical identifiers and counts for correlation. Add tests or testable redaction boundaries showing that representative sensitive strings are not emitted by foundation diagnostic events.

## 8. SQLite connection policy

Use `Microsoft.Data.Sqlite` with an exact pinned stable version.

Create one application-owned connection factory. Every opened live-database connection must apply and verify:

```sql
PRAGMA foreign_keys = ON;
PRAGMA journal_mode = WAL;
PRAGMA synchronous = FULL;
PRAGMA busy_timeout = 5000;
```

Rules:

- finite five-second busy timeout;
- no single mutable global connection;
- connection lifetime scoped to a use case/operation;
- multi-row writes in explicit short transactions;
- no transaction open while waiting for input, printing, synchronization or network activity;
- no raw filesystem copy of an open database for snapshots;
- explicit read-only connection mode;
- SQLite errors never trigger automatic delete/recreate of `live.db`.

Integration tests must query active PRAGMA values rather than merely inspect constants.

## 9. Versioned migration runner

Implement an application-owned ordered migration runner.

Required behavior:

- strictly increasing integer version and stable descriptive name;
- `schema_migrations` records version, name and application-generated timestamp;
- initial M01 migration creates only migration/foundation metadata needed now;
- later business tables use later numbered migrations;
- current-version startup is idempotent;
- unknown future schema version is rejected fail-closed;
- each migration is transactional where SQLite permits;
- a failed migration rolls back its own changes;
- failure never deletes, recreates or silently resets the source database;
- before upgrading an existing non-empty database, create and validate a pre-migration SQLite-safe recovery snapshot;
- startup remains blocked with an actionable error when safe migration cannot complete.

Do not introduce Entity Framework migrations or another ORM in M01. Use explicit SQL migrations through Microsoft.Data.Sqlite.

Minimum integration tests:

1. fresh bootstrap;
2. repeated current-version startup;
3. ordered upgrade across at least two synthetic test migrations;
4. intentional SQL migration failure;
5. rollback preserves sentinel data;
6. failure does not replace the database with an empty file;
7. pre-migration snapshot passes integrity/checksum validation;
8. future/unknown schema version rejected;
9. two runner instances cannot silently interleave conflicting migrations.

## 10. Transaction boundary

Define an Application-level transaction/use-case boundary and implement it in Infrastructure.

It must allow later aggregate saves to commit Order, items, adjustments, tax and payment effects atomically without exposing Sqlite types to Domain/Application.

M01 must include an integration-only representative multi-table transaction proving:

- all rows commit together;
- injected failure rolls all rows back;
- no recovery snapshot is reported successful for an uncommitted/rolled-back change.

Do not create fake production Catalogue or Order tables for this test. Use test-only schema inside the isolated integration database.

## 11. SQLite-safe local snapshot primitive

Implement a consistent snapshot service using supported Microsoft.Data.Sqlite/SQLite backup behavior while application writes are controlled.

Snapshot completion requires:

1. backup to staging under local `Temp`;
2. open staged database independently;
3. run `PRAGMA integrity_check` and require `ok`;
4. compute SHA-256 over the completed database;
5. write metadata with checksum, created timestamp, schema version and durable-change sequence/version;
6. atomically promote database plus metadata into `Recovery`;
7. only after validation/promotion apply cleanup.

Recovery filenames must be immutable and sortable without relying solely on filesystem modified timestamps.

Retention:

- latest five complete validated units;
- database plus metadata form one unit;
- invalid/incomplete unit is never valid;
- do not delete an older valid unit until a newer valid replacement exists;
- a failed sixth snapshot leaves the previous five intact.

M01 does not implement restoration UI, OneDrive handoff or cloud disaster recovery.

Minimum tests:

- WAL source snapshot;
- includes a just-committed row;
- excludes a rolled-back row;
- independent integrity check;
- checksum mismatch rejected;
- incomplete pair rejected;
- retention remains at five;
- failed replacement preserves five;
- staging artifacts never treated as valid.

## 12. Durable-change and recovery scheduling seam

Implement a durable-change notifier/recovery scheduler for later business use cases.

M01 behavior:

- only successful commits notify it;
- nearby notifications coalesce with a fixed three-second debounce;
- orderly shutdown flushes a pending local snapshot where creation remains possible;
- failed/rolled-back writes do not advance durable-change sequence;
- scheduling is single-flight;
- failure is logged/actionable and never claims a valid snapshot exists.

Business triggers are wired and exhaustively verified as use cases arrive in M03–M06.

## 13. Write-authority guard seam

Create an Application-level guard for every later authoritative mutation.

Define at least these technical states:

- uninitialized;
- authoritative/writable;
- non-authoritative/read-only;
- transitioning/handoff in progress;
- recovery required/blocked.

The guard provides one clear require-write-authority operation and a controlled application error when forbidden.

Tests must prove only authoritative state allows writes. Do not implement pairing, OneDrive, force takeover, handoff UI or disaster recovery. Never default an unknown state to writable.

## 14. Localization foundation

Implement resource-based WPF localization for:

- French (`fr-FR`) — default;
- Simplified Chinese (`zh-CN`).

Requirements:

- stable language-neutral keys;
- never show both languages simultaneously;
- runtime switch in the foundation shell;
- selected culture persisted in local configuration;
- switching language changes resources only;
- technical schema identifiers, paths and future business data are not translated/mutated;
- no hard-coded user-facing strings in XAML/code-behind except non-user-facing diagnostics.

The shell needs only title/status/language controls. Do not design final POS navigation/dashboard.

## 15. WPF shell and composition root

Create a minimal WPF window that:

- starts through an explicit composition root;
- initializes paths, configuration, logging and migrations in defined order;
- displays localized title and foundation-ready/startup-failure state;
- exposes language switch;
- contains no Catalogue, Caisse, Orders, Payment, Export or fake dashboard controls;
- does not hide startup/migration failure behind an empty window;
- shuts down cleanly and flushes pending local logging/recovery work.

Use built-in Microsoft dependency injection/hosting only if it simplifies composition/testing. Do not add a UI/navigation/mediator/generic-repository framework.

Code-behind is acceptable only for trivial window wiring. Keep stateful behavior in small testable services/view models without adding a large MVVM framework.

## 16. Architecture tests

Add automated tests that fail if:

- Domain references Application, Infrastructure or Desktop;
- Application references Infrastructure/Desktop/WPF/Sqlite;
- Infrastructure references Desktop;
- WPF types appear in Domain/Application public APIs;
- Microsoft.Data.Sqlite types appear in Domain/Application public APIs.

Do not add a third-party architecture-test library if ordinary reflection/project inspection is sufficient.

## 17. CI and verification commands

Run and report, adjusting only generated paths if necessary:

```powershell
dotnet --info
dotnet restore Sushi81.Pos.sln
dotnet build Sushi81.Pos.sln -c Release --no-restore
dotnet test Sushi81.Pos.sln -c Release --no-build
dotnet publish src/Sushi81.Pos.Desktop/Sushi81.Pos.Desktop.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=false
```

Do not claim Windows launch/manual verification if the environment cannot launch WPF. Complete available automated work and report the exact outstanding Windows check.

## 18. Required test inventory

Committed tests must clearly cover:

- Money conversion, rounding, signed values and overflow;
- controllable time/business date;
- opaque unique IDs;
- production and isolated paths;
- atomic configuration and malformed-config failure;
- localization switch/persistence;
- authority-state decisions;
- live SQLite PRAGMAs;
- fresh/idempotent/ordered/failing/future migrations;
- pre-migration snapshot/failure preservation;
- transaction commit/rollback;
- WAL-safe snapshot integrity/checksum;
- five-unit retention/failed replacement;
- debounce/single-flight/shutdown flush;
- dependency boundaries;
- logging exclusion of representative sensitive fixture strings.

Use only synthetic values. Do not copy real data from the legacy workbook.

## 19. Explicitly out of scope

Do not implement or scaffold speculative production APIs for:

- Catalogue/BusinessSettings tables or UI;
- Order/Payment/Tax tables or UI;
- pricing/discount/VAT formulas;
- search/dashboard;
- printer enumeration/layout/spooling;
- Hiboutik parsing;
- ClosedXML;
- export ledger/workbooks;
- OneDrive root/handoff files;
- pairing/acquisition/disaster recovery;
- annual archives;
- Inno Setup;
- login/accounts/roles;
- any legacy Hiboutik emergency model.

Only the narrow time, ID, paths, transaction, recovery-scheduling and authority-guard seams required above are authorized.

## 20. Prohibited shortcuts

M01 fails if implementation:

- targets anything other than .NET 10/WPF;
- stores money as `double`, `float` or SQLite `REAL`;
- deletes/recreates `live.db` after migration/open failure;
- filesystem-copies an open SQLite database as recovery;
- omits migration failure tests;
- places live database in OneDrive or beside executable;
- defaults unknown authority to writable;
- logs sensitive fixture payloads through production logging paths;
- adds fake business features merely to make the shell look complete;
- changes frozen specification for implementation convenience;
- commits binaries, databases, logs, local config or real business data.

## 21. Milestone completion evidence

Before declaring M01 complete, Codex must:

1. show final file/project tree;
2. list exact SDK/NuGet versions;
3. report Release build result;
4. report tests by project with passed/failed/skipped counts;
5. report self-contained win-x64 publish result;
6. identify every M01 AC as Passed or Partial with evidence paths;
7. update `docs/implementation-status.md` accurately;
8. state every manual WPF/Windows check not executed;
9. confirm no business-feature code or sensitive data was added;
10. provide final commit SHA.

M01 is not complete if a test fails, a required check is omitted without disclosure, or a material specification ambiguity is silently resolved.

