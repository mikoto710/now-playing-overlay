# Now Playing Overlay

Now Playing Overlay is a local Windows host for an OBS Browser Source. The first release will read Spotify metadata from Windows media sessions and publish the overlay from a framework-dependent, single-file Windows application with a minimal tray interface.

The repository currently contains the initial development scaffold. The media-session probe, runtime protocol, migrated overlay, and release shell are implemented in later milestones.

## Development prerequisites

- Windows 10 version 1809 or later
- .NET 10 SDK
- Node.js 22 and npm

## Validate the repository

Run `./scripts/check.ps1` from PowerShell. It restores dependencies, builds and tests the .NET solution, then checks, tests, and builds the inlined frontend.

## Publish the Windows application

Run `./scripts/publish.ps1` from PowerShell. The script clears the fixed release directory, runs the full validation chain, and publishes the framework-dependent Windows x64 application to `artifacts/publish/win-x64/NowPlayingOverlay.exe`. It fails unless that directory contains only the expected executable.

The published application requires the x64 .NET 10 Desktop Runtime and ASP.NET Core Runtime. The .NET runtime files are not bundled into the executable.

Console projects in this repository are development tools and are not target user artifacts.
