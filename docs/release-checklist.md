# Release checklist

This checklist freezes the initial Windows release contract for Now Playing Overlay.

## Release identity

- Version: `0.1.0`
- Git tag: `v0.1.0`
- GitHub release title: `Now Playing Overlay v0.1.0`
- GitHub release status: pre-release
- Platform: Windows x64
- Runtime model: framework-dependent single-file `WinExe`
- Compatibility baseline: Windows 10 1809 and OBS Studio 31.0.1 with CEF 127

The executable keeps the stable runtime name `NowPlayingOverlay.exe`. The uploaded assets are:

```text
NowPlayingOverlay-v0.1.0-win-x64.zip
NowPlayingOverlay-v0.1.0-win-x64.zip.sha256
```

The ZIP root must contain exactly:

```text
NowPlayingOverlay.exe
README.md
LICENSE
```

No runtime, HTML, image, configuration, debug-symbol, probe, or log file belongs in the user package.

## Scope and known limitations

Release notes must state all of the following:

- Spotify is the only supported player. The official Win32 and Microsoft Store applications are supported.
- Metadata and playback state come from Windows GSMTC; Spotify Web API and OAuth are not included.
- GSMTC can expose only the first credited artist for some tracks.
- Windows x64 is the only published architecture.
- Users must install the x64 .NET 10 Desktop Runtime and ASP.NET Core Runtime.
- There is no installer, automatic updater, or code signature. Windows SmartScreen may warn.
- OBS Studio 31.0.1 with CEF 127 is the validated Browser Source baseline; earlier versions are unsupported and other versions have not completed the same matrix.
- A Browser Source that never loaded while the host was running cannot reconnect by itself; it must be refreshed after the host starts.
- Real Spotify samples for missing artwork and Thumbnail failure were unavailable; automated tests cover those fallbacks.
- Network-loss behavior was not part of the completed manual matrix because it could not be isolated from Spotify streaming loss.
- C#/WinRT Embedded projection support is still treated as a release risk; do not silently restore the full SDK projection if a compatibility failure appears.

## Pre-release gates

1. Confirm the intended release commit and a clean working tree.
2. Confirm `README.md`, `LICENSE`, and this checklist are present. Do not modify the license text during packaging.
3. Confirm the host and web versions are both `0.1.0`.
4. Run `scripts/publish.ps1` without skipping its internal checks.
5. Confirm npm audit reports no vulnerabilities; frontend tests are 29/29; Host tests are 164/164; Probe tests are 11/11; and .NET reports no warnings or errors.
6. Confirm `web/dist` contains only `NowPlaying.html`.
7. Confirm `artifacts/publish/win-x64` contains only `NowPlayingOverlay.exe`.
8. Confirm the EXE is x64 PE32+, uses Windows GUI subsystem 2, carries product/file version `0.1.0`, and contains the approved application icon and embedded page.
9. Confirm `artifacts/release` contains only the versioned ZIP and checksum file, and that the ZIP contains exactly the three frozen root entries.
10. Recalculate the ZIP SHA-256 and compare it with the `.sha256` file.
11. Launch the published EXE and verify health, overlay page, protocol v1 state, tray startup, and clean exit.
12. Repeat the OBS smoke for the 350 x 70 layout, playback visibility, one track change, artwork, marquee, and host restart recovery.
13. Run `git diff --check` and confirm no unreviewed working-tree changes remain before tagging.

## Publish and post-publish gates

1. Create annotated tag `v0.1.0` only from the reviewed release commit.
2. Create GitHub pre-release `Now Playing Overlay v0.1.0` and upload only the ZIP and `.sha256` assets.
3. Copy the frozen limitations into the release notes and link to the repository source and README.
4. Download both uploaded assets from GitHub and verify the checksum again; do not rely only on local artifacts.
5. Extract the downloaded ZIP on a clean x64 Windows environment with the required runtimes and repeat startup, tray, OBS URL, and exit smoke checks.
6. Record the release commit, tag, uploaded asset sizes, SHA-256, Windows version, OBS/CEF version, and verification result.

Tagging, pushing, and creating the GitHub release require separate maintainer authorization. Running this checklist does not authorize those external actions.
