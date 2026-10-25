# Setting up entraspect

entraspect signs in to your tenant as an app registration that you create, using a certificate. This guide covers creating that app registration, granting it read-only access, adding a certificate, and connecting from entraspect.

entraspect is read-only. It only needs read permissions, and it never changes anything in your tenant.

## Before you start

- You need a role that can create app registrations and grant admin consent, such as Privileged Role Administrator or Global Administrator. If you can't grant consent yourself, create the app registration and ask an administrator to grant consent for you.
- entraspect signs in as the application (app-only), with no user present. It only supports certificates. Signing in as yourself and client secrets are not supported.
- Some checks need Microsoft Entra ID P1 or P2: today, **MFA not registered**, **MFA not required**, Groups **Licence errors** and Conditional Access **Policy hygiene** and **Unused policies**. With `LicenseAssignment.Read.All` granted (step 2), entraspect reads your tenant's licences when it connects. On a tenant without P1 or P2, those checks are grayed out on **Settings** > **Checks** and in the navigation, with "Needs an Entra ID P1 or P2 licence.", and Scan skips them. Without that permission, they run and fail with "This check needs an Entra ID P1 or P2 licence." The other checks run either way.

## 1. Create the app registration

Use an app registration dedicated to entraspect. Don't reuse one that other tools use: entraspect warns you if its app registration has permissions it doesn't need.

If you already have an app registration for entraspect, skip to step 2.

1. Sign in to the [Microsoft Entra admin center](https://entra.microsoft.com).
2. Go to **Entra ID** > **App registrations** and select **New registration**.
3. Enter a name, for example `entraspect`.
4. Under **Supported account types**, select **Accounts in this organizational directory only (Single tenant)**.
5. Leave **Redirect URI** empty. entraspect doesn't use one.
6. Select **Register**.

Registering the app also creates its enterprise application (service principal) in your tenant. You don't need to create the enterprise application separately.

On the app's **Overview** page, copy these two values. You enter them in entraspect later.

- **Directory (tenant) ID**
- **Application (client) ID**

Don't use the **Object ID**. It is a different value and sign-in fails with it.

## 2. Add the permissions

1. In the app registration, go to **API permissions** and select **Add a permission** > **Microsoft Graph** > **Application permissions**.
2. Add the permissions below. Don't add them as **Delegated permissions**; entraspect doesn't use delegated permissions.
3. Select **Grant admin consent for _your tenant_** and confirm. The **Status** column should show a green check for each permission.
4. New app registrations include the delegated `User.Read` permission by default. entraspect doesn't use it, so remove it.

| Permission | Used for |
| --- | --- |
| `Application.Read.All` | Expiring credentials (app registration secrets and certificates), Apps with no owners, Risky redirect URIs, Expiring SAML certificates (enterprise app SAML signing certificates), High-privilege apps (Microsoft Graph application permissions granted to enterprise apps) |
| `GroupMember.Read.All` | Groups with no members, Groups with no owners, Groups with one owner, Licence errors, Paused dynamic groups, Public groups, Sync errors, Policy hygiene (which groups a Conditional Access policy names still exist, and whether they have members), MFA not required (the users in groups that Conditional Access policies and role assignments name) |
| `Member.Read.Hidden` | Groups with no members (members of groups with hidden membership) |
| `AuditLog.Read.All` | MFA not registered (the Authentication methods user registration details report), MFA not required (recent single-factor sign-ins, from beta Graph), Unused policies (interactive sign-in logs). Needs Entra ID P1 or P2 |
| `Policy.Read.All` | Policy hygiene, MFA not required and Unused policies (read Conditional Access policies); MFA not required also reads security defaults and each user's per-user MFA state (beta Graph). Without it, sign-in logs leave out which Conditional Access policies applied, so Unused policies can't run. Needs Entra ID P1 or P2 |
| `User.Read.All` | Policy hygiene (checks that the users a Conditional Access policy names still exist), MFA not required (lists every user). `User.ReadBasic.All` would be enough for Policy hygiene, but it is delegated only |
| `RoleManagement.Read.Directory` | MFA not required (active directory role assignments, to find admins and the roles a policy includes or excludes) |
| `LicenseAssignment.Read.All` | Optional, recommended. Reads your tenant's subscriptions at connect, so checks your licence doesn't cover are grayed out instead of failing |

Every permission entraspect uses is a read permission. Never grant entraspect's app registration a write permission (any permission containing `ReadWrite`). Anyone with its certificate could then change your tenant.

**Settings** > **Checks** in entraspect lists the permissions each check needs, green when granted and red when missing. Checks that need a licence also show `LicenseAssignment.Read.All`, green when granted and gray (not red) when missing, because the check runs without it. If a check's permission is missing, entraspect doesn't run that check, shows a warning badge next to it in the navigation, and shows a banner on its page and on **Overview**. The banner can open this app registration's **API permissions** page in the Entra admin center and copy the permission name. entraspect never adds or consents permissions itself; you do that in the admin center. After granting a permission, select **Reconnect**. A new permission can take a few minutes to take effect.

entraspect also warns on **Overview** if the app registration has permissions no check uses, in red for write permissions.

## 3. Add a certificate

entraspect needs a certificate with a private key. entraspect keeps the private key, either in your operating system's certificate store or in a `.pfx` file. Entra only gets the public certificate.

The certificate gives read access to your whole tenant, so protect it like an admin password:

- Prefer the operating system's certificate store with a non-exportable private key, so the key can't be copied off the machine.
- Use a short lifetime, for example one year or less, and replace the certificate before it expires.
- Keep `.pfx` files out of source repositories, shared folders and backups that other people can read.
- If a certificate may have leaked, delete it from the app registration straight away.

### Windows: certificate store (recommended)

PowerShell can create the certificate straight into your personal certificate store, with a private key that can't be exported:

```powershell
$cert = New-SelfSignedCertificate -Subject "CN=entraspect" -CertStoreLocation Cert:\CurrentUser\My `
  -KeyExportPolicy NonExportable -KeySpec Signature -NotAfter (Get-Date).AddYears(1)
Export-Certificate -Cert $cert -FilePath .\entraspect.cer
$cert.Thumbprint
```

Upload `entraspect.cer` in the next step, and enter the printed thumbprint in entraspect.

### macOS and Linux: .pfx file

With OpenSSL:

```sh
openssl req -x509 -newkey rsa:2048 -sha256 -days 365 -nodes \
  -subj "/CN=entraspect" -keyout key.pem -out cert.pem
openssl x509 -in cert.pem -outform der -out entraspect.cer
openssl pkcs12 -export -inkey key.pem -in cert.pem -out entraspect.pfx
rm key.pem cert.pem
```

`openssl pkcs12` asks for a password to protect the `.pfx`. Use a strong one. entraspect asks for it each time you connect and never saves it. Keep `entraspect.pfx` somewhere only you can read, for example `chmod 600 entraspect.pfx`.

### Upload the public certificate

1. In the app registration, go to **Certificates & secrets** > **Certificates** and select **Upload certificate**.
2. Upload the public certificate (`.cer`, `.crt` or `.pem`). Never upload the `.pfx` file.
3. Note the **Thumbprint** shown after upload. It must match the thumbprint of your certificate.

## 4. Connect from entraspect

entraspect opens on **Settings** > **Connection profiles**. Overview and the check pages unlock once you're connected.

1. Select **Add** at the top right of **Connection profiles**.
2. Enter a **Profile name**, the **Tenant ID** and the **Client ID** from step 1.
3. Enter either the **Certificate (.pfx) path** or the **Certificate thumbprint**, not both. For a thumbprint, entraspect looks in the current user's personal store, then the local machine's personal store (current user only on Linux).
4. Select **Save**. Profiles are saved without passwords.
5. Select the profile and select **Connect**. If it uses a `.pfx` file, enter the password in the dialog. entraspect asks for it on every connect and never saves it.

The status card at the top of **Connection profiles** shows the connection. Sign-in errors appear there, with their `AADSTS` code (see [Troubleshooting](#troubleshooting)). The connected profile's card says **Connected** and has **Reconnect** and **Disconnect**; **Connect** is hidden until you disconnect. Use **Reconnect** after changing the app registration's permissions, and **Disconnect** to switch profiles. entraspect remembers the last profile you connected with and selects it next time. Each profile in the list has edit and delete buttons; editing shows its tenant ID, client ID and certificate.

## 5. Scan

After connecting, entraspect opens **Overview**, which shows the connected tenant.

1. Select **Scan**. It runs every check that is turned on and has its permissions. **Cancel** stops it.
2. **Overview** shows the Critical, Warning and Info counts for each section, and any check that failed or couldn't run, with the reason.
3. Select a check in the navigation, or a row on **Overview**, to see its findings. Use **Search** or **Add filter** (pick a property, then a value) to narrow them, as in the Entra admin center.
4. Select **Export** to save results to a file you choose, as CSV or JSON. On **Overview** it saves every finding from the last scan; on a check page, only the findings shown after search and filters. CSV has one row per finding and opens in Excel; JSON also has each check's status and why it failed or didn't run. Dates in both are UTC (ISO 8601, ending in `Z`). In CSV, a value starting with `=`, `+`, `-` or `@` gets a leading `'` so spreadsheet apps show it as text instead of running it as a formula; JSON keeps values unchanged.

Under **Settings**:

- **Checks** turns checks on or off and sets the check options: the expiry windows (warning and critical days) the new group grace period (groups created fewer than this many days ago are skipped by the group checks), the report-only warning days (Policy hygiene warns about report-only Conditional Access policies unchanged for at least this many days), and the sign-in lookback days (how many days of sign-in logs MFA not required and Unused policies read, 1 to 30). Unused policies reads every interactive sign-in in that period, so in a busy tenant a longer period makes the scan much slower; the scan bar shows how many sign-ins it has read, and **Cancel** stops it within one page of 1,000 sign-ins.
- **Preferences** chooses where dates and times show: UTC, your computer's time zone, or any time zone your computer knows. In a time zone you pick, times show that zone's UTC offset on that date, so daylight saving time is right for every date. It also chooses the theme: **Use system setting** (follows your operating system), **Light** or **Dark**.
- **About** shows the version and the logo, where entraspect stores its files, and **Open settings.json** and **Reload** (see below).

Results are kept in memory and are cleared when you disconnect or close entraspect, unless you export them.

> **Exports contain tenant data.** An exported file holds your tenant ID and the names and IDs of users, groups, apps and policies. Store it as you would other tenant data, share it only with people who may see it, and never commit it to a repository. Exports never contain passwords, certificates, private keys or tokens.

## Where entraspect stores its files

Profiles and settings are stored in an `entraspect` folder in your local application data folder:

- Windows: `%LOCALAPPDATA%\entraspect`
- macOS: `~/Library/Application Support/entraspect`
- Linux: `~/.local/share/entraspect`

`profiles.json` holds tenant IDs, client IDs and certificate paths or thumbprints. `settings.json` holds which checks are off, the check options, the last used profile, the time zone and the theme. Neither file ever holds passwords, private keys or tokens.

### Editing the files

You can edit both files in a text editor, for example to add several profiles at once:

1. Select **Open profiles.json** on **Settings** > **Connection profiles**, or **Open settings.json** on **Settings** > **About**. The file opens in your computer's default app for JSON files. If it doesn't exist yet, entraspect saves it first.
2. Edit the file and save it.
3. Back in entraspect, select **Reload** next to the button you used. The changes apply at once, as if you had made them in entraspect.

If the file isn't valid JSON or has a value entraspect can't use, **Reload** shows the error and keeps what is already loaded. It never changes the file, so you can fix it and select **Reload** again. Profiles are checked like profiles saved from the profile form: each needs a name, a tenant ID, a client ID that is a GUID, and either a `.pfx` path or a thumbprint. In `settings.json`, `TimeZone` is `"UTC"`, `"Local"` or a time zone ID such as `"Europe/Berlin"`, and `Theme` is `"System"`, `"Light"` or `"Dark"`.

entraspect saves these files itself whenever you change profiles or settings in the app, which replaces edits you haven't reloaded. Select **Reload** before changing anything in entraspect. Reloading profiles doesn't disconnect you, even if you renamed or removed the connected profile.

Don't add a password to either file; entraspect never reads one from them. It asks for a `.pfx` password each time you connect.

## Troubleshooting

Sign-in errors usually include an `AADSTS` code. The Entra sign-in logs (**Entra ID** > **Monitoring & health** > **Sign-in logs** > **Service principal sign-ins**, filtered by your application ID) show the full failure reason even when entraspect doesn't.

| Error | Likely cause | Fix |
| --- | --- | --- |
| `AADSTS700016` | Wrong client ID, or the app is in a different tenant | Use the **Application (client) ID** from the **Overview** page |
| `AADSTS90002` | Wrong tenant ID | Use the **Directory (tenant) ID** from the **Overview** page |
| `AADSTS700027` | Certificate not uploaded to this app registration, or expired | Upload the matching public certificate (step 3) |
| `AADSTS7000215` or `AADSTS7000222` | A client secret was used, or has expired | entraspect doesn't support client secrets; use a certificate (step 3) |
| `AADSTS53003` | Blocked by a Conditional Access policy for workload identities | Check the sign-in log to see which policy blocked it |
| Certificate not found | Thumbprint not in the certificate store, or wrong `.pfx` path | Check the thumbprint and store location, or the file path |
| Wrong password | `.pfx` password incorrect | Enter the password you set when creating the `.pfx` |
| 403 / `Authorization_RequestDenied` during a scan | Permission missing, added as delegated instead of application, or not consented | Add it as an application permission and grant consent (step 2), then reconnect |
| Unused policies: "Sign-in logs don't include Conditional Access data" | `Policy.Read.All` isn't granted or consented, so Graph leaves applied policies out of the sign-in logs | Grant `Policy.Read.All` (step 2), then reconnect |
| Unused policies: "No interactive sign-ins found in the last N days" | No one signed in interactively during the sign-in lookback days | Increase the sign-in lookback days in **Settings** > **Checks** |
| A check says it needs Entra ID P1 or P2, or is grayed out with "Needs an Entra ID P1 or P2 licence." | The tenant doesn't have the licence that check needs | No fix inside entraspect; the other checks still run. After adding a licence, select **Reconnect** |
