# Local outputs

> Status: implemented and accepted on the local `protocol-v3` branch; automated verification and manual OBS Text/Image acceptance are complete.

The Host can write the committed now-playing state to local files without requiring the overlay page. Open **Settings... > Outputs** to configure each output. Outputs are disabled by default and require absolute file paths.

## Text output

One UTF-8 `.txt` file can be enabled directly. Its path is initially filled with `%LOCALAPPDATA%\NowPlayingOverlay\NowPlaying.txt`; edit the text format, select the no-media behavior, and turn on **Write to TXT**. The default format is `{nowPlaying}`; use `{title}` for title-only output. There is no add/remove step or named output list.

JSON, artwork, and History are initially filled with `NowPlaying.json`, `Artwork.png`, and `History.txt` in the same directory. All four outputs remain off until their individual checkboxes are enabled and the settings are saved. Existing custom paths are not replaced.

Supported tokens are:

```text
nowPlaying title artist albumTitle albumArtist subtitle
trackNumber albumTrackCount genres playback source
position duration observedAt newline
```

`nowPlaying` renders `Artist - Title` when artist is present and only `Title` otherwise. `position` and `duration` use `M:SS` or `H:MM:SS`; `observedAt` is UTC ISO 8601. Missing optional fields render as empty text.

Window Title uses this same output path. Its explicit parser fills `title` and `artist`, after which `{title}`, `{artist}`, `{nowPlaying}`, JSON, and History work without a source-specific writer or second TXT setting.

Tokens can use these modifiers in order:

```text
{title|upper}
{artist|lower}
{title|truncate:40}
{artist|upper|truncate:20}
```

`truncate` counts Unicode scalar values, so it does not split a surrogate pair. Use `{{` and `}}` for literal braces. Unknown tokens, malformed modifiers, and unclosed braces are rejected when settings are saved. Templates do not execute conditions, loops, scripts, or commands.

When no track is available, each text output can clear its file, write a placeholder template, or keep the last value.

## JSON output

One optional `.json` file contains the exact Local Protocol v3 `NowPlayingStateDto`, including explicit idle and unavailable states. Compact and indented formatting are available. This is a file representation of the existing protocol, not a second schema.

## Artwork output

One optional stable `.png` file contains the current committed artwork. PNG input is copied, while accepted JPEG and WebP input is decoded and encoded as a real PNG through the Windows imaging APIs available from Windows 10 version 1809. The Host reads the exact `artworkId` from its existing artwork cache and does not fetch a URL again.

When the current state has no artwork, the file can be deleted or kept. Historical artwork archiving is not part of this output.

## History

One optional UTF-8 `.txt` file appends a custom one-line record when the committed `TrackIdentity` changes. Pause/resume, timeline correction, artwork changes, and equivalent metadata do not add duplicates. Idle, stopped, and unavailable states do not add a line or reset the last identity during the same Host run.

History uses a bounded ordered commit channel rather than the latest-state subscription. Queue overflow faults History explicitly instead of silently dropping a track. The first implementation does not rotate, truncate, or rewrite the history file automatically.

## File and failure behavior

- Current TXT, JSON, and artwork files use a same-directory temporary file followed by atomic replacement. Unchanged content is not rewritten.
- History uses ordered append rather than whole-file replacement.
- A locked, read-only, unavailable, or invalid target faults only that output. Sources, the local HTTP server, SSE, the overlay, and other outputs keep running.
- The Settings page shows a fault summary; detailed diagnostics are written without track text, rendered templates, or target paths.
- Settings changes rebuild enabled current-state outputs immediately. They do not backfill History.

## Manual OBS acceptance record

The accepted manual check covers both OBS consumers against a normal release build:

1. Enable a text output and add an OBS **Text (GDI+)** source with **Read from file** pointing to the `.txt` target.
2. Enable artwork and add an OBS **Image** source pointing to the stable `.png` target.
3. Switch rapidly between tracks containing ASCII, CJK, and emoji text; confirm the text source never displays a partially written file.
4. Exercise PNG, JPEG, and WebP source artwork; confirm OBS refreshes the stable PNG path.
5. Lock or make one target read-only; confirm its error is isolated and other targets keep updating.
6. Restart the Host and OBS; confirm current outputs rebuild and History begins a new observation session without rewriting old lines.

The automated suite covers the corresponding template, UTF-8, atomic replacement, lock isolation, protocol JSON, PNG conversion, ordered history, settings migration, and lifecycle contracts. Real OBS Text and Image sources were verified separately because rendering cannot be proven by the Host test suite.
