# Runtime packaging design

This document explains the selected Windows runtime packaging model and records the measurements used to evaluate the available .NET deployment modes. The goal is a small, predictable release that preserves the tray, media-session, artwork, and loopback HTTP behavior.

## Selected baseline

The release remains a Windows x64 framework-dependent single-file `WinExe`. Users install only the x64 .NET 10 Desktop Runtime. `scripts/publish.ps1` now rejects a diagnostic publish unless its framework closure is exactly:

- `Microsoft.NETCore.App`
- `Microsoft.WindowsDesktop.App`

The gate rejects any additional shared framework or projection library that the application does not need. Application-owned logging abstractions and the embedded WinRT projection remain bundled with the executable.

## 2026-08-11 comparison

Environment: Windows x64, .NET SDK 10.0.302, Release `win-x64`, single-file output, embedded web asset, and C#/WinRT Embedded projection. Startup is the elapsed time from process creation until `GET /health` first returned 200 on an isolated loopback port. Each runnable candidate was launched three times in sequence; the first launch and median are shown separately because self-contained single-file extraction materially affected the first sample.

| Candidate | Build | EXE bytes | First health | Median health | Median working set | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| FDD single-file | 3.48 s | 1,523,080 | 359.1 ms | 308.8 ms | 67,121,152 bytes | Selected |
| SCD single-file | 4.38 s | 117,411,362 | 1,055.9 ms | 311.5 ms | 68,739,072 bytes | Valid candidate, not selected |
| SCD + ReadyToRun | 26.94 s | 132,279,381 | 1,130.2 ms | 324.1 ms | 72,282,112 bytes | Rejected for default publishing |
| SCD + trimming | Failed | n/a | n/a | n/a | n/a | Rejected by SDK |
| Native AOT | Failed | n/a | n/a | n/a | n/a | Rejected by SDK |

The SCD executable was about 77 times the FDD size and did not improve the warmed median in this sample. ReadyToRun increased the SCD executable by another 14,868,019 bytes, increased build time, and did not produce a measured startup benefit. It is therefore not justified for this small resident tray application.

Both trimming and Native AOT stopped at `NETSDK1175`: the installed .NET 10 SDK does not support or recommend trimming Windows Forms applications. Native AOT depends on trimming, so it is not a viable current target for this WinForms host. The project does not suppress this diagnostic.

These timings are local comparative samples rather than universal performance claims. They do not replace validation on a clean Windows environment or the supported Browser Source workflow. Reconsidering the default requires a new isolated experiment, zero unexplained warnings, the full automated suite, and regression evidence for the tray, media session, artwork, HTTP/SSE, and overlay behavior.

## Verification evidence

The selected baseline passed the complete release chain:

- frontend type, format, lint, 30/30 tests, and production build;
- Host 172/172 tests;
- session probe 11/11 tests;
- .NET build with zero warnings and zero errors;
- Desktop Runtime-only dependency closure;
- one-file publish and versioned ZIP generation with a logged local SHA-256.

The generated baseline executable was 1,523,080 bytes. Validation and publish intermediates use unique system temporary directories so IDE-owned `bin`/`obj` locks cannot invalidate the release gate; cleanup remains constrained to those generated temporary roots.
