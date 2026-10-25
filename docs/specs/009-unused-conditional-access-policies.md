# Spec 009: Unused Conditional Access policies

**Status:** Ready (depends on specs 007 and 008)
**Goal:** A check in the Conditional Access nav section that finds enabled and report-only policies that didn't apply to any interactive sign-in during a recent period, based on the sign-in logs.

**Out of scope for this spec:** non-interactive, service principal and managed identity sign-ins (beta only), explaining *why* a policy didn't apply, the What If tool.

**Graph permissions:** uses `AuditLog.Read.All` (spec 005) and `Policy.Read.All` (spec 007). No new permissions. Sign-in logs need Entra ID P1 or P2. Without `Policy.Read.All`, Graph leaves the applied policies out of every sign-in.

Follow AGENTS.md. Complete one step at a time, make sure `dotnet build` and `dotnet test` pass, then commit before starting the next step.

## Step 1: Core usage summary

- Model `ConditionalAccessUsage`: the start and end of the period read, total sign-ins read, sign-ins that included applied policy data, and for each policy ID the number of sign-ins per result (`success`, `failure`, `notApplied`, `notEnabled`, `reportOnlySuccess`, `reportOnlyFailure`, `reportOnlyNotApplied`, `reportOnlyInterrupted`, and any unknown value).
- A pure Core aggregator that adds one sign-in's applied policy results at a time, so sign-ins are never held in memory all at once. Unit tests for it.
- `IDirectoryReader.GetConditionalAccessUsageAsync(DateTimeOffset since, IProgress<int>? signInsRead, CancellationToken)`, added to `CachingDirectoryReader` too.

## Step 2: The check

Use `CheckOptions.SignInLookbackDays` from spec 008.

**`ca-unused-policies`** (category `ConditionalAccess`, title "Unused policies"). Uses `GetConditionalAccessPoliciesAsync` and `GetConditionalAccessUsageAsync`.

- If no sign-ins were read: the check fails with "No interactive sign-ins found in the last N days."
- If sign-ins were read but none included applied policy data: the check fails with "Sign-in logs don't include Conditional Access data. Check that Policy.Read.All is granted, then reconnect." Never report every policy as unused in this case.
- Skip disabled policies (spec 007 covers them), policies created during the lookback period, and policies that target service principals.
- Enabled policy with no `success` or `failure` result: **Warning**, "Didn't apply to any interactive sign-in in the last N days (M sign-ins checked)."
- Report-only policy with no `reportOnlySuccess`, `reportOnlyFailure` or `reportOnlyInterrupted` result: **Info**, same message.
- `ObjectType` `ConditionalAccessPolicy`, no sub-object. The check page shows the period and the number of sign-ins read. Required permissions: `AuditLog.Read.All`, `Policy.Read.All`.

Tests: enabled unused, enabled used, report-only both ways, no sign-ins, no CA data, a policy created during the period, and a service principal policy.

## Step 3: Graph implementation

- `GET /auditLogs/signIns?$filter=createdDateTime ge {since}` with `$top=1000`, following every page. Only `appliedConditionalAccessPolicies` (policy `id` and `result`) is needed from each sign-in; the endpoint doesn't support `$select`, so map just those fields and let the rest go.
- Report progress every page (sign-ins read so far) and check the cancellation token between pages.
- Throttling: rely on the Graph SDK's retry handler, which respects `Retry-After`. Don't add parallel requests.
- Mapping tests with sample Graph `SignIn` objects, including sign-ins with `appliedConditionalAccessPolicies` missing.

## Step 4: Docs

- `docs/setup.md`: note the licence requirement for this check, and that longer periods are slow.
- `README.md`: add the check to "What it checks", saying it only looks at interactive sign-ins.

## Acceptance criteria

- `dotnet build` and `dotnet test` pass with no warnings.
- No test makes a network call.
- No new Graph permission is requested or documented.
- Manual check: a test policy that includes only a user who didn't sign in during the period is listed; a policy requiring MFA for all users is not. Cancel stops the sign-in read within one page.

## Open questions

(Agents: add anything you think is out of scope or ambiguous here instead of implementing it.)

- **Non-interactive and workload sign-ins:** v1.0 only returns interactive sign-ins. Policies aimed at non-interactive traffic or workload identities will look unused. Beta's `signInEventTypes` filter could cover them; AGENTS.md now allows beta, but non-interactive volumes are much larger.
- **Very large tenants:** a busy tenant can have millions of sign-ins a week. Should there be a cap on sign-ins read, or a sampling mode?
- **Scan duration:** this check could take much longer than the others. Should it be disabled by default, or run separately from the main Scan?
- **Decision (step 2): how the check fails.** "No interactive sign-ins" and "no Conditional Access data" are thrown as `DirectoryReadException` without a status code, so `ScanRunner` fails only this check with that message, as for any read failure. No new exception type.
- **Decision (step 2): nothing in scope.** When no enabled or report-only policy is in scope (all disabled, new, or targeting service principals), the check returns no findings without reading the sign-in logs, so it doesn't spend the slowest read in a scan or fail with "No interactive sign-ins" for a tenant that has nothing to judge.
- **Decision (step 2): showing the period.** The check can't work out its period again after the run (its "now" moves), so `ICheck` gained a default `ScanAsync` returning a `CheckRun` (findings plus an optional `DataPeriod`: start, end, entries read, entry name). `ScanRunner` calls it and keeps the period in `CheckResult.DataPeriod`; the check page adds "· 1,234 sign-in(s) from {start} to {end}" to the status line through `TimeDisplay`. Generic, so a later log-reading check gets it without UI changes.
- **Decision (step 2): columns.** Policy name and Policy ID only; no date column, since the spec names none. The lookback days and sign-in count are in the message.
- **Policies changed during the period:** a policy whose users, apps or conditions were changed during the lookback days is judged on sign-ins from before and after the change. Should a recently modified policy be skipped or noted, like policies created during the period?
