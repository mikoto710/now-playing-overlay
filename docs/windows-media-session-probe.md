# Windows Media Session Probe

The session probe is a console-only developer tool that enumerates every Windows system media
session and records session lifecycle, playback, media-property, timeline, and thumbnail
observations as JSON lines. It never uses `GetCurrentSession()` and is not part of the released
overlay application.

```powershell
dotnet run --project tools/session-probe -- --duration 30
dotnet run --project tools/session-probe -- --duration 30 --output probe-output/run.jsonl
```

`--exercise-source <exact SourceAppUserModelId>` optionally performs pause, play, stop, and three
rapid next-track requests. It refuses to run unless the exact ID resolves to one session. This
changes playback and is intended only for an explicit local test run.

Console and output-file records include media text and can reveal listening activity. Keep raw
evidence local. `probe-output/` and the local `PROBE_RESULTS.md` are ignored by Git.
