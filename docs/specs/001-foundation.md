# Spec 001: Foundation and first check (expiring app registration credentials)

**Status:** Ready
**Goal:** A working desktop app that connects to a tenant (both auth modes) and lists app registration client secrets and certificates that are expired or expiring soon.

**Out of scope for this spec:** enterprise app SAML signing certificates, Conditional Access, exporting results, scheduled scans.

**Graph permissions:** `Application.Read.All` only (delegated for interactive mode, application for app-only mode).

Follow AGENTS.md. Complete one step at a time, make sure `dotnet build` and `dotnet test` pass, then commit before starting the next step.

## Step 1: Scaffold

- Create `entraspect.slnx` and the four projects from the AGENTS.md layout. Create the App project from the `avalonia.mvvm` template.
- Add `Directory.Build.props` enabling nullable, latest C#, and `TreatWarningsAsErrors`.
- Wire up project references in the allowed dependency direction only.
- Add one placeholder test so `dotnet test` runs.

## Step 2: Core models and the check

- Models: `AppRegistration` (Id, AppId, DisplayName, Credentials) and `AppCredential` (KeyId, Type = Secret or Certificate, DisplayName, StartDateTime, EndDateTime nullable).
- `IDirectoryReader.GetAppRegistrationsAsync(CancellationToken)`.
- `Finding`, `Severity`, `ICheck`, and `CheckOptions` (`ExpiryWarningDays` default 30, `ExpiryCriticalDays` default 7).
- Check `app-credential-expiry`, evaluated per credential:
  - No `EndDateTime`: no finding.
  - Already expired, and the same app has another credential of the same type that is still valid: **Info**, "Expired N days ago; a newer credential exists, consider removing this one."
  - Already expired with no valid replacement: **Critical**, "Expired N days ago."
  - Expires within `ExpiryCriticalDays` (inclusive): **Critical**.
  - Expires within `ExpiryWarningDays` (inclusive): **Warning**.
  - Otherwise: no finding.
  - `DueDate` is the credential's `EndDateTime`. Messages include the credential type and display name.

## Step 3: Tests for the check

Using a fake `IDirectoryReader` and `FakeTimeProvider`, cover at least:
expired with no replacement; expired with a valid replacement (Info); expired secret with only a valid *certificate* (still Critical, since the type differs); within critical window; within warning window; exactly on each boundary; beyond the warning window; no end date; multiple apps with mixed results.

## Step 4: Graph implementation

- `GraphDirectoryReader : IDirectoryReader` calling `GET /applications` with `$select=id,appId,displayName,passwordCredentials,keyCredentials`, following every page, mapping to Core models.
- `ConnectionProfile` model and an auth factory that returns a `TokenCredential` for either mode as described in AGENTS.md.
- No tests that hit the network; keep mapping logic in small pure methods so it could be unit tested with sample objects.

## Step 5: UI

- **Connection view:** profile name, tenant ID, client ID, auth mode choice, and for app-only either a .pfx path (with a password box that is never saved) or a certificate thumbprint. Save and load profiles as JSON in the app data folder. Connect button.
- **Results view:** Run Scan button with a progress indicator; a sortable DataGrid with columns Severity, App, Credential, Type, Expires, Message; a summary of counts per severity; plain error text on failure (for 403, mention `Application.Read.All`).
- Keep all Graph calls off the UI thread and cancellable.

## Step 6: README setup guide

Explain how to create the app registration for each mode: platform "Mobile and desktop applications" with redirect URI `http://localhost` for interactive; certificate upload for app-only; which permission to add and that admin consent is required; how to run the app.

## Acceptance criteria

- `dotnet build` and `dotnet test` pass with no warnings.
- No test makes a network call.
- The only Graph permission requested or documented is `Application.Read.All`.
- Manual check: connecting to a real tenant in both modes lists expiring credentials correctly.

## Open questions

(Agents: add anything you think is out of scope or ambiguous here instead of implementing it.)

- **Day counting (decided in step 2 review, confirm):** "within N days (inclusive)" is measured in whole UTC calendar days between today and the credential's end date, not elapsed hours. The same day count drives both severity and the message. "Expired" still means `EndDateTime` is before the current instant, so a credential ending later today is "expires today" (Critical) and one that ended earlier today is "expired today".
- **Credentials with no end date as replacements:** a credential with no `EndDateTime` never expires, but it is currently not counted as a valid replacement for an expired credential of the same type. Should it be?
- **Not-yet-active credentials as replacements:** a credential whose `StartDateTime` is in the future currently counts as a valid replacement even though it cannot be used yet. Should it?
- **"Newer" wording:** the Info message says "a newer credential exists", but the rule only requires another still-valid credential of the same type, not a newer one.
- **Option validation:** nothing enforces `0 <= ExpiryCriticalDays <= ExpiryWarningDays`.
- **Null credential display names (decided in step 4 review, confirm):** Graph often returns null `displayName` for credentials. Mapping falls back to the credential's `KeyId` so messages stay identifiable.
- **Graph types stay in Entraspect.Graph (decided in step 4 review):** App gets an `IDirectoryReader` from `DirectoryReaderFactory.Create(profile, password)` and never sees `GraphServiceClient` or `TokenCredential`. Graph and sign-in failures surface as Core's `DirectoryReadException`, whose `StatusCode` lets the UI name `Application.Read.All` on a 403.
- **Windows .pfx loading untested:** app-only .pfx loading uses `EphemeralKeySet` on Windows and Linux and `DefaultKeySet` on macOS (verified on macOS only). Confirm `ClientCertificateCredential` signs correctly with an ephemeral key on Windows during the manual acceptance check.
- **Test tooling updates:** `coverlet.collector` 10.x, `Microsoft.NET.Test.Sdk` 18.x and `xunit.runner.visualstudio` 4.x are available. They are major-version bumps (the runner may require xUnit v3), so they are left for a separate change.
