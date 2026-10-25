# Spec 016: Guest accounts

**Status:** Ready (depends on specs 005, 007, 008 and 012)
**Goal:** Three checks in the Users nav section about guest accounts: guests who haven't signed in for a long time, guest invitations that were never accepted, and guests holding directory roles.

**Placement:** these checks report user objects, so they go under **Users**, as Entra lists guests under Users. The tenant-wide guest settings are in the External Identities section (spec 015).

**Out of scope for this spec:** guest settings (spec 015), guests who own apps or groups, eligible (PIM) role assignments, access reviews, external members created by cross-tenant synchronization, deleting or disabling guests (entraspect is read-only). See Open questions.

**Graph permissions:** none new. Everything here uses permissions earlier specs added:

- `User.Read.All` (spec 007): the guests, their invitation state and creation time.
- `AuditLog.Read.All` (spec 005): `signInActivity`, which also needs Entra ID P1 or P2.
- `RoleManagement.Read.Directory` (spec 008): role assignments and role names.
- `GroupMember.Read.All` (spec 003): members of role-assignable groups.

Confirm in the Graph docs that each is still the least-privileged application permission for its endpoint.

**Why:** Microsoft's guest governance guidance is to find and remove guests who no longer need access, and inactive accounts are a common foothold. An invitation nobody accepted is usually a stale or mistyped address. A guest with a directory role is an admin your organisation doesn't control the identity of.

**Hybrid tenants:** include every user with `userType` `Guest`, whether created by invitation or synced from on-premises.

Follow AGENTS.md. Complete one step at a time, make sure `dotnet build` and `dotnet test` pass, then commit before starting the next step.

## Step 1: Core models, options and readers

- Extend `DirectoryUser` (spec 007) with `CreatedDateTime` (nullable), `ExternalUserState` (nullable enum `PendingAcceptance`, `Accepted`, `Unknown`) and `ExternalUserStateChangeDateTime` (nullable). Spec 007 and 008's checks and tests must keep working unchanged.
- Model `GuestSignInActivity`: `UserId`, `LastSuccessfulSignIn`, `LastSignIn`, `LastNonInteractiveSignIn` (all nullable). Graph returns `0001-01-01T00:00:00Z` or an empty string for "never"; map both to null.
- Model `DirectoryRoleDefinition`: `Id`, `TemplateId`, `DisplayName`.
- `IDirectoryReader`:
  - `GetGuestSignInActivityAsync(CancellationToken)`, separate so its P1 requirement and possible failure affect only the inactive guests check.
  - `GetDirectoryRoleDefinitionsAsync(CancellationToken)`.
  - Add both to `CachingDirectoryReader` and the fake readers.
- `CheckOptions`, each with its own validation rule, a Settings > Checks field and an `InfoTip`:
  - `GuestInactiveDays` (default 90, from 30 to 365): "Inactive guests reports guests with no successful sign-in for at least this many days."
  - `GuestPendingInviteDays` (default 30, at least 1): "Pending guest invitations reports invitations not accepted after this many days."

## Step 2: Checks

All three: category `Users`, `ObjectType` `User`. Labels: `ObjectDisplayName` "Display name", `UserPrincipalName` "User principal name", `ObjectId` "Object ID", plus the extra columns per check. Hide the others. Guests are users whose `UserType` is `Guest`. Dates go through `TimeDisplay`.

**`guests-inactive`** (title "Inactive guests"). Requires `User.Read.All` and `AuditLog.Read.All`, `RequiredLicence` P1 (spec 012).

- Last activity is `LastSuccessfulSignIn`. Microsoft only started recording it in December 2023 and didn't backfill it, so when it's null, use the later of `LastSignIn` and `LastNonInteractiveSignIn`. Those also count failed attempts, so the message says "last sign-in attempt" in that case.
- Last activity at least `GuestInactiveDays` UTC calendar days ago: **Warning**, "No successful sign-in for N days." (or "No sign-in attempt for N days.")
- No activity at all and created at least `GuestInactiveDays` UTC calendar days ago: **Warning**, "Never signed in since being added N days ago." A guest created more recently: no finding.
- No activity, and `CreatedDateTime` unknown: **Info**, "Never signed in."
- A disabled guest (`AccountEnabled` false) that would be reported: **Info** instead, ending "The account is disabled; consider deleting it."
- `DueDate` is the last activity, labelled "Last sign-in". `Value` (spec 015), labelled "Invitation", shows `Accepted` or `Pending`, so admins can tell never-redeemed guests apart. It is empty when Graph has no invitation state (for example synced guests) or one entraspect doesn't know. Pending guests also appear in the next check, which is fine: the two answer different questions.

**`guests-pending-invites`** (title "Pending guest invitations"). Requires `User.Read.All`, no licence.

- `ExternalUserState` `PendingAcceptance` and the invitation (`ExternalUserStateChangeDateTime`, else `CreatedDateTime`) at least `GuestPendingInviteDays` UTC calendar days old: **Info**, "Invitation not accepted after N days. Resend it, or delete the guest if it's no longer needed."
- `DueDate` is the invitation date, labelled "Invited".

**`guests-admin-roles`** (title "Guests with admin roles"). Requires `User.Read.All`, `RoleManagement.Read.Directory` and `GroupMember.Read.All`, no licence.

- One finding per guest per active role assignment, directly or through a role-assignable group (use `GetGroupTransitiveUserIdsAsync` for group principals, as spec 008 does): **Critical**, "Guest holds the ROLE role. Their identity is managed by another organisation."
- Every directory role counts, built-in or custom, at any scope; the message names the role. `SubObjectDisplayName` is the role name, labelled "Role". Fall back to the role definition ID when the name isn't found.
- Spec 005 also flags a guest admin who lacks MFA. The overlap is intended: this check is about the role, that one about MFA.

Tests with a fake reader and `FakeTimeProvider`: the inactive boundaries (exactly N days, N+1, never signed in, created recently, unknown creation date, `LastSuccessfulSignIn` null with older attempts), disabled guests, members ignored, pending versus accepted, a guest admin through a direct assignment and through a group, a custom role, a missing role name, the option validation rules, and each check failing on its own when its read throws `DirectoryReadException`.

## Step 3: Graph implementation

- Users: add `createdDateTime`, `externalUserState` and `externalUserStateChangeDateTime` to spec 007's `GetUsersAsync` `$select`.
- Sign-in activity: `GET /users?$filter=userType eq 'Guest'&$select=id,signInActivity&$top=500`, following every page. The docs cap pages at 500 when `signInActivity` is selected. Confirm against the tenant that the `userType` filter is accepted together with `$select=signInActivity`. If it isn't, select `id,userType,signInActivity` for all users and filter in code. Don't add `ConsistencyLevel` unless Graph requires it. Map the licence error to `MissingLicence` or `CheckFailureMessage` as spec 012 does.
- Role definitions: `GET /roleManagement/directory/roleDefinitions?$select=id,templateId,displayName`, following every page. Spec 008's role assignments use `roleDefinitionId`, which for built-in roles is the template ID, so match on both `id` and `templateId`.
- All v1.0 GETs. No POST, no `$batch`, no beta.
- Pure mapping methods with unit tests, including the "never" sign-in values and unknown `externalUserState`.

## Step 4: Docs

- `docs/setup.md`: add the three checks to the permission table rows they use, and note that Inactive guests needs P1 or P2.
- `README.md`: add the three checks under Users in "What it checks".

## Acceptance criteria

- `dotnet build` and `dotnet test` pass with no warnings.
- No test makes a network call.
- No permissions beyond those from earlier specs are requested or documented.
- Manual check against the dev tenant:
  - A guest invited and never accepted appears under Pending guest invitations once older than the setting. Set it to 1 day to test.
  - A guest given a directory role (for example Directory Readers) appears under Guests with admin roles as Critical. Remove the role afterwards.
  - On a P1 or P2 tenant, a guest with no recent sign-in appears under Inactive guests. Without P1 or P2, the check is grayed out as in spec 012.
  - Changing the two options on Settings > Checks changes the results of the next scan.

## Open questions

(Agents: add anything you think is out of scope or ambiguous here instead of implementing it.)

- **Invitation column (decided during implementation):** the spec first put the invitation state in `SubObjectType`, which is meant for the type of an item inside the object. It uses `Value` from spec 015 instead, labelled "Invitation".
- **Pending invitation with no date (decided during implementation):** when Graph returns neither `externalUserStateChangeDateTime` nor `createdDateTime`, Pending guest invitations reports the guest as **Info**, "Invitation not accepted. Resend it, or delete the guest if it's no longer needed.", with no `DueDate`. Is that right, or should such guests be skipped?
- **Disabled guests in Pending guest invitations and Guests with admin roles (decided during implementation):** reported unchanged, with the same severity and message as enabled guests: a disabled guest's invitation can still be redeemed once the account is enabled, and a role assigned to a disabled guest is still assigned. Worth a lower severity or a note?
- **One finding per role (decided during implementation):** Guests with admin roles reports one finding per guest per distinct role, not per assignment. The same role assigned directly and through a group, or at two scopes, would otherwise give identical rows, since findings don't show the scope or the path. Show the scope or the group the role comes through?
- **Eligible (PIM) role assignments:** a guest eligible for a role can activate it. Reading eligibility needs `RoleEligibilitySchedule.Read.Directory` and P2. Worth adding to Guests with admin roles?
- **Guests who own apps or groups:** owners can add credentials to apps or members to groups. Spec 003 and 006 already read owners, so this may need only owner IDs and user types.
- **External members from cross-tenant synchronization:** synced users from other tenants can have `userType` `Member`. They are external, but these checks skip them. Identify them by `creationType` or the `#EXT#` UPN pattern?
- **Guests whose home tenant or domain no longer exists:** not detectable without lookups outside Graph.
- **Access reviews:** whether any recurring review covers guests. Needs `AccessReview.Read.All` and P2.
