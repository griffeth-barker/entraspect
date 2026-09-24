# Spec 008: Users not required to use MFA

**Status:** Ready (depends on specs 003 and 007)
**Goal:** A check in the Users nav section that lists enabled users whom no enforced Conditional Access policy requires to use multifactor authentication.

This check answers "who isn't required to use MFA". Spec 005 answers the separate question "who isn't registered for MFA". A user can be registered but never asked, or required but not yet registered. The check page description must say which question this check answers.

**Approach:** evaluate the tenant's Conditional Access policies against each user, using Graph v1.0 only. The sign-in log's `authenticationRequirement` property would show what happened at real sign-ins, but it is beta only, and per-user (legacy) MFA state is beta only too; both are in Open questions. This check reports what policy says, not what happened at sign-in.

**Out of scope for this spec:** per-user (legacy) MFA, simulating sign-in conditions (the What If tool), PIM-eligible role assignments, authentication strength comparison (any authentication strength counts as requiring MFA), workload identities.

**Graph permissions:** adds `RoleManagement.Read.Directory` (application). Also uses `Policy.Read.All` and `User.Read.All` (spec 007) and `GroupMember.Read.All` (spec 003). Conditional Access needs Entra ID P1 or P2.

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

## Step 2: Policy evaluation in Core

Add a pure `MfaRequirementEvaluator`. For one user, it returns the policies that fully require MFA, the policies that require MFA only in some cases, and the report-only policies that would require MFA.

- A policy **requires MFA** when its grant controls need it: `mfa` in `GrantBuiltInControls`, or `HasAuthenticationStrength`. If `GrantOperator` is `OR` and another control is listed (for example `compliantDevice`), the user can satisfy the policy without MFA; count it as "in some cases".
- A user is **in scope** when the policy includes them (`All`, their ID, a group they are a transitive member of, a role they hold through an active assignment, directly or through a role-assignable group, or a matching guest type) and doesn't exclude them in any of those ways. Exclusion wins over inclusion.
- A policy that requires MFA and has the user in scope **fully requires** MFA only when it is enabled, includes `All` applications, excludes no applications, has no user actions or authentication contexts, and `HasNarrowingConditions` is false. Otherwise it requires MFA "in some cases".
- Skip policies that target service principals.
- A guest policy limited to specific external tenants counts as "in some cases", since entraspect doesn't know each guest's home tenant.

Tests for each rule: include and exclude by user, group, nested group, role and guest type; exclusion winning; `AND` and `OR` grants; authentication strength; each narrowing condition; excluded apps; report-only; disabled policies ignored; and service principal policies ignored.

## Step 3: The check

**`users-mfa-not-required`** (category `Users`, title "Users not required to use MFA").

- If security defaults are on, give no per-user findings. Give one **Info** finding with `ObjectType` `Tenant`: "Security defaults are on. All users must register for MFA and are asked for it when Microsoft considers it necessary; admins are asked at every sign-in." Conditional Access can't be used while security defaults are on.
- Skip disabled users.
- Fully required by at least one enabled policy: no finding.
- Otherwise the severity comes from the user: holds any active directory role, **Critical**; member, **Warning**; guest, **Info**. Messages:
  - Not required by any policy: "No enforced Conditional Access policy requires MFA for this user."
  - Only in some cases: "MFA is only required in some cases, by: policy names." Severity is one level lower (Critical becomes Warning, Warning becomes Info).
  - If report-only policies would require it, add "Report-only policies that would require MFA: policy names."
- `ObjectType` `User`, `ObjectDisplayName` "Display name (UPN)", no sub-object, no `DueDate`. Required permissions: `Policy.Read.All`, `User.Read.All`, `GroupMember.Read.All`, `RoleManagement.Read.Directory`.

Tests: security defaults on, disabled user skipped, admin, member and guest with no policy, "in some cases" severities, report-only note, and a user covered by one full policy.

## Step 4: Graph implementation

- Users: `GET /users` with `$select=id,displayName,userPrincipalName,userType,accountEnabled`, `$top=999`, following every page.
- Role assignments: `GET /roleManagement/directory/roleAssignments` with `$select=roleDefinitionId,principalId`, following every page. Policies refer to built-in roles by template ID, which equals the `roleDefinitionId` for built-in roles. When the principal is a group, expand it with the transitive members call.
- Transitive members: `GET /groups/{id}/transitiveMembers/microsoft.graph.user?$select=id`, following every page, only for groups that policies or role assignments refer to.
- Security defaults: `GET /policies/identitySecurityDefaultsEnforcementPolicy`, reading `isEnabled`.
- Add the grant controls and conditions to the spec 007 policy mapping. Mapping tests with sample Graph objects, including an `OR` grant, an authentication strength, guest settings and each narrowing condition.
- Before building, confirm in the Graph docs that `RoleManagement.Read.Directory` (application) is the least-privileged permission for listing role assignments, and `Policy.Read.All` for reading security defaults.
- Don't use `POST /$batch` or `POST /directoryObjects/getByIds`.

## Step 5: Docs

- `docs/setup.md`: add `RoleManagement.Read.Directory` (application) with admin consent.
- `README.md`: add the check to "What it checks", saying it reports what Conditional Access policy requires, not what happened at sign-in.

## Acceptance criteria

- `dotnet build` and `dotnet test` pass with no warnings.
- No test makes a network call.
- Only permissions from earlier specs plus `RoleManagement.Read.Directory` are requested or documented.
- Manual check (P1 or P2 tenant), with a test policy that requires MFA for all users and all apps and excludes a test group:
  - A member in the test group appears as Warning; after removing them from the group and scanning again, they don't.
  - A test user assigned a directory role and excluded from the policy appears as Critical.
  - Changing the policy to apply only outside a named location makes users appear as "in some cases".
  - Changing the policy to report-only makes users appear with the report-only note.

## Open questions

(Agents: add anything you think is out of scope or ambiguous here instead of implementing it.)

- **Beta sign-in data:** `authenticationRequirement` on beta `signIns` shows whether sign-ins actually needed MFA. It could confirm the policy evaluation, but needs beta Graph. Should entraspect allow beta endpoints for specific checks?
- **Per-user MFA:** users with legacy per-user MFA enforced are required to use MFA even without a policy. Their state is only in beta (`/users/{id}/authentication/requirements`), so they may appear as false positives.
- **PIM-eligible roles:** users who can activate an admin role through PIM are treated as non-admins until they activate it. Reading eligibility needs `RoleEligibilitySchedule.Read.Directory`.
- **Authentication strengths:** every authentication strength counts as MFA. A custom strength that allows only single-factor methods would be counted wrongly.
- **Large tenants:** reading every user and expanding groups may be slow. Should the check be limited, or show progress per group?
