# Spec 021: Apps with admin roles

**Status:** Ready (depends on specs 006, 018 and 019)
**Goal:** A check in **Roles & admins** that lists enterprise apps (service principals), including managed identities, holding a Tier 0 or Privileged directory role. Anyone who can add a credential to such an app, or run code as the managed identity, can act with the role, so these apps are a way to take over the tenant without being an admin.

**Out of scope for this spec:** eligible (PIM) role assignments to apps, apps holding only non-privileged or custom roles, the scope of an assignment (administrative unit or single app), who can add credentials to the app (its owners, and holders of Application Administrator or Cloud Application Administrator), and Microsoft Graph application permissions (spec 006's High-privilege apps already covers them). See Open questions.

**Graph permissions:** none new. `RoleManagement.Read.Directory` for role assignments and role definitions, and `Application.Read.All` for service principals. Both are already documented as the least-privileged application permissions for those reads (specs 006 and 018: [List unifiedRoleAssignments](https://learn.microsoft.com/en-us/graph/api/rbacapplication-list-roleassignments?view=graph-rest-1.0), [List servicePrincipals](https://learn.microsoft.com/en-us/graph/api/serviceprincipal-list?view=graph-rest-1.0)). No licence is needed: any tenant can assign built-in roles to a service principal, so the check can be tested on the dev tenant before the P2 trial.

**Builds on specs 006, 018 and 019:** `PrivilegedRoles` (tiers), `RoleHolders.GetPrincipalRoles` (a principal's own roles, named and tiered) and `HighPrivilegeAppPermissionsCheck.MicrosoftTenantId`. Don't add a second copy of the role resolution logic.

**Why:**
- An app with a directory role uses it with its own token, with no user, MFA or Conditional Access for users in the way. Whoever can add a secret or certificate to the app (its owners, or an Application Administrator) or control the Azure resource behind a managed identity holds the role in practice.
- The Roles & admins checks count only users today (spec 018 open question "Service principals and groups holding roles"), so these assignments are easy to miss. The admin center shows them only on each role's Assignments page.

**Hybrid tenants:** service principals are cloud-only. Nothing to add.

Follow AGENTS.md. Complete one step at a time, make sure `dotnet build` and `dotnet test` pass, then commit before starting the next step.

## Step 1: Managed identities on enterprise apps

- Add `IsManagedIdentity` (bool) to `EnterpriseApp`.
- Graph: add `servicePrincipalType` to the `$select` of the service principals listing in `ReadEnterpriseAppsAsync`. `MapServicePrincipal` sets `IsManagedIdentity` when it is `ManagedIdentity`, ignoring case. A missing or unknown value is false and doesn't throw.
- No other check changes. Existing tests that build `EnterpriseApp` keep compiling (default false, or set it where a test needs it).
- Tests (Graph mapping): `ManagedIdentity` in any case, `Application`, `Legacy`, missing.

## Step 2: Check

**`roles-app-admins`** (title "Apps with admin roles"). Category `RolesAndAdmins`. Requires `RoleManagement.Read.Directory` and `Application.Read.All`. No `RequiredLicence`.

Which apps:
- Every enterprise app from `GetEnterpriseAppsAsync` whose ID is the principal of at least one active role assignment (`GetDirectoryRoleAssignmentsAsync`, any scope) to a role `PrivilegedRoles` rates Tier 0 or Privileged. Use `RoleHolders.GetPrincipalRoles`.
- Applications, managed identities and Microsoft's own apps are all included. Unlike High-privilege apps, Microsoft's apps aren't skipped: an admin assigned the role, so it should be reviewed.
- A principal that isn't an enterprise app (a user, a group, or an app deleted between the two reads) is skipped. Don't read users or groups.
- Duplicate app IDs in the listing are reported once.

One finding per app:
- Severity by the highest tier of its privileged roles: Tier 0 **Critical**, Privileged **Warning**. A disabled app (`AccountEnabled` false) is **Info** with "App is disabled. " before the message, as High-privilege apps does: it can't get tokens now, but turning it back on restores the role.
- Message for an application: "Anyone who can add a credential to this app, such as its owners, can use its roles." For a managed identity: "Anyone who can run code on the Azure resource behind this managed identity can use its roles."
- `ObjectType` `EnterpriseApp`, `ObjectId` and `ObjectDisplayName` the app's.
- `SubObjectType` the app type: "Managed identity" when `IsManagedIdentity`, otherwise "Microsoft" when `AppOwnerOrganizationId` is `MicrosoftTenantId`, otherwise "Registered here" when an app registration from `GetAppRegistrationsAsync` has the app's `AppId`, otherwise "Other organisation". An app from another organisation is the riskiest: its credentials are managed outside the tenant.
- `Value` the app's privileged roles, highest tier first, then by name, comma-separated, as spec 018's Roles column.

Labels: `ObjectDisplayName` "Display name", `ObjectId` "Object ID", `SubObjectType` "App type", `Value` "Roles". `SubObjectDisplayName`, `UserPrincipalName` and `DueDate` hidden.

Description (for the info tip, not the page body): why an app with a role is an admin, who can use it, that disabled apps are Info, and that eligible (PIM) assignments and Graph application permissions aren't included.

Register it in `CheckCatalog` in `RolesAndAdmins`, in title order (after "Admin role counts", before "Licensed admins").

Tests with a fake reader:
- App holding a Tier 0 role: Critical. Privileged role only: Warning. Both: Critical, roles listed Tier 0 first. Several roles of one tier: sorted by name.
- App holding only non-privileged or custom roles: no finding.
- Disabled app: Info, with the disabled message.
- Managed identity: its own message and "Managed identity" app type, even when it also matches an app registration.
- App types: Microsoft, Registered here (matched by `AppId`), Other organisation.
- Role assignment principal that is a user, a group, or no known object: no finding.
- Role assignment by definition ID versus template ID (built-in roles), as `RoleHolders` handles.
- The same role assigned twice (different scopes): one role in the Roles column.
- No role assignments at all: no findings.
- The catalog lists the check in Roles & admins with its permissions.

## Step 3: Docs

- `docs/setup.md`: add the check to the `RoleManagement.Read.Directory` and `Application.Read.All` rows.
- `README.md`: add it to the Roles & admins checks under "What it checks".
- `docs/specs/018-privileged-access.md`: in the open question "Service principals and groups holding roles", note that spec 021 reports apps holding roles.

## Acceptance criteria

- `dotnet build` and `dotnet test` pass with no warnings.
- No test makes a network call.
- No permissions beyond those from earlier specs are requested or documented. No write endpoint is called, and no beta.
- Manual check against the dev tenant (no licence needed):
  - Assign a Privileged role (for example Helpdesk Administrator) to a test app registration's enterprise app, in Roles & admins > the role > Add assignments. Apps with admin roles lists the app as Warning, app type "Registered here", with the role in the Roles column.
  - Assign it Privileged Role Administrator too: the finding becomes Critical, roles listed Privileged Role Administrator first.
  - Disable the enterprise app (Properties > Enabled for users to sign in: No): the finding becomes Info with the disabled message. Turn it back on.
  - Assign it only a non-privileged role (for example Directory Readers): no finding.
  - Remove the assignments afterwards.
  - The entraspect app itself holds no directory roles, so it never appears.

## Tests still to do

(Fill in when the spec is done.)

## Open questions

(Agents: add anything you think is out of scope or ambiguous here instead of implementing it.)

- **Who can add credentials:** the app's owners (of the app registration and of the enterprise app) and holders of Application Administrator or Cloud Application Administrator can all use the app's roles. Listing the owners, as Role group owners does for groups, would show who really holds them. Needs owner reads for these apps only.
- **Eligible (PIM) assignments:** an app can be eligible for a role. Needs `RoleEligibilitySchedule.Read.Directory` and P2, as spec 016 and 018's PIM questions.
- **Assignment scope:** `DirectoryRoleAssignment` has no scope, so a role scoped to an administrative unit or to one app registration counts as tenant-wide, as in spec 018. An app scoped to one app is much less risky. Keeping `directoryScopeId` would let the check say so or lower the severity.
- **Microsoft's own apps:** are there Microsoft service principals that hold privileged roles by design, so that every tenant would get a finding? Check on the dev tenant and with any later test tenants. If so, list them like `PrivilegedRoles` or lower them to Info.
- **Apps with credentials:** an application with no secrets or certificates and no owners can only be used by an Application Administrator. Whether the app has credentials (from the app registration, or keyCredentials on the service principal) could lower the severity, but owners and admins can add one at any time.
- **Admin role counts:** spec 018's counts include only users. Should apps holding Global Administrator count towards its "fewer than five Global Administrators" rule, and their privileged roles towards "fewer than ten privileged role assignments"?
