# entraspect

A read-only desktop app that scans a Microsoft Entra ID tenant for things admins may have missed, such as expiring app secrets and certificates.

Status: early development. See `docs/specs/` for the roadmap.

## What it checks

- **MFA not registered** (users): users who can't do multifactor authentication because they haven't registered a method that the current policy allows, including users synced from on-premises Active Directory. It answers "who isn't registered for MFA", not "who isn't required to use MFA". Needs Entra ID P1 or P2. The check page shows when Microsoft last updated the report it reads.
  - **Critical:** an admin.
  - **Warning:** a member.
  - **Info:** a guest. Guests may do MFA in their home tenant, depending on your cross-tenant access settings.
- **MFA not required** (users): enabled users, including users synced from on-premises Active Directory, whom no enforced Conditional Access policy requires to use MFA for every app. It answers "who isn't required to use MFA", not "who isn't registered for MFA", and reports what Conditional Access and legacy per-user MFA require, with recent successful single-factor sign-ins as evidence. Per-user MFA state and sign-ins come from beta Microsoft Graph, which can change without notice. Needs Entra ID P1 or P2.
  - **Critical:** a user with an active directory role, directly or through a role-assignable group.
  - **Warning:** a member.
  - **Info:** a guest, or a user with legacy per-user MFA (Microsoft recommends moving to Conditional Access).
  - A user whom policies require to use MFA only in some cases (for example only outside trusted locations, only for some apps, or with "MFA or a compliant device") is one level lower, and the finding names those policies. Report-only policies that would require MFA are named too.
  - Single-factor sign-ins in the last 7 days (the sign-in lookback, set in **Settings** > **Checks**) are counted in the message, with the most recent in its own column.
  - With security defaults on, there is one Info finding and no per-user findings.
- **Groups with no members** (groups): groups with no direct members, including groups synced from on-premises Active Directory.
  - **Warning:** an assigned group with no members. For a synced group, members outside the sync scope aren't in Entra ID, so cloud policies and assignments that use the group reach no one.
  - **Info:** a dynamic group with no members; its membership rule matches no one.
- **Groups with no owners** (groups): Microsoft 365 and security groups with no owners, including security groups synced from on-premises. Microsoft Graph documents owners as unavailable for distribution and mail-enabled security groups, so those are never reported. Groups converted to cloud management are checked like cloud groups.
  - **Warning:** a Microsoft 365 group with no owners. No one can manage its membership, and it can't be renewed when group expiration is on.
  - **Info:** a security group with no owners. For a synced security group, the owner may be set only in Active Directory (managedBy), which isn't synced to Entra ID.
- **Groups with one owner** (groups): Microsoft 365 and security groups with exactly one owner, including security groups synced from on-premises. Always **Info**. If that owner leaves, no one can manage the group. Groups with 20 or more owners are never reported (Graph returns at most 20).

Groups with no members, Groups with no owners and Groups with one owner skip groups created in the last 7 days (the new group grace period, set in **Settings** > **Checks**).

- **Licence errors** (groups): groups that assign licences (group-based licensing) where some members didn't get them. Always **Warning**. Needs Entra ID P1 or P2.
- **Paused dynamic groups** (groups): dynamic groups whose membership processing is paused, so members are no longer added or removed. Always **Warning**.
- **Public groups** (groups): public Microsoft 365 groups, which anyone in the organisation can join and read. Always **Info**.
- **Sync errors** (groups): one finding per on-premises sync error on a group, with the property, the error category and when it occurred. Always **Warning**. Only tenants that sync groups from Active Directory have these.

- **Apps with no owners** (app registrations): app registrations with no owners, so no one is accountable for their credentials and permissions. Always **Warning**. P2P Server, which Entra ID creates for remote desktop to Entra-joined devices, is skipped.
- **Expiring credentials** (app registrations): client secrets and certificates on app registrations that have expired or expire soon.
  - **Critical:** expires within 7 days, or already expired with no other valid credential of the same type.
  - **Warning:** expires within 30 days.
  - **Info:** already expired, but the app has another valid credential of the same type. Consider removing the expired one.
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

- **Policy hygiene** (Conditional Access): policies that are switched off, stuck in report-only mode, can never apply, or name users or groups that no longer exist. One policy can have several findings, one per issue. Needs Entra ID P1 or P2. Each user a policy names is looked up on its own, so the check can take a while in large tenants; the scan bar shows how far it is.
  - **Warning:** a report-only policy that hasn't been changed for 30 days or more (the report-only warning days, set in **Settings** > **Checks**); a policy that includes no users or no apps; a policy whose included groups are all empty; or a policy that includes a deleted user or group.
  - **Info:** a disabled policy, or a policy that excludes a deleted user or group (remove it from the policy). A deleted user still in the recycle bin counts as deleted.
- **Unused policies** (Conditional Access): enabled and report-only policies that didn't apply to any interactive sign-in in the last 7 days (the sign-in lookback, set in **Settings** > **Checks**). It only looks at interactive sign-ins: non-interactive, service principal and managed identity sign-ins aren't read, so a policy aimed only at them looks unused. Policies that target service principals, disabled policies (see Policy hygiene) and policies created during the period are skipped. Needs Entra ID P1 or P2. Every sign-in in the period is read, so the check can take a while in busy tenants; the check page shows the period and how many sign-ins were read.
  - **Warning:** an enabled policy that neither granted nor blocked any sign-in.
  - **Info:** a report-only policy that wouldn't have applied to any sign-in.

- **Cross-tenant defaults** (External Identities): the default cross-tenant access settings, which apply to every Entra organisation you haven't added as a partner. One finding per setting.
  - **Warning:** device claims (compliant or hybrid joined) from every tenant are trusted, invitations are redeemed automatically for every tenant (inbound or outbound), or B2B direct connect is open to every tenant (inbound or outbound). These belong on known partners.
  - **Info:** MFA from every tenant is trusted, or B2B collaboration is open to every tenant (one finding per direction; this is Microsoft's default).
- **Cross-tenant partners** (External Identities): partner organisations' settings worth reviewing, one finding per setting: trusted MFA or device claims, automatic redemption, B2B direct connect, and partners marked as service providers. Always **Info**, since partner settings are deliberate. Settings a partner inherits from the defaults aren't reported here. Partners are shown by tenant ID.
- **External collaboration** (External Identities): the tenant's guest settings.
  - **Critical:** guests have the same access to directory data as members.
  - **Warning:** anyone, guests included, can invite guests (Microsoft's default), or people can join the tenant by verifying an email address at one of its domains, without an invitation. That last setting is set only through Microsoft Graph or PowerShell; older tenants often still have it on.
  - **Info:** guests have limited access to the directory (Microsoft's default), every member can invite guests, or a value entraspect doesn't recognise.

The External Identities checks show each setting's current value in its own column and need no licence.

## Read-only by design

entraspect only uses Microsoft Graph read permissions (currently `Application.Read.All`, `GroupMember.Read.All`, `Member.Read.Hidden`, `AuditLog.Read.All`, `Policy.Read.All`, `User.Read.All`, `RoleManagement.Read.Directory` and, optionally, `LicenseAssignment.Read.All`). It never creates, changes or deletes anything in your tenant. It talks only to Microsoft sign-in and Microsoft Graph endpoints (v1.0, and beta where a check says so) and sends no telemetry.

## Setup

entraspect signs in as an app registration in your tenant, using a certificate (app-only). It doesn't support signing in as a user or client secrets. You need:

- A dedicated app registration with the Microsoft Graph application permissions `Application.Read.All`, `GroupMember.Read.All`, `Member.Read.Hidden`, `AuditLog.Read.All`, `Policy.Read.All`, `User.Read.All` and `RoleManagement.Read.Directory`, with admin consent. `LicenseAssignment.Read.All` is optional: with it, entraspect reads your tenant's licences at connect and grays out checks your licence doesn't cover.
- A certificate: the public part uploaded to the app registration, the private key in your operating system's certificate store or a `.pfx` file.

entraspect checks the permissions granted to the app registration when it connects. If a check's permission is missing, that check doesn't run, and entraspect tells you which permission to add and links to the app registration's API permissions page. It also warns if the app registration has permissions it doesn't need, especially write permissions.

Some checks need Microsoft Entra ID P1 or P2 (today, MFA not registered, MFA not required, Policy hygiene and Unused policies).

See [docs/setup.md](docs/setup.md) for step-by-step instructions and troubleshooting.

## Using entraspect

- **Settings > Connection profiles:** add a profile (tenant ID, client ID, certificate) and connect. entraspect starts here. **Open profiles.json** opens the profiles in your default app for JSON files; after editing and saving, select **Reload**.
- **Overview:** the connected tenant, **Scan**, and Critical, Warning and Info counts for each section.
- **Check pages**, grouped by object type in the navigation (Users, Groups, App registrations, Enterprise apps and Conditional Access): each check's findings, with Search and Add filter like the Entra admin center.
- **Navigation pills:** after a scan, a check with findings shows their count, coloured by the most severe one (red critical, yellow warning, blue info); hover for the count by severity. A gray **!** means the check has no result because it failed or is missing a permission; hover for the reason. A check with no findings shows nothing. A collapsed section shows the total for its checks, and a gray **!** when any of them has no result.
- **Status bar**, along the bottom: what entraspect is doing (connecting, the check being run with a progress bar, or how the last scan ended), with a small **X** to cancel a running scan. On the right: when the check on the page last ran (or the last scan, on Overview) and the connected profile.
- **Settings > Checks:** turn checks on or off, see which permissions each one needs, and set the expiry windows, new group grace period, report-only warning days and sign-in lookback days. **Settings > Preferences:** show dates and times in UTC, your computer's time zone or any other time zone, and choose a light, dark or system theme.
- **Settings > About:** the version, the logo, and where entraspect stores its files. **Open settings.json** and **Reload** work like the profiles pair.
- **Export**, next to **Scan**: save results as CSV (one row per finding, for spreadsheets) or JSON (every check's status and error, and all findings). On **Overview** it saves every finding from the last scan; on a check page, the findings shown after search and filters. Dates in exports are always UTC.

Results stay in memory and are only written to disk when you export them. Exported files contain tenant data (tenant ID, object names and IDs): store them as you would other tenant data, and never commit them to a repository.

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
