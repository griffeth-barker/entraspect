# Spec 012: Licence-gated checks

**Status:** Ready (depends on spec 005)
**Goal:** A check that needs a licence the tenant doesn't have is shown as unavailable instead of failing. Its toggle on Settings > Checks is grayed out with a short note, and Scan skips it. entraspect learns the tenant's licences at connect.

Today (spec 005 step 1) such a check runs, gets a licence error from Graph, and its page shows a red error. That stays as the fallback when entraspect can't tell which licences the tenant has.

**Out of scope for this spec:** licences other than Entra ID P1 and P2, per-user licence assignment (only the tenant matters here), and reporting licence counts or usage.

**Graph permissions:** adds `LicenseAssignment.Read.All` (application). No check requires it, and entraspect works without it, falling back to today's behaviour. The List subscribedSkus docs give it as the least-privileged application permission; higher ones are `Directory.Read.All` and `Organization.Read.All`.

Follow AGENTS.md. Complete one step at a time, make sure `dotnet build` and `dotnet test` pass, then commit before starting the next step.

## Step 1: Core model

- `LicenceRequirement` enum: `None`, `EntraIdP1`, `EntraIdP2`.
- `ICheck.RequiredLicence`, default `None`. `UsersMfaNotRegisteredCheck` returns `EntraIdP1`.
- `TenantLicence` enum: `Unknown`, `Free`, `EntraIdP1`, `EntraIdP2`. A requirement is met when the tenant licence is the same or higher (P2 meets P1). `Unknown` meets every requirement, so checks run and fail as today.
- `DirectoryConnection.TenantLicence`, default `Unknown`.
- A small pure helper in Core that says whether a check is licensed, with tests for every pair.

## Step 2: Scan and permission review

- `ScanRunner` doesn't run a check whose licence requirement isn't met. Its result has a new status `MissingLicence`. The licence is checked before permissions: a check the tenant can't use isn't reported as missing permissions. Cancellation keeps `MissingLicence` for those checks, as it keeps `MissingPermissions`.
- `PermissionReview`: permissions needed only by unlicensed checks aren't reported as missing, and aren't reported as unused either.
- `PermissionReview` knows `LicenseAssignment.Read.All` as a permission entraspect itself uses, so it is never reported as unused. Keep this list in one place in Core, next to `CheckCatalog`.
- Tests: an unlicensed check is skipped with `MissingLicence` and no missing permissions, `Unknown` runs the check, P2 meets P1, and the permission review cases above.

## Step 3: Read the licence at connect

- When the token's `roles` include `LicenseAssignment.Read.All`, `DirectoryConnector.ConnectAsync` reads `GET /v1.0/subscribedSkus` with `$select=capabilityStatus,servicePlans`, following every page.
- The tenant has P2 when any subscription whose `capabilityStatus` is `Enabled` or `Warning` has the service plan `eec0eb4f-6444-4f95-aba0-50c24d67f998` (`AAD_PREMIUM_P2`). Otherwise it has P1 with the service plan `41781fb2-bc02-4b7c-bd55-b576c07bb09d` (`AAD_PREMIUM`). Otherwise `Free`. Match on service plan IDs, not SKU IDs: bundles such as Microsoft 365 E3 and E5 include these plans. A plan whose `provisioningStatus` is `Disabled` doesn't count. The IDs come from Microsoft's "Product names and service plan identifiers for licensing" reference.
- Without the permission, or if the read fails, the licence is `Unknown` and connect still succeeds. Never fail a connect because of this read.
- Keep the mapping in a small pure method with unit tests built from sample `SubscribedSku` objects: standalone P1, P2, a bundle, a suspended subscription, a disabled plan, no premium plan, and missing values.

## Step 4: UI

- **Settings > Checks:** for an unlicensed check, the toggle is grayed out (not clickable) and the card shows a short note, "Needs an Entra ID P1 or P2 licence." The saved on/off choice isn't changed, so the check comes back as it was if the tenant gets the licence.
- **Settings > Checks, licence-gated checks only:** a `LicenseAssignment.Read.All` label next to the required permissions. Green when granted. When missing, neutral (not red, because the check still runs) with an `InfoTip`: "Grant it so entraspect can tell whether your tenant's licence covers this check."
- **Check page:** no permission banner and no red error. The status line says "Not available: needs an Entra ID P1 or P2 licence." Scan isn't clickable on this page.
- **Navigation:** the check's item is grayed out and can't be opened, with the note as its tooltip. No warning badge. After disconnecting it shows "Connect to a tenant first" like the other pages.
- **Overview:** an unlicensed check isn't listed as a problem, and Overview's counts leave it out. Selecting a section opens its first available check.
- **Fallback:** if Graph still returns a licence error (licence `Unknown`, or the detection was wrong), the check fails with the spec 005 licence message as today.
- Tests: view model tests for each point above, using a `DirectoryConnection` with each `TenantLicence`.

## Step 5: Docs

- `docs/setup.md`: add `LicenseAssignment.Read.All` (application, admin consent) as recommended but optional, with what happens without it. Update the licence note in "Before you start" and the troubleshooting row.
- `README.md`: add the permission to the read-only list and the setup list, marked optional.
- `AGENTS.md`: in "Target tenants", say checks declare `RequiredLicence`, the licence is read at connect, and unlicensed checks are skipped and grayed out; `CheckFailureMessage` stays the fallback.

## Acceptance criteria

- `dotnet build` and `dotnet test` pass with no warnings.
- No test makes a network call.
- Only permissions from earlier specs plus `LicenseAssignment.Read.All` are requested or documented.
- Manual check, tenant without P1 or P2, with `LicenseAssignment.Read.All` granted: MFA not registered's toggle is grayed out with the note, its navigation item is grayed out and can't be opened, Scan doesn't run it, and there's no permission banner or warning badge for it, even with `AuditLog.Read.All` missing.
- Same tenant without `LicenseAssignment.Read.All`: behaviour is as in spec 005 (the check runs and shows the licence message), and the check's card shows the neutral `LicenseAssignment.Read.All` label.
- Tenant with P1 or P2: the check runs as in spec 005.

## Open questions

(Agents: add anything you think is out of scope or ambiguous here instead of implementing it.)

- **Trials and grace periods:** a trial counts as long as its `capabilityStatus` is `Enabled`. `Warning` (expired, in its grace period) counts too, because features still work. Confirm.
- **Licence changes mid-connection:** the licence is read once per connect. Reconnect picks up a new licence. Is that enough?
- **Learn from errors:** should a licence error during a scan also mark the check unavailable for the rest of the connection when the licence is `Unknown`? Not in this spec.
- **Spec 008:** "MFA not required" needs P1 for Conditional Access and the sign-in logs. Its spec should set `RequiredLicence` when it is implemented.
