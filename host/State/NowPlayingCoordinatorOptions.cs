namespace NowPlayingOverlay.Host.State;

internal sealed record NowPlayingCoordinatorOptions
{
    public TimeSpan DebounceDelay { get; init; } = TimeSpan.FromMilliseconds(100);

    public void Validate()
    {
        if (DebounceDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(DebounceDelay));
        }
    }
}
