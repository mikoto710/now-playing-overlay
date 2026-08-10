namespace NowPlayingOverlay.Host.Media;

internal sealed record FakeSessionStep
{
    public FakeSessionStep(TimeSpan delay, SessionObservation observation)
    {
        if (delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay));
        }

        ArgumentNullException.ThrowIfNull(observation);
        Delay = delay;
        Observation = observation;
    }

    public TimeSpan Delay { get; }

    public SessionObservation Observation { get; }
}
