# Spec 002: Navigation shell, app-only auth and permission validation

**Status:** Ready
**Goal:** Replace the single-check connect-then-results UI with a portal-style shell: a left navigation panel grouped by object type (Users, Groups, App registrations, Enterprise apps, Conditional Access), an Overview page, a header showing the connection, and Settings at the bottom. The app starts on Settings, and the other pages unlock once connected. One Scan button runs every enabled check. entraspect drops interactive sign-in and supports app-only certificate auth only, so it can check the granted permissions exactly and warn about missing ones. Adding a check in a later spec should make it appear in the nav without UI changes.

**Out of scope for this spec:** new checks (specs 003 onwards), export (spec 010), an app icon, adding or consenting permissions from inside entraspect.

**Graph permissions:** no new permissions. `Application.Read.All` (application) is still the only one in use.

Follow AGENTS.md. Complete one step at a time, make sure `dotnet build` and `dotnet test` pass, then commit before starting the next step.

## Step 1: Check framework in Core

- Add a `CheckCategory` enum: `Users`, `Groups`, `AppRegistrations`, `EnterpriseApps`, `ConditionalAccess`. The order of the enum is the order of the nav sections.
- Add `Category` to `ICheck`. `app-credential-expiry` is `AppRegistrations`. `RequiredPermissions` lists Microsoft Graph application permissions.
- Add `CheckCatalog`: the single list of every check in the app, in nav order within each category. Later specs register new checks here and nowhere else.
- Add `ScanRunner`. It takes the reader, the enabled checks, the granted permissions, `CheckOptions`, an `IProgress<ScanProgress>` and a `CancellationToken`, and returns one `CheckResult` per check.
  - `CheckResult`: `CheckId`, `Status` (`Succeeded`, `Failed`, `MissingPermissions`, `Cancelled`), `Findings`, `MissingPermissions`, `ErrorMessage`, `StatusCode`, `CompletedAt` (from the injected `TimeProvider`).
  - A check whose `RequiredPermissions` aren't all granted doesn't run; its status is `MissingPermissions`, listing the missing ones.
  - Checks run one after another, in catalog order.
  - A `DirectoryReadException` from one check marks that check `Failed` and the scan continues with the next check. Cancellation stops the scan; the running check and any not yet run are `Cancelled`.
  - `ScanProgress` reports the check being run and its position, for example "Running check 2 of 5: Expiring credentials".
- Add `CachingDirectoryReader`, an `IDirectoryReader` decorator that fetches each collection at most once per scan, so two checks that read the same data cause one Graph read. A new instance is created for each scan. A failed read is not cached. Every later spec that adds an `IDirectoryReader` method must add it here too.
- Add `PermissionReview`, a pure Core function comparing the granted permissions with every check's `RequiredPermissions`. It returns: missing permissions (with the checks each one blocks), and granted permissions that no check uses, split into read permissions and write permissions (any permission whose name contains `ReadWrite`, or that is otherwise not read-only, such as `Mail.Send`).
- Tests: runner order, missing permissions skip a check, one failing check doesn't stop the rest, cancellation, progress reports, the caching reader calls the inner reader once for repeated calls, and `PermissionReview` for missing, unused and write permissions.

## Step 2: App-only only, and granted permissions at connect

- Remove interactive sign-in: `InteractiveBrowserCredential`, the `Interactive` auth mode and the redirect URI. A profile has name, tenant ID, client ID, and either a .pfx path or a thumbprint.
- Loading `profiles.json`: ignore the old `AuthMode` field. A profile with no certificate path or thumbprint (an old interactive profile) still loads, and shows "Add a certificate to use this profile" until it is edited.
- Connecting now gets a token immediately, instead of on the first scan (resolves the spec 001 open question "Sign-in happens at Run Scan"). Sign-in errors appear at connect time.
- Replace `DirectoryReaderFactory.Create` with `DirectoryConnector.ConnectAsync(profile, certificatePassword, cancellationToken)`, returning a Core-owned `DirectoryConnection`: the `IDirectoryReader`, the client ID, and the granted application permissions. Graph SDK and Azure.Identity types still never leave Entraspect.Graph.
- Get the granted permissions from the token's `roles` claim, decoded locally. Only decode the payload; don't validate the signature, since it's only used to compare permissions. Never log, store or show the token. A token with no `roles` claim means no permissions are granted.
- `Reconnect` creates a new credential and token, so permissions granted since the last connect are picked up.
- Sign-in failures: include the inner exception's message after the outer one, so the `AADSTS` code and description appear (resolves the spec 001 open question "Sign-in errors hide the AADSTS code").
- Tests in Entraspect.Graph.Tests for the claim parsing, using tokens built in the test from fake JSON claims. Don't commit anything that looks like a real token. Cover `roles` present, absent, empty, malformed tokens, and base64url padding.

## Step 3: Settings store and App test project

- Add `AppSettings`, saved as `settings.json` next to `profiles.json`: the IDs of disabled checks, `CheckOptions` values, and the last used profile name.
- `ProfileStore` and the new `SettingsStore` take their folder as a constructor argument, with the current app data folder as the default.
- Create `tests/Entraspect.App.Tests` (xUnit) and add it to `entraspect.slnx`. Tests use a temporary folder and cover: saving and loading profiles and settings, loading an old profile with `AuthMode`, the corrupt-file backup for both files, and that `settings.json` and `profiles.json` never contain a password (resolves the spec 001 open question "No App test project").
- Validate `CheckOptions` on load and when edited in Settings: `0 <= ExpiryCriticalDays <= ExpiryWarningDays`. Invalid values from the file fall back to the defaults (resolves the spec 001 open question "Option validation").

## Step 4: Shell

- Add FluentAvaloniaUI (latest version compatible with the Avalonia version in use; 3.1.0 targets Avalonia 12.1). Replace `FluentTheme` with the FluentAvalonia theme and keep the DataGrid theme working. The app follows the OS light or dark setting.
- Main window: a FluentAvalonia `NavigationView` on the left, with the header across the top of the content area.
- Nav structure:
  - **Overview** at the top.
  - One expandable section per `CheckCategory` that has at least one check in `CheckCatalog`, with one item per check. Sections with no checks are hidden. Sections are collapsed by default except the one holding the selected page.
  - A separator, then **Settings** in the footer, with child items **Connection profiles**, **Checks** and **About**. Use `FooterMenuItems` with `IsSettingsVisible` turned off, so Settings can have children.
- Before connecting, only Settings and its children are selectable. Overview and every check item are shown but disabled, with a tooltip "Connect to a tenant first".
- The app always starts on Settings > Connection profiles, with the last used profile selected. After a successful connect it opens Overview. Disconnect clears the results, disables the other pages and returns to Connection profiles.
- Each check item shows the finding count from the last scan (FluentAvalonia `InfoBadge` if available, otherwise the count in the label). No badge before the first scan. A check that failed or is missing permissions shows a warning marker instead of a count.
- Header: the connected profile name and tenant ID, connection status ("Not connected" or "Connected"), Connect, Reconnect and Disconnect, and Scan and Cancel buttons with a progress bar and the current `ScanProgress` text. Scan is disabled when not connected.
- If the profile uses a .pfx file, Connect asks for the password in a FluentAvalonia `ContentDialog`. The password is never saved. Reconnect asks again.
- All Graph calls stay off the UI thread and cancellable. Closing the window cancels a running scan.

## Step 5: Pages and permission banners

- **Permission banner** (FluentAvalonia `InfoBar`), shown on a check page when the check is missing permissions, and on Overview when any enabled check is:
  - Text: "This check needs the Microsoft Graph application permission X, which isn't granted to this app registration. Add it and grant admin consent, then reconnect." Overview lists every missing permission with the checks it blocks.
  - **Open in Entra admin center:** opens the app registration's API permissions page for the connected client ID in the default browser. Keep the URL in one constant, since the portal's deep-link format isn't documented. If the deep link stops working it should still land in the admin center.
  - **Copy permission names:** copies the missing names to the clipboard.
  - **Reconnect.**
  - entraspect never adds or consents permissions itself; that would need a write permission. The admin does it in the portal.
- **Excess permissions banner** on Overview and Settings > About, from `PermissionReview`:
  - Granted write permissions: an error-level banner, "This app registration has write permissions entraspect doesn't need: X. Anyone with its certificate can change your tenant. Remove them."
  - Unused read permissions: an informational banner listing them, suggesting removal.
- **Overview:** last scan time; one row per category with Critical, Warning and Info counts; below it, checks that failed or are missing permissions, each with the reason. Selecting a row or check opens that page. Before the first scan, show a short message pointing to the Scan button.
- **Check page** (one reusable view for every check): the permission banner if needed, title, description, required permissions (each marked granted or missing), last run status and error, then the findings grid.
  - Grid columns: Severity, Object, Item, Type, Due (UTC), Message. Item is `SubObjectDisplayName` and Type is `SubObjectType`. Sortable, and sorted by severity then due date by default.
  - A severity filter (Critical, Warning, Info toggles) and a text box that filters on object, item and message.
  - Severity shows as a coloured label that stays readable in light and dark themes.
- **Settings > Connection profiles:** list of saved profiles; add, edit and delete (with a confirmation). Fields: name, tenant ID, client ID, and either a .pfx path or a certificate thumbprint. Connect from here or from the header.
- **Settings > Checks:** one row per check with an enable toggle, its category, its required permissions with granted or missing status (when connected), and the `CheckOptions` fields with validation. Changes are saved immediately. Available before connecting.
- **Settings > About:** app version, a statement that entraspect is read-only, every application permission any check needs (collected from `CheckCatalog`), the excess permissions banner when connected, and the path to the app data folder.
- Remove `ConnectionView`, `ResultsView` and their view models once the new pages cover their features.

## Step 6: Docs

- Rewrite `docs/setup.md` for app-only only: remove the redirect URI and delegated permission steps, and keep the certificate steps. Recommend a dedicated app registration for entraspect with only the permissions it needs. Recommend storing the certificate in the OS certificate store with a non-exportable key where possible, and using short certificate lifetimes, because the certificate gives tenant-wide read access. Explain the permission banner and the Entra admin center button.
- Update `README.md` for the new flow and app-only auth.
- Update `AGENTS.md`: `ICheck.Category`, `CheckCatalog` as the place to register checks, `ScanRunner` and `PermissionReview`, `CachingDirectoryReader` needing each new reader method, FluentAvaloniaUI in the tech stack, and `tests/Entraspect.App.Tests` in the solution layout.

## Acceptance criteria

- `dotnet build` and `dotnet test` pass with no warnings.
- No test makes a network call.
- No new Graph permission is requested or documented, and no delegated permission is documented.
- No reference to `InteractiveBrowserCredential` remains.
- Manual check:
  - The app starts on Connection profiles. Overview and Expiring credentials are disabled until connected.
  - Connect gets a token and opens Overview. A bad client ID shows the `AADSTS` code at connect time.
  - Scan runs the check, fills the badge and the Overview counts, and the findings match the spec 001 results.
  - With `Application.Read.All` removed from the app registration and a reconnect, Expiring credentials shows the permission banner, isn't run by Scan, and Open in Entra admin center opens the app's API permissions page. After adding it back, granting consent and reconnecting, the banner goes away.
  - Granting an unneeded write permission (for example `User.ReadWrite.All`) shows the write permissions banner.
  - Cancel stops a running scan. Disabling the check in Settings > Checks removes it from the next scan and the setting survives a restart.
  - An old interactive profile loads and asks for a certificate.

## Open questions

(Agents: add anything you think is out of scope or ambiguous here instead of implementing it.)

- **Running checks in parallel:** checks run one after another to keep throttling and progress simple. Revisit if scans get slow once the sign-in log check (spec 009) exists.
- **Generic column headers:** the grid uses the same headers (Object, Item, Type) for every check. Should checks provide their own labels, for example "App" and "Credential"?
- **Suppressing findings:** admins will want to hide known and accepted findings, such as break-glass accounts. Needs a design for where suppressions are stored and how they are shown.
- **Remembering results:** results are only in memory and are lost when the app closes. Should the last scan be saved locally?
- **Permission replication delay:** a newly granted permission can take a few minutes to appear in new tokens. Should the banner say so after a Reconnect that still shows it missing?
