# Spec 005: Users without MFA registered

**Status:** Ready (depends on spec 002)
**Goal:** A check in the Users nav section that lists users who can't do multifactor authentication, because they haven't registered a method that policy allows.

This check answers "who isn't registered for MFA". Spec 008 answers the separate question "who isn't required to use MFA". The check page description must say which question this check answers.

**Out of scope for this spec:** whether policy requires MFA (spec 008), per-method strength analysis, suppressing specific accounts (see spec 002 Open questions).

**Graph permissions:** adds `AuditLog.Read.All` (application). The registration details report needs an Entra ID P1 or P2 licence.

Follow AGENTS.md. Complete one step at a time, make sure `dotnet build` and `dotnet test` pass, then commit before starting the next step.

## Step 1: Error codes on read failures

- Add an optional `ErrorCode` to `DirectoryReadException`, filled from the Graph OData error code.
- The UI message for a failed check: if the error code says the tenant lacks a premium licence (for example `Authentication_RequestFromNonPremiumTenantOrB2CTenant`), say "This check needs an Entra ID P1 or P2 licence." Otherwise show the Graph error as usual.
- Tests for the mapping from error code to message.

## Step 2: Core model and reader

- Model `UserRegistration`: `Id`, `UserPrincipalName`, `DisplayName`, `UserType` (`Member` or `Guest`), `IsAdmin`, `IsMfaRegistered`, `IsMfaCapable`, `MethodsRegistered`, `LastUpdatedDateTime`.
- `IDirectoryReader.GetUserRegistrationsAsync(CancellationToken)`, added to `CachingDirectoryReader` too.

## Step 3: The check

**`users-mfa-not-registered`** (category `Users`, title "MFA not registered"), evaluated per user:

- `IsMfaCapable` true: no finding.
- Admin (`IsAdmin` true): **Critical**.
- Member: **Warning**.
- Guest: **Info**, with "Guests may do MFA in their home tenant, depending on your cross-tenant access settings." added to the message.
- Message: "Not registered for MFA." when `IsMfaRegistered` is false, or "Registered MFA methods are not allowed by the current policy." when it is true.
- `ObjectType` `User`, `ObjectDisplayName` "Display name (UPN)", no sub-object, no `DueDate`. Required permission: `AuditLog.Read.All`.

Tests: admin, member, guest, registered but not capable, capable (no finding), and a mix.

## Step 4: Graph implementation

- `GET /reports/authenticationMethods/userRegistrationDetails?$filter=isMfaCapable eq false`, following every page. Keep the check's own `IsMfaCapable` test so the check stays correct with a fake reader.
- Keep mapping in small pure methods with unit tests using sample Graph `UserRegistrationDetails` objects.
- On the check page, show the most recent `LastUpdatedDateTime` from the report, so admins know how fresh the data is.

## Step 5: Docs

- `docs/setup.md`: add `AuditLog.Read.All` (application) with admin consent, and the licence note.
- `README.md`: add the check to "What it checks".

## Acceptance criteria

- `dotnet build` and `dotnet test` pass with no warnings.
- No test makes a network call.
- Only permissions from earlier specs plus `AuditLog.Read.All` are requested or documented.
- Manual check (P1 or P2 tenant): a test user with no MFA methods appears as Warning; after they register the Authenticator app and the report refreshes, they no longer appear. The count roughly matches the portal's Authentication methods > User registration details view filtered to "Not MFA capable".
- On a tenant without P1 or P2, the check fails with the licence message and the other checks still run.

## Open questions

(Agents: add anything you think is out of scope or ambiguous here instead of implementing it.)

- **Disabled users:** the Graph docs say this report "doesn't work for disabled users". Confirm whether disabled users are left out of the results. If they appear, filtering them needs `User.Read.All` (added in spec 007).
- **Break-glass accounts:** emergency access accounts may deliberately have only a FIDO2 key or no method. They need the suppression feature from spec 002 Open questions.
- **Service accounts and synced accounts:** no special handling. Should synced accounts or accounts with no sign-in activity be treated differently?
