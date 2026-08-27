namespace NowPlayingOverlay.Host.Outputs;

internal sealed record OutputStatusSnapshot(
    int FaultedCount,
    string Summary)
{
    public bool IsFaulted => FaultedCount > 0;
}

internal sealed record OutputTargetStatus(
    bool IsFaulted,
    string Message,
    DateTimeOffset UpdatedAt);
