using System.Windows.Forms;
using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Media.Spotify.Authorization;
using NowPlayingOverlay.Host.Media.WindowTitles;

namespace NowPlayingOverlay.Host.Shell;

internal sealed record SourceSettingsActions(
    Func<SourceProvider, CancellationToken, Task<SourceDiscoveryResult>> RefreshSources,
    Func<string, bool, CancellationToken, Task<SpotifyConnectionSnapshot>> AuthorizeSpotify,
    Func<CancellationToken, Task<SpotifyConnectionSnapshot>> DisconnectSpotify,
    Func<string> GetBrowserPlayerConnectionCode,
    Func<string> RotateBrowserPlayerConnectionCode,
    Action OpenBrowserProducer,
    Action<string> SetClipboardText,
    Func<CancellationToken, Task<WindowTitleDiscoveryResult>> RefreshWindowTitles);

internal sealed class SourceSettingsControl : UserControl
{
    private readonly SourceSettingsActions _actions;
    private readonly int _effectivePort;
    private readonly SourceSelectionSettings _currentSource;
    private readonly NumericUpDown _port;
    private readonly ComboBox _provider;
    private readonly ComboBox _source;
    private readonly Label _sourceStatus;
    private readonly Button _refresh;
    private readonly GroupBox _windowsSourceGroup;
    private readonly GroupBox _spotifySourceGroup;
    private readonly GroupBox _externalSourceGroup;
    private readonly GroupBox _windowTitleSourceGroup;
    private readonly WindowTitleSettingsControl _windowTitleSettings;
    private readonly Label _spotifyClientId;
    private readonly Label _spotifyStatus;
    private readonly Button _spotifyConnection;
    private readonly CancellationTokenSource _shutdown = new();
    private bool _disposeStarted;
    private bool _busy;
    private string? _selectedWindowsMediaInstanceId;
    private bool _hasPendingSourceSelection;
    private SpotifyConnectionSnapshot _spotifyConnectionState;
    private SourceDiscoveryResult _windowsDiscovery;

    public SourceSettingsControl(
        int currentPort,
        SourceDiscoveryResult discovery,
        SourceSelectionSettings currentSource,
        WindowsMediaSettings windowsMedia,
        SpotifyConnectionSnapshot spotifyConnection,
        WindowTitleSettings currentWindowTitle,
        WindowTitleDiscoveryResult windowTitleDiscovery,
        SourceSettingsActions actions)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(currentSource);
        currentSource.Validate();
        ArgumentNullException.ThrowIfNull(windowsMedia);
        windowsMedia.Validate();
        ArgumentNullException.ThrowIfNull(spotifyConnection);
        ArgumentNullException.ThrowIfNull(currentWindowTitle);
        currentWindowTitle.Validate();
        ArgumentNullException.ThrowIfNull(windowTitleDiscovery);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.RefreshSources);
        ArgumentNullException.ThrowIfNull(actions.AuthorizeSpotify);
        ArgumentNullException.ThrowIfNull(actions.DisconnectSpotify);
        ArgumentNullException.ThrowIfNull(actions.GetBrowserPlayerConnectionCode);
        ArgumentNullException.ThrowIfNull(actions.RotateBrowserPlayerConnectionCode);
        ArgumentNullException.ThrowIfNull(actions.OpenBrowserProducer);
        ArgumentNullException.ThrowIfNull(actions.SetClipboardText);
        ArgumentNullException.ThrowIfNull(actions.RefreshWindowTitles);

        _actions = actions;
        _effectivePort = currentPort;
        _currentSource = currentSource;
        _windowsDiscovery = discovery;
        _spotifyConnectionState = spotifyConnection;
        _selectedWindowsMediaInstanceId = windowsMedia.LastInstanceId;
        _hasPendingSourceSelection = true;
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;

        _windowTitleSettings = new WindowTitleSettingsControl(
            currentWindowTitle,
            windowTitleDiscovery);
        _windowTitleSettings.RefreshRequested += WindowTitleRefreshRequested;

        var layout = new TableLayoutPanel
        {
            ColumnCount = 3,
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            RowCount = 9,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 10));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 10));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var explanation = new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12),
            MaximumSize = new Size(680, 0),
            Text = "Choose the loopback port and one complete media source. Spotify and Browser Player connection changes take effect immediately; Save applies the selected provider.",
        };
        var portLabel = CreateLabel("Port:");
        _port = new NumericUpDown
        {
            Anchor = AnchorStyles.Left,
            Margin = Padding.Empty,
            Minimum = 1,
            MinimumSize = new Size(160, 0),
            Maximum = 65535,
            Value = currentPort,
        };
        var sourceLabel = CreateLabel("Player:");
        _provider = new ComboBox
        {
            Anchor = AnchorStyles.Left,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FormattingEnabled = true,
            Margin = Padding.Empty,
            MinimumSize = new Size(220, 0),
        };
        _provider.Items.AddRange(Enum.GetValues<SourceProvider>().Cast<object>().ToArray());
        _provider.Format += (_, args) =>
        {
            if (args.ListItem is SourceProvider provider)
            {
                args.Value = provider.ToDisplayName();
            }
        };
        _provider.SelectedItem = currentSource.Provider;
        _provider.SelectedIndexChanged += (_, _) => UpdateProviderPanels();
        _source = new ComboBox
        {
            Anchor = AnchorStyles.Left,
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FormattingEnabled = true,
            Margin = Padding.Empty,
            MinimumSize = new Size(360, 0),
        };
        _source.Format += (_, args) =>
        {
            if (args.ListItem is SourceOption option)
            {
                args.Value = option.Label;
            }
        };
        _source.SelectedIndexChanged += (_, _) =>
        {
            _selectedWindowsMediaInstanceId = SelectedWindowsMediaInstanceId;
            _hasPendingSourceSelection = true;
            UpdateWindowsSelectionStatus();
        };
        _refresh = new Button
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(8, 0, 0, 0),
            Padding = new Padding(8, 2, 8, 2),
            Text = "Refresh",
        };
        _refresh.Click += RefreshClicked;
        _sourceStatus = new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0),
            MaximumSize = new Size(680, 0),
        };

        var windowsLayout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            Padding = new Padding(8),
            RowCount = 2,
        };
        windowsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        windowsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        windowsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        windowsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        windowsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        windowsLayout.Controls.Add(sourceLabel, 0, 0);
        windowsLayout.Controls.Add(_source, 1, 0);
        windowsLayout.Controls.Add(_refresh, 2, 0);
        windowsLayout.Controls.Add(_sourceStatus, 0, 1);
        windowsLayout.SetColumnSpan(_sourceStatus, 3);
        _windowsSourceGroup = new GroupBox
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            Text = "Windows Media",
        };
        _windowsSourceGroup.Controls.Add(windowsLayout);

        _spotifyClientId = new Label
        {
            AutoSize = true,
            Margin = Padding.Empty,
        };
        _spotifyStatus = new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0),
            MaximumSize = new Size(520, 0),
        };
        _spotifyConnection = new Button
        {
            Anchor = AnchorStyles.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(12, 0, 0, 0),
            Padding = new Padding(8, 2, 8, 2),
            Text = "Spotify Connection...",
        };
        _spotifyConnection.Click += SpotifyConnectionClicked;
        var spotifyLayout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            Padding = new Padding(8),
            RowCount = 2,
        };
        spotifyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        spotifyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        spotifyLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        spotifyLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        spotifyLayout.Controls.Add(_spotifyClientId, 0, 0);
        spotifyLayout.SetColumnSpan(_spotifyClientId, 2);
        spotifyLayout.Controls.Add(_spotifyStatus, 0, 1);
        spotifyLayout.Controls.Add(_spotifyConnection, 1, 1);
        _spotifySourceGroup = new GroupBox
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            Text = "Spotify API",
        };
        _spotifySourceGroup.Controls.Add(spotifyLayout);

        var externalExplanation = new Label
        {
            AutoSize = true,
            Margin = Padding.Empty,
            MaximumSize = new Size(680, 0),
            Text = "Install the Tampermonkey script, then copy the connection code.",
        };
        var installBrowserProducer = new Button
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
            Padding = new Padding(8, 2, 8, 2),
            Text = "Install Browser Producer...",
        };
        installBrowserProducer.Click += (_, _) => RunBrowserPlayerAction(
            _actions.OpenBrowserProducer);
        var copyConnectionCode = new Button
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(8, 0, 0, 0),
            Padding = new Padding(8, 2, 8, 2),
            Text = "Copy Connection Code",
        };
        copyConnectionCode.Click += (_, _) => CopyBrowserPlayerConnectionCode(rotate: false);
        var rotateConnectionCode = new Button
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 8, 0, 0),
            Padding = new Padding(8, 2, 8, 2),
            Text = "Rotate Code...",
        };
        rotateConnectionCode.Click += (_, _) => CopyBrowserPlayerConnectionCode(rotate: true);
        var externalButtons = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 10, 0, 0),
            RowCount = 2,
        };
        externalButtons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        externalButtons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        externalButtons.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        externalButtons.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        externalButtons.Controls.Add(installBrowserProducer, 0, 0);
        externalButtons.Controls.Add(copyConnectionCode, 1, 0);
        externalButtons.Controls.Add(rotateConnectionCode, 0, 1);
        var externalLayout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            Padding = new Padding(8),
            RowCount = 2,
        };
        externalLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        externalLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        externalLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        externalLayout.Controls.Add(externalExplanation, 0, 0);
        externalLayout.Controls.Add(externalButtons, 0, 1);
        _externalSourceGroup = new GroupBox
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            Text = "Browser Player",
        };
        _externalSourceGroup.Controls.Add(externalLayout);

        _windowTitleSourceGroup = new GroupBox
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            Text = "Window Title",
        };
        _windowTitleSourceGroup.Controls.Add(_windowTitleSettings);

        layout.Controls.Add(explanation, 0, 0);
        layout.SetColumnSpan(explanation, 3);
        layout.Controls.Add(portLabel, 0, 1);
        layout.Controls.Add(_port, 1, 1);
        layout.SetColumnSpan(_port, 2);
        layout.Controls.Add(CreateLabel("Provider:"), 0, 3);
        layout.Controls.Add(_provider, 1, 3);
        layout.SetColumnSpan(_provider, 2);
        layout.Controls.Add(_windowsSourceGroup, 0, 5);
        layout.SetColumnSpan(_windowsSourceGroup, 3);
        layout.Controls.Add(_spotifySourceGroup, 0, 6);
        layout.SetColumnSpan(_spotifySourceGroup, 3);
        layout.Controls.Add(_externalSourceGroup, 0, 7);
        layout.SetColumnSpan(_externalSourceGroup, 3);
        layout.Controls.Add(_windowTitleSourceGroup, 0, 8);
        layout.SetColumnSpan(_windowTitleSourceGroup, 3);
        Controls.Add(layout);

        ApplyDiscovery(discovery);
        UpdateSpotifyConnectionState();
        UpdateProviderPanels();
    }

    public event EventHandler? BusyChanged;

    public int SelectedPort => decimal.ToInt32(_port.Value);

    public SourceProvider SelectedProvider =>
        _provider.SelectedItem is SourceProvider provider
            ? provider
            : throw new InvalidOperationException("A media source provider must be selected.");

    public string? SelectedInstanceId => SelectedProvider switch
    {
        SourceProvider.WindowsMedia => SelectedWindowsMediaInstanceId,
        SourceProvider.SpotifyApi => SourceKey.SpotifyApi().InstanceId,
        SourceProvider.ExternalPush => SourceKey.ExternalPush().InstanceId,
        SourceProvider.WindowTitle => SelectedWindowTitle.Target?.InstanceId,
        _ => throw new InvalidOperationException("The selected source provider is not supported."),
    };

    public WindowTitleSettings SelectedWindowTitle => _windowTitleSettings.SelectedSettings;

    public bool IsBusy => _busy;

    public bool TryValidateProviderConnection(IWin32Window owner)
    {
        if (SelectedProvider == SourceProvider.SpotifyApi
            && _spotifyConnectionState.State.Status != SpotifyConnectionStatus.Connected)
        {
            MessageBox.Show(
                owner,
                "Connect Spotify before selecting Spotify API as the active provider.",
                "Spotify Connection Required",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            _spotifyConnection.Focus();
            return false;
        }

        return true;
    }

    public void ValidateSelection()
    {
        if (SelectedProvider == SourceProvider.WindowTitle && SelectedWindowTitle.Target is null)
        {
            throw new InvalidDataException("Choose a window before selecting Window Title.");
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposeStarted)
        {
            _disposeStarted = true;
            _shutdown.Cancel();
            _shutdown.Dispose();
        }

        base.Dispose(disposing);
    }

    private string? SelectedWindowsMediaInstanceId =>
        (_source.SelectedItem as SourceOption)?.InstanceId;

    private async void RefreshClicked(object? sender, EventArgs args)
    {
        _selectedWindowsMediaInstanceId = SelectedWindowsMediaInstanceId;
        _hasPendingSourceSelection = true;
        SetRefreshState(refreshing: true);
        try
        {
            var discovery = await _actions.RefreshSources(
                SourceProvider.WindowsMedia,
                _shutdown.Token);
            ApplyDiscovery(discovery);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            _sourceStatus.Text = $"Could not refresh Windows Media players: {error.Message}";
        }
        finally
        {
            if (!IsDisposed && !_shutdown.IsCancellationRequested)
            {
                SetRefreshState(refreshing: false);
            }
        }
    }

    private void SpotifyConnectionClicked(object? sender, EventArgs args)
    {
        using var dialog = new SpotifyConnectionDialog(
            _spotifyConnectionState,
            _effectivePort,
            _actions.AuthorizeSpotify,
            _actions.DisconnectSpotify,
            _actions.SetClipboardText);
        dialog.ShowDialog(this);
        _spotifyConnectionState = dialog.CurrentConnection;
        UpdateSpotifyConnectionState();
        if (dialog.ConnectionRemoved && SelectedProvider == SourceProvider.SpotifyApi)
        {
            _provider.SelectedItem = SourceProvider.WindowsMedia;
        }
    }

    private void UpdateSpotifyConnectionState()
    {
        _spotifyClientId.Text = _spotifyConnectionState.ClientId is null
            ? "Client ID: (Not configured)"
            : $"Client ID: {_spotifyConnectionState.ClientId}";
        _spotifyStatus.Text = _spotifyConnectionState.State.Status switch
        {
            SpotifyConnectionStatus.Disconnected => "Spotify is not connected.",
            SpotifyConnectionStatus.Connected => "Spotify is connected and ready to use.",
            SpotifyConnectionStatus.ClientIdMismatch =>
                "The stored credential belongs to a different Client ID. Connect again.",
            SpotifyConnectionStatus.CredentialUnavailable =>
                "The stored Spotify credential cannot be read. Disconnect or connect again.",
            _ => throw new ArgumentOutOfRangeException(nameof(_spotifyConnectionState)),
        };
    }

    private void UpdateProviderPanels()
    {
        var provider = SelectedProvider;
        _windowsSourceGroup.Visible = provider == SourceProvider.WindowsMedia;
        _spotifySourceGroup.Visible = provider == SourceProvider.SpotifyApi;
        _externalSourceGroup.Visible = provider == SourceProvider.ExternalPush;
        _windowTitleSourceGroup.Visible = provider == SourceProvider.WindowTitle;
        _refresh.Enabled = provider == SourceProvider.WindowsMedia && !_busy;
        _source.Enabled = provider == SourceProvider.WindowsMedia && !_busy;
        if (provider == SourceProvider.WindowsMedia)
        {
            UpdateWindowsSelectionStatus();
        }
    }

    private async void WindowTitleRefreshRequested(object? sender, EventArgs args)
    {
        _windowTitleSettings.SetRefreshing(refreshing: true);
        SetBusy(busy: true);
        try
        {
            var discovery = await _actions.RefreshWindowTitles(_shutdown.Token);
            _windowTitleSettings.ApplyDiscovery(discovery);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            MessageBox.Show(
                this,
                $"Could not refresh windows. {error.Message}",
                "Window Title",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            if (!IsDisposed && !_shutdown.IsCancellationRequested)
            {
                _windowTitleSettings.SetRefreshing(refreshing: false);
                SetBusy(busy: false);
            }
        }
    }

    private void CopyBrowserPlayerConnectionCode(bool rotate)
    {
        if (rotate)
        {
            var confirmation = MessageBox.Show(
                this,
                "Rotating the connection code immediately disconnects every existing browser Producer. Continue?",
                "Rotate Browser Player Connection",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (confirmation != DialogResult.Yes)
            {
                return;
            }
        }

        RunBrowserPlayerAction(() =>
        {
            var code = rotate
                ? _actions.RotateBrowserPlayerConnectionCode()
                : _actions.GetBrowserPlayerConnectionCode();
            _actions.SetClipboardText(code);
            MessageBox.Show(
                this,
                rotate
                    ? "A new connection code was copied. Reconfigure the browser Producer from its Tampermonkey menu."
                    : "The connection code was copied. Paste it into Configure Now Playing Overlay in the Tampermonkey menu.",
                "Browser Player Connection",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        });
    }

    private void RunBrowserPlayerAction(Action action)
    {
        try
        {
            action();
        }
        catch (Exception error)
        {
            MessageBox.Show(
                this,
                $"The Browser Player action could not be completed. {error.Message}",
                "Browser Player",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void ApplyDiscovery(SourceDiscoveryResult discovery)
    {
        _windowsDiscovery = discovery;
        var selected = _hasPendingSourceSelection
            ? _selectedWindowsMediaInstanceId
            : discovery.State.ActiveSource?.Key.InstanceId;
        var options = new List<SourceOption>
        {
            new(null, "(Not configured)"),
        };
        options.AddRange(discovery.Sources.Select(source =>
            new SourceOption(source.Key.InstanceId, source.Key.InstanceId)));
        if (selected is not null
            && options.All(option => !string.Equals(
                option.InstanceId,
                selected,
                StringComparison.Ordinal)))
        {
            options.Add(new SourceOption(selected, $"{selected} (currently unavailable)"));
        }

        _source.BeginUpdate();
        try
        {
            _source.Items.Clear();
            _source.Items.AddRange(options.Cast<object>().ToArray());
            _source.SelectedIndex = Math.Max(
                0,
                options.FindIndex(option => string.Equals(
                    option.InstanceId,
                    selected,
                    StringComparison.Ordinal)));
        }
        finally
        {
            _source.EndUpdate();
        }

        UpdateWindowsSelectionStatus();
    }

    private void UpdateWindowsSelectionStatus()
    {
        _sourceStatus.Text = BuildWindowsSelectionStatusText(
            SelectedWindowsMediaInstanceId,
            _currentSource,
            _windowsDiscovery);
    }

    internal static string BuildWindowsSelectionStatusText(
        string? selectedInstanceId,
        SourceSelectionSettings currentSource,
        SourceDiscoveryResult discovery)
    {
        ArgumentNullException.ThrowIfNull(currentSource);
        ArgumentNullException.ThrowIfNull(discovery);
        if (selectedInstanceId is null)
        {
            return "No player is selected.";
        }

        var selectionIsApplied = currentSource.Provider == SourceProvider.WindowsMedia
            && string.Equals(
                currentSource.InstanceId,
                selectedInstanceId,
                StringComparison.Ordinal);
        if (selectionIsApplied)
        {
            return BuildStatusText(discovery.State);
        }

        var playerIsDiscovered = discovery.Sources.Any(source =>
            source.Key.Provider == SourceProvider.WindowsMedia
            && string.Equals(
                source.Key.InstanceId,
                selectedInstanceId,
                StringComparison.Ordinal));
        return playerIsDiscovered
            ? "The selected player is available. Save to apply."
            : "The selected player is not currently available. The selection will be kept.";
    }

    internal static string BuildStatusText(SourceManagerState state)
    {
        return state.Status switch
        {
            SourceStatus.Unconfigured => "No player is selected.",
            SourceStatus.Starting => "Windows Media player discovery is starting.",
            SourceStatus.Available => "The selected player is available.",
            SourceStatus.Unavailable when state.Reason == SourceStatusReason.Missing =>
                "The selected player is not currently available. The selection will be kept.",
            SourceStatus.Unavailable when state.Reason == SourceStatusReason.Ambiguous =>
                "Multiple exact sessions match this player and no single playing session can be selected.",
            SourceStatus.Unavailable when state.Reason == SourceStatusReason.PlatformUnavailable =>
                "Windows Media sessions are temporarily unavailable.",
            SourceStatus.Unavailable => "The selected player is unavailable.",
            SourceStatus.Faulted => "Player discovery faulted. Open the logs for details.",
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };
    }

    private void SetRefreshState(bool refreshing)
    {
        _refresh.Enabled = SelectedProvider == SourceProvider.WindowsMedia && !refreshing;
        _source.Enabled = SelectedProvider == SourceProvider.WindowsMedia && !refreshing;
        if (refreshing)
        {
            _sourceStatus.Text = "Refreshing Windows Media players...";
        }
        SetBusy(refreshing);
    }

    private void SetBusy(bool busy)
    {
        if (_busy == busy)
        {
            return;
        }

        _busy = busy;
        BusyChanged?.Invoke(this, EventArgs.Empty);
    }

    private static Label CreateLabel(string text)
    {
        return new Label
        {
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            Margin = new Padding(0, 0, 8, 0),
            Text = text,
        };
    }

    private sealed record SourceOption(string? InstanceId, string Label);
}
