# A-PC post-V1 guarded cleanup execution

**Status:** controller-authorized local maintenance contract  
**Handoff:** `A-PC-POST-V1-CLEANUP-EXECUTION-02`  
**Repository baseline:** `6ff2e04ce17e34addf58cf6dcfa756d4b7fae8aa`

## Purpose

Clean the owner’s Windows A computer after V1 completion without changing Sushi81 POS product behavior or production data.

A is the paired read-only production device. B remains authoritative/writable.

This contract authorizes local maintenance only.

## Keep

Retain all development tools for future maintenance:
- Git for Windows;
- Codex;
- .NET SDKs/runtimes;
- Visual Studio Build Tools;
- PowerShell;
- Node runtime used by Codex;
- other shared/unknown development tooling.

No tool uninstall is authorized.

Retain/protect all production state:
- `%LOCALAPPDATA%\Sushi81 POS\`
- `%LOCALAPPDATA%\Programs\Sushi81 POS\`
- OneDrive Sushi81 deployment material;
- Credential Manager handoff credentials;
- printer configuration;
- pairing/device identity/authority/recovery/archive/MaintenanceBackups state.

Never open/dump production database contents and never run the in-app business-data reset.

## Accepted production installer identity

Accepted application source:
`469c8761061b0ecf488e06386a97b9e10652a916`

CI:
`36253271385`

Artifact:
`10909188508`

Accepted artifact ZIP SHA-256:
`f97e5da9092fe0634f7498574c27e8265094af22bae96bf7615d6c212b129ce9`

Accepted installer:
`Sushi81POS-Setup-1.0.0-469c876.exe`

Installer SHA-256:
`995a09946a31ee1fb824d038f4af02ee59b0554c3628c3888c3268c0ebf92805`

Never substitute an older installer.

## Fail-closed preflight

Before destructive action:

1. Confirm the local clone is `cimerosef/sushi81-pos`.
2. Run `git fetch --prune origin`.
3. Confirm remote/GitHub `main` is exactly:
   `6ff2e04ce17e34addf58cf6dcfa756d4b7fae8aa`.
4. Recheck:
   - `git status --porcelain=v2 --branch --untracked-files=all`
   - `git branch -vv`
   - `git log --branches --not --remotes --oneline`
   - `git stash list`
   - `git worktree list --porcelain`
5. Resolve/canonicalize every deletion target.
6. Refuse any target equal to or below a protected production root or the configured OneDrive Sushi81 root.
7. Stop if any local branch has commits not reachable from current GitHub main.
8. Stop if any worktree has user work that cannot first be preserved.
9. Stop if the preservation package cannot be created and verified.

Do not improvise around a failed precondition.

## Preserve unique legacy local state

Create a private maintenance root under:
`%USERPROFILE%\Sushi81-POS-Maintenance\`

Create:
`A-PC-precleanup-legacy-state\`

Preserve at least:
- `git-status-before.txt`
- `branches-before.txt`
- `worktrees-before.txt`
- `stash-list-before.txt`
- binary-capable tracked-change patch `working-tree.patch`
- a Git bundle sufficient to reconstruct local refs/stash history
- manifest of untracked non-ignored entries
- copies of untracked non-ignored files that are not positively generated output

Do not preserve generated output merely because it is untracked when it is clearly under:
- `bin/`
- `obj/`
- `artifacts/`
- `publish/`
- `TestResults/`
- `coverage/`
- known generated milestone candidate/build roots.

If an untracked item is ambiguous, preserve it.

Do not package secrets or real business data. If such content is unexpectedly detected, stop and report category only.

Create a SHA-256/size manifest and verify the bundle/patch/copied files before deleting the old clone.

This preservation package must remain after cleanup.

## Accepted installer archival

Create:
`%USERPROFILE%\Sushi81-POS-Maintenance\Releases\V1.0.0\`

If the exact accepted ZIP/EXE already exists locally, verify hashes before keeping it.

If absent, attempt to retrieve GitHub Actions artifact `10909188508` using already-authorized GitHub access without exposing credentials.

If retrieved:
- retain exact ZIP;
- extract/copy exact installer EXE;
- verify both accepted hashes;
- keep provenance files if present;
- write `V1-baseline.txt`.

If retrieval is unavailable:
- do not substitute an older build;
- write source/artifact/hash facts to `V1-baseline.txt`;
- mark installer archival `PENDING`;
- continue cleanup because the accepted installer was already absent before cleanup.

## Remove completed Sushi81 worktrees

For every registered Sushi81 worktree except the canonical primary clone:
- verify it belongs to this repository;
- verify status is clean or its unique state is already preserved;
- use `git worktree remove`;
- use force only after verifying nothing unpreserved remains;
- run repository-specific `git worktree prune` after removals;
- do not delete global `%USERPROFILE%\.codex`;
- do not touch non-Sushi81 Codex worktrees.

## Replace old development clone

After preservation verification and worktree removal:

1. record old clone size;
2. remove only the old Sushi81 development clone;
3. clone `cimerosef/sushi81-pos` fresh into the same canonical project location;
4. checkout `main`;
5. fetch/prune;
6. verify:
   - HEAD = `6ff2e04ce17e34addf58cf6dcfa756d4b7fae8aa`
   - `origin/main` = same SHA
   - working tree clean
   - no stale local milestone branches/worktrees/stashes
   - no generated `bin/obj/artifacts/TestResults` residue.

Do not copy historical generated artifacts back.

## Clean project-specific temporary residue

Under `%TEMP%`, remove only paths positively attributable to Sushi81/Codex project build/test/candidate/delivery/preproduction/cutover/M01-M13 work.

Safety:
- canonicalize paths;
- reject reparse-point/junction escapes;
- never traverse OneDrive;
- never delete generic unrelated temp folders;
- skip locked/in-use items and report them;
- do not delete current cleanup logs/script.

## Clear regenerable NuGet caches

Run:
`dotnet nuget locals all --clear`

Then:
`dotnet nuget locals all --list`

Do not manually delete arbitrary directories outside NuGet-reported cache locations and do not restore/build merely to refill caches.

## Keep tooling unchanged

Do not uninstall or modify:
- Git;
- Codex;
- .NET SDK 8/10;
- .NET runtimes 6/8/9/10;
- ASP.NET runtimes;
- Windows Desktop runtimes;
- Visual Studio Build Tools;
- PowerShell;
- Node;
- PATH/environment configuration.

## Final verification

Verify the fresh clone:
- `main` checked out;
- HEAD/origin main exactly current V1 main;
- `git status --short` empty;
- no extra local Sushi81 worktrees;
- no stash;
- no generated residue.

Verify production protection by metadata only:
- protected program/data roots still exist;
- no protected production path was a cleanup target;
- no OneDrive/credential/printer action occurred.

Verify maintenance package:
- preservation directory exists;
- manifest hashes verify;
- `V1-baseline.txt` exists;
- installer archive status is VERIFIED or PENDING, never substituted.

Record disk free space before/after and actual reclaimed bytes/GiB.

## Repository behavior

This cleanup is local maintenance. Codex must not:
- modify product/source files;
- create commits;
- push implementation changes;
- open another PR;
- merge this mailbox PR;
- start feature/bug work.

No repository push is required if no repository files are changed. Completion evidence is comment-only.

## Public completion

Post one top-level comment on the active mailbox PR beginning exactly:

`CODEX_DONE: A-PC-POST-V1-CLEANUP-EXECUTION-02`

Include:
- cleanup success / partial / stopped;
- preservation package created+verified YES/NO;
- old clone replaced with fresh main YES/NO;
- worktrees removed count only;
- project-temp aggregate bytes removed;
- NuGet cache cleared YES/NO;
- total actual reclaimed GiB;
- development tools retained YES;
- production protected roots untouched YES/NO;
- accepted installer locally archived VERIFIED/PENDING;
- blockers by category only;
- `browserNotification` outcome;
- explicit statement that no source/product behavior/production business data changed.

Do not publish local paths or private preservation contents.

After DONE or fail-closed stop, stop.
