# Spec 010: Export results

**Status:** Ready (depends on spec 002)
**Goal:** Save scan results to a CSV or JSON file, from a single check page or for the whole scan.

**Out of scope for this spec:** scheduled or unattended scans, sending results anywhere (email, webhook, upload), HTML or PDF reports, comparing two scans.

**Graph permissions:** none. Export only writes a local file chosen by the user. No network calls.

Follow AGENTS.md. Complete one step at a time, make sure `dotnet build` and `dotnet test` pass, then commit before starting the next step.

## Step 1: Exporters in Core

- `ScanReport`: scan time (UTC), profile name, tenant ID, app version, and the `CheckResult`s (spec 002) with each check's ID, title and category.
- `CsvFindingExporter` and `JsonScanExporter`, writing to a `Stream`, with no UI or file dialog code.
- CSV: one row per finding. Columns: `CheckId`, `CheckTitle`, `Category`, `Severity`, `ObjectType`, `ObjectId`, `ObjectDisplayName`, `SubObjectType`, `SubObjectDisplayName`, `DueDate`, `Message`. UTF-8 with a byte order mark so Excel reads it correctly. RFC 4180 quoting.
- CSV formula injection: display names come from the tenant and could contain formulas. Prefix any cell that starts with `=`, `+`, `-`, `@`, a tab or a carriage return with a single quote (`'`).
- JSON: the `ScanReport` metadata, each check's status and error, and all findings. Dates are ISO 8601 UTC. Enums are strings.
- Tests: quoting (commas, quotes, new lines), the formula-injection prefix, empty optional fields, and a JSON round trip.

## Step 2: UI

- An Export button on each check page exports the findings currently shown, with the severity and text filters applied.
- An Export button on Overview exports every finding from the last scan.
- The user picks CSV or JSON and the location in the platform save dialog (Avalonia `StorageProvider`). Default file name: `entraspect-<profile>-<yyyyMMdd-HHmm>.csv` or `.json`, with characters that aren't valid in file names removed from the profile name.
- Export is disabled until a scan has finished. Write errors are shown plainly on the page.
- Tests in `Entraspect.App.Tests` for the default file name.

## Step 3: Docs

- `README.md`: mention export.
- `docs/setup.md` or README: warn that exported files contain tenant data (tenant ID, object names and IDs) and should be stored accordingly, and never committed to a repository.

## Acceptance criteria

- `dotnet build` and `dotnet test` pass with no warnings.
- No test makes a network call.
- No Graph permission is added.
- Manual check: a CSV export from Overview opens in Excel with correct columns and non-ASCII names intact. A JSON export includes a failed check with its error. A display name starting with `=` shows as text in Excel.

## Open questions

(Agents: add anything you think is out of scope or ambiguous here instead of implementing it.)

- **Tenant display name:** the report only has the tenant ID. Reading the tenant name needs `GET /organization`, which may need another permission for app-only mode.
- **Export without a scan:** should Settings > Checks be exportable as a list of checks and required permissions, for a change request to get consent?
