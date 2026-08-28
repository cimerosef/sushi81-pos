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
