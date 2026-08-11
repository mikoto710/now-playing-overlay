using Windows.Media;
using Windows.Media.Control;
using NowPlayingOverlay.Host.Media;
using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.Media.Windows;

internal sealed class WindowsMediaSessionAdapter : IMediaSessionAdapter
{
    private readonly GlobalSystemMediaTransportControlsSession _session;
    private bool _disposed;

    public WindowsMediaSessionAdapter(GlobalSystemMediaTransportControlsSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        SourceAppUserModelId = session.SourceAppUserModelId;
        _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
        _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
    }

    public event EventHandler? Changed;

    public string SourceAppUserModelId { get; }

    public MediaSessionPlaybackStatus GetPlaybackStatus()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return MapPlaybackStatus(_session.GetPlaybackInfo().PlaybackStatus);
    }

    public async ValueTask<SessionObservation> ReadAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var playbackStatus = GetPlaybackStatus();
        var media = await _session.TryGetMediaPropertiesAsync();
        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = new WindowsMediaSessionSnapshot
        {
            SourceAppUserModelId = SourceAppUserModelId,
            PlaybackStatus = playbackStatus,
            Title = media.Title,
            Artist = media.Artist,
            AlbumTitle = media.AlbumTitle,
            AlbumArtist = media.AlbumArtist,
            Subtitle = media.Subtitle,
            TrackNumber = media.TrackNumber,
            AlbumTrackCount = media.AlbumTrackCount,
            PlaybackType = MapPlaybackType(media.PlaybackType),
            Genres = media.Genres.Cast<string?>().ToArray(),
            ArtworkReader = media.Thumbnail is null ? null : new WindowsArtworkReader(media.Thumbnail),
        };
        return MapSnapshot(snapshot);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
        _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
    }

    internal static SessionObservation MapSnapshot(WindowsMediaSessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var playback = MapPlaybackState(snapshot.PlaybackStatus);
        if (playback == PlaybackState.Idle)
        {
            return SessionObservation.Create(
                snapshot.SourceAppUserModelId,
                PlaybackState.Idle);
        }

        TrackMetadata? track = null;
        if (MediaTextNormalizer.Normalize(snapshot.Title).Length > 0)
        {
            track = TrackMetadata.Create(
                snapshot.Title,
                snapshot.Artist,
                snapshot.AlbumTitle,
                snapshot.AlbumArtist,
                snapshot.Subtitle,
                ToPositiveUInt32(snapshot.TrackNumber),
                ToPositiveUInt32(snapshot.AlbumTrackCount),
                snapshot.PlaybackType,
                snapshot.Genres);
        }

        if (playback == PlaybackState.Playing && track is null)
        {
            return SessionObservation.Create(
                snapshot.SourceAppUserModelId,
                PlaybackState.Idle);
        }

        return SessionObservation.Create(
            snapshot.SourceAppUserModelId,
            playback,
            track,
            track is null ? null : snapshot.ArtworkReader);
    }

    internal static MediaSessionPlaybackStatus MapPlaybackStatus(
        GlobalSystemMediaTransportControlsSessionPlaybackStatus playbackStatus)
    {
        return playbackStatus switch
        {
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed =>
                MediaSessionPlaybackStatus.Closed,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Opened =>
                MediaSessionPlaybackStatus.Opened,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing =>
                MediaSessionPlaybackStatus.Changing,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped =>
                MediaSessionPlaybackStatus.Stopped,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing =>
                MediaSessionPlaybackStatus.Playing,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused =>
                MediaSessionPlaybackStatus.Paused,
            _ => throw new ArgumentOutOfRangeException(
                nameof(playbackStatus),
                playbackStatus,
                "Windows media playback status is invalid."),
        };
    }

    internal static MediaPlaybackKind? MapPlaybackType(MediaPlaybackType? playbackType)
    {
        return playbackType switch
        {
            null => null,
            MediaPlaybackType.Unknown => MediaPlaybackKind.Unknown,
            MediaPlaybackType.Music => MediaPlaybackKind.Music,
            MediaPlaybackType.Video => MediaPlaybackKind.Video,
            MediaPlaybackType.Image => MediaPlaybackKind.Image,
            _ => throw new ArgumentOutOfRangeException(
                nameof(playbackType),
                playbackType,
                "Windows media playback type is invalid."),
        };
    }

    private static PlaybackState MapPlaybackState(MediaSessionPlaybackStatus playbackStatus)
    {
        return playbackStatus switch
        {
            MediaSessionPlaybackStatus.Playing => PlaybackState.Playing,
            MediaSessionPlaybackStatus.Paused => PlaybackState.Paused,
            MediaSessionPlaybackStatus.Stopped => PlaybackState.Stopped,
            MediaSessionPlaybackStatus.Closed or
            MediaSessionPlaybackStatus.Opened or
            MediaSessionPlaybackStatus.Changing => PlaybackState.Idle,
            _ => throw new ArgumentOutOfRangeException(
                nameof(playbackStatus),
                playbackStatus,
                "Media session playback status is invalid."),
        };
    }

    private static uint? ToPositiveUInt32(int value)
    {
        return value > 0 ? checked((uint)value) : null;
    }

    private void OnMediaPropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        MediaPropertiesChangedEventArgs args)
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnPlaybackInfoChanged(
        GlobalSystemMediaTransportControlsSession sender,
        PlaybackInfoChangedEventArgs args)
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
