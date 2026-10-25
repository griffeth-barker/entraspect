# Spec 015: External collaboration settings

**Status:** Ready (depends on specs 002, 007 and 010)
**Goal:** A new **External Identities** section in the navigation, matching the Entra admin center, with three checks on the tenant's guest and cross-tenant settings: external collaboration settings (guest access, who can invite, self-service join), cross-tenant access defaults, and cross-tenant access partner settings.

**Out of scope for this spec:** guest user accounts (spec 016), the invitation domain allow and block list (beta only, see Open questions), tenant restrictions, cross-tenant synchronization, B2C and External ID user flows, Teams and SharePoint external sharing, guest access reviews.

**Graph permissions:** none new. Every endpoint here needs only `Policy.Read.All` (application), which spec 007 added. The Graph docs list `Policy.Read.All` as the least-privileged application permission for both `GET /policies/authorizationPolicy` and `GET /policies/crossTenantAccessPolicy/default`; confirm the same for `/partners` before starting. No licence needed: these settings exist on every tenant.

**Why:** by default every Entra tenant lets anyone, guests included, invite guests, and gives guests limited read access to the directory. The CIS Microsoft 365 Foundations Benchmark asks for guest access to be limited at least (5.1.6.2) and for invitations to come from admins and the Guest Inviter role only (5.1.6.3). Cross-tenant access defaults decide what every other Entra organisation can do with yours. Trust and direct connect settings belong on known partners, not on the defaults.

Follow AGENTS.md. Complete one step at a time, make sure `dotnet build` and `dotnet test` pass, then commit before starting the next step.

## Step 1: Navigation section and setting values

- Add `CheckCategory.ExternalIdentities` after `ConditionalAccess`, so the section sits last, where Entra puts it. Title "External Identities", with a FluentAvalonia symbol icon that no other section uses. Like every section, it's hidden until it has a check, and collapsible.
- Add `Finding.Value` (optional string): the current value of the setting a finding is about, for example "Everyone". These checks report settings rather than objects, and the current value needs its own column; AGENTS.md doesn't allow folding it into another field. Add `FindingLabels.Value` (null hides the column, and null is the default), and show it in the findings grid, Search, Add filter (a Text field) and both exporters (spec 010: a `Value` column in CSV after `SubObjectDisplayName`, and a `value` property in JSON).
- Update AGENTS.md: the `Finding` bullet lists `Value`, and the `CheckCategory` description no longer says only "kind of directory object", since this section groups tenant settings, as Entra does.
- Tests: the section appears only when it has a check, the column is hidden when the label is null, and Search, the filter and both exporters include `Value`.

## Step 2: Core models and readers

Models, mapped defensively (missing or unknown values never throw):

- `AuthorizationPolicySettings`: `GuestUserRoleId` (string, may be empty), `AllowInvitesFrom` (enum `None`, `AdminsAndGuestInviters`, `AdminsGuestInvitersAndAllMembers`, `Everyone`, `Unknown`), `AllowInvitesFromRaw` (the string Graph returned, for the message when unknown), `AllowEmailVerifiedUsersToJoinOrganization` (nullable bool).
- `CrossTenantAccessSettings`, used for both the defaults and each partner:
  - `TenantId` (null for the defaults), `IsServiceDefault` (defaults only), `IsServiceProvider` (partners only, nullable).
  - `InboundTrust`: `IsMfaAccepted`, `IsCompliantDeviceAccepted`, `IsHybridJoinedDeviceAccepted`, all nullable bool. Null on a partner means "inherits the defaults".
  - `AutomaticRedemptionInbound`, `AutomaticRedemptionOutbound`: nullable bool, from `automaticUserConsentSettings`.
  - `B2bCollaborationInbound`, `B2bCollaborationOutbound`, `B2bDirectConnectInbound`, `B2bDirectConnectOutbound`: each a nullable `CrossTenantAccess` with `Users` and `Applications`. Each of those holds an `AccessType` (`Allowed`, `Blocked`, `Unknown`) and `IncludesAll` (the targets include `AllUsers` or `AllApplications`). Null means "not set" (inherited, on a partner).
  - Helpers: `IsOpenToAll` (users and applications both `Allowed` with `IncludesAll`) and `IsAllowedForAny` (anything other than blocked for all users and all applications).

`IDirectoryReader`:

- `GetAuthorizationPolicyAsync(CancellationToken)`
- `GetCrossTenantAccessDefaultsAsync(CancellationToken)`
- `GetCrossTenantAccessPartnersAsync(CancellationToken)`

Three separate methods, so a failure fails only the check that reads it. Add them to `CachingDirectoryReader` and the fake readers.

## Step 3: Checks

All three: category `ExternalIdentities`, required permission `Policy.Read.All`, no licence. `ObjectType` is `AuthorizationPolicy` or `CrossTenantAccessPolicy`. Messages say what the setting does and what to do, in one or two sentences. The check description (shown in the page's info tip) says which Entra admin center page holds the setting: **External Identities** > **External collaboration settings**, or **Cross-tenant access settings**.

Labels for the two settings checks: `ObjectDisplayName` "Setting" (the setting's name as the admin center shows it), `ObjectId` "Graph property" (for example `allowInvitesFrom`), `Value` "Current value". The others are hidden.

**`external-collaboration-settings`** (title "External collaboration"), from the authorization policy:

- **Guest user access** (`guestUserRoleId`):
  - `a0b1b346-4d3e-4e8b-98f8-753987be4970` (same access as members): **Critical**, "Guests have the same access to directory data as members. Limit guest access."
  - `10dae51f-b6af-4016-8d66-8c2a99b929b3` (limited access, the default): **Info**, "Guests can see other users and group memberships. Consider restricting them to their own directory objects."
  - `2af84b1e-32c8-42b7-82bc-daa82404023b` (restricted to their own objects): no finding.
  - Any other or empty value: **Info**, "Unrecognised guest access level." The current value shows the raw ID.
  - Current value: the admin center's wording for the three known levels.
- **Guest invite restrictions** (`allowInvitesFrom`):
  - `Everyone` (the default): **Warning**, "Anyone in the organisation, guests included, can invite guests. Limit invitations to admins and the Guest Inviter role."
  - `AdminsGuestInvitersAndAllMembers`: **Info**, "Every member can invite guests. Consider limiting invitations to admins and the Guest Inviter role."
  - `AdminsAndGuestInviters` and `None`: no finding. `Unknown`: **Info** with the raw value.
- **Self-service join** (`allowEmailVerifiedUsersToJoinOrganization` true): **Warning**, "People can join this tenant by verifying an email address at one of its domains, without an invitation." False or null: no finding.

**`cross-tenant-access-defaults`** (title "Cross-tenant defaults"), from the default cross-tenant access settings. The display name names the direction, for example "Inbound trust: MFA":

- Inbound trust accepted from every tenant, one finding per setting that is true:
  - MFA: **Info**, "MFA done in any external tenant satisfies your MFA policies. Consider trusting MFA only for partner organisations."
  - Compliant device and hybrid joined device: **Warning**, "Device claims from any external tenant are trusted. Trust device claims only from partner organisations you manage devices with."
- Automatic invitation redemption (`automaticUserConsentSettings`), inbound or outbound true: **Warning**, "Invitations are redeemed without a consent prompt for every external tenant. Turn this on only for partner organisations."
- B2B direct connect inbound or outbound `IsAllowedForAny`: **Warning**, "B2B direct connect is open to every external tenant. Allow it only for partner organisations." It's blocked by default.
- B2B collaboration inbound or outbound `IsOpenToAll`: **Info**, "Users from every external tenant can be invited (inbound) / Your users can be invited by every external tenant (outbound). This is the default; consider allowing only the organisations you work with." One finding per direction.
- Current value: "Allowed for all", "Allowed for some", "Blocked", "On" or "Off".

**`cross-tenant-access-partners`** (title "Cross-tenant partners"), one row per partner setting worth reviewing. Partner settings are deliberate, so these are all **Info**, for review. Labels: `ObjectDisplayName` "Tenant ID", `SubObjectDisplayName` "Setting", `Value` "Current value", with `ObjectId` hidden.

- Inbound trust (MFA, compliant device, hybrid joined device) true: "This organisation's MFA (or device) claims are trusted."
- Automatic redemption inbound or outbound true.
- B2B direct connect inbound or outbound `IsAllowedForAny`.
- `IsServiceProvider` true: "Marked as a service provider (for example a Microsoft partner with delegated admin)."
- A null property inherits the defaults: no finding here, since the defaults check covers it.
- A partner with nothing to report: no finding.

Tests with a fake reader: every value of each setting (including unknown and null), the boundary between "open to all" and "allowed for some", partners inheriting (null) versus overriding, and each check failing on its own when its read throws `DirectoryReadException`.

## Step 4: Graph implementation

- `GET /policies/authorizationPolicy?$select=guestUserRoleId,allowInvitesFrom,allowEmailVerifiedUsersToJoinOrganization`.
- `GET /policies/crossTenantAccessPolicy/default`.
- `GET /policies/crossTenantAccessPolicy/partners`, following every page.
- All v1.0 GETs. No POST, no `$batch`, no beta.
- Keep the mapping in small pure methods with unit tests using sample Graph objects and JSON, including unknown enum values, missing `targets`, and a partner with every property null.

## Step 5: Docs

- `docs/setup.md`: add the three checks to the `Policy.Read.All` row of the permissions table.
- `README.md`: add the External Identities section and its three checks to "What it checks".

## Acceptance criteria

- `dotnet build` and `dotnet test` pass with no warnings.
- No test makes a network call.
- No permissions beyond those from earlier specs are requested or documented.
- Manual check against the dev tenant:
  - External Identities appears as its own collapsible section, last in the navigation.
  - A tenant still on the defaults shows Guest invite restrictions "Everyone" (Warning), Guest user access "Limited" (Info), and B2B collaboration open to all in both directions (Info).
  - Changing a setting in the admin center (for example invitations to admins and Guest Inviters only) and scanning again removes that finding.
  - Exported CSV and JSON include the current value.

## Open questions

(Agents: add anything you think is out of scope or ambiguous here instead of implementing it.)

- **Invitation domain allow and block list:** CIS recommends sending invitations only to allowed domains. As of 2023, Graph exposed this list only as the legacy `B2BManagementPolicy` at `beta/legacy/policies`. Check whether a v1.0 or supported beta endpoint exists now. If only the legacy one does, decide whether it's worth a beta call under AGENTS.md's beta rules.
- **Partner names:** partner settings return only a tenant ID. `GET /tenantRelationships/findTenantInformationByTenantId(tenantId='...')` returns the name and default domain, but needs `CrossTenantInformation.ReadBasic.All`, a new permission. Worth it for readability?
- **Severity of trusting MFA from every tenant:** set to Info here because Microsoft presents it as a convenience. Reconsider if a benchmark rates it higher.
- **Other authorization policy settings:** `allowedToSignUpEmailBasedSubscriptions`, `defaultUserRolePermissions` (users creating apps, tenants or security groups), `allowUserConsentForRiskyApps` and `blockMsolPowerShell` are tenant hygiene rather than external collaboration. A "User settings" spec could cover them.
- **Tenant restrictions v2 and cross-tenant sync:** not reviewed here.
- **Guest self-service sign-up user flows and email one-time passcode:** not reviewed here.
- **Navigation order (decided while building):** the checks are sorted by title within the section, as AGENTS.md requires: Cross-tenant defaults, Cross-tenant partners, External collaboration. The spec listed them in another order.
- **Current value labels (decided while building):** short labels instead of the admin center's long sentences: "Same as members" and "Limited" for guest access, "Everyone" and "Members and admins" for invitations, "On" for switches, "Allowed for all" and "Allowed for some" for B2B settings. An unknown value shows the raw text Graph returned.
- **Email-verified join (decided while building):** `allowEmailVerifiedUsersToJoinOrganization` isn't on the External collaboration settings page; it's set only through Graph or PowerShell. The finding is named "Email-verified users can join" and the check's info tip says so. Older tenants often still have it on, so a tenant on the defaults may also show this Warning, which the acceptance list doesn't mention.
- **`IsAllowedForAny` (decided while building):** false when users or applications are blocked for all their targets (either one blocks everyone), and false when either side is missing or has an unknown access type, so an unreadable setting is never reported as open. On a partner, a B2B setting with only one side set therefore counts as not allowed, even if the other side is inherited from open defaults. Revisit if that hides real partner settings.
- **Authorization policy read as JSON (decided while building):** the Graph SDK maps `allowInvitesFrom` to an enum and `guestUserRoleId` to a GUID, dropping values it doesn't know. To show the raw value as this spec asks, the policy is read as JSON through the Graph client's request adapter (still a v1.0 GET). The cross-tenant settings use the SDK types; an unknown `accessType` maps to Unknown.
- **Partner findings' Object ID (decided while building):** the hidden Object ID column holds the partner's tenant ID, so exports keep it next to the "Tenant ID" display name. For the two settings checks, Object ID holds the Graph property path, for example `inboundTrust.isMfaAccepted`.
- **B2B collaboration on partners:** not reported by Cross-tenant partners, since the spec doesn't list it. A partner that blocks or limits collaboration is deliberate; one that opens it is what partners are for.
- **Value column test coverage:** the grid hides the Value column when its label is null (`CheckPageView` code-behind). No headless UI test covers the grid columns yet; the view model's filter fields and search are tested.
