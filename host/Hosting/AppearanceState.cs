using NowPlayingOverlay.Host.Configuration;

namespace NowPlayingOverlay.Host.Hosting;

/// <summary>
/// Thread-safe authority for the effective appearance served by HTTP.
/// </summary>
internal sealed class AppearanceState
{
    private readonly object _gate = new();
    private EffectiveAppearanceSettings _current;

    public AppearanceState(AppearanceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _current = settings.ToEffective();
    }

    public EffectiveAppearanceSettings GetCurrent()
    {
        lock (_gate)
        {
            return _current;
        }
    }

    public void Set(AppearanceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var effective = settings.ToEffective();
        lock (_gate)
        {
            _current = effective;
        }
    }
}
