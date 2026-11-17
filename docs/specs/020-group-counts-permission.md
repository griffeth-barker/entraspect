# Spec 020: Group member and owner counts need Group.Read.All

**Status:** Ready (fixes specs 003, 007 and 011)
**Goal:** The group checks that use member or owner counts declare the permission those counts really need, and the checks that don't use them keep working without it.

**Out of scope for this spec:** counting members or owners with one call per group, replacing `GroupMember.Read.All` in checks that don't read the counts, and `Member.Read.Hidden` (unchanged). See Open questions.

**Graph permissions:** adds **`Group.Read.All`** (application) to four checks. It is a read permission. Spec 003 already noted it is acceptable to the project owner.

Follow AGENTS.md. Complete one step at a time, make sure `dotnet build` and `dotnet test` pass, then commit before starting the next step.

## The bug

Found by the user on 2026-11-16. The permission review said `Group.Read.All` was unused, since no check declared it. With it removed, every check that reads groups failed with HTTP 403 `Authorization_RequestDenied`, although Settings > Checks showed all their permissions as granted.

`GetGroupsAsync` reads groups with three listings: the group properties, then `GET /groups?$select=id&$expand=members($select=id)` and `GET /groups?$select=id&$expand=owners($select=id)` for the counts. A probe on the dev tenant, with a token holding `GroupMember.Read.All`, `Member.Read.Hidden`, `User.Read.All` and others but not `Group.Read.All`, gave:

- 200 for the property listing, and for each of its `$select` properties on its own.
- **403** for both expand listings.
- 200 for `$filter=hasMembersWithLicenseErrors eq true`, `GET /groups/{id}/owners` and `GET /groups/{id}/transitiveMembers/microsoft.graph.user`.

The List groups docs list `GroupMember.Read.All` as enough for `GET /groups` and don't tie `$expand` to another permission, so the docs are wrong or incomplete here. Because the counts were part of `GetGroupsAsync`, all nine checks that read groups failed (the seven group checks, Policy hygiene and Role group owners), although only four use the counts.

## Step 1: Split the counts from the group read

- Remove `HasMembers`, `OwnerCount` and `HasOwners` from `DirectoryGroup`.
- New Core model `GroupCounts`: `GroupId`, `HasMembers`, `OwnerCount` (nullable, with the same meaning and owner-availability rule as before) and the computed `HasOwners`.
- New `IDirectoryReader.GetGroupCountsAsync(CancellationToken)`, returning the counts by group ID (case-insensitive) for every group the listings return. Add it to `CachingDirectoryReader` and the fake readers.
- `GetGroupsAsync` drops the two expand listings, so it needs only `GroupMember.Read.All`.
- Graph: `GetGroupCountsAsync` makes the two expand listings. The owners listing also selects `groupTypes`, `securityEnabled`, `mailEnabled` and `onPremisesSyncEnabled`, so the owner-availability rule stays in Entraspect.Graph without a third listing. Keep `$top=100` and follow every page.
- These four checks call `GetGroupCountsAsync` and require `Group.Read.All` in place of `GroupMember.Read.All`:
  - Groups with no members (`Group.Read.All`, `Member.Read.Hidden`)
  - Groups with no owners
  - Groups with one owner
  - Policy hygiene (`Policy.Read.All`, `User.Read.All`, `Group.Read.All`), for the "only includes empty groups" rule. It still reads `GetGroupsAsync` for which groups exist.
- A group in `GetGroupsAsync` with no entry in the counts (created between the two reads) is skipped by the count rules.
- The other five checks that read groups (Licence errors, Paused dynamic groups, Public groups, Sync errors and Role group owners) are unchanged and keep `GroupMember.Read.All`.
- Since the 403 hint and the permission banner come from `RequiredPermissions`, they now name `Group.Read.All` for the four checks. No UI change.

Tests: the checks with counts from the fake reader, a group missing from the counts, the new reader method cached once per scan, the mapping of the owners listing (kinds, synced groups, the 20 cap), and the catalog's permissions for the four checks.

## Step 2: Docs

- `docs/setup.md` and `README.md`: add `Group.Read.All` to the permissions tables for the four checks, and take them out of the `GroupMember.Read.All` rows.
- Spec 003, 007 and 011: note where they say `GroupMember.Read.All` covers members and owners that spec 020 corrects it.

## Acceptance criteria

- `dotnet build` and `dotnet test` pass with no warnings.
- No test makes a network call.
- The only new permission is `Group.Read.All`, on the four checks above. No write endpoint is called.
- Manual check against the dev tenant:
  - Without `Group.Read.All`: the four checks show the permission banner naming it, aren't run, and the other group checks and Role group owners run without errors. The permission review no longer calls `Group.Read.All` unused when it is granted.
  - With `Group.Read.All`: the four checks run and give the same findings as before.

## Tests still to do

- The manual checks above.
- With `Group.Read.All` granted but `GroupMember.Read.All` removed, the two expand listings still succeed (run the probe from 2026-11-16 again). The four checks assume `Group.Read.All` alone is enough.

## Open questions

(Agents: add anything you think is out of scope or ambiguous here instead of implementing it.)

- **Which permission the expands really need:** `Group.Read.All` is known to work. A narrower one might too (for example `Group.ReadBasic.All` with `GroupMember.Read.All`, or `Directory.Read.All` is broader). Worth probing if Microsoft documents it.
- **Applications' owner expand:** `GET /applications?$expand=owners($select=id)` works with `Application.Read.All` today. If Microsoft tightens it the same way, App registrations with no owners would fail the same way.
- **Per-group counting:** `GET /groups/{id}/members?$select=id&$top=1` and `/owners` work with `GroupMember.Read.All`, but cost two calls per group and `$batch` is forbidden, so they are too slow for large tenants.
