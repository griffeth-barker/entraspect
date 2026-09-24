# entraspect

A read-only desktop app that scans a Microsoft Entra ID tenant for things admins may have missed, such as expiring app secrets and certificates.

Status: early development. See `docs/specs/` for the roadmap.

## What it checks

- **Expiring app registration credentials:** client secrets and certificates on app registrations that have expired or expire soon.
  - **Critical:** expires within 7 days, or already expired with no other valid credential of the same type.
  - **Warning:** expires within 30 days.
  - **Info:** already expired, but the app has another valid credential of the same type. Consider removing the expired one.

## Read-only by design

entraspect only uses the Microsoft Graph permission `Application.Read.All`. It never creates, changes or deletes anything in your tenant. It talks only to Microsoft sign-in and Microsoft Graph endpoints and sends no telemetry.

## Setup

entraspect connects through an app registration in your tenant. It supports two sign-in modes:

- **Interactive:** you sign in in a browser. Needs a **Mobile and desktop applications** redirect URI of `http://localhost` and the delegated `Application.Read.All` permission.
- **App-only:** entraspect signs in with a certificate. Needs an uploaded certificate and the application `Application.Read.All` permission. Client secrets are not supported.

Both modes need admin consent. See [docs/setup.md](docs/setup.md) for step-by-step instructions and troubleshooting.

## Running

entraspect runs on Windows, macOS and Linux. Building from source needs the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```sh
dotnet run --project src/Entraspect.App
```

## Development

```sh
dotnet build
dotnet test
```

Tests use fake data and never connect to a tenant. See `AGENTS.md` for project rules and `docs/specs/` for feature specs.
