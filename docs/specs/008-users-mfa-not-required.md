# Spec 008: Users not required to use MFA

**Status:** Ready (depends on specs 003 and 007)
**Goal:** A check in the Users nav section that lists enabled users whom no enforced Conditional Access policy requires to use multifactor authentication.

This check answers "who isn't required to use MFA". Spec 005 answers the separate question "who isn't registered for MFA". A user can be registered but never asked, or required but not yet registered. The check page description must say which question this check answers.

**Approach:** evaluate the tenant's Conditional Access policies against each user (v1.0). For users that no policy fully covers, also read two things that are only in beta:

- Per-user (legacy) MFA state, `GET /beta/users/{id}/authentication/requirements`, so users with per-user MFA aren't reported as not required.
- Recent successful single-factor sign-ins, from `GET /beta/auditLogs/signIns` (`authenticationRequirement` isn't in v1.0), as evidence in the finding message.

**Out of scope for this spec:** simulating sign-in conditions (the What If tool), PIM-eligible role assignments, authentication strength comparison (any authentication strength counts as requiring MFA), workload identities, non-interactive sign-ins, flagging users whom policy covers but who still signed in with a single factor.

**Graph permissions:** adds `RoleManagement.Read.Directory` (application). Also uses `Policy.Read.All` and `User.Read.All` (spec 007), `GroupMember.Read.All` (spec 003) and `AuditLog.Read.All` (spec 005). `Policy.Read.All` is the least-privileged application permission for per-user MFA state, and `AuditLog.Read.All` for beta sign-ins. Conditional Access and sign-in logs need Entra ID P1 or P2.

Follow AGENTS.md. Complete one step at a time, make sure `dotnet build` and `dotnet test` pass, then commit before starting the next step.

## Step 1: Core models and readers

- Extend `ConditionalAccessPolicy` (spec 007) with:
  - `ExcludeRoles`, `ExcludeApplications`.
  - `GuestTypes` for included and excluded guests (from `includeGuestsOrExternalUsers` and `excludeGuestsOrExternalUsers`), and whether they are limited to specific external tenants.
  - `GrantBuiltInControls`, `GrantOperator` (`AND` or `OR`), `HasAuthenticationStrength`.
  - `HasNarrowingConditions`: true when the policy sets any of locations, platforms, user risk levels, sign-in risk levels, a device filter, or client app types other than `all` (or other than a set containing both `browser` and `mobileAppsAndDesktopClients`).
- Model `DirectoryUser`: `Id`, `UserPrincipalName`, `DisplayName`, `UserType`, `AccountEnabled`.
- Model `DirectoryRoleAssignment`: `RoleDefinitionId`, `PrincipalId`. Only active assignments.
- New `IDirectoryReader` methods, all added to `CachingDirectoryReader` too:
  - `GetUsersAsync(CancellationToken)`
  - `GetDirectoryRoleAssignmentsAsync(CancellationToken)`
  - `GetGroupTransitiveUserIdsAsync(string groupId, CancellationToken)`, cached per group ID
  - `GetSecurityDefaultsEnabledAsync(CancellationToken)`
  - `GetPerUserMfaStateAsync(string userId, CancellationToken)`, returning `Disabled`, `Enabled`, `Enforced` or `Unknown`, cached per user ID
  - `GetSingleFactorSignInsAsync(DateTimeOffset since, CancellationToken)`, returning, per user ID, the number of successful single-factor interactive sign-ins and the most recent one. Aggregate while reading, like spec 009, so sign-ins are never held in memory all at once.

## Step 2: Policy evaluation in Core

Add a pure `MfaRequirementEvaluator`. For one user, it returns the policies that fully require MFA, the policies that require MFA only in some cases, and the report-only policies that would require MFA.

- A policy **requires MFA** when its grant controls need it: `mfa` in `GrantBuiltInControls`, or `HasAuthenticationStrength`. If `GrantOperator` is `OR` and another control is listed (for example `compliantDevice`), the user can satisfy the policy without MFA; count it as "in some cases".
- A user is **in scope** when the policy includes them (`All`, their ID, a group they are a transitive member of, a role they hold through an active assignment, directly or through a role-assignable group, or a matching guest type) and doesn't exclude them in any of those ways. Exclusion wins over inclusion.
- A policy that requires MFA and has the user in scope **fully requires** MFA only when it is enabled, includes `All` applications, excludes no applications, has no user actions or authentication contexts, and `HasNarrowingConditions` is false. Otherwise it requires MFA "in some cases".
- Skip policies that target service principals.
- A guest policy limited to specific external tenants counts as "in some cases", since entraspect doesn't know each guest's home tenant.

Tests for each rule: include and exclude by user, group, nested group, role and guest type; exclusion winning; `AND` and `OR` grants; authentication strength; each narrowing condition; excluded apps; report-only; disabled policies ignored; and service principal policies ignored.

## Step 3: The check

Add `CheckOptions.SignInLookbackDays` (default 7, range 1 to 30), shown in Settings > Checks with a note that longer periods are slower in busy tenants. Spec 009 uses the same option.

**`users-mfa-not-required`** (category `Users`, title "MFA not required").

- If security defaults are on, give no per-user findings. Give one **Info** finding with `ObjectType` `Tenant`: "Security defaults are on. All users must register for MFA and are asked for it when Microsoft considers it necessary; admins are asked at every sign-in." Conditional Access can't be used while security defaults are on.
- Skip disabled users.
- Fully required by at least one enabled policy: no finding.
- Otherwise read the user's per-user MFA state. `Enabled` or `Enforced`: **Info**, "MFA is required by legacy per-user MFA, not Conditional Access. Microsoft recommends moving to Conditional Access." `Unknown`: carry on as `Disabled`, and add "Per-user MFA state couldn't be read." to the message.
- Otherwise the severity comes from the user: holds any active directory role, **Critical**; member, **Warning**; guest, **Info**. Messages:
  - Not required by any policy: "No enforced Conditional Access policy requires MFA for this user."
  - Only in some cases: "MFA is only required in some cases, by: policy names." Severity is one level lower (Critical becomes Warning, Warning becomes Info).
  - If report-only policies would require it, add "Report-only policies that would require MFA: policy names."
  - If the user had successful single-factor sign-ins in the lookback period, add "N successful single-factor sign-ins in the last D days, most recent DATE." The sign-ins don't change the severity.
- Read sign-ins only when at least one finding needs them, and read them once per scan.
- `ObjectType` `User`, `ObjectDisplayName` "Display name (UPN)", no sub-object, no `DueDate`. Required permissions: `Policy.Read.All`, `User.Read.All`, `GroupMember.Read.All`, `RoleManagement.Read.Directory`, `AuditLog.Read.All`. The check page says the per-user MFA state and sign-in data come from beta Graph.

Tests: security defaults on, disabled user skipped, admin, member and guest with no policy, "in some cases" severities, report-only note, a user covered by one full policy, per-user MFA `Enabled`, `Enforced` and `Unknown`, sign-in evidence in the message, and no sign-in read when there are no findings.

## Step 4: Graph implementation

- Users: `GET /users` with `$select=id,displayName,userPrincipalName,userType,accountEnabled`, `$top=999`, following every page.
- Role assignments: `GET /roleManagement/directory/roleAssignments` with `$select=roleDefinitionId,principalId`, following every page. Policies refer to built-in roles by template ID, which equals the `roleDefinitionId` for built-in roles. When the principal is a group, expand it with the transitive members call.
- Transitive members: `GET /groups/{id}/transitiveMembers/microsoft.graph.user?$select=id`, following every page, only for groups that policies or role assignments refer to.
- Security defaults: `GET /policies/identitySecurityDefaultsEnforcementPolicy`, reading `isEnabled`.
- Per-user MFA (beta): `GET /beta/users/{id}/authentication/requirements`, reading `perUserMfaState`. One call per user, only for users no policy fully covers. Report progress. A missing or unrecognised value maps to `Unknown`.
- Single-factor sign-ins (beta): `GET /beta/auditLogs/signIns?$filter=createdDateTime ge {since}` with `$top=1000`, following every page, keeping sign-ins with `authenticationRequirement` `singleFactorAuthentication` and `status/errorCode` 0. Only `userId`, `createdDateTime`, `authenticationRequirement` and `status` are needed; use `$select` if beta accepts it. Before building, check whether beta accepts `authenticationRequirement` and `status/errorCode` in `$filter`; if it does, filter on the server instead. Check the cancellation token between pages.
- Beta calls follow the AGENTS.md rules for beta. Mapping tests with sample beta JSON, including missing and unknown values.
- Add the grant controls and conditions to the spec 007 policy mapping. Mapping tests with sample Graph objects, including an `OR` grant, an authentication strength, guest settings and each narrowing condition.
- Before building, confirm in the Graph docs that `RoleManagement.Read.Directory` (application) is the least-privileged permission for listing role assignments, and `Policy.Read.All` for reading security defaults.
- Don't use `POST /$batch` or `POST /directoryObjects/getByIds`.

## Step 5: Docs

- `docs/setup.md`: add `RoleManagement.Read.Directory` (application) with admin consent.
- `README.md`: add the check to "What it checks", saying it reports what Conditional Access and per-user MFA require, with recent single-factor sign-ins as evidence, and that part of it uses beta Graph.

## Acceptance criteria

- `dotnet build` and `dotnet test` pass with no warnings.
- No test makes a network call.
- Only permissions from earlier specs plus `RoleManagement.Read.Directory` are requested or documented.
- Manual check (P1 or P2 tenant), with a test policy that requires MFA for all users and all apps and excludes a test group:
  - A member in the test group appears as Warning; after removing them from the group and scanning again, they don't.
  - A test user assigned a directory role and excluded from the policy appears as Critical.
  - Changing the policy to apply only outside a named location makes users appear as "in some cases".
  - Changing the policy to report-only makes users appear with the report-only note.
  - A test user excluded from the policy with per-user MFA enforced appears as Info with the per-user MFA message.
  - A test user excluded from the policy who signs in with a password only shows the single-factor sign-in count after the sign-in reaches the logs.

## Open questions

(Agents: add anything you think is out of scope or ambiguous here instead of implementing it.)

- **Covered but single-factor:** a user that policy fully covers can still have single-factor sign-ins (for example to an app a policy excludes, or through a gap in the evaluation). Should that be its own finding?
- **Shared sign-in read:** this check and spec 009 both read the sign-in logs. Should one read feed both?
- **Per-user MFA calls:** one call per uncovered user can be slow in large tenants with weak policy coverage.
- **PIM-eligible roles:** users who can activate an admin role through PIM are treated as non-admins until they activate it. Reading eligibility needs `RoleEligibilitySchedule.Read.Directory`.
- **Authentication strengths:** every authentication strength counts as MFA. A custom strength that allows only single-factor methods would be counted wrongly.
- **Large tenants:** reading every user and expanding groups may be slow. Should the check be limited, or show progress per group?
