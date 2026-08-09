# Now Playing Overlay

Now Playing Overlay is a local Windows host for an OBS Browser Source. The first release will read Spotify metadata from Windows media sessions and publish the overlay from a self-contained, single-file Windows application with a minimal tray interface.

The repository currently contains the initial development scaffold. The media-session probe, runtime protocol, migrated overlay, and release shell are implemented in later milestones.

## Development prerequisites

- Windows 10 version 1809 or later
- .NET 10 SDK
- Node.js 22 and npm

## Validate the scaffold

Run `./scripts/check.ps1` from PowerShell. It restores dependencies, builds and tests the .NET solution, then checks, tests, and builds the inlined frontend.

Publishing is intentionally unavailable until the release milestone. Console projects in this repository are development tools and are not target user artifacts.
