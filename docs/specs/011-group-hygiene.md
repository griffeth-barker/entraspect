# Spec 011: More group hygiene checks

**Status:** Ready (depends on specs 002 and 003)
**Goal:** Five more checks in the Groups nav section, built on the group data spec 003 already reads: dynamic groups with paused processing, groups with on-premises sync errors, groups with licence assignment errors, groups with only one owner, and public Microsoft 365 groups.

**Out of scope for this spec:** role-assignable group risks (owners are covered by spec 019), dynamic rule processing errors (beta only), groups whose members are all disabled, group expiration policy, external senders, inactive groups. See Open questions.

**Graph permissions:** none new. Everything comes from `GET /groups`, which `GroupMember.Read.All` (spec 003) already covers. Group-based licensing needs Entra ID P1 or P2.

**Hybrid tenants:** many organisations sync groups from on-premises Active Directory. Include synced groups wherever Graph gives usable data, as in spec 003.

Follow AGENTS.md. Complete one step at a time, make sure `dotnet build` and `dotnet test` pass, then commit before starting the next step.

## Step 1: Core model and reader

Extend `DirectoryGroup` (spec 003):

- `Visibility`: nullable `GroupVisibility` (`Public`, `Private`, `HiddenMembership`). Null when Graph returns no value or an unknown one.
- `IsDynamicProcessingPaused`: true when `membershipRuleProcessingState` is `Paused` (case-insensitive). False for groups that aren't dynamic.
- `SyncErrors`: list of `GroupSyncError` (`Category`, `PropertyCausingError`, `Value`, `OccurredDateTime` nullable), from `onPremisesProvisioningErrors`. Empty when there are none.
- `OwnerCount`: nullable int, null in exactly the cases where `HasOwners` is null today. Graph returns at most 20 expanded owners, so 20 means "20 or more". `HasOwners` becomes a computed property, `OwnerCount > 0`, so spec 003's checks and tests keep working unchanged.

Add `IDirectoryReader.GetGroupIdsWithLicenseErrorsAsync(CancellationToken)`, returning the IDs of groups whose members have licence assignment errors. It is a separate method so that a failure here fails only the licence check. Add it to `CachingDirectoryReader` and the fake readers too.

## Step 2: Checks

All five checks: category `Groups`, `ObjectType` `Group`, required permission `GroupMember.Read.All`. Register them in `CheckCatalog` after spec 003's two checks, in the order below. Columns are named after the group's properties: "Display name" and "Object ID", plus the extra columns listed per check. Hide the other columns.

**`group-single-owner`** (title "Groups with one owner"):

- Microsoft 365 group with `OwnerCount` 1: **Info**, "Microsoft 365 group has only one owner. If they leave, no one can manage its membership."
- Security group with `OwnerCount` 1, including synced security groups: **Info**, "Security group has only one owner."
- `OwnerCount` null, 0 or more than 1: no finding. Zero owners is reported by `group-no-owners`.
- Skips groups inside the new group grace period (`CheckOptions.NewGroupGraceDays`), like spec 003.

**`group-public`** (title "Public groups"):

- Microsoft 365 group with `Visibility` `Public`: **Info**, "Anyone in the organisation can join this Microsoft 365 group and read its content."
- Other kinds and visibilities: no finding.

**`group-dynamic-paused`** (title "Paused dynamic groups"):

- Dynamic group with `IsDynamicProcessingPaused`: **Warning**, "Dynamic membership processing is paused, so members are no longer added or removed."

**`group-license-errors`** (title "Licence errors"):

- Group whose ID `GetGroupIdsWithLicenseErrorsAsync` returns: **Warning**, "Some members of this security group didn't get the licences it assigns." The message names the group kind, as in spec 003.

**`group-sync-errors`** (title "Sync errors"):

- One finding per entry in `SyncErrors`: **Warning**, "On-premises sync error on PROPERTY: VALUE." (`PROPERTY` and `VALUE` from the error; leave out the parts that are empty.)
- `SubObjectType` is the error category (for example `PropertyConflict`), `SubObjectDisplayName` the property causing the error, `DueDate` the time the error occurred. Labels: `SubObjectDisplayName` "Property", `SubObjectType` "Category", `DueDate` "Occurred". `OccurredDateTime` goes through `TimeDisplay` like every date.

Tests with a fake reader and `FakeTimeProvider`: each kind, synced groups (single-owner and sync errors), the grace period boundary for single owner, the 20-owner cap (no finding), visibility values including null, a paused group that isn't dynamic (no finding), a group with several sync errors (one finding each), empty error fields, and the licence check failing on its own when its read throws `DirectoryReadException`.

## Step 3: Graph implementation

- Add `membershipRuleProcessingState`, `visibility` and `onPremisesProvisioningErrors` to the `$select` of spec 003's group listing.
- Owner count: take it from spec 003's owners `$expand` listing (count of expanded owners, at most 20) instead of a yes or no.
- Licence errors: `GET /groups?$filter=hasMembersWithLicenseErrors eq true&$select=id`, following every page. The List groups docs show this filter, without `ConsistencyLevel` or `$count`. `hasMembersWithLicenseErrors` is never returned in a response, so it can only be used as a filter.
- No new permissions, no POST, no `$batch`.
- Keep mapping (visibility, paused state, sync errors) in small pure methods with unit tests using sample Graph `Group` objects, including unknown visibility and paused-state values.

## Step 4: Docs

- `docs/setup.md`: add the five checks to the `GroupMember.Read.All` row of the permissions table.
- `README.md`: add the five checks to "What it checks".

## Acceptance criteria

- `dotnet build` and `dotnet test` pass with no warnings.
- No test makes a network call.
- No permissions beyond those from earlier specs are requested or documented.
- Manual check: a Microsoft 365 group with one owner appears under Groups with one owner, a public Microsoft 365 group under Public groups, and a dynamic group whose processing has been paused under Paused dynamic groups.
- Licence errors and sync errors may not be reproducible in the dev tenant (sync errors need a hybrid tenant). If so, say so when closing the spec; their unit tests stand in.

## Open questions

(Agents: add anything you think is out of scope or ambiguous here instead of implementing it.)

- **Role-assignable group risks (owners: see [spec 019](019-role-group-owners.md)):** owners of a group that can be assigned to Entra roles can add themselves to it, and so effectively hold its roles. A guest in such a group is also risky. Needs owner and member IDs, user types (`User.Read.All`) and the group's role assignments (`RoleManagement.Read.Directory`).
- **Dynamic rule processing errors:** only beta exposes rule processing failures (`membershipRuleProcessingStatus`). Would need a beta exception named in a spec.
- **Groups whose members are all disabled:** effectively empty, like spec 003's question about owners who can't act. Needs `User.Read.All`.
- **Expiring Microsoft 365 groups:** `expirationDateTime` combined with the expiration policy, especially for groups with no owners to renew them.
- **External senders allowed:** `allowExternalSenders` is only returned by `GET /groups/{id}`, so it needs one call per group.
- **Paused processing on purpose:** admins sometimes pause dynamic processing on purpose, for example while changing a rule. Should the check skip groups paused recently? Graph has no "paused since" date in v1.0.
- **Public security groups:** `visibility` is mainly for Microsoft 365 groups. Is `Public` on a security group meaningful enough to report?
- **Navigation order (decided in step 2):** step 2 asks for the five checks after spec 003's two in the order listed, but AGENTS.md and `CheckCatalogTests` sort checks by title within each section. They follow the title order: Groups with no members, Groups with no owners, Groups with one owner, Licence errors, Paused dynamic groups, Public groups, Sync errors.
