# Spec 006: App registration and enterprise app hygiene

**Status:** Ready (depends on specs 002 and 004)
**Goal:** Three checks: app registrations with no owners and app registrations with risky redirect URIs (App registrations section), and enterprise apps holding high-privilege Microsoft Graph application permissions (Enterprise apps section).

**Out of scope for this spec:** unused credentials and service principal sign-in activity (these endpoints are beta only), delegated permission grants (`oauth2PermissionGrants`), permissions on APIs other than Microsoft Graph, DNS lookups or any request to a redirect URI.

**Graph permissions:** no new permissions. `Application.Read.All` covers application owners, service principals and app role assignments.

This spec reports *other apps'* write permissions, such as `Directory.ReadWrite.All`. entraspect only reads these names; it never requests them. The read-only rule in AGENTS.md is unaffected.

Follow AGENTS.md. Complete one step at a time, make sure `dotnet build` and `dotnet test` pass, then commit before starting the next step.

## Step 1: Core models and readers

- Extend `AppRegistration` with `SignInAudience`, `RedirectUris` (list of `Platform` — `Web`, `Spa`, `PublicClient` — and `Uri` string), and `HasOwners`.
- Extend `EnterpriseApp` (spec 004) with `AppOwnerOrganizationId` (nullable).
- Model `AppPermissionGrant`: `ServicePrincipalId`, `ServicePrincipalDisplayName`, `PermissionName` (the app role `value`, for example `Mail.Send`), `CreatedDateTime`.
- `IDirectoryReader.GetGraphApplicationPermissionGrantsAsync(CancellationToken)`, added to `CachingDirectoryReader` too.

## Step 2: Checks

**`app-no-owners`** (category `AppRegistrations`, title "Apps with no owners"): an app registration with no owners gives **Warning**, "App registration has no owners. No one is accountable for its credentials and permissions." `ObjectType` `AppRegistration`.

**`app-risky-redirect-uris`** (category `AppRegistrations`, title "Risky redirect URIs"), one finding per matching URI:

- Contains `*`: **Warning**, "Wildcard redirect URI."
- `http://` scheme with a host other than `localhost`, `127.0.0.1` or `[::1]`, on the Web or SPA platform: **Warning**, "Redirect URI uses unencrypted HTTP."
- Loopback host on the Web or SPA platform: **Info**, "Redirect URI points to this computer; usually left over from development."
- Loopback on the PublicClient platform is normal for desktop and CLI apps and gives no finding.
- A URI that isn't a valid absolute URI: **Info**, "Redirect URI is not a valid URI."
- `SubObjectType` `Redirect URI`, `SubObjectDisplayName` "platform: URI". Only look at the URI text; never resolve or contact it.

**`sp-high-privilege-app-permissions`** (category `EnterpriseApps`, title "High-privilege apps"), one finding per matching grant:

- **Critical** (can be used to take over the tenant): `RoleManagement.ReadWrite.Directory`, `AppRoleAssignment.ReadWrite.All`, `Application.ReadWrite.All`, `Directory.ReadWrite.All`, `RoleAssignmentSchedule.ReadWrite.Directory`, `RoleEligibilitySchedule.ReadWrite.Directory`, `PrivilegedAccess.ReadWrite.AzureADGroup`, `UserAuthenticationMethod.ReadWrite.All`, `Policy.ReadWrite.ConditionalAccess`.
- **Warning** (tenant-wide data access): `Mail.ReadWrite`, `Mail.Send`, `Files.ReadWrite.All`, `Sites.ReadWrite.All`, `Sites.FullControl.All`, `User.ReadWrite.All`, `Group.ReadWrite.All`, `GroupMember.ReadWrite.All`.
- Keep both lists in one Core file with a comment explaining the two tiers, so they are easy to review and change.
- Service principals owned by Microsoft (`AppOwnerOrganizationId` is Microsoft's tenant, `f8cdef31-a31e-4b4a-93e4-5f571e91255a`) are skipped.
- `ObjectType` `EnterpriseApp`, `SubObjectType` `Application permission`, `SubObjectDisplayName` the permission name, message "Has Microsoft Graph application permission X, granted on DATE."

Tests for each rule, including each URI case, Microsoft-owned service principals, and permissions not in either list.

## Step 3: Graph implementation

- Add `signInAudience,web,spa,publicClient` to the `$select` of the existing `GET /applications` call. Get owner presence with `$expand=owners($select=id)` or a per-app fallback, as described in spec 003 step 3.
- Add `appOwnerOrganizationId` to the spec 004 `GET /servicePrincipals` `$select`.
- Grants: find the Microsoft Graph service principal (`appId` `00000003-0000-0000-c000-000000000000`) and read its `appRoles` to map role IDs to names. Then list `GET /servicePrincipals/{graph-sp-id}/appRoleAssignedTo`, following every page. This returns every application permission on Microsoft Graph in one listing instead of one call per app. Confirm in the Graph docs that `Application.Read.All` is enough for `appRoleAssignedTo`. Skip assignments whose `principalType` isn't `ServicePrincipal`.
- Don't use `POST /$batch`.
- Keep mapping in small pure methods with unit tests using sample Graph objects.

## Step 4: Docs

- `README.md`: add the three checks to "What it checks".
- `docs/setup.md`: no permission change.

## Acceptance criteria

- `dotnet build` and `dotnet test` pass with no warnings.
- No test makes a network call.
- No new Graph permission is requested or documented.
- Manual check: a test app registration with its owner removed and a Web redirect URI of `http://example.com` shows up in both App registrations checks. A test app granted `Mail.Send` (application) shows up as Warning. A public client app with `http://localhost` gives no redirect URI finding.

## Open questions

(Agents: add anything you think is out of scope or ambiguous here instead of implementing it.)

- **Unused credentials and apps:** needs `servicePrincipalSignInActivities` or `appCredentialSignInActivities`, which are beta only. AGENTS.md now allows beta, so this could be its own spec.
- **Delegated grants:** admin-consented delegated permissions (for example `Mail.ReadWrite` for all users) are also high risk. Needs `oauth2PermissionGrants` and a check of its own.
- **Other APIs:** high-privilege permissions on APIs other than Microsoft Graph (for example Exchange Online `full_access_as_app`) aren't checked.
- **Microsoft-owned apps:** skipping Microsoft's own service principals hides noise, but also hides anything unusual they hold. Should it be a setting?
- **Subdomain takeover:** a redirect URI on a domain that no longer exists is a real risk, but detecting it needs DNS lookups, which the no-telemetry rule forbids.
