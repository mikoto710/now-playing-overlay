# Now Playing Overlay

<p align="center">
  <img src="host/Assets/NowPlayingOverlay.png" width="128" alt="Now Playing Overlay application icon">
</p>

A small Windows tray application that shows the current track and artwork in an OBS Browser Source.

It supports two independent media sources:

- **Windows Media** — reads a selected Windows media session, including Spotify, browsers, and other compatible players.
- **Spotify API** — reads the current track from your Spotify account using your own Spotify Developer application Client ID.

The overlay can customize its colors, typography, background, and artwork layout.

## Requirements

- Windows 10 version 1809 or later, x64
- OBS Studio with Browser Source support
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)

The runtime can also be installed with WinGet:

```powershell
winget install Microsoft.DotNet.DesktopRuntime.10
```

## Quick start

1. Download the latest ZIP from [Releases](https://github.com/mikoto710/now-playing-overlay/releases), extract it, and run `NowPlayingOverlay.exe`.
2. Open **Settings...** from the tray icon.
3. Select a provider:
   - **Windows Media:** start playback, select **Refresh**, and choose the player.
   - **Spotify API:** open **Spotify Connection...**, enter your Client ID, and authorize in the browser.
4. Optionally customize the overlay on the **Appearance** tab, then select **Save**.
5. Select **Copy OBS URL** from the tray menu.
6. Add a **Browser** source in OBS, paste the URL, and set its size to `350 × 70`.

The default overlay URL is:

```text
http://127.0.0.1:13130/NowPlaying.html
```

The application has no main window. Use the tray menu to open a preview, change settings, view logs, or exit.

## Spotify setup

Create an application in the [Spotify Developer Dashboard](https://developer.spotify.com/dashboard) and register this redirect URI:

```text
http://127.0.0.1:13130/oauth/spotify/callback
```

If you changed the application port, replace `13130` with the current port. The complete URI, including the port and path, must match the Dashboard entry.

Only the Client ID is required. Authorization uses the system browser and PKCE; no Client Secret is stored by the application.

## Notes

- The local server listens only on `127.0.0.1`.
- Only one instance runs for each Windows user.
- Pausing or stopping playback hides the overlay.
- If OBS is blank, use **Open Overlay Preview** first, then refresh the Browser Source.
- Settings and protected Spotify credentials are stored under `%LOCALAPPDATA%\NowPlayingOverlay`.

## Development

Requires the .NET 10 SDK, Node.js 22, and npm.

```powershell
.\scripts\check.ps1
.\scripts\publish-fast.ps1
.\scripts\publish.ps1
```

- `check.ps1` runs the normal validation chain.
- `publish-fast.ps1` creates a quick Debug executable without tests.
- `publish.ps1` runs release validation and creates the release package.

Protocol and release details live in [`docs`](docs), not in this README.

## License

[GNU General Public License v3.0](LICENSE)
