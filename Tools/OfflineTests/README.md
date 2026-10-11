# Offline rule tests

Runs `Assets/_Project/Tests/EditMode` against the game-rule core without opening Unity
(useful while the editor holds the project lock). Requires the .NET SDK and Unity 6000.3.19f1 installed.

```bash
MM_DATA_DIR="$PWD/Assets/_Project/Resources/Data" dotnet test Tools/OfflineTests/OfflineTests.csproj
```

Inside Unity, the same tests run from Test Runner (EditMode) and load data through `Resources`.
