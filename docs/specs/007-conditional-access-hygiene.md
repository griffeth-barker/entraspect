# Spec 007: Conditional Access policy hygiene

**Status:** Ready (depends on specs 002 and 003)
**Goal:** A check in the Conditional Access nav section that finds policies that are switched off, stuck in report-only, can never apply, or refer to users or groups that no longer exist.

**Out of scope for this spec:** whether a policy applied to real sign-ins (spec 009), overlapping or conflicting policies, coverage gaps (for example "no policy requires MFA for admins"), named locations, authentication strengths, references to applications and directory roles.

**Graph permissions:** adds `Policy.Read.All` and `User.Read.All` (application). Also uses `GroupMember.Read.All` from spec 003. `User.ReadBasic.All` would be enough for the lookups, but it is delegated only, so app-only auth needs `User.Read.All`. Conditional Access needs Entra ID P1 or P2.

Follow AGENTS.md. Complete one step at a time, make sure `dotnet build` and `dotnet test` pass, then commit before starting the next step.

## Step 1: Core models and readers

- Model `ConditionalAccessPolicy`: `Id`, `DisplayName`, `State` (`Enabled`, `Disabled`, `ReportOnly`), `CreatedDateTime`, `ModifiedDateTime` (both nullable), `IncludeUsers`, `ExcludeUsers`, `IncludeGroups`, `ExcludeGroups`, `IncludeRoles`, `IncludeGuestsOrExternalUsers` (bool), `IncludeApplications`, `IncludeUserActions`, `IncludeAuthenticationContexts`, `TargetsServicePrincipals` (bool, from `clientApplications`).
- `IDirectoryReader.GetConditionalAccessPoliciesAsync(CancellationToken)`.
- `IDirectoryReader.GetMissingUserIdsAsync(IReadOnlyCollection<string> userIds, CancellationToken)`: returns the IDs that no longer exist.
- Both added to `CachingDirectoryReader` too. Missing groups come from `GetGroupsAsync` (spec 003), so no extra group lookups are needed.

## Step 2: The check

Add `CheckOptions.ReportOnlyWarningDays` (default 30), shown in Settings > Checks with validation (`>= 0`).

**`ca-policy-hygiene`** (category `ConditionalAccess`, title "Policy hygiene"). One policy can have several findings, one per rule. Special values in the ID lists (`All`, `None`, `GuestsOrExternalUsers`, `Office365`, `MicrosoftAdminPortals`) are not object IDs.

- **Disabled:** **Info**, "Policy is disabled. Last changed DATE." `SubObjectType` `Disabled`.
- **Report-only too long:** report-only and not modified for more than `ReportOnlyWarningDays` UTC calendar days (use `CreatedDateTime` if `ModifiedDateTime` is empty): **Warning**, "Policy has been in report-only mode for N days and is not enforced." `SubObjectType` `Report-only`.

The rules below apply only to enabled and report-only policies.

- **Targets no users:** `IncludeUsers` is empty or only `None`, and `IncludeGroups`, `IncludeRoles` and `IncludeGuestsOrExternalUsers` are all empty, and `TargetsServicePrincipals` is false: **Warning**, "Policy doesn't include any users, so it never applies." `SubObjectType` `No users`.
- **Targets no apps:** `IncludeApplications` is empty or only `None`, with no user actions and no authentication contexts: **Warning**, "Policy doesn't include any apps, so it never applies." `SubObjectType` `No apps`.
- **Only empty groups:** the policy includes users only through groups (no users, roles or guests), and every included group has no members according to `GetGroupsAsync`: **Warning**, "Every group this policy includes is empty, so it applies to no one." `SubObjectType` `Empty groups`.
- **Deleted objects:** one finding per included or excluded user or group ID that no longer exists. Included: **Warning**, "Policy includes a user (or group) that has been deleted: ID." Excluded: **Info**, "Policy excludes a user (or group) that has been deleted: ID; remove it from the policy." `SubObjectType` `Deleted reference`, `SubObjectDisplayName` the ID.

`ObjectType` `ConditionalAccessPolicy`. Required permissions: `Policy.Read.All`, `User.Read.All`, `GroupMember.Read.All`.

Tests for every rule, the special values, the report-only boundary, a policy with several findings, and service principal policies not flagged as targeting no users.

## Step 3: Graph implementation

- `GET /identity/conditionalAccess/policies`, following every page.
- Missing users: for each distinct user ID in the policies, `GET /users/{id}?$select=id`. A 404 means the user is deleted. Any other error fails the read as usual; don't treat it as "deleted". Report progress, since this is one call per referenced user.
- Don't use `POST /directoryObjects/getByIds` or `POST /$batch`. They are POST endpoints, forbidden by AGENTS.md even though they only read.
- Keep mapping in small pure methods with unit tests using sample Graph `ConditionalAccessPolicy` objects, including the special values.

## Step 4: Docs

- `docs/setup.md`: add `Policy.Read.All` and `User.Read.All` (application) with admin consent, and the licence note.
- `README.md`: add the check to "What it checks".

## Acceptance criteria

- `dotnet build` and `dotnet test` pass with no warnings.
- No test makes a network call.
- Only permissions from earlier specs plus `Policy.Read.All` and `User.Read.All` are requested or documented.
- Manual check with test policies: a disabled policy (Info), a report-only policy with `ReportOnlyWarningDays` set to 0 (Warning), a policy that only includes an empty group (Warning), and a policy that excludes a user who was then deleted (Info).

## Open questions

(Agents: add anything you think is out of scope or ambiguous here instead of implementing it.)

- **Microsoft-managed policies:** Microsoft creates some policies automatically, often in report-only mode. Can they be identified from Graph v1.0, and should they be excluded from the report-only rule?
- **Application references:** a policy can include an app that no longer has a service principal in the tenant. Some Microsoft apps are valid targets without one, so this could cause false positives.
- **Directory role references:** `IncludeRoles` holds role template IDs; roles with no active members are like empty groups but need role membership reads (`RoleManagement.Read.Directory`).
- **Deleted users still in the recycle bin:** a soft-deleted user returns 404 from `/users/{id}`, so it counts as deleted even though it can be restored for 30 days.
- **Decision (step 1): application filters.** A policy can include apps through an application filter (custom security attributes) instead of listing them in `includeApplications`. The model has `IncludeApplicationsByFilter` so such a policy isn't reported as targeting no apps.
- **Decision (step 1): progress for user lookups.** `GetMissingUserIdsAsync` takes an optional `IProgress<int>` (IDs looked up so far) before the `CancellationToken`, so the scan bar can show how far the lookups are.
- **Decision (step 2): report-only boundary.** A report-only policy is a Warning when it has been unchanged for *at least* `ReportOnlyWarningDays` UTC calendar days, not "more than", so that 0 flags every report-only policy, as the manual check expects (a test policy made today is 0 days old). This matches `NewGroupGraceDays`, where 0 checks every group.
- **Decision (step 2): dates in their own column.** The disabled message is "Policy is disabled." without the date. Every finding has the policy's modified date (created date if never modified) in the "Modified date" column, so it goes through `TimeDisplay` like other dates.
- **Decision (step 2): columns.** "Policy name", "Policy ID", "Issue" (the `SubObjectType` values above), "Deleted object ID" and "Modified date".
- **Decision (step 2): deleted and empty groups.** For the empty groups rule a deleted included group counts as applying to no one, so a policy that includes one empty and one deleted group gets both findings. A policy whose included groups are all deleted gets only the deleted reference findings.
- **Decision (step 2): progress.** `ICheck` has a second `RunAsync` overload with an `IProgress<string>`, which the scan calls (by default it calls the plain one). This check reports "looking up user N of M", shown in the scan bar as "Running check 9 of 9: Policy hygiene (looking up user 4 of 20)".
- **Hidden group membership:** a Microsoft 365 group with hidden membership looks empty without `Member.Read.Hidden`, which this check doesn't require, so it could be reported as an empty group. Should the check require `Member.Read.Hidden` too, as Groups with no members does?
- **Licence error code:** I couldn't confirm which Graph error code `GET /identity/conditionalAccess/policies` returns on a tenant without P1 or P2. Only `Authentication_RequestFromNonPremiumTenantOrB2CTenant` is mapped in `CheckFailureMessage`; if the dev tenant shows another code, add it there.
- **`Policy.Read.ConditionalAccess`:** the permissions reference lists this narrower application permission, but the List policies page names only `Policy.Read.All`. If Microsoft documents it for the endpoint, switch to it.
