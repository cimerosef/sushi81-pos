# Pre-production test-data cutover utility

This temporary owner utility performs the one-time pre-production removal of the test business dataset. It is not part of the normal POS workflow and is not included in the Sushi81 POS installer.

## Before running

- Run only when the project controller has authorized the owner cutover and GitHub Issue #4 is **CLOSED**. Closing the gate prevents another Codex handoff from starting while the owner prepares the real catalogue.
- Use the same Windows account that owns `%LOCALAPPDATA%\Sushi81 POS`; do not run elevated or under another account.
- Close Sushi81 POS and verify it is not running.
- Keep this utility and all backup files local. Never upload `live.db`, its backup, archive files, or a preflight report to GitHub or another shared location.
- Do not run a disaster-recovery, transfer, or recovery action as part of this cutover.

## Dry run

Run `Sushi81.Pos.PreProductionCutoverReset.exe` with no arguments. It is read-only. It prints the resolved data root, validates the accepted installed application and closed retained authority state, checks the database/schema/integrity, and reports counts without exposing order contents. No backup is created and no database or archive content is changed.

Stop if any preflight check fails. Do not repair authority files or the database manually to make the utility proceed.

## Owner-authorized execution

Read the exact resolved data-root path printed by the dry run. Copy that full path into `--confirm-root` without changing it, then run:

```powershell
.\Sushi81.Pos.PreProductionCutoverReset.exe `
  --execute `
  --confirmation DELETE-ALL-TEST-BUSINESS-DATA `
  --confirm-root "<exact data root printed by the dry run>"
```

The utility repeats all preflight checks. It refuses to mutate unless the installed application provenance is version 1.0.0 from accepted source head `9f4521627b21f44c2dc5452f03db840a143e1ee4`, canonical authority state is schema 2 in `ClosedRetainedAuthority` with matching OneDrive lineage/device membership and no active transfer/recovery evidence, `live.db` is valid at schema migration 11, and a non-empty test dataset remains.

Before the single SQLite transaction, it creates an owner-visible timestamped folder under `%LOCALAPPDATA%\Sushi81 POS\CutoverBackups`. The folder contains a SQLite-consistent `live.db` backup, its SHA-256, and the local Archive files with SHA-256 values. Keep this folder private and retain it until the owner/controller confirms the new production recovery/handoff state is established.

Only the approved pre-production business tables are cleared. The transaction advances `business_data_revision` once and preserves `business_settings`, `schema_migrations`, `foundation_metadata` structure, `live.db`, authority/device/configuration files, Recovery, Cache/Logs, OneDrive System membership, GitHub handoff assets, Windows Credential Manager, and printer configuration. Active local Archive files are backed up before removal. The utility never contacts OneDrive or GitHub.

A successful run leaves local Recovery and remote disaster-recovery/handoff artifacts in place. Test-era safety artifacts may remain temporarily. The owner must import the real catalogue on authoritative device A through the normal application and then perform the normal close/flush/handoff so a clean newer recovery/handoff state supersedes the test-era evidence before production operation. No disaster-recovery action is authorized in the interim.

After a successful reset, the retained `Complete` backup manifest permanently blocks another `--execute` attempt, even if business rows are later reintroduced. Keep the entire `CutoverBackups` folder intact as the one-time guard and rollback record. If the utility reports an incomplete cutover backup or a rollback failure, leave Sushi81 POS closed and keep every backup folder intact. Do not delete or recreate `live.db`, edit SQLite rows manually, or start production operation; ask the project controller to guide recovery from the local backup.
