<p align="center">
  <img src="assets/entraspect-icon-with-lettermark-cropped.png" alt="entraspect" width="560">
</p>

<p align="center">
  A read-only desktop app that scans a Microsoft Entra ID tenant for the hygiene issues admins miss.
</p>

entraspect connects to your tenant as an app registration with read-only Microsoft Graph permissions, runs 30 checks across users, groups, apps, roles, Conditional Access and External Identities, and shows what it finds by severity. Examples: app secrets and SAML signing certificates about to expire, admins without phishing-resistant MFA, Conditional Access policies that never apply, and owners of privileged role-assignable groups.

> **Status:** early development. Expect rough edges and breaking changes. There are no pre-built releases yet; build from source (see [Getting started](#getting-started)).

## Highlights

- **Read-only.** entraspect uses only Microsoft Graph read permissions and never creates, changes or deletes anything in your tenant. It warns you if its app registration holds permissions it doesn't need, especially write permissions.
- **App-only, certificate-based sign-in.** No user sign-in, no client secrets. The certificate stays in your operating system's certificate store or a `.pfx` file whose password is asked for on every connect and never saved.
- **Stays on your computer.** No telemetry, no backend: entraspect talks only to Microsoft sign-in and Microsoft Graph. Results stay in memory until you export them.
- **Hybrid aware.** Users, groups and admins synced from on-premises Active Directory are checked too, as far as Graph allows, and labelled as synced.
- **Licence aware.** Checks that need Entra ID P1 or P2 are grayed out on tenants without that licence, instead of failing.
- **Tells you what's missing.** A check whose permission isn't granted doesn't run; entraspect names the permission and links to the app registration's API permissions page in the Entra admin center.
- **Cross-platform.** Windows, macOS and Linux, built with .NET 10 and Avalonia.

## Checks

Every finding is **Critical**, **Warning** or **Info**. Expand a section for how each check decides the severity.

| Section | Check | What it finds | Licence |
| --- | --- | --- | --- |
| Users | Disabled users with access | Disabled members who still hold a directory role or have licences assigned | |
| Users | Guests with admin roles | Guests holding an active directory role | |
| Users | Inactive guests | Guests with no successful sign-in for 90 days or more | P1 |
| Users | Inactive users | Members with no successful sign-in for 90 days or more | P1 |
| Users | MFA not registered | Users who haven't registered a method they can use for MFA | P1 |
| Users | MFA not required | Enabled users whom no enforced Conditional Access policy requires to use MFA | P1 |
| Users | Pending guest invitations | Guests who haven't accepted their invitation after 30 days | |
| Users | Risky users | Users that ID Protection rates at risk, or that an admin confirmed compromised | P2 |
| Groups | Groups with no members | Groups with no direct members | |
| Groups | Groups with no owners | Microsoft 365 and security groups with no owners | |
| Groups | Groups with one owner | Microsoft 365 and security groups with exactly one owner | |
| Groups | Licence errors | Group-based licensing that failed for some members | P1 |
| Groups | Paused dynamic groups | Dynamic groups whose membership processing is paused | |
| Groups | Public groups | Public Microsoft 365 groups | |
| Groups | Sync errors | On-premises sync errors on groups | |
| App registrations | Apps with no owners | App registrations no one is accountable for | |
| App registrations | Expiring credentials | Client secrets and certificates that have expired or expire soon | |
| App registrations | Risky redirect URIs | Wildcard, plain `http://` and leftover `localhost` redirect URIs | |
| Enterprise apps | Expiring SAML certificates | SAML token signing certificates that have expired or expire soon | |
| Enterprise apps | High-privilege apps | Apps holding Microsoft Graph application permissions that can take over the tenant or reach tenant-wide data | |
| Roles & admins | Admin role counts | Too many or too few Global Administrators, and too many privileged role assignments | |
| Roles & admins | Licensed admins | Admins whose account has licences assigned, instead of a separate unlicensed admin account | |
| Roles & admins | No phishing-resistant MFA | Admins without a passkey, security key, Windows Hello, Platform Credential or certificate | P1 |
| Roles & admins | Role group owners | Owners of role-assignable groups that hold a privileged role | |
| Roles & admins | Synced admins | Admins whose account is synced from on-premises Active Directory | |
| Conditional Access | Policy hygiene | Policies that are off, stuck in report-only, can never apply, or name deleted users or groups | P1 |
| Conditional Access | Unused policies | Policies that didn't apply to any interactive sign-in in the last 7 days | P1 |
| External Identities | Cross-tenant defaults | Default cross-tenant access settings that trust or open up every tenant | |
| External Identities | Cross-tenant partners | Partner organisations' trust and access settings worth reviewing | |
| External Identities | External collaboration | Guest access and invitation settings | |

P1 means Microsoft Entra ID P1 or P2; P2 means P2. Thresholds such as the expiry windows, inactivity days and sign-in lookback are set in **Settings** > **Checks**.

<details>
<summary>Users: severities and details</summary>

- **Disabled users with access**: disabled members who still hold a directory role (active, at any scope, directly or through a role-assignable group) or have licences assigned. Re-enabling the account restores that access at once. One finding per role, and one for licences, with the access and the source (Cloud or Synced) in their own columns. Messages about users synced from on-premises Active Directory say that the account is managed there.
  - **Critical:** a Tier 0 or privileged role (the built-in roles Microsoft marks PRIVILEGED).
  - **Warning:** any other role.
  - **Info:** licences assigned.
- **Guests with admin roles**: guests holding an active directory role, built-in or custom, at any scope, directly or through a role-assignable group. Always **Critical**: a guest's identity is managed by another organisation. One finding per guest and role, with the role in its own column. Eligible (PIM) assignments aren't included.
- **Inactive guests**: guests, invited or synced from on-premises, with no successful sign-in for at least 90 days (the inactive guest days, set in **Settings** > **Checks**, from 30 to 365). Microsoft started recording successful sign-ins in December 2023, so for a guest without one the latest sign-in attempt counts, and the message says so. The last sign-in and the invitation state (Accepted or Pending) have their own columns. Needs Entra ID P1 or P2.
  - **Warning:** an enabled guest with no recent sign-in, or one who never signed in and was added at least that long ago.
  - **Info:** a disabled guest that would be reported (consider deleting it), or a guest who never signed in whose creation date is unknown.
- **Inactive users**: members, cloud-only or synced from on-premises, with no successful sign-in for at least 90 days (the inactive user days, set in **Settings** > **Checks**, from 30 to 365, separate from the guest setting). As for guests, the latest sign-in attempt counts for a user without a recorded successful sign-in. The last sign-in and the source (Cloud or Synced) have their own columns. Needs Entra ID P1 or P2.
  - **Critical:** an inactive user holding a Tier 0 or privileged role; the message names the role.
  - **Warning:** an enabled member with no recent sign-in, or one who never signed in and was created at least that long ago.
  - **Info:** a disabled member without a role (consider deleting it; disabled members with a role are under Disabled users with access), or a member who never signed in whose creation date is unknown.
- **MFA not registered**: users who can't do multifactor authentication because they haven't registered a method that the current policy allows, including users synced from on-premises Active Directory. It answers "who isn't registered for MFA", not "who isn't required to use MFA". Needs Entra ID P1 or P2. The check page shows when Microsoft last updated the report it reads.
  - **Critical:** an admin.
  - **Warning:** a member.
  - **Info:** a guest. Guests may do MFA in their home tenant, depending on your cross-tenant access settings.
- **MFA not required**: enabled users, including users synced from on-premises Active Directory, whom no enforced Conditional Access policy requires to use MFA for every app. It answers "who isn't required to use MFA", not "who isn't registered for MFA", and reports what Conditional Access and legacy per-user MFA require, with recent successful single-factor sign-ins as evidence. Per-user MFA state and sign-ins come from beta Microsoft Graph, which can change without notice. Needs Entra ID P1 or P2.
  - **Critical:** a user with an active directory role, directly or through a role-assignable group.
  - **Warning:** a member.
  - **Info:** a guest, or a user with legacy per-user MFA (Microsoft recommends moving to Conditional Access).
  - A user whom policies require to use MFA only in some cases (for example only outside trusted locations, only for some apps, or with "MFA or a compliant device") is one level lower, and the finding names those policies. Report-only policies that would require MFA are named too.
  - Single-factor sign-ins in the last 7 days (the sign-in lookback, set in **Settings** > **Checks**) are counted in the message, with the most recent in its own column.
  - With security defaults on, there is one Info finding and no per-user findings.
- **Pending guest invitations**: guests who haven't accepted their invitation 30 days after it was sent (the pending invitation days, set in **Settings** > **Checks**). Always **Info**: resend the invitation, or delete the guest if it's no longer needed. The invitation date has its own column.
- **Risky users**: members and guests that Microsoft Entra ID Protection rates at risk, or that an admin confirmed compromised; remediated, dismissed and confirmed-safe users aren't reported. The risk level and when the risk was last updated have their own columns. Investigate in the Entra admin center under **ID Protection** > **Risky users**; entraspect never confirms or dismisses risk. Needs Entra ID P2.
  - **Critical:** confirmed compromised, or at risk with a high risk level.
  - **Warning:** at risk with a medium risk level.
  - **Info:** at risk with a low risk level, or with the level hidden.

</details>

<details>
<summary>Groups: severities and details</summary>

- **Groups with no members**: groups with no direct members, including groups synced from on-premises Active Directory.
  - **Warning:** an assigned group with no members. For a synced group, members outside the sync scope aren't in Entra ID, so cloud policies and assignments that use the group reach no one.
  - **Info:** a dynamic group with no members; its membership rule matches no one.
- **Groups with no owners**: Microsoft 365 and security groups with no owners, including security groups synced from on-premises. Microsoft Graph documents owners as unavailable for distribution and mail-enabled security groups, so those are never reported. Groups converted to cloud management are checked like cloud groups.
  - **Warning:** a Microsoft 365 group with no owners. No one can manage its membership, and it can't be renewed when group expiration is on.
  - **Info:** a security group with no owners. For a synced security group, the owner may be set only in Active Directory (managedBy), which isn't synced to Entra ID.
- **Groups with one owner**: Microsoft 365 and security groups with exactly one owner, including security groups synced from on-premises. Always **Info**. If that owner leaves, no one can manage the group. Groups with 20 or more owners are never reported (Graph returns at most 20).

Groups with no members, Groups with no owners and Groups with one owner skip groups created in the last 7 days (the new group grace period, set in **Settings** > **Checks**).

- **Licence errors**: groups that assign licences (group-based licensing) where some members didn't get them. Always **Warning**. Needs Entra ID P1 or P2.
- **Paused dynamic groups**: dynamic groups whose membership processing is paused, so members are no longer added or removed. Always **Warning**.
- **Public groups**: public Microsoft 365 groups, which anyone in the organisation can join and read. Always **Info**.
- **Sync errors**: one finding per on-premises sync error on a group, with the property, the error category and when it occurred. Always **Warning**. Only tenants that sync groups from Active Directory have these.

</details>

<details>
<summary>App registrations and enterprise apps: severities and details</summary>

- **Apps with no owners**: app registrations with no owners, so no one is accountable for their credentials and permissions. Always **Warning**. P2P Server, which Entra ID creates for remote desktop to Entra-joined devices, is skipped.
- **Expiring credentials**: client secrets and certificates on app registrations that have expired or expire soon.
  - **Critical:** expires within 7 days, or already expired with no other valid credential of the same type.
  - **Warning:** expires within 30 days.
  - **Info:** already expired, but the app has another valid credential of the same type. Consider removing the expired one.
- **Risky redirect URIs**: one finding per risky redirect URI, with its platform. entraspect only reads the URI text; it never looks up or contacts the URI.
  - **Warning:** a wildcard (`*`) on any platform, or `http://` to a host other than this computer on the Web or Single-page application platform.
  - **Info:** `localhost`, `127.0.0.1` or `[::1]` on the Web or Single-page application platform, usually left over from development, or a URI that isn't valid. Localhost on the Mobile and desktop applications platform is normal for desktop and command-line apps and isn't reported.
- **Expiring SAML certificates**: the active token signing certificate of enterprise apps that use SAML single sign-on, the one shown as Active in the app's SAML Certificates section.
  - **Critical:** the active certificate expires within 7 days or has already expired. Once it expires, SAML sign-in to the app may fail.
  - **Warning:** the active certificate expires within 30 days, or the app has no active signing certificate.
  - **Info:** an inactive signing certificate has expired; consider removing it. Findings on disabled apps are also Info.
  - When a newer certificate exists but isn't active, the finding says so: make it active and give it to the service provider.
- **High-privilege apps**: enterprise apps holding high-privilege Microsoft Graph application permissions, one finding per permission, with the date it was granted. Microsoft's own apps are skipped. Delegated permissions and permissions on other APIs aren't checked. entraspect only reads these permission names; it never requests them.
  - **Critical:** a permission that can be used to take over the tenant: `RoleManagement.ReadWrite.Directory`, `AppRoleAssignment.ReadWrite.All`, `Application.ReadWrite.All`, `Directory.ReadWrite.All`, `RoleAssignmentSchedule.ReadWrite.Directory`, `RoleEligibilitySchedule.ReadWrite.Directory`, `PrivilegedAccess.ReadWrite.AzureADGroup`, `UserAuthenticationMethod.ReadWrite.All` or `Policy.ReadWrite.ConditionalAccess`.
  - **Warning:** a permission with tenant-wide data access: `Mail.ReadWrite`, `Mail.Send`, `Files.ReadWrite.All`, `Sites.ReadWrite.All`, `Sites.FullControl.All`, `User.ReadWrite.All`, `Group.ReadWrite.All` or `GroupMember.ReadWrite.All`.
  - **Info:** either kind of permission on a disabled app. It can't be used while the app is disabled, but comes back if the app is enabled again.

Both expiry checks use the same expiry windows, set in **Settings** > **Checks**.

</details>

<details>
<summary>Roles & admins: severities and details</summary>

The Roles & admins checks look at admins: users, members and guests, holding at least one Tier 0 role (such as Global Administrator, Privileged Role Administrator or Hybrid Identity Administrator) or another role Microsoft marks privileged. Roles count when active, at any scope, directly or through a role-assignable group; eligible (PIM) assignments aren't included. Each admin's privileged roles have their own column, highest tier first.

- **Admin role counts**: how many users hold Global Administrator, and how many privileged role assignments there are. Users who get a role through a group count once each.
  - **Warning:** 5 or more Global Administrators (Microsoft recommends fewer than five), or fewer than 2 (keep at least two, such as two emergency access accounts).
  - **Info:** 10 or more privileged role assignments (Microsoft recommends fewer than ten).
- **Licensed admins**: admins whose account has licences assigned, with the number in its own column. Always **Info**: use a separate, unlicensed account for admin work, so email and everyday apps aren't used with admin rights.
- **No phishing-resistant MFA**: enabled admins who haven't registered a passkey, FIDO2 security key, Windows Hello for Business, Platform Credential for macOS or certificate-based authentication. **Warning** for Tier 0 admins, **Info** for other admins and for admins missing from the registration report. Admins without any MFA are also reported by MFA not registered. Needs Entra ID P1 or P2. The check page shows when Microsoft last updated the report it reads.
- **Role group owners**: owners of role-assignable groups that hold a Tier 0 or other privileged role, one finding per group and owner. An owner can add anyone to the group, including themselves, and so give them its roles. **Critical** when the group holds a Tier 0 role, **Warning** otherwise. Owners with a direct Global Administrator or Privileged Role Administrator assignment aren't listed, since they can manage every role-assignable group anyway. Only active role assignments to the group count; eligible (PIM) assignments and PIM for Groups aren't included.
- **Synced admins**: admins whose account is synced from on-premises Active Directory, so anyone who takes over that account or the sync server gets their roles. **Critical** for Tier 0 admins, **Warning** for other admins. Use cloud-only accounts for admin work.

</details>

<details>
<summary>Conditional Access: severities and details</summary>

- **Policy hygiene**: policies that are switched off, stuck in report-only mode, can never apply, or name users or groups that no longer exist. One policy can have several findings, one per issue. Needs Entra ID P1 or P2. Each user a policy names is looked up on its own, so the check can take a while in large tenants; the scan bar shows how far it is.
  - **Warning:** a report-only policy that hasn't been changed for 30 days or more (the report-only warning days, set in **Settings** > **Checks**); a policy that includes no users or no apps; a policy whose included groups are all empty; or a policy that includes a deleted user or group.
  - **Info:** a disabled policy, or a policy that excludes a deleted user or group (remove it from the policy). A deleted user still in the recycle bin counts as deleted.
- **Unused policies**: enabled and report-only policies that didn't apply to any interactive sign-in in the last 7 days (the sign-in lookback, set in **Settings** > **Checks**). It only looks at interactive sign-ins: non-interactive, service principal and managed identity sign-ins aren't read, so a policy aimed only at them looks unused. Policies that target service principals, disabled policies (see Policy hygiene) and policies created during the period are skipped. Needs Entra ID P1 or P2. Every sign-in in the period is read, so the check can take a while in busy tenants; the check page shows the period and how many sign-ins were read.
  - **Warning:** an enabled policy that neither granted nor blocked any sign-in.
  - **Info:** a report-only policy that wouldn't have applied to any sign-in.

</details>

<details>
<summary>External Identities: severities and details</summary>

- **Cross-tenant defaults**: the default cross-tenant access settings, which apply to every Entra organisation you haven't added as a partner. One finding per setting.
  - **Warning:** device claims (compliant or hybrid joined) from every tenant are trusted, invitations are redeemed automatically for every tenant (inbound or outbound), or B2B direct connect is open to every tenant (inbound or outbound). These belong on known partners.
  - **Info:** MFA from every tenant is trusted, or B2B collaboration is open to every tenant (one finding per direction; this is Microsoft's default).
- **Cross-tenant partners**: partner organisations' settings worth reviewing, one finding per setting: trusted MFA or device claims, automatic redemption, B2B direct connect, and partners marked as service providers. Always **Info**, since partner settings are deliberate. Settings a partner inherits from the defaults aren't reported here. Partners are shown by tenant ID.
- **External collaboration**: the tenant's guest settings.
  - **Critical:** guests have the same access to directory data as members.
  - **Warning:** anyone, guests included, can invite guests (Microsoft's default), or people can join the tenant by verifying an email address at one of its domains, without an invitation. That last setting is set only through Microsoft Graph or PowerShell; older tenants often still have it on.
  - **Info:** guests have limited access to the directory (Microsoft's default), every member can invite guests, or a value entraspect doesn't recognise.

The External Identities checks show each setting's current value in its own column and need no licence.

</details>

Some checks read Microsoft Graph beta endpoints where v1.0 doesn't have the data (MFA not required reads per-user MFA state and sign-ins from beta). Beta can change without notice; if it does, only the check that uses it fails, and its page says it uses beta data.

## Permissions

entraspect needs these Microsoft Graph **application** permissions, with admin consent. All are read-only.

| Permission | Used by |
| --- | --- |
| `Application.Read.All` | App registration and enterprise app checks |
| `AuditLog.Read.All` | Sign-in activity, the MFA registration report and sign-in logs (Inactive guests and users, MFA checks, No phishing-resistant MFA, Unused policies) |
| `GroupMember.Read.All` | Group checks, and members of role-assignable groups and groups named in policies |
| `IdentityRiskyUser.Read.All` | Risky users |
| `Member.Read.Hidden` | Groups with no members (groups with hidden membership) |
| `Policy.Read.All` | Conditional Access, security defaults, and External Identities settings |
| `RoleManagement.Read.Directory` | Directory role assignments (every check about admins) |
| `User.Read.All` | Every check about users and guests |
| `LicenseAssignment.Read.All` | Optional, recommended: reads your tenant's licences at connect, so checks your licence doesn't cover are grayed out instead of failing |

You can grant only some of them: checks whose permissions are missing don't run, and the rest do. **Settings** > **Checks** shows each check's permissions, green when granted and red when missing. [docs/setup.md](docs/setup.md#2-add-the-permissions) explains exactly what each permission is used for.

## Getting started

### Requirements

- Windows, macOS or Linux, with the [.NET 10 SDK](https://dotnet.microsoft.com/download) to build from source.
- A Microsoft Entra ID tenant. Most checks work on any tenant; checks marked P1 or P2 above need that licence.
- Someone who can create an app registration and grant admin consent, such as a Privileged Role Administrator or Global Administrator.

### Set up and run

1. **Create an app registration** for entraspect, add the permissions above as application permissions, grant admin consent, and upload a certificate. [docs/setup.md](docs/setup.md) walks through it, including creating a certificate on each platform.
2. **Build and run** entraspect:

   ```sh
   git clone <this repository>
   cd entraspect
   dotnet run --project src/Entraspect.App
   ```

3. **Connect.** entraspect opens on **Settings** > **Connection profiles**. Add a profile with the tenant ID, client ID and either the certificate's thumbprint (from the certificate store) or its `.pfx` path, then select **Connect**.
4. **Scan.** On **Overview**, select **Scan**. Each section shows its Critical, Warning and Info counts; select a check in the navigation to see its findings.

If connecting fails, see [Troubleshooting](docs/setup.md#troubleshooting) for the common `AADSTS` errors.

## Using entraspect

- **Settings > Connection profiles:** add a profile (tenant ID, client ID, certificate) and connect. entraspect starts here. **Open profiles.json** opens the profiles in your default app for JSON files; after editing and saving, select **Reload**.
- **Overview:** the connected tenant, **Scan**, and Critical, Warning and Info counts for each section.
- **Check pages**, grouped by object type in the navigation (Users, Groups, App registrations, Enterprise apps, Roles & admins, Conditional Access and External Identities): each check's findings, with Search and Add filter like the Entra admin center.
- **Navigation pills:** after a scan, a check with findings shows their count, coloured by the most severe one (red critical, yellow warning, blue info); hover for the count by severity. A gray **!** means the check has no result because it failed or is missing a permission; hover for the reason. A check with no findings shows nothing. A collapsed section shows the total for its checks, and a gray **!** when any of them has no result.
- **Status bar**, along the bottom: what entraspect is doing (connecting, the check being run with a progress bar, or how the last scan ended), with a small **X** to cancel a running scan. On the right: when the check on the page last ran (or the last scan, on Overview) and the connected profile.
- **Settings > Checks:** turn checks on or off, see which permissions each one needs, and set the expiry windows, new group grace period, inactive guest and user days, pending invitation days, report-only warning days and sign-in lookback days. **Settings > Preferences:** show dates and times in UTC, your computer's time zone or any other time zone, and choose a light, dark or system theme.
- **Settings > About:** the version, the logo, and where entraspect stores its files. **Open settings.json** and **Reload** work like the profiles pair.
- **Export**, next to **Scan**: save results as CSV (one row per finding, for spreadsheets) or JSON (every check's status and error, and all findings). On **Overview** it saves every finding from the last scan; on a check page, the findings shown after search and filters. Dates in exports are always UTC.

## Your data

- **Results** stay in memory and are cleared when you disconnect or close entraspect, unless you export them.
- **Exports** contain tenant data: your tenant ID and the names and IDs of users, groups, apps and policies. Store them as you would other tenant data, and never commit them to a repository. Exports never contain passwords, certificates, private keys or tokens.
- **Profiles and settings** are saved as `profiles.json` and `settings.json` in an `entraspect` folder in your local application data folder (`%LOCALAPPDATA%\entraspect` on Windows, `~/Library/Application Support/entraspect` on macOS, `~/.local/share/entraspect` on Linux). They hold tenant IDs, client IDs, certificate paths or thumbprints and your preferences; never passwords, private keys or tokens.
- **Network:** entraspect connects only to Microsoft sign-in and Microsoft Graph. It sends no telemetry. Opening an Entra admin center page, for example to add a missing permission, happens in your browser; you make any change there, never entraspect.

## Development

```sh
dotnet build
dotnet test
```

The tests use fake data and never connect to a tenant or the network.

| Project | Contents |
| --- | --- |
| `src/Entraspect.Core` | Models, the checks and the `IDirectoryReader` interface. No UI and no Graph SDK. |
| `src/Entraspect.Graph` | The `IDirectoryReader` implementation over Microsoft Graph, and authentication. |
| `src/Entraspect.App` | The Avalonia desktop app: views and view models. |
| `tests/` | xUnit tests for each project. |
| `docs/specs/` | Numbered feature specs, one per feature, with open questions. |

Each check is one class in `src/Entraspect.Core/Checks/`, registered in `CheckCatalog`, with a matching test file. New features start as a spec in `docs/specs/`. [AGENTS.md](AGENTS.md) has the project's rules and conventions (read-only Graph access, no secrets in the repo, no network in tests, no telemetry); follow them in any contribution.

## Licence

entraspect is released under the [MIT licence](LICENSE).

entraspect is an independent project and isn't affiliated with or endorsed by Microsoft. Microsoft, Microsoft Entra and Microsoft Graph are trademarks of the Microsoft group of companies.
