# Spec 001: Foundation and first check (expiring app registration credentials)

**Status:** Ready
**Goal:** A working desktop app that connects to a tenant (both auth modes) and lists app registration client secrets and certificates that are expired or expiring soon.

**Out of scope for this spec:** enterprise app SAML signing certificates, Conditional Access, exporting results, scheduled scans.

**Graph permissions:** `Application.Read.All` only (delegated for interactive mode, application for app-only mode).

Follow AGENTS.md. Complete one step at a time, make sure `dotnet build` and `dotnet test` pass, then commit before starting the next step.

## Step 1: Scaffold

- Create `entraspect.sln` and the four projects from the AGENTS.md layout. Create the App project from the `avalonia.mvvm` template.
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
