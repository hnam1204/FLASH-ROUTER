# AUTH-BROWSER-01 DI Startup Hotfix

Date: 2026-10-16
Scope: P0 constructor ambiguity and ASP.NET Core host startup recovery.

## Verdict

**GO for the P0 hotfix.** The ambiguous `BrowserProfileUiPort` activation is removed, the full host graph builds with DI validation enabled, the real ASP.NET Core host starts, and the HTTP/HTTPS launch profiles serve the application and static assets successfully.

## Root Cause

`BrowserProfileUiPort` intentionally retained two public constructors after the AUTH-BROWSER-01 refactor:

- `BrowserProfileUiPort(IDbContextFactory<FlashAiDbContext>, LocalDataPaths, IChromeExecutableResolver, IChromeProcessLauncher)` at `src/Modules/Connections/Application/BrowserProfileUiPort.cs:14`.
- `BrowserProfileUiPort(IBrowserProfileManager, IChromeProcessLauncher)` at `src/Modules/Connections/Application/BrowserProfileUiPort.cs:23`.

`ConnectionsModuleRegistration` used `services.AddScoped<BrowserProfileUiPort>()` at the same time that all dependencies for both constructors were registered. Microsoft DI therefore found multiple valid public constructors and reported `Unable to activate type BrowserProfileUiPort. The following constructors are ambiguous` when `builder.Build()` reached `Program.cs:45`. `Program.cs:45` was the detection point, not the origin of the ambiguity.

## Minimal Fix

`src/Modules/Connections/ConnectionsModuleRegistration.cs:76` now registers `BrowserProfileUiPort` with an explicit factory that selects the canonical AUTH-BROWSER-01 constructor:

```csharp
services.AddScoped<BrowserProfileUiPort>(serviceProvider =>
    new BrowserProfileUiPort(
        serviceProvider.GetRequiredService<IBrowserProfileManager>(),
        serviceProvider.GetRequiredService<IChromeProcessLauncher>()));
```

The legacy public constructor remains available for existing direct callers and tests. The canonical constructor uses the existing singleton `IBrowserProfileManager` and `IChromeProcessLauncher`; `BrowserProfileManager`, Vault, SQLite, DPAPI keys, and Chrome profile behavior were not rebuilt or changed. No other `BrowserProfileUiPort` registrations or direct constructions exist beyond this explicit DI factory and the existing compatibility test constructor call.

## Added Integration Coverage

`tests/FlashAI.IntegrationTests/FlashRouterHostCompositionTests.cs:25` builds the full host service composition used by `Program.cs`, including Razor components, Foundation, and every application module. It enables `ValidateOnBuild=true` and `ValidateScopes=true`, calls `builder.Build()`, creates a scope, and resolves:

- `IBrowserProfileUiPort` as `BrowserProfileUiPort`;
- `IBrowserAuthOrchestrator` as `BrowserAuthOrchestrator`;
- `IBrowserProfileManager`;
- `IChromeProcessLauncher`.

The test uses a unique temporary data root and deletes only that test root in its `finally` block.

## Verification Evidence

| Check | Result |
|---|---:|
| DI-focused host composition test | 1/1 passed |
| Architecture tests | 8/8 passed |
| Full integration tests | 115/115 passed |
| Debug build | 0 errors |
| Release build | 0 errors; 24 existing analyzer warnings |
| HTTP launch profile (`http`) | Host started on `http://localhost:5270` |
| HTTP `/health/ready` | HTTP 200 |
| HTTP `/` | HTTP 200 |
| HTTP Blazor script `/_framework/blazor.web.js` | HTTP 200 |
| HTTP app CSS `/app.css` | HTTP 200 |
| HTTP bundled CSS `/FLASH_ROUTER.styles.css` | HTTP 200 |
| HTTP module CSS | All 11 module bundles returned HTTP 200 |
| HTTPS launch profile (`https`) | Host started on `https://localhost:7146` and `http://localhost:5270` |
| HTTPS `/health/ready` | HTTPS 200 |
| HTTPS `/` | HTTPS 200 |
| HTTPS fallback `/health/ready` | HTTP 200 |

Runtime checks used only disposable roots under `.local/test-data/di-startup-runtime-*`, with provider sync disabled. No supplied credentials, live ChatGPT login, provider API, browser cookies/tokens, or real Chrome profile was used. The configured user data root, existing SQLite/Vault/DPAPI data, and existing accounts were not selected or modified.

## Residual Notes

- Release retains 24 analyzer warnings already present in the solution; none is a build failure or related to `BrowserProfileUiPort` activation.
- The Visual Studio launch settings were exercised through `dotnet run --launch-profile http` and `dotnet run --launch-profile https`; both profiles reached the live host and HTTP client checks.
- The final verdict is **GO** for this P0 startup hotfix.
