# Now Playing Overlay

<p align="center">
  <img src="host/Assets/NowPlayingOverlay.png" width="128" alt="Now Playing Overlay application icon">
</p>

A Windows tray app that displays the current track and artwork in an OBS Browser Source. Customize the overlay's appearance or export track information to local files.

## Quick start

Requires **Windows 10 1809+ (x64)**, **OBS Studio**, and the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).

1. Download the ZIP from [Releases](https://github.com/mikoto710/now-playing-overlay/releases), extract it, and run `NowPlayingOverlay.exe`.
2. Open **Settings...** from the tray icon, choose and configure a source below, then select **Save**.
3. Select **Copy OBS URL** from the tray menu.
4. Add a **Browser** source in OBS, paste the URL, and set its size to **350 × 70** for the default scale.

The app runs in the system tray. Use **Appearance** in Settings to customize the overlay, or **Open Overlay Preview** from the tray menu to check it before using OBS.

## Choose a source

| Source | Setup |
| --- | --- |
| **Windows Media** | Start playback, select **Refresh**, and choose a Windows media session, such as Spotify or a compatible browser/player. |
| **Spotify API** | Connect your Spotify account using your own Developer app Client ID; see below. |
| **Browser Player** | Install the Tampermonkey Producer and pair it with the app. Supports YouTube, Spotify Web, and other listed players. [Setup and supported sites](docs/browser-producer.md#install-and-connect). |
| **Window Title** | Choose a desktop window and use its whole title or configure a title/artist split. [Setup and limitations](docs/window-title.md#setup). |

### Spotify API setup

Create an app in the [Spotify Developer Dashboard](https://developer.spotify.com/dashboard) with this redirect URI:

```text
http://127.0.0.1:13130/oauth/spotify/callback
```

If you changed the local port, update the URI to match. In **Spotify Connection...**, enter the Client ID and authorize in your browser. No Client Secret is required.

## Usage notes

- Pausing or stopping playback hides the overlay. **Window Title** cannot detect pause state and stays visible while a usable caption exists; it provides no artwork or timeline.
- If OBS is blank, check **Open Overlay Preview**, then refresh the OBS Browser Source.
- Under **Settings... > Outputs**, optionally enable TXT, JSON, artwork PNG, or track history files. [Output guide](docs/outputs.md).
- The local server listens only on `127.0.0.1`. Settings are stored in `%LOCALAPPDATA%\NowPlayingOverlay`.

## Development

Requires the .NET 10 SDK, Node.js 22, and npm. From the repository root:

```powershell
npm --prefix web install
.\scripts\check.ps1       # Validate
.\scripts\publish.ps1     # Validate and package a release
```

See [technical documentation](docs) and the [release checklist](docs/release-checklist.md).

## Credits and license

Inspired by [Snip](https://github.com/dlrudie/Snip), [Tuna](https://github.com/univrsal/tuna), and [Zyphen's Now Playing](https://github.com/ZyphenVisuals/zyphens-now-playing). Browser site readers are independently implemented for this project.

[GNU General Public License v3.0](LICENSE)
