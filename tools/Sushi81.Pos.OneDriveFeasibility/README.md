# Deterministic protocol simulator

This non-shipping tool models an N-device claim transport with explicit per-device visibility. It intentionally supports delayed, reordered, duplicated and dropped observations so that a fixed sleep or local file existence cannot accidentally become a distributed lock.

Run the executable from this directory with:

```powershell
dotnet run --project .\Sushi81.Pos.OneDriveFeasibility.csproj -c Release
```

The `ClaimOnlyAcquisitionProtocol` is an executable counterexample baseline: two devices can each see only their own claim and both become writable. `FailClosedAcquisitionProtocol` permits a single known participant, but blocks N-device acquisition unless an explicit documented atomic exclusive grant exists. The simulator does not invent such a grant from file synchronization.

## Windows/OneDrive observation and synthetic publication commands

The same executable also exposes the observation-only Windows harness. It never opens or changes the POS `live.db`, never activates write authority, and all generated payloads contain synthetic data.

The harness targets `net10.0-windows10.0.19041.0` and pins the Windows SDK targeting pack to `10.0.26100.87`.

```powershell
dotnet run --project .\Sushi81.Pos.OneDriveFeasibility.csproj -c Release -- roots --json
dotnet run --project .\Sushi81.Pos.OneDriveFeasibility.csproj -c Release -- validate-root <path> --json
dotnet run --project .\Sushi81.Pos.OneDriveFeasibility.csproj -c Release -- observe <file> --json
dotnet run --project .\Sushi81.Pos.OneDriveFeasibility.csproj -c Release -- publish <registered-OneDrive-root> --device device-a --lineage <guid> --generation 1 --version 1
dotnet run --project .\Sushi81.Pos.OneDriveFeasibility.csproj -c Release -- inspect <synthetic-Handoff-directory> --json
dotnet run --project .\Sushi81.Pos.OneDriveFeasibility.csproj -c Release -- claim <claims-directory> --lineage <guid> --device device-a
dotnet run --project .\Sushi81.Pos.OneDriveFeasibility.csproj -c Release -- observe-claims <claims-directory> --lineage <guid> --json
```

`roots` calls the documented `StorageProviderSyncRootManager.GetCurrentSyncRoots()` API and prints only path, provider identifier and technical identity. A root is accepted only when it is beneath a registered root whose metadata identifier starts with `OneDrive!`; a local folder named `OneDrive` is rejected. `observe` calls the documented Cloud Files `CfGetPlaceholderStateFromAttributeTag` function after reading `FileAttributeTagInfo` through `GetFileInformationByHandleEx`. Only a real `IN_SYNC` placeholder state is accepted for the narrow publication wait; missing, local, pending, partial, invalid, API-error and unknown states fail closed.

`publish` creates a closed synthetic SQLite database in a temporary local staging directory, verifies `PRAGMA integrity_check`, computes SHA-256, moves the immutable snapshot into the requested synthetic `Handoff` directory, waits for confirmed `IN_SYNC`, and only then creates and waits for its matching ready marker. If the snapshot or marker cannot be confirmed synchronized, the unit is not reported as released. The command is expected to remain blocked on an ordinary local directory, which is evidence that file existence is not treated as cloud publication.

The M02 directed proof uses only local authority/target state plus immutable handoff artifacts; it does not require a shared mutable lifecycle coordinator:

```powershell
dotnet run --project .\Sushi81.Pos.OneDriveFeasibility.csproj -c Release -- directed-source-run <registered-OneDrive-root> --state-dir <device-local-synthetic-dir> --device <source> --target <target> --lineage <guid> --generation <n> --version <n> --json
dotnet run --project .\Sushi81.Pos.OneDriveFeasibility.csproj -c Release -- directed-target-acquire <registered-OneDrive-root> --state-dir <device-local-synthetic-dir> --device <target> --source <source> --target <target> --lineage <guid> --generation <n> --version <n> --transfer-id <guid> --json
dotnet run --project .\Sushi81.Pos.OneDriveFeasibility.csproj -c Release -- directed-target-promote --state-dir <device-local-synthetic-dir> --device <target> --source <source> --target <target> --lineage <guid> --generation <n> --version <n> --transfer-id <guid> --json
dotnet run --project .\Sushi81.Pos.OneDriveFeasibility.csproj -c Release -- directed-source-resume <registered-OneDrive-root> --state-dir <device-local-synthetic-dir> --device <source> --target <target> --lineage <guid> --generation <n> --version <n> --transfer-id <guid> --json
```

`directed-lifecycle-complete` is retained only as a target-local diagnostic audit append and is not part of the real two-device authority flow. The optional append-only ledger must never be used as a write-authority or current-holder input.

## GitHub Release Asset transport (current normal-handoff revalidation)

The approved normal handoff transport is a dedicated private GitHub repository (conceptually `cimerosef/sushi81-pos-handoff`) with one long-lived release tag `sushi81-handoff-v1`. The source-code repository is never used as an operational handoff store. The harness uses direct .NET `HttpClient` and no Git CLI, Actions, LFS, Packages or filesystem synchronization. For later operator testing, use a fine-grained PAT restricted to that repository with only the required Contents read/write permission for release bootstrap/assets.

Set the fine-grained token only for the process environment; do not put it in command arguments, files or screenshots:

```powershell
$env:SUSHI81_GITHUB_HANDOFF_TOKEN = '<temporary-token>'
$project = '.\Sushi81.Pos.OneDriveFeasibility.csproj'
dotnet run --project $project -c Release -- github-transport-check --owner cimerosef --repo sushi81-pos-handoff --release-tag sushi81-handoff-v1 --create-release --json
dotnet run --project $project -c Release -- github-directed-source-run --state-dir <synthetic-state-a> --device device-a --target device-b --lineage <guid> --generation 7 --version 1 --transfer-id <guid> --owner cimerosef --repo sushi81-pos-handoff --release-tag sushi81-handoff-v1 --json
dotnet run --project $project -c Release -- github-directed-target-acquire --state-dir <synthetic-state-b> --device device-b --source device-a --target device-b --lineage <guid> --generation 7 --version 1 --transfer-id <guid> --owner cimerosef --repo sushi81-pos-handoff --release-tag sushi81-handoff-v1 --json
dotnet run --project $project -c Release -- github-remote-inspect --owner cimerosef --repo sushi81-pos-handoff --release-tag sushi81-handoff-v1 --json
```

The source command requires a strict HTTP 201 `uploaded` receipt with exact asset name/size/ID and matching `sha256:<hex>` digest before durable relinquishment; it uploads the same-basename `.grant.json` only afterwards. The target validates the exact grant/snapshot identity and hash before the durable pending/evidence/final-cursor write gate. Existing OneDrive commands remain historical diagnostics and are not consulted by these GitHub commands. CI uses deterministic fake HTTP only; real private-repository A → B v1 and B → A v2 evidence is a separate operator gate.
