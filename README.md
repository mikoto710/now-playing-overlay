# Now Playing Overlay

<p align="center">
  <img src="host/Assets/NowPlayingOverlay.png" width="128" alt="Now Playing Overlay application icon">
</p>

A small Windows tray application that shows the current track and artwork in an OBS Browser Source.

It supports three independent media sources:

- **Windows Media** — reads a selected Windows media session, including Spotify, browsers, and other compatible players.
- **Spotify API** — reads the current track from your Spotify account using your own Spotify Developer application Client ID.
- **Browser Player** — receives playback metadata from supported browser players through the Host-provided Tampermonkey Producer.

The overlay can customize its colors, typography, background, and artwork layout. The Host can also write templated text, protocol JSON, current artwork, and track history to local files.

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
   - **Browser Player:** install the browser Producer from the running Host, copy the connection code, and paste it into the userscript menu once.
4. Optionally customize the overlay on **Appearance** or configure local files on **Outputs**, then select **Save**.
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

## Browser Player setup

Browser Player is the simple browser-integration path. It does not require a separate bridge application or any manual ingest-protocol work:

1. Install [Tampermonkey](https://www.tampermonkey.net/) in the browser that plays your music.
2. Open **Settings...**, choose **Browser Player**, and select **Install Browser Producer...**.
3. Select **Copy Connection Code**.
4. On a supported music page, open Tampermonkey's **Now Playing Overlay Browser Producer** menu, choose **Configure Now Playing Overlay**, and paste the code.
5. Save **Browser Player** as the active provider and start playback.

The Producer has site-specific metadata and artwork readers for Spotify Web, YouTube/YouTube Music, SoundCloud, Deezer, Yandex Music, Pretzel, Plex, Chillhop, and Bilibili, followed by a general Media Session fallback. These readers use separate page fields for title and artist; they do not guess the order of an `Artist - Title` string.

The script manages authentication, Producer identity, ordering, heartbeat, retry, Host restart recovery, multi-tab ownership, and current-cover transfer internally. Artwork URLs stay in the browser: the script retrieves supported images and uploads only validated bytes to the local Host. Its key is stored in userscript-private storage and is sent only to `127.0.0.1`. Use **Rotate Code...** to invalidate the old code, clear the active lease, and copy a replacement. See [Browser Producer](docs/browser-producer.md) for troubleshooting and adapter guidance.

## Notes

- The local server listens only on `127.0.0.1`.
- Only one instance runs for each Windows user.
- Pausing or stopping playback hides the overlay.
- If OBS is blank, use **Open Overlay Preview** first, then refresh the Browser Source.
- Settings and protected Spotify credentials are stored under `%LOCALAPPDATA%\NowPlayingOverlay`.
- The Browser Player ingest key is protected for the current Windows user and is never placed in the overlay URL.

## Local outputs

Open **Settings... > Outputs** to configure one templated TXT file, one Local Protocol v3 JSON file, one stable current-artwork PNG, and one append-only History file. Outputs are disabled by default and work even when no OBS Browser Source is open.

TXT templates use tokens such as `{nowPlaying}`, `{title}`, `{artist}`, `{albumTitle}`, `{playback}`, `{position}`, and `{observedAt}`. Current files are replaced atomically; History adds one line only when the committed track identity changes. A failure in one target does not stop media sources, the overlay, or other outputs.

See [Local outputs](docs/outputs.md) for the complete token syntax, no-media behavior, file semantics, and OBS acceptance checklist.

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

The release ZIP contains `NowPlayingOverlay.exe`, the README, and the license. The userscript remains embedded in the executable and is installed through **Install Browser Producer...** while the Host is running.

Protocol, Outputs, and release details live in [`docs`](docs), not in this README.

## Acknowledgements

This project draws inspiration from [Snip](https://github.com/dlrudie/Snip) and [Tuna](https://github.com/univrsal/tuna) for now-playing metadata and source-integration patterns. The Browser Producer's site extraction behavior is independently implemented for this project's local protocol.

The front-end overlay presentation was inspired by [Zyphen's Now Playing](https://github.com/ZyphenVisuals/zyphens-now-playing).

## License

[GNU General Public License v3.0](LICENSE)
