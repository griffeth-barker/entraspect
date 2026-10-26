# Spec 019: Owners of privileged role-assignable groups

**Status:** Ready (depends on specs 017 and 018)
**Goal:** A check in **Roles & admins** that lists the owners of role-assignable groups holding a Tier 0 or Privileged role. A group owner can add members to the group, including themselves, so every owner of such a group can give anyone the group's roles.

**Out of scope for this spec:** eligible (PIM) role assignments to groups, PIM for Groups (eligible membership or ownership), role-assignable groups holding only non-privileged or custom roles, groups that hold no role, and who can manage group owners through other roles (Groups Administrator and similar). See Open questions.

**Graph permissions:** none new. `User.Read.All`, `RoleManagement.Read.Directory` and `GroupMember.Read.All`, as spec 018's checks. `GroupMember.Read.All` is the least-privileged application permission for `GET /groups/{id}/owners` ([List group owners](https://learn.microsoft.com/en-us/graph/api/group-list-owners?view=graph-rest-1.0)). No licence is required to read: a tenant without P1 or P2 can't create role-assignable groups, so the check finds nothing there.

**Builds on specs 017 and 018:** `PrivilegedRoles` (tiers), `RoleHolders` (role name and tier for an assignment) and `AdminCheckRules.RequiredPermissions`. Don't add a second copy of the role-resolution logic.

**Why:**
- Microsoft: "By default, Privileged Role Administrators can manage the membership of a role-assignable group, but you can delegate the management of role-assignable groups by adding group owners." ([Use Microsoft Entra groups to manage role assignments](https://learn.microsoft.com/en-us/entra/identity/role-based-access-control/groups-concept)). An owner of a group that holds Global Administrator can make themselves, or anyone, a Global Administrator, without holding any admin role.
- Role-assignable groups are protected against most other ways in (no dynamic membership, no nesting, Privileged Role Administrator needed to create them, Privileged Authentication Administrator needed to reset members' and owners' credentials). Owners are the delegation path admins forget.

**Hybrid tenants:** role-assignable groups are cloud-only (Microsoft doesn't support assigning roles to on-premises groups), but a synced user can own one. Synced owners are included like any other.

Follow AGENTS.md. Complete one step at a time, make sure `dotnet build` and `dotnet test` pass, then commit before starting the next step.

## Step 1: Core model and reader

- Add a Core model `GroupOwner`: `Id`, `Kind` (a `GroupOwnerKind`: `User`, `ServicePrincipal`, `Other`), `DisplayName` (nullable), `UserPrincipalName` (nullable, users only).
- Add `IDirectoryReader.GetGroupOwnersAsync(string groupId, CancellationToken)`, returning the group's owners. Empty if the group doesn't exist, as `GetGroupTransitiveUserIdsAsync` does, so a group deleted during a scan doesn't fail the check.
- Add it to `CachingDirectoryReader`, cached per group ID for one scan, and to every fake reader in the tests.
- Expose role resolution from `RoleHolders` so the check can name and tier a group's own assignments without a copy of the private `Role` and `Roles` logic. For example a public `RoleHolders.GetPrincipalRoles(assignments, definitions)` returning, per principal ID (case-insensitive), the distinct `HeldRole`s assigned to it directly, highest tier first, then by name. `GetAsync` keeps its behaviour and tests.
- Tests: `CachingDirectoryReader` reads each group's owners once; the new `RoleHolders` method (template ID versus definition ID, unnamed definitions, ordering, duplicates).

## Step 2: Check

**`roles-group-owners`** (title "Role group owners"). Category `RolesAndAdmins`. Requires `AdminCheckRules.RequiredPermissions`. No `RequiredLicence`.

Which groups:
- Groups from `GetGroupsAsync` with `IsRoleAssignable` true, that are the principal of at least one active role assignment (`GetDirectoryRoleAssignmentsAsync`, any scope) to a role `PrivilegedRoles` rates Tier 0 or Privileged.
- Only those groups' owners are read, one `GetGroupOwnersAsync` call per group. A tenant has at most 500 role-assignable groups.
- A role-assignable group with no owners: no finding. Only Privileged Role Administrators and Global Administrators can manage its membership, which is the intended state.

Which owners: every owner, user or service principal, enabled or disabled, member or guest, cloud-only or synced, except a user owner who holds Global Administrator or Privileged Role Administrator through a direct active assignment (`PrincipalId` is the user). Those roles can manage every role-assignable group's membership anyway, so ownership adds nothing. A user holding either role only through this or another group is still reported: removing them from that group wouldn't remove their ownership, and they could add themselves back.

One finding per (group, owner):
- Severity by the highest tier of the group's privileged roles: Tier 0 **Critical**, Privileged **Warning**.
- Message: "Owner can add anyone to this group, including themselves, and so give them its roles. Remove the owner, or protect the account like an admin with these roles."
- `ObjectType` `Group`, `ObjectId` and `ObjectDisplayName` the group's.
- `SubObjectType` the owner kind: "User", "Service principal" or "Other". `SubObjectDisplayName` the owner's display name, falling back to the owner's ID when Graph returns none (for example a service principal whose name the granted permissions don't cover).
- `UserPrincipalName` the owner's UPN, for user owners. Take the display name and UPN from `GetUsersAsync` (already read for the role holders) when the owner is a known user, otherwise from what `GetGroupOwnersAsync` returned.
- `Value` the group's privileged roles, highest tier first, then by name, comma-separated, as spec 018's Roles column.

Labels: `ObjectDisplayName` "Group", `ObjectId` "Object ID", `SubObjectDisplayName` "Owner", `SubObjectType` "Owner type", `UserPrincipalName` "Owner UPN", `Value` "Roles", `DueDate` hidden. Each property in its own column; never combine the owner's name with their UPN.

Description (for the info tip, not the page body): what a role-assignable group is, why owners can elevate themselves, the GA/PRA exception, and that eligible (PIM) assignments and PIM for Groups aren't included.

Register it in `CheckCatalog` in `RolesAndAdmins`, in title order (after "No phishing-resistant MFA", before "Synced admins").

Tests with a fake reader:
- Group holding a Tier 0 role with one owner: Critical. Privileged role only: Warning. Both: Critical, roles listed Tier 0 first.
- Group holding only non-privileged or custom roles: no finding, and its owners aren't read.
- Role-assignable group with no role assignment: no finding, owners not read.
- A group that isn't role-assignable but somehow is a role assignment principal (fake data): no finding.
- Group with no owners: no finding.
- Several owners: one finding each. Service principal owner. Owner with no display name falls back to the ID.
- Owner with a direct GA assignment: skipped. Direct PRA: skipped. GA only through this group: reported. GA through another group: reported.
- Disabled, guest and synced user owners: reported.
- Role assignment by definition ID versus template ID (built-in roles), as `RoleHolders` handles.
- The check failing on its own when `GetGroupOwnersAsync` throws `DirectoryReadException`.

## Step 3: Graph implementation

- `GET /groups/{id}/owners`, v1.0, following every `@odata.nextLink`. No `$select` or `$filter`: the List group owners docs say query parameters other than `$expand` need advanced query (`ConsistencyLevel: eventual`), which nothing else here needs. Read `@odata.type`, `id`, `displayName` and, for users, `userPrincipalName`.
- Map `#microsoft.graph.user` to `User`, `#microsoft.graph.servicePrincipal` to `ServicePrincipal`, anything else to `Other`; map defensively (missing type or name doesn't throw). Keep the mapping in a pure, tested static method in Entraspect.Graph.
- A 404 for the group returns an empty list, as `ReadGroupTransitiveUserIdsAsync` does.
- No `POST /$batch`, no beta.

## Step 4: Docs

- `docs/setup.md`: add the check to the rows of the three permissions it uses.
- `README.md`: add it to the Roles & admins checks under "What it checks".
- `docs/specs/011-group-hygiene.md`: its out-of-scope list mentions role-assignable group risks; point to this spec.

## Acceptance criteria

- `dotnet build` and `dotnet test` pass with no warnings.
- No test makes a network call.
- No permissions beyond those from earlier specs are requested or documented. No write endpoint is called.
- Manual check against the dev tenant (needs P1 or P2 to create the group):
  - Create a role-assignable group, assign it a Privileged role (for example Helpdesk Administrator) and add a non-admin user as owner. Role group owners lists that owner as Warning, with the role in the Roles column.
  - Assign the group Global Administrator too: the finding becomes Critical, roles listed Global Administrator first.
  - Make a user with a direct Global Administrator assignment an owner too: not listed.
  - Remove the owners: no findings.
  - Clean up the test group afterwards in the Entra admin center.

## Open questions

(Agents: add anything you think is out of scope or ambiguous here instead of implementing it.)

- **Eligible (PIM) role assignments to groups:** a group eligible for Global Administrator is as dangerous to own, since members activate the role. Reading eligibility needs `RoleEligibilitySchedule.Read.Directory` and P2, the same question as spec 018.
- **PIM for Groups:** a user eligible for ownership or membership of a role-assignable group through PIM for Groups isn't an owner or member until they activate. Reading that needs `PrivilegedEligibilitySchedule.Read.AzureADGroup`.
- **Service principal owners in v1.0:** the List group owners docs say service principals aren't listed as group owners in v1.0 yet "due to the staged rollout". If that's still true, service principal owners are missed (a missed finding). Check against the dev tenant by making an app an owner; beta would need a spec change.
- **Owners who are also members:** they already hold the roles, so ownership adds little for them today, but it lets them add others and add themselves back after removal. Reported for now.
- **Groups Administrator and similar roles:** whether roles other than Global Administrator and Privileged Role Administrator can change owners or members of role-assignable groups. Microsoft's docs say managing role-assignable group membership through Graph needs `RoleManagement.ReadWrite.Directory`, which suggests not.
- **Non-privileged roles:** owners of groups holding only `Other` roles (including custom roles) aren't reported. An Info finding may be worth adding once custom role permissions are assessed (spec 017).
- **Severity for risky owners:** a guest, synced or disabled owner of a Tier 0 group is no more Critical than any other owner, since Critical is already the top. Privileged-tier groups could raise those to Critical.
