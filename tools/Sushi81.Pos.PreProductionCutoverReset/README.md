# Pre-production test-data cutover utility

This temporary owner utility performs the one-time pre-production removal of the test business dataset. It is not part of the normal POS workflow and is not included in the Sushi81 POS installer.

## Before running

- Run only when the project controller has authorized the owner cutover and GitHub Issue #4 is **CLOSED**. Closing the gate prevents another Codex handoff from starting while the owner prepares the real catalogue.
- Use the same Windows account that owns `%LOCALAPPDATA%\Sushi81 POS`; do not run elevated or under another account.
- Close Sushi81 POS and verify it is not running.
- Keep this utility and all backup files local. Never upload `live.db`, its backup, archive files, or a preflight report to GitHub or another shared location.
- Do not run a disaster-recovery, transfer, or recovery action as part of this cutover.

## Dry run

Run `Sushi81.Pos.PreProductionCutoverReset.exe` with no arguments. It is read-only. It prints the resolved data root, validates the accepted installed application and settled retained authority (`Authoritative` or `ClosedRetainedAuthority`, with the desktop closed), checks the database/schema/integrity, and reports counts without exposing order contents. The report shows the actual accepted authority phase. No backup is created and no database or archive content is changed.

Stop if any preflight check fails. Do not repair authority files or the database manually to make the utility proceed.

## Owner-authorized execution

Read the exact resolved data-root path printed by the dry run. Copy that full path into `--confirm-root` without changing it, then run:

```powershell
.\Sushi81.Pos.PreProductionCutoverReset.exe `
  --execute `
  --confirmation DELETE-ALL-TEST-BUSINESS-DATA `
  --confirm-root "<exact data root printed by the dry run>"
```

The utility repeats all preflight checks. It refuses to mutate unless the installed application provenance is version 1.0.0 from accepted source head `9f4521627b21f44c2dc5452f03db840a143e1ee4`, canonical authority state is schema 2 in settled retained authority (`Authoritative` or `ClosedRetainedAuthority`, with the desktop closed) with matching OneDrive lineage/device membership and no active transfer/recovery evidence, `live.db` is valid at schema migration 11, and a non-empty test dataset remains. Authority `BusinessRevision` may be behind the database revision and will be synchronized by the cutover; an authority revision ahead of the database is contradictory and stops preflight. The current close-and-retain flow leaves phase 2 (`Authoritative`) persisted; the utility does not claim the UI writes phase 3.

Before mutation, it creates an owner-visible timestamped folder under `%LOCALAPPDATA%\Sushi81 POS\CutoverBackups`. The folder contains an exact byte-for-byte `live.db` copy, its SHA-256, the exact private `authority-state.json` bytes and SHA-256, the local Archive files and hashes, and a manifest with before/after revision and rollback provenance. These files contain local owner data: keep the folder private, never upload it or its contents, and retain it until the owner/controller confirms the new production recovery/handoff state is established.

Only the approved pre-production business tables are cleared. The SQLite transaction advances `business_data_revision` exactly once and preserves `business_settings`, `schema_migrations`, `foundation_metadata` structure, Recovery, Cache/Logs, OneDrive System files, GitHub handoff assets, Windows Credential Manager, and printer configuration. Active local Archive files are backed up before removal. As part of that same rollback-coordinated operation, the utility updates only the canonical authority protocol counters: `Protocol.BusinessRevision` becomes the new database revision, `Protocol.Revision` advances exactly once, and `UpdatedAtUtc` advances. It preserves schema version, derived write state, device/display/lineage identity, generation, handoff version, phase, last-recovery evidence, and the established null Transfer/Recovery fields. This technical revision sync does not transfer authority or change the authority owner/target, lineage, generation, handoff version, or device identity. Before POS can reopen, it atomically replaces `Config/authority-state.json`, flushes the file to disk, and verifies the persisted hash and canonical model. Every other Config file and every OneDrive `System` file must retain its original path set and SHA-256.

Database, Archive, and authority replacement are treated as one recoverable operation. If a step after database commit fails, the utility restores the exact database bytes, Archive file set/content, and exact authority-state bytes from the private local backups, then verifies their hashes before recording `RolledBack`. If exact restoration cannot be proven, it leaves the backup intact, keeps the application closed, and reports controller-guided recovery. Do not manually change the database or authority file to clear a failed state.

A successful run leaves local Recovery and remote disaster-recovery/handoff artifacts in place. Before the owner reopens POS, the synchronized authority revision already matches the cutover database revision, so M07 startup reads the same business revision that the database exposes. Test-era safety artifacts may remain temporarily. The owner must then verify the clean state, import the real catalogue on authoritative device A through the normal application, and perform the normal close/flush plus exact-target A-to-B handoff so a clean newer recovery/handoff state supersedes the test-era evidence before production operation. No disaster-recovery action is authorized in the interim.

After a successful reset, the retained `Complete` backup manifest permanently blocks another `--execute` attempt, even if business rows are later reintroduced. Keep the entire `CutoverBackups` folder intact as the one-time guard and rollback record. If the utility reports an incomplete cutover backup or a rollback failure, leave Sushi81 POS closed and keep every backup folder intact. Do not delete or recreate `live.db`, edit SQLite rows manually, or start production operation; ask the project controller to guide recovery from the local backup.
