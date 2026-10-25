# Spec 018: Privileged access

**Status:** Ready (depends on specs 005, 016 and 017)
**Goal:** A new **Roles & admins** section in the navigation, named and placed as in the Entra admin center, with four checks on who holds admin roles: how many Global Administrators and privileged assignments there are, admins synced from on-premises, admins without phishing-resistant MFA, and admins whose accounts are also licensed for everyday work.

**Out of scope for this spec:** permanent versus eligible (PIM) assignments, admin units and scoped assignments as a separate concern, custom role permissions, break-glass account handling, inactive or disabled admins (spec 017 already raises them to Critical), guest admins (spec 016). See Open questions.

**Graph permissions:** none new. `User.Read.All` (spec 007), `RoleManagement.Read.Directory` (spec 008), `GroupMember.Read.All` (spec 003) and `AuditLog.Read.All` (spec 005, for the registration report). Admins without phishing-resistant MFA needs Entra ID P1 or P2, as spec 005 does; the others need no licence.

**Builds on spec 017:** `PrivilegedRoles` (Tier 0 and Privileged role lists) and `RoleHolders` (who holds which role, directly or through role-assignable groups). Don't add a second copy of either.

**Why:**
- Microsoft's role best practices say to keep fewer than five Global Administrators and fewer than ten privileged role assignments, with two cloud-only emergency access accounts holding Global Administrator ([best practices](https://learn.microsoft.com/en-us/entra/identity/role-based-access-control/best-practices), [privileged roles](https://learn.microsoft.com/en-us/entra/identity/role-based-access-control/privileged-roles-permissions)).
- Microsoft's guidance on protecting Microsoft 365 from on-premises attacks is that no on-premises (synced) account should hold admin rights in the cloud: admin accounts should be cloud-only and use phishing-resistant MFA ([Protect Microsoft 365 from on-premises attacks](https://learn.microsoft.com/en-us/azure/active-directory/architecture/protect-m365-from-on-premises-attacks)).
- Separate, unlicensed admin accounts keep email and browsing, the usual ways in, away from admin rights.

**Hybrid tenants:** Synced admins is the hybrid-specific check. Synced role-assignable groups don't exist (role-assignable groups are cloud-only), but a synced *user* added to one counts.

Follow AGENTS.md. Complete one step at a time, make sure `dotnet build` and `dotnet test` pass, then commit before starting the next step.

## Step 1: Navigation section

- Add `CheckCategory.RolesAndAdmins` between `EnterpriseApps` and `ConditionalAccess`, where Entra places Roles & admins. Title "Roles & admins", with a FluentAvalonia symbol icon no other section uses. Like every section, it's hidden until it has a check, and collapsible.
- Category values are saved and exported by name, never by number (settings store check IDs; the exporters write `Category` as a string). Confirm that nothing persists the enum's numeric value, so inserting a value in the middle is safe.
- Tests: the section order, and the section hidden with no checks.

## Step 2: Checks

All four: category `RolesAndAdmins`. "Admins" means users who hold at least one role that `PrivilegedRoles` rates Tier 0 or Privileged, through `RoleHolders`: active assignments only, at any scope, directly or through a role-assignable group. Members and guests both count; guests are also reported by spec 016. Severity names the highest tier the user holds.

User columns as spec 016: "Display name", "User principal name", "Object ID", and `SubObjectDisplayName` "Roles", listing the user's privileged roles, highest tier first, comma-separated. Never combine two properties in one field: the roles list is one property (the user's roles).

**`roles-admin-counts`** (title "Admin role counts"). Requires `User.Read.All`, `RoleManagement.Read.Directory` and `GroupMember.Read.All`. Findings are about roles, not users: `ObjectType` `Role`, `ObjectId` the role template ID, `ObjectDisplayName` the role name. Labels "Role", "Object ID" and `Value` "Holders"; the user columns are hidden.

- Global Administrator held by 5 or more distinct users: **Warning**, "N users hold Global Administrator. Microsoft recommends fewer than five."
- Global Administrator held by fewer than 2 users: **Warning**, "Only N user(s) hold Global Administrator. Keep at least two, such as two emergency access accounts, so you can't lose access to the tenant."
- Users holding Tier 0 or Privileged roles: count distinct (user, role) pairs. 10 or more: **Info**, "N privileged role assignments. Microsoft recommends fewer than ten." `ObjectDisplayName` "Privileged roles", `ObjectId` empty.
- Assignments through groups count once per user who receives the role.

**`roles-synced-admins`** (title "Synced admins"). Requires the same three permissions. One finding per admin with `OnPremisesSyncEnabled` true:

- Holds a Tier 0 role: **Critical**, "Admin account is synced from on-premises Active Directory. Anyone who takes over that account or the sync server gets these roles. Use a cloud-only account for admin work."
- Holds only Privileged roles: **Warning**, with the same text.
- Non-privileged roles only: no finding.

**`roles-admins-no-phishing-resistant-mfa`** (title "No phishing-resistant MFA"). Requires the same three permissions plus `AuditLog.Read.All`; `RequiredLicence` P1, reading spec 005's registration report (`GetUserRegistrationsAsync`, cached per scan).

- Phishing-resistant methods are passkeys (device-bound, including Microsoft Authenticator and Windows Hello passkeys), FIDO2 security keys, Windows Hello for Business and certificate-based authentication (multifactor). Put the matching `methodsRegistered` values in one Core list, `PhishingResistantMethods`, taking the exact strings from the Graph `userRegistrationDetails` docs and the authentication methods activity docs. Add a comment with the URL and date you read them. Match them case-insensitively, and treat unknown values as not phishing-resistant.
- An admin with none of them registered: Tier 0 **Warning**, Privileged **Info**, "No phishing-resistant method registered (passkey, security key, Windows Hello or certificate). Admins should sign in with one."
- An admin missing from the report: **Info**, "No registration details for this admin." Spec 005 explains that the report can leave users out.
- Admins without any MFA are spec 005's (Critical). This check is about the strength of the MFA, so both may list the same admin.

**`roles-licensed-admins`** (title "Licensed admins"). Requires the same three permissions.

- An admin whose `AssignedLicenceCount` (spec 017) is above 0: **Info**, "Admin account has N licence(s) assigned. Use a separate, unlicensed account for admin work, so email and everyday apps aren't used with admin rights."
- `Value` labelled "Licences" with the count.

Tests with a fake reader:
- Admin role counts: 0, 1, 2, 4 and 5 Global Administrators, roles reached through a group (counted once per user), the 9/10 boundary for privileged assignments.
- Synced admins: each tier, synced versus cloud, a synced user holding a role through a group.
- No phishing-resistant MFA: each method value, unknown values, mixed-case values, an admin missing from the report, non-admins ignored.
- Licensed admins: 0 and N licences.
- The roles column order.
- Each check failing on its own when its read throws `DirectoryReadException`.

## Step 3: Graph implementation

No new endpoints: users with `onPremisesSyncEnabled` and `assignedLicenses` (spec 017), role assignments and definitions (specs 008 and 016), transitive group members (spec 008), and the registration report (spec 005). If the registration report's `$select` leaves out `methodsRegistered`, add it. Keep any mapping change in a pure, tested method. All v1.0 GETs, no `$batch`, no beta.

## Step 4: Docs

- `docs/setup.md`: add the four checks to the rows of the permissions they use, and note that No phishing-resistant MFA needs P1 or P2.
- `README.md`: add the Roles & admins section and its four checks to "What it checks".
- `AGENTS.md`: mention `PhishingResistantMethods` next to `PrivilegedRoles`.

## Acceptance criteria

- `dotnet build` and `dotnet test` pass with no warnings.
- No test makes a network call.
- No permissions beyond those from earlier specs are requested or documented.
- Manual check against the dev tenant:
  - Roles & admins appears as its own collapsible section, after Enterprise apps.
  - Admin role counts reflects the number of Global Administrators. A dev tenant with one shows the "fewer than 2" Warning.
  - A licensed test admin appears under Licensed admins.
  - In a hybrid tenant, a synced user given a privileged role appears under Synced admins. If the dev tenant isn't hybrid, say so when closing the spec; the unit tests stand in.
  - Without P1 or P2, No phishing-resistant MFA is grayed out (spec 012).

## Open questions

(Agents: add anything you think is out of scope or ambiguous here instead of implementing it.)

- **Permanent versus eligible (PIM) assignments:** Microsoft recommends just-in-time (eligible) assignments for privileged roles. Reading eligibility needs `RoleEligibilitySchedule.Read.Directory` and P2, two things to weigh. An "Admins with permanent assignments" check would follow.
- **Break-glass accounts:** they legitimately hold Global Administrator permanently, may be excluded from MFA policies, and don't sign in. They count towards the two-Global-Administrator minimum, but other checks here and in spec 017 may flag them. Ignoring accepted findings is the general answer.
- **Service principals and groups holding roles:** role assignments to apps (service principals) aren't counted or reported here. Apps with directory roles are a takeover path like apps with high-privilege Graph permissions (spec 006).
- **Scoped assignments:** a role assigned to an administrative unit counts as if tenant-wide. Should the admin unit name be shown?
- **Custom roles:** never privileged in `PrivilegedRoles`; see spec 017.
- **Mailboxes:** a licence is a stand-in for "has a mailbox". Reading mailboxes needs Exchange Online permissions, which entraspect doesn't use.
