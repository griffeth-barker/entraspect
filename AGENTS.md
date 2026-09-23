# entraspect

Cross-platform desktop app (C# / .NET 10 / Avalonia) that scans a Microsoft Entra ID tenant and surfaces hygiene issues admins may have missed: expiring app registration secrets and certificates, expiring enterprise app (SAML) signing certificates, unused or ineffective Conditional Access policies, and similar.

## Non-negotiable rules

1. **Read-only.** entraspect must never modify a tenant. Only use Microsoft Graph permissions ending in `.Read.All` (or narrower read scopes). Never add a `ReadWrite` scope and never call a POST/PATCH/PUT/DELETE Graph endpoint. If a feature seems to need write access, stop and ask.
2. **No secrets in the repo.** No tenant IDs, client IDs, certificates, passwords, or tokens in code, tests, or committed config. Client secrets are not a supported auth method; app-only auth uses certificates only.
3. **No real tenant in tests.** Unit tests use fake implementations of `IDirectoryReader`. Nothing in the test suite may make a network call.
4. **No telemetry.** No outbound calls other than Microsoft identity and Graph endpoints.
5. **Work from a spec.** Implement the spec in `docs/specs/` you were pointed to, one step at a time. Don't expand scope; add ideas to that spec's "Open questions" section instead.

## Tech stack

- .NET 10, C# latest, nullable enabled, warnings as errors (via `Directory.Build.props`)
- Avalonia (latest stable) with MVVM via CommunityToolkit.Mvvm; `Avalonia.Controls.DataGrid` for tables
- Microsoft.Graph .NET SDK (v5+) and Azure.Identity
- xUnit for tests; `Microsoft.Extensions.TimeProvider.Testing` for date logic

## Solution layout

```
entraspect.slnx
Directory.Build.props
src/Entraspect.Core/          models, checks, IDirectoryReader. No UI, no Graph SDK reference.
src/Entraspect.Graph/         IDirectoryReader implementation + authentication
src/Entraspect.App/           Avalonia UI (views, view models)
tests/Entraspect.Core.Tests/
docs/specs/                   numbered feature specs
```

Dependency direction: App → Graph → Core, and App → Core. Core references nothing else in the solution and has no Graph SDK dependency.

## Core concepts

- **`IDirectoryReader`**: async read methods returning simple Core-owned models (e.g. `AppRegistration`, `AppCredential`). Entraspect.Graph is the only place Graph is touched.
- **`ICheck`**: one hygiene check. Has `Id` (stable, kebab-case), `Title`, `Description`, `RequiredPermissions`, and `Task<IReadOnlyList<Finding>> RunAsync(IDirectoryReader reader, CheckOptions options, CancellationToken ct)`.
- **`Finding`**: `CheckId`, `Severity` (Info / Warning / Critical), `ObjectType`, `ObjectId`, `ObjectDisplayName`, `Message`, optional `DueDate`.
- Checks get "now" from an injected `TimeProvider`, never `DateTime.Now`, so date logic is testable.

## Authentication

Two modes, both via Azure.Identity, scope `https://graph.microsoft.com/.default`:

- **Interactive (delegated):** `InteractiveBrowserCredential` using a user-supplied public client app registration (tenant ID + client ID, redirect URI `http://localhost`).
- **App-only:** `ClientCertificateCredential` with tenant ID, client ID, and a certificate loaded from either a .pfx file (password prompted at runtime, never stored) or the OS certificate store by thumbprint.

Connection profiles (name, tenant ID, client ID, auth mode, cert path or thumbprint; never passwords) are saved as JSON in the user's application data folder, never in the repo.

## Commands

- Build: `dotnet build`
- Test: `dotnet test`
- Run: `dotnet run --project src/Entraspect.App`

A step is only done when build and tests both pass.

## Conventions

- One check per file under `src/Entraspect.Core/Checks/`, with a matching test file.
- Async all the way; accept and pass `CancellationToken`.
- Graph paging: always follow every page (`PageIterator` or `@odata.nextLink`).
- View models never expose Graph SDK types.
- Show Graph errors plainly in the UI; for 403s, name the permission that is likely missing.
- Commit after each completed spec step: `spec-NNN step N: <summary>`.
