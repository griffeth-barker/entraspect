# entraspect

Cross-platform desktop app (C# / .NET 10 / Avalonia) that scans a Microsoft Entra ID tenant and surfaces hygiene issues admins may have missed: expiring app registration secrets and certificates, expiring enterprise app (SAML) signing certificates, unused or ineffective Conditional Access policies, and similar.

## Non-negotiable rules

1. **Read-only.** entraspect must never modify a tenant. Only use Microsoft Graph permissions ending in `.Read.All` (or narrower read scopes). Never add a `ReadWrite` scope and never call a POST/PATCH/PUT/DELETE Graph endpoint. This includes POST endpoints that only read, such as `POST /$batch` and `POST /directoryObjects/getByIds`, and actions on read resources, such as sign-in `confirmCompromised` or `dismiss`. If a feature seems to need write access, stop and ask.
2. **No secrets in the repo.** No tenant IDs, client IDs, certificates, passwords, or tokens in code, tests, or committed config. Client secrets are not a supported auth method; auth uses certificates only. Never log, store or display access tokens. Test tokens are built in the test from fake claims; never commit anything that looks like a real token.
3. **No real tenant in tests.** Unit tests use fake implementations of `IDirectoryReader`. Nothing in the test suite may make a network call.
4. **No telemetry.** No outbound calls other than Microsoft identity and Graph endpoints. Opening a Microsoft Entra admin center page in the user's default browser (for example to add a missing permission) is allowed; the admin makes any change there, never entraspect.
5. **Work from a spec.** Implement the spec in `docs/specs/` you were pointed to, one step at a time. Don't expand scope; add ideas to that spec's "Open questions" section instead.

## Tech stack

- .NET 10, C# latest, nullable enabled, warnings as errors (via `Directory.Build.props`)
- Avalonia (latest stable) with MVVM via CommunityToolkit.Mvvm; `Avalonia.Controls.DataGrid` for tables
- FluentAvaloniaUI for the theme and controls (`FANavigationView`, `FAInfoBar`, `FAInfoBadge`, `FAContentDialog`). Its theme includes the DataGrid styles, so don't add Avalonia's `FluentTheme` or DataGrid theme
- Microsoft.Graph .NET SDK (v5+) and Azure.Identity
- xUnit for tests; `Microsoft.Extensions.TimeProvider.Testing` for date logic

## Solution layout

```
entraspect.slnx
Directory.Build.props
src/Entraspect.Core/          models, checks, IDirectoryReader. No UI, no Graph SDK reference.
src/Entraspect.Graph/         IDirectoryReader implementation + authentication
src/Entraspect.App/           Avalonia UI (views, view models)
tests/Entraspect.Core.Tests/   check tests against fake IDirectoryReader
tests/Entraspect.Graph.Tests/  pure mapping/helper tests only; never create a Graph client or credential
tests/Entraspect.App.Tests/    view model, settings and profile store tests, using a temporary folder and fakes
docs/specs/                   numbered feature specs
```

Dependency direction: App → Graph → Core, and App → Core. Core references nothing else in the solution and has no Graph SDK dependency.

## Core concepts

- **`IDirectoryReader`**: async read methods returning simple Core-owned models (e.g. `AppRegistration`, `AppCredential`). Entraspect.Graph is the only place Graph is touched.
- **`ICheck`**: one hygiene check. Has `Id` (stable, kebab-case), `Title`, `Description`, `Category` (a `CheckCategory`: the navigation section it appears in), `RequiredPermissions`, `Labels` (a `FindingLabels` naming the findings grid columns after the object's properties, for example "Display name" and "Object ID"; a null label hides that column), and `Task<IReadOnlyList<Finding>> RunAsync(IDirectoryReader reader, CheckOptions options, CancellationToken ct)`. A check that reads a report Microsoft refreshes on its own schedule also implements `GetDataUpdatedAsync` (default null) so its page shows when the report was last updated.
- **`Finding`**: `CheckId`, `Severity` (Info / Warning / Critical), `ObjectType`, `ObjectId`, `ObjectDisplayName`, `Message`, optional `DueDate`, optional `UserPrincipalName` (findings about users), optional `SubObjectType` / `SubObjectDisplayName` (the item inside the object, e.g. a credential inside an app registration). Never combine two properties in one field, such as a display name with a UPN: a display name is for presentation and can repeat, while IDs and UPNs are unique. Give each its own field and column.
- **`CheckCatalog`**: the single list of every check, in navigation order within each category. Register a new check here and nowhere else; the navigation, Settings > Checks and Scan pick it up without UI changes.
- **`ScanRunner`**: runs the enabled checks one after another and returns one `CheckResult` per check. A check missing a permission isn't run (`MissingPermissions`); a `DirectoryReadException` fails only that check, keeping its `StatusCode` and Graph `ErrorCode` in the `CheckResult`; cancellation marks the rest `Cancelled`.
- **`CachingDirectoryReader`**: wraps the reader for one scan so each collection is read once. Every new `IDirectoryReader` method must be added here too.
- **`PermissionReview`**: compares granted permissions with every check's `RequiredPermissions`: missing ones (with the checks they block), and unused ones split into read and write.
- **`CheckOptions`**: shared check settings (the expiry windows), validated by `CheckOptions.Validate` and `Normalize`. Add new options with their own rule.
- Checks get "now" from an injected `TimeProvider`, never `DateTime.Now`, so date logic is testable.

## Authentication

App-only only, via Azure.Identity `ClientCertificateCredential`, scope `https://graph.microsoft.com/.default`: tenant ID, client ID, and a certificate loaded from either a .pfx file (password prompted at runtime, never stored) or the OS certificate store by thumbprint. Interactive (delegated) sign-in is not supported. App-only means the granted permissions are exactly the app registration's application permissions, so entraspect can check them.

- `DirectoryConnector.ConnectAsync` gets a token at connect time and returns a Core-owned `DirectoryConnection` (reader, client ID, granted permissions). Graph SDK and Azure.Identity types never leave Entraspect.Graph.
- Granted permissions come from the access token's `roles` claim, decoded locally at connect. Checks declare the application permissions they need in `RequiredPermissions`; a check whose permissions aren't all granted isn't run, and the UI shows which permission is missing.
- entraspect never grants or consents permissions itself. It can link to the app registration's API permissions page in the Entra admin center.
- Warn when the app registration holds permissions entraspect doesn't need, especially write permissions.

Connection profiles (name, tenant ID, client ID, cert path or thumbprint; never passwords) are saved as `profiles.json`, and `AppSettings` (disabled checks, `CheckOptions`, last used profile, time zone preference) as `settings.json`, in an `entraspect` folder in the user's local application data folder, never in the repo.

## Target tenants

Design checks for tenants with Entra ID P1 or P2. When Graph says the tenant lacks the licence a check needs, that check fails with a plain message and the others still run: `CheckFailureMessage` maps the Graph error code (for example `Authentication_RequestFromNonPremiumTenantOrB2CTenant`) to "This check needs an Entra ID P1 or P2 licence." Add any new licence error code there. Prefer Graph v1.0. Beta endpoints are allowed when the data isn't available in v1.0, under these rules:

- The spec names each beta endpoint and why v1.0 isn't enough. Don't switch a v1.0 call to beta on your own.
- The read-only rules apply to beta exactly as to v1.0.
- Call beta through the existing Graph client's request adapter with the `https://graph.microsoft.com/beta` base URL, and map the JSON into internal types in Entraspect.Graph. Don't add the `Microsoft.Graph.Beta` package unless a spec says so.
- Beta can change without notice: map defensively (missing or unknown values don't throw), and a beta failure fails only the check that uses it.
- Check pages for checks that use beta data say so.

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
- Permissions in specs, docs and `RequiredPermissions` are Microsoft Graph application permissions. Before adding one, confirm in the Graph docs that it is the least-privileged application permission for the endpoint and that it is a read permission.
- Commit after each completed spec step: `spec-NNN step N: <summary>`.

## UI conventions

- Layout: navigation on the left, the page on the right, no header. Connection status and Connect, Reconnect and Disconnect live on Settings > Connection profiles; the scan bar (Scan, Cancel, progress) sits on Overview and every check page.
- View models reach the window (dialogs, clipboard, browser) only through `IUserInteraction`, so tests can fake it.
- Keep pages light on text. Put descriptions and explainers in an info icon (`InfoTip`) next to the title, heading or field they explain, not in body text. Short status and error messages stay visible.
- A check's `Title` is its navigation label: at most `CheckCatalog.MaxTitleLength` (26) characters. Don't repeat the section name; "Expiring credentials" sits under App registrations.
- Every date or time shown goes through `TimeDisplay`, which follows the UTC or local time choice in Settings > Preferences.
- Info is always blue (the app overrides FluentAvalonia's accent-derived Attention colour); don't use the accent colour to mean info.
- Check pages filter like the Entra admin center: Search plus Add filter (property, then value). Don't add per-page filter controls.
- Required permissions are shown only on Settings > Checks, as labels in each check's card: green when granted, red when missing. A check page doesn't list them; it shows the permission banner when any are missing.
