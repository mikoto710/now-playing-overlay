# Now Playing Overlay

<p align="center">
  <img src="host/Assets/NowPlayingOverlay.png" width="128" alt="Now Playing Overlay application icon">
</p>

Now Playing Overlay is a lightweight local Windows application that displays the current track from a user-selected Windows Media session in a transparent OBS Browser Source. It serves the overlay on loopback and runs from the system tray.

Playback metadata and artwork are read directly from the selected app's Windows GSMTC session. The current version does not require a Web API login or intermediate metadata files.

## Requirements

- Windows 10 version 1809 (build 17763) or later, x64
- A media player that publishes a Windows GSMTC session
- OBS Studio 31.0.1 for the currently validated Browser Source baseline (CEF 127)
- The x64 .NET 10 Desktop Runtime

The application is framework-dependent, so the .NET runtime files are not bundled into the executable. The Desktop Runtime includes everything the application needs; no additional server or embedded-browser runtime is required. Install it from the official [.NET 10 download page](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), or use WinGet:

```powershell
winget install Microsoft.DotNet.DesktopRuntime.10
```

The .NET 10 SDK also supplies the required runtimes, but end users do not need the SDK.

## Download and start

1. Download `NowPlayingOverlay-v0.1.0-win-x64.zip` and its `.sha256` file from the project's [Releases](https://github.com/mikoto710/now-playing-overlay/releases) page.
2. Optionally verify the ZIP against the published SHA-256, then extract it. Keep `README.md` and `LICENSE` for reference; the application itself only needs `NowPlayingOverlay.exe` at runtime.
3. Place the executable in a directory where you want to keep it, then run it.
4. Look for the Now Playing Overlay icon in the Windows notification area. The application intentionally has no main window.
5. Open **Settings...** from the tray. On **General**, select an exact player ID; optionally use **Appearance** to choose the Default style or configure the supported colors, background opacity, and corner radius. Save, then play a track.

Only one instance can run for the current Windows user. The current preview has no installer, automatic updater, or code signature; only run an executable obtained from a source you trust.

## Add the overlay to OBS

1. Right-click the tray icon and select **Copy OBS URL**.
2. In OBS, add a new **Browser** source. Do not select **Local file**.
3. Paste the copied URL into **URL**.
4. Set **Width** to `350` and **Height** to `70`.
5. Leave **Shutdown source when not visible** disabled.
6. Leave **Refresh browser source when scene becomes active** disabled.
7. Select **OK**.

The default URL is:

```text
http://127.0.0.1:13130/NowPlaying.html
```

The overlay is visible only while the selected player reports a playing track. Pausing or stopping playback hides it; resuming playback shows it again without replaying an unchanged text transition.

## Tray menu

| Item                  | Action                                                                                       |
| --------------------- | -------------------------------------------------------------------------------------------- |
| Status                | Shows host startup, selected-source, Windows media-session, or fault status.                 |
| **Copy OBS URL**      | Copies the URL for the current running port.                                                 |
| **Open Overlay Preview** | Opens the overlay at a selected preview resolution. Double-click opens the default size.  |
| **Open Logs**         | Opens the application log directory.                                                         |
| **Settings...**       | Opens General source/port settings and the Appearance tab.                                  |
| **Exit**              | Stops the local server and exits the application.                                            |

## Customize the appearance

Open **Settings...**, then select the **Appearance** tab. **Default** preserves the original green-artist, white-track, opaque dark-background style. **Custom** supports artist color, track color, background color, background opacity from `0` to `100` percent, and an overall corner radius from `0` to `35` logical pixels. **Reset to Default** restores all five values and selects Default.

Appearance is saved together with the General settings. **Cancel** discards all pending changes. A newly opened Preview or OBS page uses the saved appearance; refresh an already loaded page to apply a change.

## Connection and startup behavior

An OBS page that has already loaded can reconnect automatically after Now Playing Overlay restarts. If the connection stays unavailable, the page hides stale track information after approximately five seconds and restores the latest complete state when it reconnects.

There is one important startup boundary: if OBS tries to load the Browser Source before Now Playing Overlay is running, the page itself was never loaded and therefore cannot reconnect. Start Now Playing Overlay, then open the Browser Source properties and refresh the current page or select **OK** to load it again.

If OBS appears to retain an old page despite the correct URL, refresh the Browser Source. If that does not help, fully exit both OBS and Now Playing Overlay, start Now Playing Overlay again, and then restart OBS.

## Change the port

The server listens only on `127.0.0.1`; it is not exposed to other computers on the network. The default port is `13130`.

To change it:

1. Select **Settings...** from the tray menu.
2. Choose an available port and save it.
3. The running server starts the new port first and asks already loaded overlay pages to follow it.
4. Copy the new OBS URL and update the saved Browser Source for future OBS reloads and restarts.

The old port remains available for a short migration period and then closes. If the new port is occupied or the setting cannot be saved, the old port remains active and the saved configuration is not changed. The application cannot rewrite the Browser Source URL stored by OBS.

If the saved port is occupied during startup, the application offers to save another one instead of silently choosing a random port; because the server did not start in that case, restart the application after saving the replacement.

For a temporary one-run override, start the executable from PowerShell with:

```powershell
.\NowPlayingOverlay.exe --Host:Port=14130
```

User settings are stored in `%LOCALAPPDATA%\NowPlayingOverlay\settings.json`.

## Troubleshooting

### The overlay is blank

- Confirm that the selected player is actively playing a track. A blank overlay while paused or stopped is expected.
- Check the tray status. `Source Not Configured` means a player still needs to be selected. A missing or ambiguous status keeps the saved selection without switching to another app.
- Select **Open Overlay Preview**. If it works in the default browser, verify the OBS URL and the `350 x 70` source size.
- If OBS first loaded the source while the application was not running, refresh the Browser Source after starting the application.

### The application reports a missing framework

Install the x64 .NET 10 Desktop Runtime. The plain .NET Runtime alone is not sufficient because the tray uses Windows Forms.

### The port is unavailable

Use the startup prompt or **Settings...** to select another available loopback port. A running instance moves without restarting; a startup failure still requires restarting after saving the replacement. Replace the saved URL in OBS afterward.

Windows may reserve a port even when no ordinary process is listening on it. The startup prompt treats supported TCP and HTTP.sys conflict results as an unavailable port and lets the user choose a replacement.

### The selected player is missing or ambiguous

- Open **Settings...**, start playback in the intended player, and select **Refresh**.
- Confirm the raw player ID still matches the saved selection. The application deliberately does not guess a replacement ID.
- If multiple sessions have the same exact ID, leave only one active or make exactly one of them play so the source can be disambiguated.
- Open the logs if Windows Media sessions remain unavailable.

### Artist credits are incomplete

The current version uses metadata supplied by the selected player through Windows GSMTC. Some players expose fewer artist credits or lower-resolution artwork than their own interface. The overlay preserves the selected source's values and does not merge fields from a network API.

### Collect logs

Select **Open Logs**, or open:

```text
%LOCALAPPDATA%\NowPlayingOverlay\logs
```

The current file is `NowPlayingOverlay.log`; bounded rotated logs are kept in the same directory. Review logs for private local information before sharing them.

## Current scope and limitations

- The current source is a user-selected Windows GSMTC player ID; the application does not maintain an executable allowlist or follow the system current session automatically.
- Playback state and metadata come from Windows GSMTC; there is no Spotify Web API or OAuth integration.
- Windows x64 is the only release architecture. ARM64 is not currently published.
- OBS Studio 31.0.1 with CEF 127 is the compatibility baseline. Earlier OBS/CEF versions are outside the initial compatibility target; other versions have not yet completed the same validation matrix.
- Missing-artwork and artwork-failure fallback paths have automated coverage, but the initial validation did not have a reproducible real player session for those cases.
- Network behavior belongs to the selected player; the local overlay host itself does not poll a metadata service.

## Development

Development requires Windows 10 version 1809 or later, the .NET 10 SDK, Node.js 22, and npm.

Run the normal product validation chain from the repository root. It reuses ignored
incremental artifacts and the existing frontend dependencies:

```powershell
.\scripts\check.ps1
```

The release script performs the clean dependency install and also runs the separate
media-session probe tests before publishing. The probe is kept out of the normal
product check because it is a development diagnostic, not part of the shipped host.

The frontend package is under `web`, not the repository root. For frontend-only work, use `npm --prefix web <command>`; for example:

```powershell
npm --prefix web run check
npm --prefix web test
npm --prefix web run build
```

Create the framework-dependent Windows x64 single-file application with:

```powershell
.\scripts\publish.ps1
```

The script runs the full validation chain. The runtime publish directory still contains only:

```text
artifacts/publish/win-x64/NowPlayingOverlay.exe
```

It also creates the versioned GitHub release assets under `artifacts/release`:

```text
NowPlayingOverlay-v0.1.0-win-x64.zip
NowPlayingOverlay-v0.1.0-win-x64.zip.sha256
```

The publish gate also verifies that the framework closure contains only `Microsoft.NETCore.App` and `Microsoft.WindowsDesktop.App`. The reasoning and measurements behind the selected framework-dependent single-file format are recorded in [docs/runtime-optimization.md](docs/runtime-optimization.md).

Console projects in this repository are development probes and are not user release artifacts.

## License

Now Playing Overlay is licensed under the [GNU General Public License v3.0](LICENSE).
