# Spec 017: User hygiene

**Status:** Ready (depends on specs 012 and 016)
**Goal:** Three more checks in the Users nav section: member accounts nobody has signed in to for a long time, disabled accounts that still hold roles or licences, and users Microsoft Entra ID Protection flags as risky. Also adds two shared pieces spec 018 builds on: a curated list of privileged roles, and one place that works out who holds which role.

**Out of scope for this spec:** guest accounts (spec 016), admin-specific checks such as Global Administrator counts and synced admins (spec 018), risky sign-ins and risk detections, password age, disabled users who still own apps or groups, deleting or disabling anything (entraspect is read-only). See Open questions.

**Graph permissions:** one new, **`IdentityRiskyUser.Read.All`** (application), for Risky users only. The Graph docs list it as the least-privileged, and only, application permission for `GET /identityProtection/riskyUsers`, and it is read-only. Everything else uses permissions earlier specs added: `User.Read.All` (spec 007), `AuditLog.Read.All` (spec 005), `RoleManagement.Read.Directory` (spec 008) and `GroupMember.Read.All` (spec 003). Inactive users needs Entra ID P1 or P2 (`signInActivity`). Risky users needs P2.

**Never call the riskyUser actions.** `confirmCompromised`, `confirmSafe` and `dismiss` are POSTs that change the tenant, and AGENTS.md rule 1 forbids them.

**Why:** stale accounts are a common way in, since nobody notices them being used. A disabled account that keeps a role or licence comes back with its access the moment someone re-enables it, and licences cost money. Risky users are accounts Microsoft already believes may be compromised.

**Hybrid tenants:** include users synced from on-premises Active Directory. A synced account is disabled and re-enabled on-premises, so messages about disabled synced users say so.

Follow AGENTS.md. Complete one step at a time, make sure `dotnet build` and `dotnet test` pass, then commit before starting the next step.

## Step 1: Shared role pieces, models, options and readers

**Privileged roles.** Add `PrivilegedRoles` in Core, a curated list in the style of spec 006's `HighPrivilegePermissions`, because Graph v1.0 has no "privileged" flag on role definitions (`isPrivileged` is beta only).

- Two tiers, keyed by built-in role template ID, each with its name:
  - **Tier 0**, roles that can take over the tenant: Global Administrator, Privileged Role Administrator, Privileged Authentication Administrator, Hybrid Identity Administrator, Partner Tier2 Support.
  - **Privileged**: every other built-in role Microsoft marks **PRIVILEGED** on the "Microsoft Entra built-in roles" permissions reference page.
- Take the template IDs from that page. Don't type them from memory, and add a comment with the page URL and the date you read it.
- Custom roles are never privileged here. That goes in Open questions, since custom roles can hold privileged permissions.
- `PrivilegedRoles.TierOf(roleDefinitionId)` returns `Tier0`, `Privileged` or `Other`.

**Role holders.** Spec 016's Guests with admin roles works out who holds which role inside its own check: direct assignments, plus users in role-assignable groups through `GetGroupTransitiveUserIdsAsync`. Move that into a shared Core helper, `RoleHolders`. It takes the assignments, role definitions and a reader, and returns for each user ID the distinct roles they hold (ID, name, tier). Guests with admin roles uses it, and its behaviour and tests stay unchanged. Spec 018 and this spec's checks use it too.

**Users.** Extend `DirectoryUser` with:
- `OnPremisesSyncEnabled` (nullable bool);
- `AssignedLicenceCount` (int: how many licences are assigned directly or through groups; 0 when none). The Graph `assignedLicenses` property already includes group-based licences.

Spec 007, 008 and 016's checks and tests must keep working unchanged.

**Sign-in activity for all users.** Replace spec 016's `GetGuestSignInActivityAsync` with `GetUserSignInActivityAsync`, which reads every user, members and guests. Rename `GuestSignInActivity` to `UserSignInActivity`. Inactive guests filters guests in code; its behaviour and tests don't change. A single read serves both checks within a scan (`CachingDirectoryReader`).

**Risky users.** Model `RiskyUser`: `Id`, `UserPrincipalName`, `DisplayName`, `RiskLevel` (`Low`, `Medium`, `High`, `Hidden`, `None`, `Unknown`), `RiskState` (`AtRisk`, `ConfirmedCompromised`, `Remediated`, `Dismissed`, `ConfirmedSafe`, `None`, `Unknown`), `RiskDetail` (string, may be empty), `RiskLastUpdated` (nullable), `IsDeleted` (bool). Add `IDirectoryReader.GetRiskyUsersAsync(CancellationToken)`.

Add every new or changed reader method to `CachingDirectoryReader` and the fake readers.

**Options** (each with its own validation rule, a Settings > Checks field and an `InfoTip`):
- `UserInactiveDays`: default 90, from 30 to 365. "Inactive users reports member accounts with no successful sign-in for at least this many days." Separate from `GuestInactiveDays`, since organisations often keep guests on a shorter leash.

## Step 2: Checks

All three: category `Users`, `ObjectType` `User`. Columns as spec 016: "Display name", "User principal name" and "Object ID", plus the extra columns per check. Members only, unless the check says otherwise; guests are spec 016's. Day counts are UTC calendar days, "at least N", as in spec 016. Dates go through `TimeDisplay`.

**`users-inactive`** (title "Inactive users"). Requires `User.Read.All`, `AuditLog.Read.All`, `RoleManagement.Read.Directory` and `GroupMember.Read.All`; `RequiredLicence` P1.

- Last activity is worked out as in spec 016: `LastSuccessfulSignIn`, else the later of the two last attempts ("last sign-in attempt" in the message).
- Last activity at least `UserInactiveDays` ago: **Warning**, "No successful sign-in for N days." Never signed in and created at least `UserInactiveDays` ago: **Warning**, "Never signed in since being created N days ago." Created more recently: no finding. Never signed in, creation date unknown: **Info**, "Never signed in."
- A user who would be reported and holds a Tier 0 or Privileged role: **Critical** instead, with the message ending "Holds ROLE." (the highest-tier role, or "and N more roles").
- A disabled user who would be reported and holds no role: **Info**, ending "The account is disabled; consider deleting it." A disabled user who holds a role is reported by Disabled users with access, so leave them out here.
- `DueDate` labelled "Last sign-in". `Value` labelled "Source": "Synced" when `OnPremisesSyncEnabled` is true, else "Cloud", so admins know where to act.

**`users-disabled-access`** (title "Disabled users with access"). Requires `User.Read.All`, `RoleManagement.Read.Directory` and `GroupMember.Read.All`; no licence. One finding per kind of access per disabled member:

- Holds a Tier 0 or Privileged role: **Critical**, "Disabled account still holds ROLE. Re-enabling it restores admin access; remove the role." Holds only other roles: **Warning**, with the same text. One finding per distinct role. `SubObjectDisplayName` is the role, labelled "Access".
- Has licences: **Info**, "Disabled account still has N licence(s) assigned. Remove them if the user has left." `SubObjectDisplayName` "Licences".
- Synced users: the message adds "This account is managed in on-premises Active Directory."
- `Value` labelled "Source", as above.

**`users-risky`** (title "Risky users"). Requires `IdentityRiskyUser.Read.All`; `RequiredLicence` P2. Members and guests, since Identity Protection rates both.

- Only users whose `RiskState` is `AtRisk` or `ConfirmedCompromised` and who aren't deleted. Remediated, dismissed and confirmed-safe users are dealt with already.
- `ConfirmedCompromised`: **Critical**, "Confirmed compromised. Reset the password, revoke sessions and review the account's activity."
- `AtRisk` with `RiskLevel` `High`: **Critical**; `Medium`: **Warning**; `Low`: **Info**. Message: "At risk (LEVEL) according to Microsoft Entra ID Protection." It adds the risk detail when it isn't empty or `none`.
- `RiskLevel` `Hidden`, `None` or `Unknown` while at risk: **Info**, "At risk; the risk level isn't shown."
- `Value` labelled "Risk level". `DueDate` labelled "Risk updated" (`RiskLastUpdated`).
- The check description says what to do next happens in the Entra admin center (**ID Protection** > **Risky users**). entraspect never confirms or dismisses risk.

Tests with a fake reader and `FakeTimeProvider`:
- Inactive users: the boundaries as in spec 016's tests, the Critical bump for Tier 0, privileged and other roles (only Tier 0 and Privileged bump), disabled with and without roles, guests ignored, synced versus cloud.
- Disabled users with access: role tiers, a role through a group, licences, enabled users ignored.
- Risky users: every state and level, deleted users ignored, empty and `none` risk detail.
- `PrivilegedRoles.TierOf`, the `RoleHolders` helper, and Guests with admin roles unchanged after the move.
- Each check failing on its own when its read throws `DirectoryReadException`.

## Step 3: Graph implementation

- Users: add `onPremisesSyncEnabled` and `assignedLicenses` to `GetUsersAsync`'s `$select`, and map the licence count.
- Sign-in activity: spec 016's guest read without the guest filter: `GET /users?$select=id,userType,signInActivity&$top=500`, following every page. Keep spec 016's mapping of "never" values. This replaces the filtered read and its HTTP 400 fallback, which are no longer needed.
- Risky users: `GET /identityProtection/riskyUsers?$filter=riskState eq 'atRisk' or riskState eq 'confirmedCompromised'&$top=500`, following every page. If the filter is rejected, read without it and filter in code. Map defensively: unknown enum values map to `Unknown`. Map Graph's licence error through `CheckFailureMessage`, adding its error code there if it's a new one.
- All v1.0 GETs. No POST (including the riskyUser actions), no `$batch`, no beta.
- Pure mapping methods with unit tests, including unknown risk values, a missing `riskLastUpdatedDateTime` and `assignedLicenses` missing or empty.

## Step 4: Docs

- `docs/setup.md`: add `IdentityRiskyUser.Read.All` (application, needs P2) to the permissions table, and add the three checks to the rows of the permissions they use.
- `README.md`: add the three checks under Users in "What it checks".
- `AGENTS.md`: mention `PrivilegedRoles` and `RoleHolders` in Core concepts, so later checks reuse them.

## Acceptance criteria

- `dotnet build` and `dotnet test` pass with no warnings.
- No test makes a network call.
- The only new permission requested or documented is `IdentityRiskyUser.Read.All`.
- Guests with admin roles and Inactive guests give the same results as before.
- Manual check against the dev tenant:
  - A disabled test user given a licence or a role appears under Disabled users with access. Remove them afterwards.
  - Without P1 or P2, Inactive users is grayed out. Without P2, Risky users is grayed out (spec 012).
  - Without `IdentityRiskyUser.Read.All`, Risky users shows the permission banner and the other checks still run.
  - On a P1 or P2 tenant, a member with no recent sign-in appears under Inactive users. On a P2 tenant, a test user flagged by ID Protection (or confirmed compromised in the admin center) appears under Risky users.

## Open questions

(Agents: add anything you think is out of scope or ambiguous here instead of implementing it.)

- **Break-glass accounts:** emergency access accounts are meant to be unused, unlicensed and to hold Global Administrator, so Inactive users will report them as Critical. Ignoring accepted findings (a separate spec) is the general answer. Until then, should there be a list of excluded user IDs in Settings?
- **Disabled users who own apps or groups:** owners can still be relied on for approvals and credentials. Needs owner IDs, which spec 003 and 006 don't keep today.
- **Custom roles with privileged permissions:** never treated as privileged here. A custom role holding, for example, `microsoft.directory/users/password/update` is as dangerous as a built-in one.
- **Disabled for how long:** Graph has no "disabled since" date. The audit log has it, but only for 30 days.
- **Risky sign-ins and risk detections:** `GET /identityProtection/riskyServicePrincipals` (workload identities, needs Workload ID Premium) and risk detections could be their own checks.
- **Password age:** Microsoft no longer recommends forced password expiry, so old passwords aren't reported. Revisit if a benchmark asks for it.
- **Global Reader and other read-only roles are Privileged:** Microsoft marks Global Reader, Security Reader, AI Reader and Attribute Provisioning Reader PRIVILEGED, so `PrivilegedRoles` rates them Privileged as specced. An inactive or disabled Global Reader is therefore Critical in Inactive users and Disabled users with access. Should read-only roles get their own, lower tier?
- **Licence message for Risky users:** `CheckFailureMessage.LicenceRequired` says "P1 or P2". If Graph refuses Risky users for lack of P2 with a known licence error code, the message understates what is needed. The riskyUsers docs name no error code, and mapping a generic one such as `Forbidden` would turn a missing permission into a licence message, so nothing was added. The `RequiredLicence` P2 gating normally stops the check running first.
- **Friendly risk detail names:** the message shows Graph's `riskDetail` code as is (for example "Risk detail: adminGeneratedTemporaryPassword."), leaving out empty, `none`, `hidden` and `unknownFutureValue`. Newer values come back as `unknownFutureValue` because entraspect doesn't send `Prefer: include-unknown-enum-members`. A table of plain names could replace the codes.
- **Inactive users needs the role permissions to run at all:** `RoleManagement.Read.Directory` and `GroupMember.Read.All` only change severity (the Critical bump, and leaving disabled role holders to Disabled users with access), but without them the check doesn't run. It could run without them and skip the role rules.
- **Sign-in activity for every user:** Inactive guests and Inactive users share one read of every user's `signInActivity`, which Graph pages at 500, so a large tenant needs more requests than the 999-user users read. A tenant with few guests now reads every member's activity for Inactive guests too.
- **"Holds ROLE and N more roles":** N counts the user's other Tier 0 and Privileged roles only, since only those raise the severity. Other roles aren't mentioned.
- **Shared user helpers:** `GuestCheckRules` became `UserCheckRules`, with the last-activity rule that Inactive guests and Inactive users share. A disabled synced member in Inactive users also gets "This account is managed in on-premises Active Directory.", as the Hybrid tenants paragraph asks.
- **`$filter` with `or` on riskyUsers:** the docs don't say whether it is supported. If Graph rejects it with HTTP 400, entraspect reads every risky user and keeps the two states in code. Check against a P2 tenant.
