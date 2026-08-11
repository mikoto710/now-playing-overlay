# Now Playing Overlay

<p align="center">
  <img src="host/Assets/NowPlayingOverlay.png" width="128" alt="Now Playing Overlay application icon">
</p>

Now Playing Overlay is a lightweight local Windows application that displays the current Spotify track in a transparent `350 x 70` OBS Browser Source. It reads Spotify's Windows media session, serves the overlay on loopback, and runs from the system tray.

It does not require Snip text or artwork files, and the current version does not require a Spotify Web API login.

## Requirements

- Windows 10 version 1809 (build 17763) or later, x64
- Spotify for Windows: the official Win32 version or the Microsoft Store version
- OBS Studio 31.0.1 for the currently validated Browser Source baseline (CEF 127)
- The x64 .NET 10 Desktop Runtime **and** ASP.NET Core Runtime

The application is framework-dependent, so the .NET runtime files are not bundled into the executable. Install both runtimes from the official [.NET 10 download page](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), or use WinGet:

```powershell
winget install Microsoft.DotNet.DesktopRuntime.10
winget install Microsoft.DotNet.AspNetCore.10
```

The .NET 10 SDK also supplies the required runtimes, but end users do not need the SDK.

## Download and start

1. Download `NowPlayingOverlay-v0.1.0-win-x64.zip` and its `.sha256` file from the project's [Releases](https://github.com/mikoto710/now-playing-overlay/releases) page.
2. Optionally verify the ZIP against the published SHA-256, then extract it. Keep `README.md` and `LICENSE` for reference; the application itself only needs `NowPlayingOverlay.exe` at runtime.
3. Place the executable in a directory where you want to keep it, then run it.
4. Look for the Now Playing Overlay icon in the Windows notification area. The application intentionally has no main window.
5. Start Spotify and play a track. The tray status should change from `Waiting for Spotify` to a Spotify playback status.

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
http://127.0.0.1:10598/NowPlaying.html
```

The overlay is visible only while Spotify reports a playing track. Pausing or stopping playback hides it; resuming playback shows it again without replaying an unchanged text transition.

## Tray menu

| Item                  | Action                                                                                       |
| --------------------- | -------------------------------------------------------------------------------------------- |
| Status                | Shows host startup, Spotify, Windows media-session, or fault status.                         |
| **Copy OBS URL**      | Copies the URL for the current running port.                                                 |
| **Open Overlay**      | Opens the overlay in the default browser. Double-clicking the tray icon does the same thing. |
| **Open Logs**         | Opens the application log directory.                                                         |
| **Configure Port...** | Saves a different available loopback port. A restart is required.                            |
| **Exit**              | Stops the local server and exits the application.                                            |

## Connection and startup behavior

An OBS page that has already loaded can reconnect automatically after Now Playing Overlay restarts. If the connection stays unavailable, the page hides stale track information after approximately five seconds and restores the latest complete state when it reconnects.

There is one important startup boundary: if OBS tries to load the Browser Source before Now Playing Overlay is running, the page itself was never loaded and therefore cannot reconnect. Start Now Playing Overlay, then open the Browser Source properties and refresh the current page or select **OK** to load it again.

If OBS appears to retain an old page despite the correct URL, refresh the Browser Source. If that does not help, fully exit both OBS and Now Playing Overlay, start Now Playing Overlay again, and then restart OBS.

## Change the port

The server listens only on `127.0.0.1`; it is not exposed to other computers on the network. The default port is `10598`.

To change it:

1. Select **Configure Port...** from the tray menu.
2. Choose an available port and save it.
3. Exit and restart Now Playing Overlay.
4. Copy the new OBS URL and update the Browser Source.

The running instance stays on its old port until it exits. If the saved port is occupied during startup, the application offers to save another one instead of silently choosing a random port.

For a temporary one-run override, start the executable from PowerShell with:

```powershell
.\NowPlayingOverlay.exe --Host:Port=13130
```

User settings are stored in `%LOCALAPPDATA%\NowPlayingOverlay\settings.json`.

## Troubleshooting

### The overlay is blank

- Confirm that Spotify is actively playing a track. A blank overlay while paused or stopped is expected.
- Check the tray status. `Waiting for Spotify` means that no supported Spotify media session is currently bound.
- Select **Open Overlay**. If it works in the default browser, verify the OBS URL and the `350 x 70` source size.
- If OBS first loaded the source while the application was not running, refresh the Browser Source after starting the application.

### The application reports a missing framework

Install both the x64 .NET 10 Desktop Runtime and ASP.NET Core Runtime. Installing only the plain .NET Runtime is not sufficient.

### The port is unavailable

Use the startup prompt or **Configure Port...** to select another available loopback port. Restart the application and replace the URL in OBS afterward.

### Spotify is playing but the tray keeps waiting

- Confirm that you are using either the official Spotify Win32 application or the Microsoft Store application.
- Restart Spotify, begin playback, and wait for the Windows media session to reappear.
- Browser media sessions and unsupported players are intentionally ignored.
- Open the logs if the status does not recover.

### Artist credits are incomplete

The current version uses metadata supplied by Spotify through Windows GSMTC. In some tracks, that source exposes only the first credited artist even though Spotify's own interface shows several artists. Optional Spotify Web API metadata enhancement is planned for a later milestone and is not part of the current release.

### Collect logs

Select **Open Logs**, or open:

```text
%LOCALAPPDATA%\NowPlayingOverlay\logs
```

The current file is `NowPlayingOverlay.log`; bounded rotated logs are kept in the same directory. Review logs for private local information before sharing them.

## Current scope and limitations

- Spotify is the only supported player in the initial release. The application icon is deliberately player-neutral so other sources can be added later.
- Playback state and metadata come from Windows GSMTC; there is no Spotify Web API or OAuth integration yet.
- Windows x64 is the only release architecture. ARM64 is not currently published.
- OBS Studio 31.0.1 with CEF 127 is the compatibility baseline. Earlier OBS/CEF versions are outside the initial compatibility target; other versions have not yet completed the same validation matrix.
- Missing-artwork and artwork-failure fallback paths have automated coverage, but the initial validation did not have a reproducible real Spotify sample for those cases.
- Network-loss behavior was not included in the completed manual matrix because Spotify playback could not be isolated reliably from the loss of its own streaming connection.

## Development

Development requires Windows 10 version 1809 or later, the .NET 10 SDK, Node.js 22, and npm.

Run the complete validation chain from the repository root:

```powershell
.\scripts\check.ps1
```

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

Console projects in this repository are development probes and are not user release artifacts.

## License

Now Playing Overlay is licensed under the [GNU General Public License v3.0](LICENSE).
