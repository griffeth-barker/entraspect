# Spec 014: Severity pills in the navigation and a status bar

**Status:** Ready
**Goal:** Make the navigation and the results pages easier to read. Check counts in the navigation use the same severity pills as Overview, lined up in one column with the section chevrons. Scan and connection status moves out of the page body into a status bar along the bottom of the main pane.

**Out of scope for this spec:** changing the Overview table or its pills, new filters or columns, a header or toolbar, notifications outside the window (dock or taskbar badges), and changing what a scan does.

**Graph permissions:** none. Nothing here touches Microsoft Graph.

Follow AGENTS.md. Complete one step at a time, make sure `dotnet build` and `dotnet test` pass, then commit before starting the next step.

## Background

Today each check's navigation item shows FluentAvalonia's round `FAInfoBadge`: the finding count in the accent colour (orange on the user's machine), or a caution marker when the check failed or is missing a permission. The badge sits a little to the right of the section chevrons, so badges and chevrons don't line up, and a check with no findings shows an orange "0". The accent colour carries meaning there, which AGENTS.md says not to do. Overview shows the same results as severity pills (`Border.label` with `critical`, `warning`, `info` or `neutral`, defined in `App.axaml`).

Each results page also has its own status text: the scan bar (`ScanBar.axaml`) shows the progress bar and "Scan finished at 17:21 UTC." next to Scan, Overview shows "Last scan: …" below the connection card, and each check page shows "Last run …" (with "· Report updated …" for report checks). The same moment shows up two or three times on one page.

## Step 1: Navigation badge state

View model only; the view stays as it is until step 2.

- Replace `NavItemViewModel.BadgeValue`, `HasBadge` and `HasWarning` with two independent badges:
  - **Count badge:** `BadgeCount` (int, 0 means no count badge), `BadgeSeverity` (`Severity?`, the highest severity among the findings) and `CountToolTip`: the breakdown by severity, most severe first, leaving out severities with none, for example "1 critical, 3 warning".
  - **Problem badge:** `HasProblem` (bool) and `ProblemToolTip`: why there's no result, for example "Check failed" or "Missing permissions".
- A check's badges after a scan:
  - Succeeded with findings: the count badge, with the total and the highest severity.
  - Succeeded with no findings: no badge. The lack of a badge means no problem; there is no "nothing found" mark.
  - Failed: the problem badge, "Check failed". Missing permissions: the problem badge, "Missing permissions".
  - Before the first scan, cancelled, not run, or missing a licence: no badge, as today.
- **Sections** (Users, Groups and so on) show a summary only while collapsed; while expanded they show no badge, since each child shows its own. While collapsed:
  - the count badge, with the total of their checks' findings and the highest severity among them, and the breakdown as its tooltip;
  - and, if any child has a problem, the problem badge too, with a tooltip that counts them, for example "1 check failed", "2 checks failed" or "1 check failed, 1 check missing permissions". A failed check inside a collapsed section is always visible, never only in a tooltip.
  - The badges update when the section is expanded or collapsed and when a scan finishes.
- Settings > Checks: `SetWarning` sets the problem badge with "A check is missing permissions", or clears it.
- Tests: each case above, the count tooltip breakdown, a collapsed and an expanded section, a collapsed section with one failed child and one child with findings (both badges, each with its tooltip), and Settings > Checks. Update the existing tests that use `BadgeValue`, `HasBadge` and `HasWarning`.

## Step 2: Severity pills in the navigation

- Show the badge as a pill in the same style as Overview's: `Border.label` with `critical`, `warning` or `info` from `BadgeSeverity`, holding `BadgeCount`. Smaller than on Overview so it fits the item height: about 11 to 12 pt text and a minimum width that fits two digits, so "1", "7" and "10" line up.
- The problem badge is a `neutral` (gray) pill holding a "!" glyph, not red or yellow, so a check without a result never looks like a severity. Gray already means "no result" on Overview, where unlicensed checks show gray "–" pills.
- Each pill's tooltip is its `CountToolTip` or `ProblemToolTip`. A disabled item (not connected, unlicensed) shows no badge.
- **One column for every right-side marker.** A child's pill is centred on the same vertical line as its section's chevron, and a collapsed section's count pill sits just left of its chevron. When a collapsed section shows both badges, the "!" pill sits just left of the count pill. Every pill, chevron and section summary in the pane lines up, at any window height and with the pane scrolled.
- FluentAvalonia's `FANavigationViewItem.InfoBadge` only takes an `FAInfoBadge`. Either restyle `FAInfoBadge` so it looks like the pill and can be placed in the chevron column, or put the pill in the item's content with the text, aligned to the right edge where the chevron column starts. Use whichever gives the alignment above without retemplating the whole `FANavigationViewItemPresenter`; say which in the commit message.
- Remove the `FAInfoBadge` styles in `MainWindow.axaml` that are no longer used.
- Colours come from the existing theme brushes, so the pills stay readable in light and dark themes (spec 013).
- Tests: none for the view. Check by hand (see Acceptance criteria).

## Step 3: Status bar state

View model only.

Add a `StatusBarViewModel` (or properties on `MainViewModel`, whichever keeps `MainViewModel` simpler) that every page shows. It has a left part for what is happening now and a right part for context.

- **Left: activity.**
  - Not connected: "Not connected".
  - Connecting: "Connecting to *profile name*…", with an indeterminate progress bar.
  - Scanning: the current `ScanProgress.Message`, for example "Running check 3 of 10: Policy hygiene (looking up user 4 of 20)", with a determinate progress bar at `(Position - 1) / Total` (a check's own detail doesn't move the bar), and indeterminate while the text is "Starting scan…". A cancel command (the existing `CancelCommand`) is available only while scanning.
  - After a scan: "Scan finished at *time*", "Scan cancelled" or, after an unexpected error, "Scan stopped with an error".
  - Connected, no scan yet: "Connected. Select Scan to check the tenant."
- **Right: context.**
  - On a check page, what the page's `StatusText` says today: "Last run *date time*", "Last run *date time* · Report updated *date time*", "Last run *date time* failed", "Not run: missing permissions" or "Last scan cancelled". Nothing before the first scan.
  - On Overview, "Last scan *date time*", or nothing before the first scan.
  - On Settings pages, nothing.
  - Then, on every page while connected, the connected profile's name. It is plain text: clicking it does nothing.
- Every date and time goes through `TimeDisplay` and updates when the time zone preference changes.
- The status bar shows status only. Errors stay on the page where they belong (see step 4).
- Tests: each activity state, including the progress bar value and determinate or indeterminate, and that cancel is available only while scanning; the context for a check page in each result state, for Overview and for a Settings page; the profile name while connected and not otherwise; a time zone change re-rendering the times. Use the existing fakes and `FakeTimeProvider`.

## Step 4: Show the status bar and clean up the pages

- Add the status bar along the bottom of the main pane, the area to the right of the navigation, spanning its full width. The navigation pane keeps its full height. It is a thin bar (one line of text) with a top border and the window's secondary background, so it reads as chrome rather than content. Text is in the `muted` style; the progress bar is a short bar next to the activity text.
- **Cancel** is a small gray "X" icon button right after the activity text and progress bar, shown only while scanning, with the tooltip "Cancel scan" and an accessible name of "Cancel scan". It is not an accent or standard-size button.
- The page content scrolls above it; the status bar never scrolls with the page.
- **Remove from the pages:**
  - The progress bar, progress text and Cancel button in `ScanBar`. It keeps only Scan and `ScanErrorText`.
  - "Last scan: …" on Overview.
  - The "Last run …" status line on check pages (`StatusText` moves to the status bar).
- **Keep on the pages:**
  - Scan.
  - Errors: `ScanErrorText` and a check page's `ErrorText`. AGENTS.md keeps short error messages visible where they apply.
  - "Not available: needs an Entra ID P1 or P2 licence." on an unlicensed check page, and the "turned off in Settings" note on a disabled check, because they explain why the page is empty.
  - The permission banners and the unused permissions banner.
  - The connection card on Overview, unchanged.
- Settings > Connection profiles keeps its own connection status next to Connect, Reconnect and Disconnect.
- Tests: update view model tests that read the removed properties; nothing should still bind to them.

## Step 5: Docs

- `AGENTS.md` "UI conventions": the scan bar is now just Scan on Overview and every check page; progress, Cancel (a small "X"), and scan and connection status are in the status bar at the bottom of the main pane; the navigation shows severity pills (count and highest severity, gray "!" for a check without a result, no badge for no findings, a summary on collapsed sections only), lined up in the chevron column.
- `README.md` "Using entraspect": the status bar, and what the pills in the navigation mean.

## Acceptance criteria

- `dotnet build` and `dotnet test` pass with no warnings.
- No test makes a network call.
- No Graph permission is added; no new outbound call.
- Manual check, in light and dark themes:
  - After a scan, each check with findings shows a pill coloured by its highest severity with the total count, matching the Overview colours; a check with no findings shows nothing; a failed check and a check missing a permission show a gray "!" with the reason on hover. Hovering a count shows the breakdown.
  - Pills and chevrons line up in one column from top to bottom, including with a two-digit count and with the pane scrolled.
  - Collapsing a section shows its summary pill; expanding it hides the summary. A collapsed section with a failed check shows a gray "!" next to its count, saying which check failed on hover.
  - Connecting shows "Connecting to *profile*…" in the status bar; a scan shows the running check and a bar that fills as checks finish, including the "looking up user N of M" detail; the small "X" next to it cancels the scan ("Scan cancelled"); afterwards "Scan finished at *time*". There's no Cancel button on the page.
  - On a check page the status bar shows "Last run …" (and "Report updated …" on MFA not registered); on Overview "Last scan …"; on Settings only the profile name. None of these appear in the page body any more.
  - A failed check still shows its error on its page, and a scan error still shows under Scan.
  - Changing the time zone in Settings > Preferences updates the times in the status bar.

## Open questions

(Agents: add anything you think is out of scope or ambiguous here instead of implementing it.)

- **Decision: Cancel** moves into the status bar as a small gray "X" next to the scan activity, not a standard button. The page keeps only Scan.
- **Decision: Overview connection card** stays as it is for now.
- **Decision: the profile name** in the status bar is plain text; clicking it does nothing for now.
- **Decision: expanded sections** show no summary; only collapsed sections do.
- **Decision: no "nothing found" mark.** A check scanned with no findings shows no badge; the lack of a badge means no problem.
- **Decision: failed checks in a collapsed section** show a gray "!" pill next to the section's count, with a tooltip saying how many checks failed or are missing permissions.
- **Export confirmation (implemented as a stopgap, confirm).** The scan bar used to show "Exported to *file*." after an export. With the progress text gone, the status bar's activity shows "Exported to *file*" until the next scan. Keep it there, or drop it?
- **Export stays next to Scan.** Step 4 says the scan bar keeps only Scan and `ScanErrorText`; Export (spec 010) and its error were kept with it, since nothing else offers export.
- **Settings in the footer has no summary.** Only check sections summarise while collapsed. A collapsed Settings doesn't show the "!" of Settings > Checks (missing permissions); Settings starts expanded. Should it?
- **Long status text.** The status bar is one line: on a narrow window the activity and the context trim with an ellipsis and show the full text on hover. The sign-in period of report checks ("· 12,345 sign-in(s) from … to …") is part of the context and is often trimmed. Show it elsewhere?
