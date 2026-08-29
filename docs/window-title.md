# Window Title source

Window Title is a fallback source for desktop players that expose the current item in a normal top-level window caption but do not publish a Windows media session.

## Setup

1. Open **Settings...** and choose **Window Title**.
2. Select **Refresh**, then choose the application window.
3. Choose **Use whole title** or **Split title**.
4. Check the Title and Artist preview, then select **Save**.

The window list shows the executable name and current caption. The saved target uses the executable identity and window class so the Host can find the same kind of window after the process restarts. Process IDs and window handles are never persisted.

If more than one visible top-level window has the same saved identity, the source reports an ambiguous target and publishes no track. It never selects the first matching window silently.

## Parsing

**Use whole title** publishes the complete normalized caption as `title` and leaves `artist` empty.

**Split title** requires three explicit choices:

- the exact literal separator;
- its first or last occurrence;
- whether the left side is Title or Artist.

The right side becomes the other field. Surrounding whitespace is removed after splitting. The Host does not guess a universal `Artist - Title` order and does not run a regular expression, replacement chain, or script.

If the separator is missing or either side is empty, the source publishes Idle with no track. Switch to **Use whole title** when the complete caption is the desired output.

## Runtime behavior

The Windows implementation follows the same basic discovery direction as Tuna's Window Title source: enumerate visible top-level windows, associate each caption with its owning process, and monitor the selected target. This project keeps only one explicit target and a small literal parser rather than copying Tuna's regex, replace, cut, and pause options.

The selected target is checked once per second. A change signal is emitted only when the effective title, artist, availability, or ambiguity changes.

State mapping is:

| Window state | Published state |
| --- | --- |
| Target missing | Unavailable, no track |
| Multiple matching windows | Unavailable, no track |
| Empty caption or failed explicit split | Idle, no track |
| Usable whole or split caption | Playing with title and artist |

A window caption does not prove that audio is playing. Window Title therefore treats a usable caption as playing so the overlay and current outputs can display it. It does not publish Paused, Stopped, Timeline, or Artwork.

## Outputs

Window Title submits the same committed snapshot as every other source. Configure files under **Settings... > Outputs**:

- `{title}` writes the parsed title;
- `{artist}` writes the parsed artist;
- `{nowPlaying}` writes `Artist - Title` when artist exists, otherwise only Title;
- JSON and History include the same committed metadata and the `window-title` provider token.

There is no Window Title-specific TXT writer. Outputs remain disabled until individually enabled and saved.

## Scope and limitations

- Discovery covers visible top-level desktop application windows. Child controls are not read.
- Some packaged applications, helper-hosted windows, elevated applications, and custom-rendered interfaces may not expose a usable caption or stable executable identity.
- Opening two equivalent player windows can make the target ambiguous until one is closed.
- Title text is displayed in Settings and committed as metadata, but is not written to diagnostics.
