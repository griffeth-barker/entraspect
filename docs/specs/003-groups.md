# Spec 003: Groups with no members and groups with no owners

**Status:** Ready (depends on spec 002)
**Goal:** Two checks in the Groups nav section: groups that have no members, and groups that have no owners.

**Out of scope for this spec:** nested or transitive membership, owners who are disabled or deleted, dynamic distribution groups (not returned by `GET /groups`), group expiration policy.

**Graph permissions:** adds `GroupMember.Read.All` (application). The Graph docs list `Group-NestingSupport.ReadWrite.All` as the least-privileged permission for listing groups; it is a write permission and must not be used. `GroupMember.Read.All` also covers listing groups, members and owners.

Follow AGENTS.md. Complete one step at a time, make sure `dotnet build` and `dotnet test` pass, then commit before starting the next step.

## Step 1: Core model and reader

- Model `DirectoryGroup`: `Id`, `DisplayName`, `Kind` (`Microsoft365`, `Security`, `MailEnabledSecurity`, `Distribution`), `IsDynamic`, `IsSyncedFromOnPremises`, `IsRoleAssignable`, `CreatedDateTime` (nullable), `HasMembers`, `HasOwners`.
  - `Kind`: `groupTypes` contains `Unified` gives `Microsoft365`; otherwise `securityEnabled` and `mailEnabled` both true gives `MailEnabledSecurity`; `securityEnabled` only gives `Security`; `mailEnabled` only gives `Distribution`.
  - `IsDynamic`: `groupTypes` contains `DynamicMembership`.
  - `HasOwners` is nullable. It is null when Graph doesn't return owners for that kind of group: groups synced from on-premises, distribution groups and mail-enabled security groups.
- `IDirectoryReader.GetGroupsAsync(CancellationToken)`, added to `CachingDirectoryReader` too.
- Membership is direct members only.

## Step 2: Checks

Add `CheckOptions.NewGroupGraceDays` (default 7). Groups created fewer than that many UTC calendar days ago are skipped by both checks, so new groups being set up don't show up. Groups with no `CreatedDateTime` are not skipped. Show the option in Settings > Checks with validation (`>= 0`).

**`group-no-members`** (category `Groups`, title "Groups with no members"), evaluated per group:

- Has members: no finding.
- Dynamic group with no members: **Info**, "Dynamic group has no members; its rule matches no one."
- Any other group with no members: **Warning**, "Group has no members."
- Messages include the group kind, for example "Security group has no members."

**`group-no-owners`** (category `Groups`, title "Groups with no owners"), evaluated per group:

- `HasOwners` is null or true: no finding.
- Microsoft 365 group with no owners: **Warning**, "Microsoft 365 group has no owners. No one can manage its membership, and it can't be renewed when group expiration is on."
- Security group with no owners: **Info**, "Security group has no owners."

Both checks: `ObjectType` is `Group`, no sub-object, no `DueDate`. Required permission: `GroupMember.Read.All`.

Tests with a fake reader and `FakeTimeProvider`, covering each kind, dynamic and assigned, synced groups (no owner finding), the grace period boundary (exactly `NewGroupGraceDays` old is checked), no created date, and groups that have both no members and no owners (a finding from each check).

## Step 3: Graph implementation

- `GET /groups` with `$select=id,displayName,groupTypes,securityEnabled,mailEnabled,onPremisesSyncEnabled,isAssignableToRole,createdDateTime`, `$top=999`, following every page.
- Member and owner presence: prefer two further listings, `GET /groups?$select=id&$expand=members($select=id)` and `GET /groups?$select=id&$expand=owners($select=id)`, following every page. Only whether each group has at least one entry matters. Before building on this, confirm in the Graph docs how many expanded items are returned, the page size allowed with `$expand`, and whether both relationships can be expanded in one request.
- If `$expand` doesn't work for this, fall back to `GET /groups/{id}/members?$top=1&$select=id` and `GET /groups/{id}/owners?$top=1&$select=id` per group, and report progress as groups are read. Record the choice in Open questions.
- Don't use `POST /$batch` to reduce the number of calls. It is a POST endpoint and AGENTS.md forbids POST, even though the batched requests would be reads.
- Keep mapping (including `Kind`) in small pure methods with unit tests using sample Graph `Group` objects.

## Step 4: Docs

- `docs/setup.md`: add `GroupMember.Read.All` (application) to the permissions step, with admin consent.
- `README.md`: add both checks to "What it checks".

## Acceptance criteria

- `dotnet build` and `dotnet test` pass with no warnings.
- No test makes a network call.
- Only `Application.Read.All` and `GroupMember.Read.All` are requested or documented.
- Manual check: an empty test security group appears under Groups with no members (after the grace period, or with `NewGroupGraceDays` set to 0), a Microsoft 365 group with its owner removed appears under Groups with no owners, and a synced group never appears under Groups with no owners.
- Without `GroupMember.Read.All` granted, both checks show the permission banner and aren't run, and the credential check still succeeds.

## Open questions

(Agents: add anything you think is out of scope or ambiguous here instead of implementing it.)

- **Nested groups:** a group whose only member is an empty group counts as having members. Should the no-members check look at transitive membership?
- **Owners who can't act:** a group whose only owner is disabled or deleted effectively has no owner. Detecting this needs user details (`User.Read.All`).
- **Role-assignable groups:** empty or ownerless role-assignable groups are treated like any other group. Should they have their own severity?
- **Severity for empty groups:** empty assigned groups are Warning. They are clutter rather than a risk, unless something (an app assignment or a Conditional Access exclusion) refers to them. Spec 007 looks at Conditional Access references; should this check be Info instead?
