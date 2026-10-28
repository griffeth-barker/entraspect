# Security policy

entraspect reads a Microsoft Entra ID tenant with an app registration's certificate, so security bugs matter here. Thank you for reporting them privately.

## Reporting a vulnerability

Report it through GitHub: on this repository's **Security** tab, select **Report a vulnerability**. Please don't open a public issue, discussion or pull request for a vulnerability.

Include what you found, how to reproduce it, and which commit or version you tested. Never include real tenant data, access tokens, certificates, private keys or passwords; use made-up values instead.

## Supported versions

There are no releases yet. Only the latest commit on `main` gets security fixes.

## In scope

Anything that breaks entraspect's promises, for example:

- entraspect changing a tenant, or calling a Microsoft Graph endpoint other than a read (`GET`).
- entraspect asking for, or working with, a write permission.
- Access tokens, certificate private keys or `.pfx` passwords being logged, saved, shown or sent anywhere.
- entraspect connecting anywhere other than Microsoft sign-in and Microsoft Graph.
- A crafted Graph response (for example a malicious display name) that makes entraspect run code, write files outside its own folder, or produce an export that runs code when opened.

## Out of scope

- Findings entraspect reports about your tenant. Those are what it's for.
- Vulnerabilities in Microsoft Entra ID or Microsoft Graph. Report those to the [Microsoft Security Response Center](https://msrc.microsoft.com/report).
- Someone who already controls your computer or account reading entraspect's files or certificate.
