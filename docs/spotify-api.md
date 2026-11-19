# Spotify API polling and diagnostics

The Spotify source calls `GET /v1/me/player/currently-playing`. After each request
finishes, it waits **3 seconds while playing** or **5 seconds while paused, idle
(including HTTP 204), or showing unsupported content**. With negligible response
time, normal polling makes about 1,200 queries per hour while playing or 720 while idle. A change
just after a poll is picked up by the next poll, plus network and overlay rendering
time. Switching sources or exiting cancels the pending read or wait.

Playback and OAuth token responses each have **5 seconds from receipt of the
headers** to finish reading the entire body, even when bytes continue to arrive.
The size limits are 1 MiB for playback and 64 KiB for tokens. The client checks
the declared length and enforces the limit while reading streams of unknown length.
When playback or a background token refresh times out or loses its connection,
the source retries after 5, 10, 20, 40, then 60 seconds. A successful poll restores
the normal interval. On a failed poll, the source can still show the last result
as stale if it is at most 10 seconds old. An interactive authorization code
exchange sends once and returns any failure to the connection dialog.

## HTTP 429

The playback and OAuth token clients share a request policy. Admission and
cooldown counters are independent for each Client ID and endpoint. A token
cooldown permits playback queries while a cached access token is still usable.
Calls to the same endpoint are serialized and recheck admission before sending.
If a playback request is processing a 429, a second request waits for the cooldown
decision before acquiring or refreshing a token.

| Response classification | Local retry intervals |
| --- | --- |
| `error.reason = QUOTA_EXCEEDED` | 15, 30, then 60 minutes for each further 429 |
| Other, missing, or unreadable reason (`rate_or_unknown`) | 30, 60, 120, 240, then 300 seconds for each further 429 |

Each 429 gets its own classification. An explicit `QUOTA_EXCEEDED` confirms quota
exhaustion. For every other 429 the precise cause remains unknown. The backoff
step counts consecutive 429 responses for that endpoint across classifications.

The delay is the greater of a valid `Retry-After` and the local interval, plus
0–10% of the local interval as positive jitter. `Retry-After` accepts seconds or an
HTTP date, and can exceed the local cap. The app uses the time remaining until
that date when it schedules the next request. Invalid or past headers retain the
local minimum. These intervals are the overlay's retry defaults. Spotify measures the
Web API call rate over a rolling 30 second window. [Spotify rate limits](https://developer.spotify.com/documentation/web-api/concepts/rate-limits).

429 bodies have a separate 16 KiB limit and a 5 second timeout. If the body is
empty, malformed, oversized, interrupted, or cancelled after the headers arrive,
the client still records the 429 and honors any valid `Retry-After`. A subsequent
HTTP 2xx response clears the corresponding endpoint's cooldown and failure count.

## Persistence

`%LOCALAPPDATA%\NowPlayingOverlay\spotify-cooldowns.json` stores a SHA-256 hash of
the Client ID, endpoint, classification, failure count, first failure time, and UTC
retry deadline. Updates flush a sibling temporary file before atomically replacing
the target. Source selection and reauthorization preserve this state.

While the app runs, a monotonic clock measures the wait. At startup the app
compares the saved UTC deadline with the current UTC time. The first failure
timestamp serves diagnostics and may fall after the retry deadline if the clock
was set back between two 429 responses. The app checks admission before each
send, including after a long wait. If the file cannot be read or written, the
app logs a warning. A write failure keeps the cooldown in memory for this run.

All Development Mode apps under one developer account share Spotify's quota.
The file records only the 429 responses observed by this overlay.
[Spotify Development Mode quota updates](https://developer.spotify.com/blog/2026-07-23-web-api-quota-updates).

## Logs and tray status

Open `%LOCALAPPDATA%\NowPlayingOverlay\logs\NowPlayingOverlay.log` and search for
`Spotify HTTP 429`. Each entry gives the endpoint, classification, normalized reason,
server delay, chosen delay, next attempt UTC, consecutive count, and body parse
status. Restored cooldowns and recovery write Information entries, including the
polling interval when polling resumes. Every 15 minutes, `Spotify request summary`
reports actual HTTP sends, successes, and 429 responses for each endpoint that has
sent a request, including zero counts for an idle window. `Successes` and
`Responses429` count the corresponding headers in the window when they arrive,
even if the body later fails. A 401 retry and a token refresh each count
as a send. Local cooldown checks do not add to the count. `PollingState` and
`IntervalSeconds` give the current polling state and wait.

Diagnostic fields use fixed labels and numeric values. Raw response bodies,
authorization headers, credentials, Client IDs, and track metadata are excluded.
The tray displays `Quota Exceeded` for confirmed quota exhaustion and `Rate Limited`
for other 429 responses.

## Verification

Tests use HTTP handlers, controllable delays, a monotonic fake clock, and real
temporary files. Reconstructing both the policy and source checks restart behavior.

```powershell
dotnet test host.tests/NowPlayingOverlay.Host.Tests.csproj --filter 'FullyQualifiedName~Spotify'
```
