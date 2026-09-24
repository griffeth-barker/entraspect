# Setting up entraspect

entraspect signs in to your tenant through an app registration that you create. This guide covers creating that app registration, granting it read-only access, and connecting from entraspect.

entraspect is read-only. It only needs one Microsoft Graph permission, `Application.Read.All`, and it never changes anything in your tenant.

## Before you start

- You need a role that can create app registrations and grant admin consent, such as Privileged Role Administrator or Global Administrator. If you can't grant consent yourself, create the app registration and ask an administrator to grant consent for you.
- Decide which sign-in mode you will use:
  - **Interactive:** you sign in as yourself in a browser. Good for ad-hoc scans from your own machine.
  - **App-only:** entraspect signs in as the application using a certificate, with no user present. Good for a dedicated scanning machine. Client secrets are not supported.

You can use one app registration for both modes or a separate one for each.

## 1. Create the app registration

If you already have an app registration for entraspect, skip to step 2.

1. Sign in to the [Microsoft Entra admin center](https://entra.microsoft.com).
2. Go to **Entra ID** > **App registrations** and select **New registration**.
3. Enter a name, for example `entraspect`.
4. Under **Supported account types**, select **Accounts in this organizational directory only (Single tenant)**.
5. Leave **Redirect URI** empty for now. You set it in step 2.
6. Select **Register**.

Registering the app also creates its enterprise application (service principal) in your tenant. You don't need to create the enterprise application separately.

On the app's **Overview** page, copy these two values. You enter them in entraspect later.

- **Directory (tenant) ID**
- **Application (client) ID**

Don't use the **Object ID**. It is a different value and sign-in fails with it.

## 2. Set the redirect URI (interactive mode only)

Skip this step if you only use app-only mode.

1. In the app registration, go to **Authentication**.
2. Add a platform or redirect URI, and choose **Mobile and desktop applications**.
3. Enter the custom redirect URI `http://localhost` exactly: `http`, not `https`, with no port, path or trailing slash.
4. Save.

The platform type must be **Mobile and desktop applications**. Don't add `http://localhost` under **Web** or **Single-page application**. If you do, sign-in fails with an `InteractiveBrowserCredential authentication failed` error, because Entra then treats entraspect as an app that needs a client secret. If `http://localhost` is already listed under Web or Single-page application, remove it there and add it under Mobile and desktop applications.

You don't need to turn on **Allow public client flows**.

## 3. Add the permission

1. In the app registration, go to **API permissions** and select **Add a permission** > **Microsoft Graph**.
2. Add `Application.Read.All` as the type that matches your sign-in mode:
   - **Interactive:** select **Delegated permissions**.
   - **App-only:** select **Application permissions**.
   - Both modes: add it under both types.
3. Select **Grant admin consent for _your tenant_** and confirm. The **Status** column should show a green check for `Application.Read.All`.

New app registrations include the delegated `User.Read` permission by default. entraspect doesn't use it, so you can remove it.

In interactive mode, entraspect can only see what the signed-in user can see. A user with the **Global Reader** role can read every app registration and makes no changes.

## 4. Upload a certificate (app-only mode only)

Skip this step if you only use interactive mode.

App-only mode needs a certificate with a private key. entraspect keeps the private key: either in a `.pfx` file or in your operating system's certificate store. Entra only gets the public certificate.

1. In the app registration, go to **Certificates & secrets** > **Certificates** and select **Upload certificate**.
2. Upload the public certificate (`.cer`, `.crt` or `.pem`). Never upload the `.pfx` file.
3. Note the **Thumbprint** shown after upload. You can use it to find the certificate in your certificate store.

If you don't have a certificate, you can create a self-signed one. With OpenSSL:

```sh
openssl req -x509 -newkey rsa:2048 -sha256 -days 365 -nodes \
  -subj "/CN=entraspect" -keyout key.pem -out cert.pem
openssl x509 -in cert.pem -outform der -out entraspect.cer
openssl pkcs12 -export -inkey key.pem -in cert.pem -out entraspect.pfx
rm key.pem cert.pem
```

`openssl pkcs12` asks for a password to protect the `.pfx`. Upload `entraspect.cer` to Entra and keep `entraspect.pfx` somewhere safe, outside any source repository.

On Windows, PowerShell can create the certificate straight into your personal certificate store:

```powershell
$cert = New-SelfSignedCertificate -Subject "CN=entraspect" -CertStoreLocation Cert:\CurrentUser\My `
  -KeyExportPolicy NonExportable -KeySpec Signature -NotAfter (Get-Date).AddYears(1)
Export-Certificate -Cert $cert -FilePath .\entraspect.cer
$cert.Thumbprint
```

Upload `entraspect.cer` to Entra, then enter the printed thumbprint in entraspect.

## 5. Connect from entraspect

1. Start entraspect.
2. Enter a **Profile name**, the **Tenant ID** and the **Client ID** from step 1.
3. Choose the **Authentication mode**:
   - **Interactive:** nothing else to enter.
   - **AppOnly:** enter either the **Certificate (.pfx) path** and its password, or the **Certificate thumbprint**. For a thumbprint, entraspect looks in the current user's personal store, then the local machine's personal store (current user only on Linux).
4. Optionally select **Save profile** so you don't have to enter these again. The certificate password is never saved.
5. Select **Connect**, then **Run Scan**.

In interactive mode, a browser window opens when you select **Run Scan**. Finish signing in there, then return to entraspect.

Saved profiles are stored in `profiles.json` in an `entraspect` folder in your local application data folder:

- Windows: `%LOCALAPPDATA%\entraspect`
- macOS: `~/Library/Application Support/entraspect`
- Linux: `~/.local/share/entraspect`

## Troubleshooting

Sign-in errors usually include an `AADSTS` code. The Entra sign-in logs (**Entra ID** > **Monitoring & health** > **Sign-in logs**, filtered by your application ID) show the full failure reason even when entraspect doesn't.

| Error | Likely cause | Fix |
| --- | --- | --- |
| `AADSTS7000218` or `AADSTS9002327` | `http://localhost` is set under **Web** or **Single-page application** | Move it to **Mobile and desktop applications** (step 2) |
| `AADSTS50011` | Redirect URI doesn't match | Set it to exactly `http://localhost` (step 2) |
| `AADSTS700016` | Wrong client ID, or the app is in a different tenant | Use the **Application (client) ID** from the **Overview** page |
| `AADSTS90002` | Wrong tenant ID | Use the **Directory (tenant) ID** from the **Overview** page |
| `AADSTS65001` | Admin consent not granted | Grant admin consent (step 3) |
| `AADSTS700027` | Certificate not uploaded to this app registration, or expired | Upload the matching public certificate (step 4) |
| `AADSTS53003` | Blocked by a Conditional Access policy | Check the sign-in log to see which policy blocked it |
| 403 / `Authorization_RequestDenied` during the scan | Permission missing, of the wrong type or not consented | Add `Application.Read.All` as the correct type and grant consent (step 3) |
| Sign-in fails with no code | Browser window was closed or timed out | Run the scan again and finish signing in in the browser window that opens |
