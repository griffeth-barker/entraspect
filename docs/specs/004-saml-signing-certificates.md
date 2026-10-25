# Spec 004: Expiring SAML signing certificates on enterprise apps

**Status:** Ready (depends on spec 002)
**Goal:** A check in the Enterprise apps nav section that lists SAML single sign-on apps whose token signing certificate has expired or expires soon.

**Out of scope for this spec:** SAML token encryption certificates, apps using OIDC or password-based SSO, checking the certificate the service provider has on its side, notification email settings.

**Graph permissions:** no new permissions. `Application.Read.All` covers service principals.

Follow AGENTS.md. Complete one step at a time, make sure `dotnet build` and `dotnet test` pass, then commit before starting the next step.

## Step 1: Share the expiry rules

- Move the day-counting and severity-window logic out of `AppCredentialExpiryCheck` into a small shared Core helper (for example `ExpiryRules`): whole UTC calendar days between now and an end date, expired or not, and the Critical or Warning window from `CheckOptions`.
- No behaviour change. All spec 001 check tests still pass unchanged. Add direct tests for the helper.

## Step 2: Core model and reader

- Model `EnterpriseApp`: `Id`, `AppId`, `DisplayName`, `AccountEnabled`, `PreferredSingleSignOnMode` (string, nullable), `PreferredTokenSigningKeyThumbprint` (nullable), `SigningCertificates`.
- Model `SigningCertificate`: `KeyId`, `Thumbprint` (uppercase hex), `DisplayName`, `StartDateTime`, `EndDateTime` (nullable).
- `IDirectoryReader.GetEnterpriseAppsAsync(CancellationToken)`, added to `CachingDirectoryReader` too. Spec 006 reuses this method.

## Step 3: The check

**`saml-signing-certificate-expiry`** (category `EnterpriseApps`, title "Expiring SAML certificates"). Only apps with `PreferredSingleSignOnMode` equal to `saml` (case-insensitive) are checked.

- The active certificate is the signing certificate whose thumbprint matches `PreferredTokenSigningKeyThumbprint`.
- No signing certificates, or no certificate matches the preferred thumbprint: **Warning**, "SAML app has no active signing certificate."
- Active certificate already expired: **Critical**, "Active signing certificate expired N days ago. SAML sign-in to this app may be failing."
- Active certificate expires within `ExpiryCriticalDays`: **Critical**. Within `ExpiryWarningDays`: **Warning**. Otherwise no finding for it.
- When the active certificate has a finding and another signing certificate exists that is valid for longer, add to the message: "A newer certificate exists but is not active. Make it active and give it to the service provider." The severity doesn't change, because someone still has to act.
- An inactive signing certificate that has expired: **Info**, "Inactive signing certificate expired N days ago; consider removing it."
- Disabled apps (`AccountEnabled` false): any finding above becomes **Info**, and the message starts with "App is disabled."
- `ObjectType` `EnterpriseApp`, `SubObjectType` `Signing certificate`, `SubObjectDisplayName` the certificate display name or, if empty, its thumbprint, `DueDate` the certificate's end date. Required permission: `Application.Read.All`.

Tests: active expired, active in each window and on each boundary, active fine with an old inactive one expired, a newer inactive certificate present, no active certificate, disabled app, non-SAML apps ignored, and mixed apps.

## Step 4: Graph implementation

- `GET /servicePrincipals` with `$select=id,appId,displayName,accountEnabled,preferredSingleSignOnMode,preferredTokenSigningKeyThumbprint,keyCredentials`, `$top=999`, following every page. Filter to SAML apps in the check, not in the query.
- Signing certificates are the `keyCredentials` with `type` `AsymmetricX509Cert` and `usage` `Sign`. Entra stores a matching `Verify` credential for each certificate; ignore those. The thumbprint is the hex of `customKeyIdentifier`.
- Keep mapping in small pure methods with unit tests using sample Graph `ServicePrincipal` objects, including a Sign and Verify pair for the same certificate.

## Step 5: Docs

- `README.md`: add the check to "What it checks".
- `docs/setup.md`: no permission change; mention that SAML certificates come from the same `Application.Read.All` permission.

## Acceptance criteria

- `dotnet build` and `dotnet test` pass with no warnings.
- No test makes a network call.
- No new Graph permission is requested or documented.
- Manual check: a SAML enterprise app shows its active certificate's expiry, matching the SAML Certificates section of the app's Single sign-on page in the portal.

## Open questions

(Agents: add anything you think is out of scope or ambiguous here instead of implementing it.)

- **Separate thresholds:** SAML certificates use the same `ExpiryWarningDays` and `ExpiryCriticalDays` as app credentials. Rolling a SAML certificate needs coordination with the service provider, so a longer warning window might suit it better.
- **Microsoft-owned apps:** some Microsoft first-party service principals may use SAML. Should they be excluded?
- **Page size (step 4 choice):** the spec asks for `$top=999`, but the [List servicePrincipals docs](https://learn.microsoft.com/en-us/graph/api/serviceprincipal-list?view=graph-rest-1.0) say "the default and maximum page size is 100 service principal objects", so the reader asks for 100 and follows every `@odata.nextLink`. The same page notes that `$select` with `keyCredentials` on service principals is throttled at 150 requests per minute per tenant; at 100 per page that is about 15,000 service principals a minute, and the Graph SDK retries 429s. To confirm against a large tenant: that the scan completes without hitting that limit.
- **Permission confirmed:** the List servicePrincipals docs give `Application.Read.All` as the least-privileged application permission (higher: `Application.ReadWrite.OwnedBy`, `Application.ReadWrite.All`, `Directory.Read.All`).
- **Not-yet-valid newer certificate:** a newer inactive certificate counts for the "A newer certificate exists" hint when it ends later than the active one and hasn't expired, whatever its start date. Should a certificate whose start date is still in the future be left out?
