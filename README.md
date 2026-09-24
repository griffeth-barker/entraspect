# entraspect

A read-only desktop app that scans a Microsoft Entra ID tenant for things admins may have missed, such as expiring app secrets and certificates.

Status: early development. See `docs/specs/` for the roadmap.

## What it checks

- **Expiring app registration credentials:** client secrets and certificates on app registrations that have expired or expire soon.
  - **Critical:** expires within 7 days, or already expired with no other valid credential of the same type.
  - **Warning:** expires within 30 days.
  - **Info:** already expired, but the app has another valid credential of the same type. Consider removing the expired one.

## Read-only by design

entraspect only uses Microsoft Graph read permissions (currently `Application.Read.All`). It never creates, changes or deletes anything in your tenant. It talks only to Microsoft sign-in and Microsoft Graph endpoints and sends no telemetry.

## Setup

entraspect signs in as an app registration in your tenant, using a certificate (app-only). It doesn't support signing in as a user or client secrets. You need:

- A dedicated app registration with the Microsoft Graph application permission `Application.Read.All` and admin consent.
- A certificate: the public part uploaded to the app registration, the private key in your operating system's certificate store or a `.pfx` file.

entraspect checks the permissions granted to the app registration when it connects. If a check's permission is missing, that check doesn't run and its page tells you which permission to add.

Some checks need Microsoft Entra ID P1 or P2.

See [docs/setup.md](docs/setup.md) for step-by-step instructions and troubleshooting.

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
