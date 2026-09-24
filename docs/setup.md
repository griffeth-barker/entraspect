# Setting up entraspect

entraspect signs in to your tenant as an app registration that you create, using a certificate. This guide covers creating that app registration, granting it read-only access, adding a certificate, and connecting from entraspect.

entraspect is read-only. It only needs read permissions, and it never changes anything in your tenant.

## Before you start

- You need a role that can create app registrations and grant admin consent, such as Privileged Role Administrator or Global Administrator. If you can't grant consent yourself, create the app registration and ask an administrator to grant consent for you.
- entraspect signs in as the application (app-only), with no user present. It only supports certificates. Signing in as yourself and client secrets are not supported.
- Some checks need Microsoft Entra ID P1 or P2. On a tenant without it, those checks fail with a message saying so, and the other checks still run.

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
| `Application.Read.All` | Expiring app registration secrets and certificates |

Every permission entraspect uses is a read permission. Never grant entraspect's app registration a write permission (any permission containing `ReadWrite`). Anyone with its certificate could then change your tenant.

If a check's permission is missing, entraspect doesn't run that check and shows a banner on its page. The banner can open this app registration's **API permissions** page in the Entra admin center and copy the permission name. entraspect never adds or consents permissions itself; you do that in the admin center. After granting a permission, reconnect in entraspect. A new permission can take a few minutes to take effect.

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

1. Start entraspect.
2. Enter a **Profile name**, the **Tenant ID** and the **Client ID** from step 1.
3. Enter either the **Certificate thumbprint** or the **Certificate (.pfx) path**. For a thumbprint, entraspect looks in the current user's personal store, then the local machine's personal store (current user only on Linux).
4. Optionally save the profile so you don't have to enter these again. The certificate password is never saved.
5. Select **Connect**. If you use a `.pfx` file, enter its password.
6. Run a scan.

Saved profiles are stored in `profiles.json` in an `entraspect` folder in your local application data folder:

- Windows: `%LOCALAPPDATA%\entraspect`
- macOS: `~/Library/Application Support/entraspect`
- Linux: `~/.local/share/entraspect`

The file contains tenant IDs, client IDs and certificate paths or thumbprints, but never passwords or private keys.

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
| A check says it needs Entra ID P1 or P2 | The tenant doesn't have the licence that check needs | No fix inside entraspect; the other checks still run |
