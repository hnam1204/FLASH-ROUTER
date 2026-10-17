# Development runbook

From the workspace root:

```powershell
$env:DOTNET_CLI_HOME = (Join-Path (Get-Location) '.dotnet-home')
$env:NUGET_PACKAGES = (Join-Path (Get-Location) '.nuget\packages')
dotnet restore '.\FLASH ROUTER.sln'
dotnet build '.\FLASH ROUTER.sln'
dotnet test '.\FLASH ROUTER.sln'
dotnet run --project '.\FLASH ROUTER\FLASH ROUTER.csproj' --launch-profile http
```

The provider integration tests are offline and fixture-based. Run the full regression with `dotnet test '.\FLASH ROUTER.sln' --no-restore` after a restore, and use `dotnet build '.\FLASH ROUTER.sln' --no-restore` for the final build check. BE-04/BE-05 do not require live provider keys.

The default configuration is Integrated and uses `.local/data/flashrouter.db`. To exercise the in-memory Preview composition explicitly, set `FlashAI:AccountStorageMode` to `Preview` in a local configuration override; never use Preview data as SQLite persistence evidence.

Integrated Authenticator credentials are stored under `.local/security/vault` through `ISecretVault` and the existing Windows DPAPI-protected Data Protection key ring. Do not copy, delete, replace, or inspect key-ring XML files. Use synthetic credentials only in tests; never place secrets on the command line or in logs. Preview Authenticator data remains in memory and must not be used as vault evidence.

Do not use real provider credentials or point the application at a production database. In Integrated mode, the host applies the single EF migration pipeline at startup. Before changing an existing database schema, inspect and back up the database; do not recreate the file, drop tables, truncate data, or remove migration history.

Provider credentials are resolved from the BE-02 vault by provider and connection scope. The `/connections` provider panel is truthful about offline readiness; a stored credential is not evidence of a successful provider check. Manual checks require an authorized connection. `Check All` requires explicit confirmation, returns partial results on cancellation, and the provider scheduler is disabled unless `FlashAI:Connections:ProviderSync:Enabled=true` is deliberately set in a local override. Never enable live checks with credentials or billable capabilities without explicit approval.

For disposable SQLite integration tests, use the test project. The fixture creates a unique `.local/test-data/be01-*` database per test and clears SQLite pools before cleanup.

## CLI-01 Windows launcher

Build and test the launcher with the solution. Use `-p:UseAppHost=false` for Debug builds when Visual Studio or another process may hold the Windows apphost executable:

```powershell
dotnet build '.\FLASH ROUTER.sln' -c Debug -p:UseAppHost=false
dotnet test '.\tests\FlashAI.ArchitectureTests\FlashAI.ArchitectureTests.csproj' -c Debug --no-build --no-restore
dotnet test '.\tests\FlashAI.IntegrationTests\FlashAI.IntegrationTests.csproj' -c Debug --no-build --no-restore
```

For launcher commands, set `FLASHROUTER_DATA_ROOT` to a disposable `.local/test-data` directory and set `FLASHROUTER_NO_BROWSER=1`. Use a versioned release directory for E2E; never copy over a running `FLASH_ROUTER.exe`. See `docs/runbooks/windows-cli.md` for the command matrix and installer sandbox.
