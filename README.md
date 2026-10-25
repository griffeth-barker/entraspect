# entraspect

A read-only desktop app that scans a Microsoft Entra ID tenant for things admins may have missed, such as expiring app secrets and certificates.

Status: early development. See `docs/specs/` for the roadmap.

## What it checks

- **MFA not registered** (users): users who can't do multifactor authentication because they haven't registered a method that the current policy allows, including users synced from on-premises Active Directory. It answers "who isn't registered for MFA", not "who isn't required to use MFA". Needs Entra ID P1 or P2. The check page shows when Microsoft last updated the report it reads.
  - **Critical:** an admin.
  - **Warning:** a member.
  - **Info:** a guest. Guests may do MFA in their home tenant, depending on your cross-tenant access settings.
- **Groups with no members** (groups): groups with no direct members, including groups synced from on-premises Active Directory.
  - **Warning:** an assigned group with no members. For a synced group, members outside the sync scope aren't in Entra ID, so cloud policies and assignments that use the group reach no one.
  - **Info:** a dynamic group with no members; its membership rule matches no one.
- **Groups with no owners** (groups): Microsoft 365 and security groups with no owners, including security groups synced from on-premises. Microsoft Graph documents owners as unavailable for distribution and mail-enabled security groups, so those are never reported. Groups converted to cloud management are checked like cloud groups.
  - **Warning:** a Microsoft 365 group with no owners. No one can manage its membership, and it can't be renewed when group expiration is on.
  - **Info:** a security group with no owners. For a synced security group, the owner may be set only in Active Directory (managedBy), which isn't synced to Entra ID.

Both group checks skip groups created in the last 7 days (the new group grace period, set in **Settings** > **Checks**).

- **Expiring credentials** (app registrations): client secrets and certificates on app registrations that have expired or expire soon.
  - **Critical:** expires within 7 days, or already expired with no other valid credential of the same type.
  - **Warning:** expires within 30 days.
  - **Info:** already expired, but the app has another valid credential of the same type. Consider removing the expired one.
- **Apps with no owners** (app registrations): app registrations with no owners, so no one is accountable for their credentials and permissions. Always **Warning**.
- **Risky redirect URIs** (app registrations): one finding per risky redirect URI, with its platform. entraspect only reads the URI text; it never looks up or contacts the URI.
  - **Warning:** a wildcard (`*`) on any platform, or `http://` to a host other than this computer on the Web or Single-page application platform.
  - **Info:** `localhost`, `127.0.0.1` or `[::1]` on the Web or Single-page application platform, usually left over from development, or a URI that isn't valid. Localhost on the Mobile and desktop applications platform is normal for desktop and command-line apps and isn't reported.
- **Expiring SAML certificates** (enterprise apps): the active token signing certificate of enterprise apps that use SAML single sign-on, the one shown as Active in the app's SAML Certificates section.
  - **Critical:** the active certificate expires within 7 days or has already expired. Once it expires, SAML sign-in to the app may fail.
  - **Warning:** the active certificate expires within 30 days, or the app has no active signing certificate.
  - **Info:** an inactive signing certificate has expired; consider removing it. Findings on disabled apps are also Info.
  - When a newer certificate exists but isn't active, the finding says so: make it active and give it to the service provider.
- **High-privilege apps** (enterprise apps): enterprise apps holding high-privilege Microsoft Graph application permissions, one finding per permission, with the date it was granted. Microsoft's own apps are skipped. Delegated permissions and permissions on other APIs aren't checked. entraspect only reads these permission names; it never requests them.
  - **Critical:** a permission that can be used to take over the tenant: `RoleManagement.ReadWrite.Directory`, `AppRoleAssignment.ReadWrite.All`, `Application.ReadWrite.All`, `Directory.ReadWrite.All`, `RoleAssignmentSchedule.ReadWrite.Directory`, `RoleEligibilitySchedule.ReadWrite.Directory`, `PrivilegedAccess.ReadWrite.AzureADGroup`, `UserAuthenticationMethod.ReadWrite.All` or `Policy.ReadWrite.ConditionalAccess`.
  - **Warning:** a permission with tenant-wide data access: `Mail.ReadWrite`, `Mail.Send`, `Files.ReadWrite.All`, `Sites.ReadWrite.All`, `Sites.FullControl.All`, `User.ReadWrite.All`, `Group.ReadWrite.All` or `GroupMember.ReadWrite.All`.
  - **Info:** either kind of permission on a disabled app. It can't be used while the app is disabled, but comes back if the app is enabled again.

Both expiry checks use the same expiry windows, set in **Settings** > **Checks**.

## Read-only by design

entraspect only uses Microsoft Graph read permissions (currently `Application.Read.All`, `GroupMember.Read.All`, `Member.Read.Hidden`, `AuditLog.Read.All` and, optionally, `LicenseAssignment.Read.All`). It never creates, changes or deletes anything in your tenant. It talks only to Microsoft sign-in and Microsoft Graph endpoints and sends no telemetry.

## Setup

entraspect signs in as an app registration in your tenant, using a certificate (app-only). It doesn't support signing in as a user or client secrets. You need:

- A dedicated app registration with the Microsoft Graph application permissions `Application.Read.All`, `GroupMember.Read.All`, `Member.Read.Hidden` and `AuditLog.Read.All`, with admin consent. `LicenseAssignment.Read.All` is optional: with it, entraspect reads your tenant's licences at connect and grays out checks your licence doesn't cover.
- A certificate: the public part uploaded to the app registration, the private key in your operating system's certificate store or a `.pfx` file.

entraspect checks the permissions granted to the app registration when it connects. If a check's permission is missing, that check doesn't run, and entraspect tells you which permission to add and links to the app registration's API permissions page. It also warns if the app registration has permissions it doesn't need, especially write permissions.

Some checks need Microsoft Entra ID P1 or P2 (today, MFA not registered).

See [docs/setup.md](docs/setup.md) for step-by-step instructions and troubleshooting.

## Using entraspect

- **Settings > Connection profiles:** add a profile (tenant ID, client ID, certificate) and connect. entraspect starts here.
- **Overview:** the connected tenant, **Scan**, and Critical, Warning and Info counts for each section.
- **Check pages**, grouped by object type in the navigation (Users, Groups, App registrations and Enterprise apps today; Conditional Access as its checks are added): each check's findings, with Search and Add filter like the Entra admin center.
- **Settings > Checks:** turn checks on or off, see which permissions each one needs, and set the expiry windows. **Settings > Preferences:** show times in UTC or local time.

Results stay in memory and are never written to disk.

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
