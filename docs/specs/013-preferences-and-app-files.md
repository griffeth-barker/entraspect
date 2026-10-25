# Spec 013: Preferences and app files

**Status:** Draft (step 4 waits for the logo)
**Goal:** More choice in Settings > Preferences (any time zone, light or dark theme), buttons to open `profiles.json` and `settings.json` for editing, and the entraspect logo on the About page once it exists.

**Out of scope for this spec:** per-profile preferences, a built-in JSON editor, watching the files for changes (see Open questions), and any other app icon work beyond showing the logo.

**Graph permissions:** none. Nothing here touches Microsoft Graph. Opening a local file in the user's default app is not an outbound call, so the no-telemetry rule is unaffected.

Follow AGENTS.md. Complete one step at a time, make sure `dotnet build` and `dotnet test` pass, then commit before starting the next step.

## Step 1: Time zone drop-down

Today Settings > Preferences has two radio buttons, UTC and local time (`AppSettings.UseLocalTime`).

- Replace them with one drop-down, "Show dates and times in". Entries, in order: "UTC", "Local time (*computer's zone name*)", then every zone from `TimeZoneInfo.GetSystemTimeZones()`, sorted by UTC offset then name, each shown by its display name, for example "(UTC+01:00) Amsterdam, Berlin, Bern, Rome, Stockholm, Vienna".
- `AppSettings` gets `TimeZone` (string): `"UTC"`, `"Local"`, or a time zone ID. Default `"UTC"`, as today. Store the IANA ID where the platform gives one (macOS and Linux); on Windows store the Windows ID. When loading, an ID the platform doesn't know is converted with `TimeZoneInfo.TryConvertIanaIdToWindowsId` or `TryConvertWindowsIdToIanaId`; if that fails too, use UTC and show a short message on the Preferences page ("The saved time zone isn't available on this computer; showing UTC.").
- Migration: a `settings.json` with `"UseLocalTime": true` and no `TimeZone` loads as `"Local"`. `UseLocalTime` is no longer written.
- `TimeDisplay` follows the choice. Formats stay as today for UTC and local time. For a chosen zone, `DateTime` and `Time` end with the zone's offset at that instant, for example "2026-10-24 14:13 (UTC+02:00)", so daylight saving is right for each date. `ZoneLabel` (column headers such as "Expires (UTC)") is "UTC", "local", or the zone's ID, for example "Europe/Berlin".
- Changes apply at once, as today: every page re-renders its dates.
- Tests: `TimeDisplay` with a custom zone (including a date on each side of a daylight saving change), the migration, an unknown ID, and the Preferences view model saving the choice. Build zones with `TimeZoneInfo.CreateCustomTimeZone`; tests must not depend on the computer's own zone.

## Step 2: Theme drop-down

- Settings > Preferences gets a second drop-down, "Theme": "Use system setting" (default), "Light", "Dark".
- `AppSettings.Theme`: an enum `AppTheme { System, Light, Dark }`, default `System`. A missing or unknown value loads as `System`.
- The choice applies at once, without a restart, by setting `Application.RequestedThemeVariant` to `Default`, `Light` or `Dark`. Confirm that FluentAvaloniaTheme (`PreferSystemTheme="True"`) follows `RequestedThemeVariant` when it isn't `Default`; if it doesn't, use FluentAvalonia's own theme setting instead. Apply the saved theme at startup before the main window shows, so it doesn't flash the other theme.
- View models change the theme only through `IUserInteraction` (add `SetTheme(AppTheme)`), so tests can fake it.
- The severity labels, Info blue and the gray "not available" pills must stay readable in both themes; they already use theme brushes, so check this by hand rather than adding new colours.
- Tests: the view model saves the choice and calls `SetTheme`; settings without `Theme` load as `System`.

## Step 3: Open profiles.json and settings.json

- **Settings > Connection profiles:** an "Open profiles.json" button and a "Reload" button, with an `InfoTip`: "Edit profiles in a text editor, save, then select Reload. Passwords are never saved."
- **Settings > About**, in the App data folder section: the same pair for `settings.json`.
- "Open" opens the file in the computer's default app for `.json` files, through a new `IUserInteraction.OpenFileAsync(string path)` (Avalonia `Launcher.LaunchFileInfoAsync`). If the file doesn't exist yet, entraspect first saves the current profiles or settings so there is something to edit. If no app can open it, show the error plainly with the file's path.
- "Reload" reads the file again and applies it as if the user had made the same changes in the app: profiles are validated like profiles saved from the profile form, and settings go through `CheckOptions.Validate` and `Normalize`. Toggled checks, expiry windows, time zone and theme update at once.
- A file that can't be read or is invalid: show the error plainly, keep what is already loaded, and write nothing, so the user's edit isn't overwritten and can be fixed. Don't use the startup behaviour of backing the file up and starting empty.
- entraspect saves these files itself when the user changes something in the app, which overwrites unsaved outside edits. The `InfoTip` says to reload before changing things in the app.
- Reloading profiles doesn't disconnect. If the connected profile was removed or renamed, the connection keeps working, and Reconnect follows the same rules as when the profile is deleted in the app.
- Tests: the open calls go to the fake `IUserInteraction` with the right path; a missing file is created first; reload applies valid changes; invalid JSON and invalid values show an error and leave both the file and the loaded state unchanged. Use a temporary folder.

## Step 4: Logo on the About page

The logo artwork is in `assets/`. The About page uses `entraspect-icon-with-lettermark-cropped-xp.png` (the logo with its lettermark, transparent background) and the window icon is `entraspect-icon-square.png`.

- Show the logo at the top of Settings > About, at a size that fits the page without scrolling on a small window. The logo's lettermark is the page heading, so the page has no "About entraspect" title.
- Use it as the window icon too.
- Light and dark versions, if the logo has them, follow the theme from step 2.
- Packaging icons (macOS `.icns`, Windows `.ico`) are out of scope; add them to Open questions when the logo exists.

## Step 5: Docs

- `README.md` "Using entraspect": Preferences now covers the time zone and theme; Connection profiles and About can open the JSON files.
- `docs/setup.md`: where `profiles.json` and `settings.json` are, that they can be edited and reloaded, and that passwords are never in them.

## Acceptance criteria

- `dotnet build` and `dotnet test` pass with no warnings.
- No test makes a network call or depends on the computer's time zone.
- No Graph permission is added; no new outbound call.
- Manual check: choosing a zone such as "(UTC+09:00) Osaka, Sapporo, Tokyo" changes every date shown, including column headers and the Overview scan time; an existing `settings.json` with `UseLocalTime: true` opens with local time selected; Light and Dark switch at once and survive a restart; "Use system setting" follows the operating system; editing a profile name in `profiles.json`, saving and selecting Reload shows the new name; a broken `settings.json` shows an error on Reload and the file is left as the user wrote it.

## Open questions

(Agents: add anything you think is out of scope or ambiguous here instead of implementing it.)

- **Watching the files:** a `FileSystemWatcher` could reload automatically after the editor saves, instead of a Reload button. It would also need to handle partial writes and the app's own saves.
- **Open folder:** a button to show the app data folder in Finder or File Explorer, next to its path on About.
- **Theme follows the OS at runtime:** "Use system setting" should switch when the OS switches while entraspect is open. Confirm Avalonia does this on macOS, Windows and Linux.
- **Reload and profiles without a certificate:** Reload validates profiles like the profile form, which needs a .pfx path or a thumbprint. A legacy profile with neither still loads at startup (with "Add a certificate to use this profile."), but makes Reload fail until it is fixed or removed. Should Reload accept it the way startup does?
- **Logo in dark theme:** the lettermark's "entra" is dark gray, which may be hard to read on the dark theme's background. A light-on-dark variant would follow the theme.
- **Packaging icons:** macOS `.icns` and Windows `.ico` files made from `entraspect-icon-square.png`. Until the app is packaged as a `.app` bundle, the macOS Dock shows the generic executable icon: Avalonia doesn't set the Dock icon from `Window.Icon`, and `dotnet run` has no bundle to take it from.
