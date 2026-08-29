using System.Reflection;
using System.Windows.Forms;
using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Media.Spotify.Authorization;
using NowPlayingOverlay.Host.Media.WindowTitles;
using NowPlayingOverlay.Host.Outputs;

namespace NowPlayingOverlay.Host.Shell;

internal sealed class SettingsDialog : Form
{
    internal const string ProjectUrl = "https://github.com/mikoto710/now-playing-overlay";

    private readonly Func<SourceProvider, CancellationToken, Task<SourceDiscoveryResult>>
        _refreshSources;
    private readonly Func<string, bool, CancellationToken, Task<SpotifyConnectionSnapshot>>
        _authorizeSpotify;
    private readonly Func<CancellationToken, Task<SpotifyConnectionSnapshot>> _disconnectSpotify;
    private readonly Func<string> _getBrowserPlayerConnectionCode;
    private readonly Func<string> _rotateBrowserPlayerConnectionCode;
    private readonly Action _openBrowserProducer;
    private readonly Action<string> _setClipboardText;
    private readonly Func<CancellationToken, Task<WindowTitleDiscoveryResult>> _refreshWindowTitles;
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
    private readonly Button _save;
    private readonly OutputSettingsControl _outputs;
    private readonly RadioButton _defaultAppearance;
    private readonly RadioButton _customAppearance;
    private readonly Button _artistColor;
    private readonly Button _trackColor;
    private readonly Button _backgroundColor;
    private readonly NumericUpDown _backgroundOpacity;
    private readonly NumericUpDown _cornerRadius;
    private readonly ComboBox _fontFamily;
    private readonly NumericUpDown _artistFontSize;
    private readonly ComboBox _artistFontWeight;
    private readonly NumericUpDown _trackFontSize;
    private readonly ComboBox _trackFontWeight;
    private readonly CheckBox _artworkVisible;
    private readonly NumericUpDown _artworkSize;
    private readonly ComboBox _artworkPosition;
    private readonly ComboBox _artworkFit;
    private readonly NumericUpDown _artworkCornerRadius;
    private readonly CancellationTokenSource _shutdown = new();
    private bool _disposeStarted;
    private CustomAppearanceSettings _customAppearanceDraft;
    private string? _selectedWindowsMediaInstanceId;
    private bool _hasPendingSourceSelection;
    private SpotifyConnectionSnapshot _spotifyConnectionState;
    private SourceDiscoveryResult _windowsDiscovery;

    public SettingsDialog(
        int currentPort,
        SourceDiscoveryResult discovery,
        SourceSelectionSettings currentSource,
        WindowsMediaSettings windowsMedia,
        SpotifyConnectionSnapshot spotifyConnection,
        AppearanceSettings currentAppearance,
        Func<SourceProvider, CancellationToken, Task<SourceDiscoveryResult>> refreshSources,
        Func<string, bool, CancellationToken, Task<SpotifyConnectionSnapshot>> authorizeSpotify,
        Func<CancellationToken, Task<SpotifyConnectionSnapshot>> disconnectSpotify,
        Func<string>? getBrowserPlayerConnectionCode = null,
        Func<string>? rotateBrowserPlayerConnectionCode = null,
        Action? openBrowserProducer = null,
        Action<string>? setClipboardText = null,
        OutputSettings? currentOutputs = null,
        OutputStatusSnapshot? outputStatus = null,
        Func<string, string>? renderOutputPreview = null,
        WindowTitleSettings? currentWindowTitle = null,
        WindowTitleDiscoveryResult? windowTitleDiscovery = null,
        Func<CancellationToken, Task<WindowTitleDiscoveryResult>>? refreshWindowTitles = null,
        Action? openProjectPage = null)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(currentSource);
        currentSource.Validate();
        _currentSource = currentSource;
        ArgumentNullException.ThrowIfNull(windowsMedia);
        windowsMedia.Validate();
        _windowsDiscovery = discovery;
        _spotifyConnectionState = spotifyConnection
            ?? throw new ArgumentNullException(nameof(spotifyConnection));
        ArgumentNullException.ThrowIfNull(currentAppearance);
        currentAppearance.Validate();
        _customAppearanceDraft = currentAppearance.Custom;
        _refreshSources = refreshSources ?? throw new ArgumentNullException(nameof(refreshSources));
        _authorizeSpotify = authorizeSpotify ?? throw new ArgumentNullException(nameof(authorizeSpotify));
        _disconnectSpotify = disconnectSpotify ?? throw new ArgumentNullException(nameof(disconnectSpotify));
        _getBrowserPlayerConnectionCode = getBrowserPlayerConnectionCode ?? (() => string.Empty);
        _rotateBrowserPlayerConnectionCode = rotateBrowserPlayerConnectionCode
            ?? _getBrowserPlayerConnectionCode;
        _openBrowserProducer = openBrowserProducer ?? (() => { });
        _setClipboardText = setClipboardText ?? new ClipboardTextWriter().SetText;
        currentOutputs ??= new OutputSettings();
        currentOutputs.Validate();
        _outputs = new OutputSettingsControl(
            currentOutputs,
            outputStatus ?? new OutputStatusSnapshot(
                0,
                "Outputs are ready. No output errors are recorded."),
            renderOutputPreview ?? (_ => string.Empty));
        currentWindowTitle ??= new WindowTitleSettings();
        currentWindowTitle.Validate();
        windowTitleDiscovery ??= new WindowTitleDiscoveryResult(
            [],
            SourceManagerState.Unconfigured);
        _refreshWindowTitles = refreshWindowTitles ?? (_ => Task.FromResult(
            new WindowTitleDiscoveryResult([], SourceManagerState.Unconfigured)));
        _windowTitleSettings = new WindowTitleSettingsControl(
            currentWindowTitle,
            windowTitleDiscovery);
        _windowTitleSettings.RefreshRequested += WindowTitleRefreshRequested;
        openProjectPage ??= () => { };
        _effectivePort = currentPort;
        _selectedWindowsMediaInstanceId = windowsMedia.LastInstanceId;
        _hasPendingSourceSelection = true;
        Text = "Settings";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(800, 760);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;

        var generalLayout = new TableLayoutPanel
        {
            ColumnCount = 3,
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            RowCount = 9,
        };
        generalLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        generalLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        generalLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        generalLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        generalLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        generalLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 10));
        generalLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        generalLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 10));
        generalLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        generalLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        generalLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        generalLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var generalExplanation = new Label
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
        installBrowserProducer.Click += (_, _) => RunBrowserPlayerAction(_openBrowserProducer);
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

        generalLayout.Controls.Add(generalExplanation, 0, 0);
        generalLayout.SetColumnSpan(generalExplanation, 3);
        generalLayout.Controls.Add(portLabel, 0, 1);
        generalLayout.Controls.Add(_port, 1, 1);
        generalLayout.SetColumnSpan(_port, 2);
        generalLayout.Controls.Add(CreateLabel("Provider:"), 0, 3);
        generalLayout.Controls.Add(_provider, 1, 3);
        generalLayout.SetColumnSpan(_provider, 2);
        generalLayout.Controls.Add(_windowsSourceGroup, 0, 5);
        generalLayout.SetColumnSpan(_windowsSourceGroup, 3);
        generalLayout.Controls.Add(_spotifySourceGroup, 0, 6);
        generalLayout.SetColumnSpan(_spotifySourceGroup, 3);
        generalLayout.Controls.Add(_externalSourceGroup, 0, 7);
        generalLayout.SetColumnSpan(_externalSourceGroup, 3);
        generalLayout.Controls.Add(_windowTitleSourceGroup, 0, 8);
        generalLayout.SetColumnSpan(_windowTitleSourceGroup, 3);

        var appearanceExplanation = new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12),
            MaximumSize = new Size(680, 0),
            Text = "Choose Default to preserve the product style, or Custom to change supported colors, typography, and artwork.",
        };
        _defaultAppearance = new RadioButton
        {
            AutoSize = true,
            Margin = Padding.Empty,
            Text = "Default",
        };
        _customAppearance = new RadioButton
        {
            AutoSize = true,
            Margin = new Padding(16, 0, 0, 0),
            Text = "Custom",
        };
        _defaultAppearance.CheckedChanged += (_, _) =>
        {
            if (_defaultAppearance.Checked)
            {
                SelectDefaultAppearance();
            }
        };
        _customAppearance.CheckedChanged += (_, _) =>
        {
            if (_customAppearance.Checked)
            {
                SelectCustomAppearance();
            }
        };
        var presets = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = Padding.Empty,
            WrapContents = false,
        };
        presets.Controls.AddRange([_defaultAppearance, _customAppearance]);

        _artistColor = CreateColorButton(_customAppearanceDraft.ArtistColor, EditArtistColor);
        _trackColor = CreateColorButton(_customAppearanceDraft.TrackColor, EditTrackColor);
        _backgroundColor = CreateColorButton(
            _customAppearanceDraft.BackgroundColor,
            EditBackgroundColor);
        _backgroundOpacity = new NumericUpDown
        {
            Anchor = AnchorStyles.Left,
            Margin = Padding.Empty,
            Maximum = 100,
            Minimum = 0,
            MinimumSize = new Size(110, 0),
            Value = _customAppearanceDraft.BackgroundOpacityPercent,
        };
        _cornerRadius = new NumericUpDown
        {
            Anchor = AnchorStyles.Left,
            Margin = Padding.Empty,
            Maximum = 35,
            Minimum = 0,
            MinimumSize = new Size(110, 0),
            Value = _customAppearanceDraft.CornerRadius,
        };
        _fontFamily = new ComboBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            AutoCompleteMode = AutoCompleteMode.SuggestAppend,
            AutoCompleteSource = AutoCompleteSource.ListItems,
            DisplayMember = nameof(FontFamilyOption.Label),
            DropDownStyle = ComboBoxStyle.DropDown,
            FormattingEnabled = true,
            Margin = Padding.Empty,
            MaxLength = CustomAppearanceSettings.MaximumFontFamilyLength,
            MinimumSize = new Size(170, 0),
        };
        _fontFamily.DropDown += (_, _) => UpdateFontFamilyDropDownWidth();
        PopulateFontFamilies(_customAppearanceDraft.FontFamily);
        _artistFontSize = CreateNumericControl(
            CustomAppearanceSettings.MinimumArtistFontSize,
            CustomAppearanceSettings.MaximumArtistFontSize,
            _customAppearanceDraft.ArtistFontSize);
        _artistFontWeight = CreateFontWeightControl(_customAppearanceDraft.ArtistFontWeight);
        _trackFontSize = CreateNumericControl(
            CustomAppearanceSettings.MinimumTrackFontSize,
            CustomAppearanceSettings.MaximumTrackFontSize,
            _customAppearanceDraft.TrackFontSize);
        _trackFontWeight = CreateFontWeightControl(_customAppearanceDraft.TrackFontWeight);
        _artworkVisible = new CheckBox
        {
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            Checked = _customAppearanceDraft.ArtworkVisible,
            Margin = Padding.Empty,
            Text = "Show",
        };
        _artworkSize = CreateNumericControl(
            CustomAppearanceSettings.MinimumArtworkSize,
            CustomAppearanceSettings.MaximumArtworkSize,
            _customAppearanceDraft.ArtworkSize);
        _artworkPosition = CreateEnumChoiceControl(_customAppearanceDraft.ArtworkPosition);
        _artworkFit = CreateEnumChoiceControl(_customAppearanceDraft.ArtworkFit);
        _artworkCornerRadius = CreateNumericControl(
            0,
            35,
            _customAppearanceDraft.ArtworkCornerRadius);
        _artworkVisible.CheckedChanged += (_, _) => SetArtworkDetailControlsEnabled(
            _customAppearance.Checked && _artworkVisible.Checked);

        var appearanceRowHeight = GetAppearanceRowHeight(
            _artistColor,
            _trackColor,
            _backgroundColor,
            _backgroundOpacity,
            _cornerRadius,
            _fontFamily,
            _artistFontSize,
            _artistFontWeight,
            _trackFontSize,
            _trackFontWeight);
        var appearanceRowHeights = Enumerable.Repeat(appearanceRowHeight, 5).ToArray();

        var colorsLayout = CreateAppearanceGroupLayout(appearanceRowHeights);
        AddAppearanceRow(colorsLayout, 0, "Artist color:", _artistColor);
        AddAppearanceRow(colorsLayout, 1, "Track color:", _trackColor);
        AddAppearanceRow(colorsLayout, 2, "Background:", _backgroundColor);
        AddAppearanceRow(colorsLayout, 3, "Opacity:", _backgroundOpacity, "%");
        AddAppearanceRow(colorsLayout, 4, "Corner radius:", _cornerRadius, "px");

        var typographyLayout = CreateAppearanceGroupLayout(appearanceRowHeights);
        AddAppearanceRow(typographyLayout, 0, "Font:", _fontFamily);
        AddAppearanceRow(typographyLayout, 1, "Artist size:", _artistFontSize, "px");
        AddAppearanceRow(typographyLayout, 2, "Artist weight:", _artistFontWeight);
        AddAppearanceRow(typographyLayout, 3, "Track size:", _trackFontSize, "px");
        AddAppearanceRow(typographyLayout, 4, "Track weight:", _trackFontWeight);

        var appearanceGroups = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 1,
        };
        appearanceGroups.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        appearanceGroups.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        appearanceGroups.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        appearanceGroups.Controls.Add(CreateAppearanceGroup("Colors", colorsLayout), 0, 0);
        appearanceGroups.Controls.Add(CreateAppearanceGroup("Typography", typographyLayout), 1, 0);

        var artworkRowHeight = GetAppearanceRowHeight(
            _artworkVisible,
            _artworkSize,
            _artworkPosition,
            _artworkFit,
            _artworkCornerRadius);
        var artworkLayout = new TableLayoutPanel
        {
            AutoSize = false,
            ColumnCount = 9,
            Dock = DockStyle.Top,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
            Height = artworkRowHeight * 2,
            Margin = Padding.Empty,
            RowCount = 2,
        };
        artworkLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        artworkLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        artworkLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        artworkLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        artworkLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        artworkLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        artworkLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        artworkLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        artworkLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        artworkLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, artworkRowHeight));
        artworkLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, artworkRowHeight));
        AddArtworkField(artworkLayout, 0, 0, "Visible:", _artworkVisible);
        AddArtworkField(artworkLayout, 1, 0, "Position:", _artworkPosition);
        AddArtworkField(artworkLayout, 2, 0, "Fit:", _artworkFit);
        AddArtworkField(artworkLayout, 0, 1, "Size:", _artworkSize, "px");
        AddArtworkField(
            artworkLayout,
            1,
            1,
            "Radius:",
            _artworkCornerRadius,
            "px");
        var artworkGroup = CreateAppearanceGroup("Artwork", artworkLayout);
        artworkGroup.Margin = new Padding(0, 12, 0, 0);

        var resetAppearance = new Button
        {
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
            Padding = new Padding(8, 2, 8, 2),
            Text = "Reset to Default",
        };
        resetAppearance.Click += (_, _) => ResetAppearance();
        var reloadNote = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(12, 0, 0, 0),
            Text = "Changes apply after Preview or OBS reloads.",
        };
        var appearanceFooter = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 12, 0, 0),
            RowCount = 1,
        };
        appearanceFooter.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        appearanceFooter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        appearanceFooter.Controls.Add(resetAppearance, 0, 0);
        appearanceFooter.Controls.Add(reloadNote, 1, 0);

        var styleLayout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 12),
            RowCount = 1,
        };
        styleLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        styleLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        styleLayout.Controls.Add(CreateLabel("Style:"), 0, 0);
        styleLayout.Controls.Add(presets, 1, 0);

        var appearanceLayout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Padding = new Padding(12),
            RowCount = 5,
        };
        appearanceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < appearanceLayout.RowCount; row++)
        {
            appearanceLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }
        appearanceLayout.Controls.Add(appearanceExplanation, 0, 0);
        appearanceLayout.Controls.Add(styleLayout, 0, 1);
        appearanceLayout.Controls.Add(appearanceGroups, 0, 2);
        appearanceLayout.Controls.Add(artworkGroup, 0, 3);
        appearanceLayout.Controls.Add(appearanceFooter, 0, 4);

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
        };
        var generalTab = new TabPage("General")
        {
            AutoScroll = true,
        };
        generalTab.Controls.Add(generalLayout);
        var appearanceTab = new TabPage("Appearance")
        {
            AutoScroll = true,
        };
        appearanceTab.Controls.Add(appearanceLayout);
        var outputsTab = new TabPage("Outputs");
        outputsTab.Controls.Add(_outputs);
        var aboutProject = new LinkLabel
        {
            AutoSize = true,
            Margin = new Padding(0, 12, 0, 12),
            Text = ProjectUrl,
        };
        aboutProject.LinkClicked += (_, _) => openProjectPage();
        var aboutLayout = new FlowLayoutPanel
        {
            AutoScroll = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(24),
            WrapContents = false,
        };
        aboutLayout.Controls.AddRange(
        [
            new Label { AutoSize = true, Text = "Now Playing Overlay" },
            new Label { AutoSize = true, Text = $"Version {CurrentVersion}" },
            new Label
            {
                AutoSize = true,
                Margin = new Padding(0, 12, 0, 0),
                Text = "Shows the current track and artwork from supported players.",
            },
            aboutProject,
            new Label { AutoSize = true, Text = "GNU General Public License v3.0" },
        ]);
        var aboutTab = new TabPage("About");
        aboutTab.Controls.Add(aboutLayout);
        tabs.TabPages.AddRange([generalTab, appearanceTab, outputsTab, aboutTab]);

        _save = new Button
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
            MinimumSize = new Size(75, 0),
            Padding = new Padding(8, 2, 8, 2),
            Text = "Save",
        };
        _save.Click += SaveClicked;
        var cancel = new Button
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            DialogResult = DialogResult.Cancel,
            Margin = new Padding(8, 0, 0, 0),
            MinimumSize = new Size(75, 0),
            Padding = new Padding(8, 2, 8, 2),
            Text = "Cancel",
        };
        var buttons = new FlowLayoutPanel
        {
            Anchor = AnchorStyles.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 16, 0, 0),
            WrapContents = false,
        };
        buttons.Controls.AddRange([_save, cancel]);

        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            RowCount = 2,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(tabs, 0, 0);
        layout.Controls.Add(buttons, 0, 1);

        AcceptButton = _save;
        CancelButton = cancel;
        Controls.Add(layout);
        _defaultAppearance.Checked = currentAppearance.Preset == AppearancePreset.Default;
        _customAppearance.Checked = currentAppearance.Preset == AppearancePreset.Custom;
        ApplyDiscovery(discovery);
        UpdateSpotifyConnectionState();
        UpdateProviderPanels();
    }

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

    private string? SelectedWindowsMediaInstanceId =>
        (_source.SelectedItem as SourceOption)?.InstanceId;

    public AppearanceSettings SelectedAppearance
    {
        get
        {
            var custom = _customAppearance.Checked
                ? ReadAppearanceControls()
                : _customAppearanceDraft;
            var appearance = new AppearanceSettings
            {
                Preset = _customAppearance.Checked
                    ? AppearancePreset.Custom
                    : AppearancePreset.Default,
                Custom = custom,
            };
            appearance.Validate();
            return appearance;
        }
    }

    public OutputSettings SelectedOutputs => _outputs.SelectedOutputs;

    public WindowTitleSettings SelectedWindowTitle => _windowTitleSettings.SelectedSettings;

    internal static string CurrentVersion
    {
        get
        {
            var assembly = typeof(SettingsDialog).Assembly;
            return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                    .InformationalVersion
                ?? assembly.GetName().Version?.ToString(3)
                ?? "Unknown";
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

    private async void RefreshClicked(object? sender, EventArgs args)
    {
        _selectedWindowsMediaInstanceId = SelectedWindowsMediaInstanceId;
        _hasPendingSourceSelection = true;
        SetRefreshState(refreshing: true);
        try
        {
            var discovery = await _refreshSources(SourceProvider.WindowsMedia, _shutdown.Token);
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

    private void SaveClicked(object? sender, EventArgs args)
    {
        if (SelectedProvider == SourceProvider.SpotifyApi
            && _spotifyConnectionState.State.Status != SpotifyConnectionStatus.Connected)
        {
            MessageBox.Show(
                this,
                "Connect Spotify before selecting Spotify API as the active provider.",
                "Spotify Connection Required",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            _spotifyConnection.Focus();
            return;
        }

        if (_customAppearance.Checked && !TrySelectEnteredFontFamily())
        {
            MessageBox.Show(
                this,
                "Type to search, then choose an installed font from the list.",
                "Choose a Font",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            _fontFamily.Focus();
            _fontFamily.DroppedDown = true;
            return;
        }

        try
        {
            _ = SelectedOutputs;
            var windowTitle = SelectedWindowTitle;
            if (SelectedProvider == SourceProvider.WindowTitle && windowTitle.Target is null)
            {
                throw new InvalidDataException("Choose a window before selecting Window Title.");
            }
        }
        catch (InvalidDataException error)
        {
            MessageBox.Show(
                this,
                error.Message,
                "Check Settings",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private void SpotifyConnectionClicked(object? sender, EventArgs args)
    {
        using var dialog = new SpotifyConnectionDialog(
            _spotifyConnectionState,
            _effectivePort,
            _authorizeSpotify,
            _disconnectSpotify,
            _setClipboardText);
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
        _refresh.Enabled = provider == SourceProvider.WindowsMedia;
        _source.Enabled = provider == SourceProvider.WindowsMedia;
        if (provider == SourceProvider.WindowsMedia)
        {
            UpdateWindowsSelectionStatus();
        }
    }

    private async void WindowTitleRefreshRequested(object? sender, EventArgs args)
    {
        _windowTitleSettings.SetRefreshing(refreshing: true);
        _save.Enabled = false;
        try
        {
            var discovery = await _refreshWindowTitles(_shutdown.Token);
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
                _save.Enabled = true;
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
                ? _rotateBrowserPlayerConnectionCode()
                : _getBrowserPlayerConnectionCode();
            _setClipboardText(code);
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

    private void EditArtistColor(object? sender, EventArgs args)
    {
        EditColor(_artistColor);
    }

    private void EditTrackColor(object? sender, EventArgs args)
    {
        EditColor(_trackColor);
    }

    private void EditBackgroundColor(object? sender, EventArgs args)
    {
        EditColor(_backgroundColor);
    }

    private void EditColor(Button button)
    {
        using var dialog = new ColorDialog
        {
            Color = ColorTranslator.FromHtml(button.Text),
            FullOpen = true,
            SolidColorOnly = true,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            SetColorButton(button, ToHexColor(dialog.Color));
        }
    }

    private void ResetAppearance()
    {
        var defaults = new CustomAppearanceSettings();
        ApplyAppearanceControls(defaults);
        _customAppearanceDraft = defaults;
        _defaultAppearance.Checked = true;
        SetAppearanceControlsEnabled(enabled: false);
    }

    private void SelectDefaultAppearance()
    {
        _customAppearanceDraft = ReadAppearanceControls();
        ApplyAppearanceControls(new CustomAppearanceSettings());
        SetAppearanceControlsEnabled(enabled: false);
    }

    private void SelectCustomAppearance()
    {
        ApplyAppearanceControls(_customAppearanceDraft);
        SetAppearanceControlsEnabled(enabled: true);
    }

    private CustomAppearanceSettings ReadAppearanceControls()
    {
        return new CustomAppearanceSettings
        {
            ArtistColor = _artistColor.Text,
            TrackColor = _trackColor.Text,
            BackgroundColor = _backgroundColor.Text,
            BackgroundOpacityPercent = decimal.ToInt32(_backgroundOpacity.Value),
            CornerRadius = decimal.ToInt32(_cornerRadius.Value),
            FontFamily = ReadEnteredFontFamily(),
            ArtistFontSize = decimal.ToInt32(_artistFontSize.Value),
            ArtistFontWeight = GetSelectedFontWeight(_artistFontWeight),
            TrackFontSize = decimal.ToInt32(_trackFontSize.Value),
            TrackFontWeight = GetSelectedFontWeight(_trackFontWeight),
            ArtworkVisible = _artworkVisible.Checked,
            ArtworkSize = decimal.ToInt32(_artworkSize.Value),
            ArtworkPosition = GetSelectedEnum<ArtworkPosition>(_artworkPosition),
            ArtworkFit = GetSelectedEnum<ArtworkFit>(_artworkFit),
            ArtworkCornerRadius = decimal.ToInt32(_artworkCornerRadius.Value),
        };
    }

    private void ApplyAppearanceControls(CustomAppearanceSettings appearance)
    {
        appearance.Validate();
        SetColorButton(_artistColor, appearance.ArtistColor);
        SetColorButton(_trackColor, appearance.TrackColor);
        SetColorButton(_backgroundColor, appearance.BackgroundColor);
        _backgroundOpacity.Value = appearance.BackgroundOpacityPercent;
        _cornerRadius.Value = appearance.CornerRadius;
        SelectFontFamily(appearance.FontFamily);
        _artistFontSize.Value = appearance.ArtistFontSize;
        SelectFontWeight(_artistFontWeight, appearance.ArtistFontWeight);
        _trackFontSize.Value = appearance.TrackFontSize;
        SelectFontWeight(_trackFontWeight, appearance.TrackFontWeight);
        _artworkVisible.Checked = appearance.ArtworkVisible;
        _artworkSize.Value = appearance.ArtworkSize;
        SelectEnum(_artworkPosition, appearance.ArtworkPosition);
        SelectEnum(_artworkFit, appearance.ArtworkFit);
        _artworkCornerRadius.Value = appearance.ArtworkCornerRadius;
    }

    private void SetAppearanceControlsEnabled(bool enabled)
    {
        _artistColor.Enabled = enabled;
        _trackColor.Enabled = enabled;
        _backgroundColor.Enabled = enabled;
        _backgroundOpacity.Enabled = enabled;
        _cornerRadius.Enabled = enabled;
        _fontFamily.Enabled = enabled;
        _artistFontSize.Enabled = enabled;
        _artistFontWeight.Enabled = enabled;
        _trackFontSize.Enabled = enabled;
        _trackFontWeight.Enabled = enabled;
        _artworkVisible.Enabled = enabled;
        SetArtworkDetailControlsEnabled(enabled && _artworkVisible.Checked);
    }

    private void SetArtworkDetailControlsEnabled(bool enabled)
    {
        _artworkSize.Enabled = enabled;
        _artworkPosition.Enabled = enabled;
        _artworkFit.Enabled = enabled;
        _artworkCornerRadius.Enabled = enabled;
    }

    private void SetRefreshState(bool refreshing)
    {
        var windowsSelected = SelectedProvider == SourceProvider.WindowsMedia;
        _refresh.Enabled = windowsSelected && !refreshing;
        _save.Enabled = !refreshing;
        _source.Enabled = windowsSelected && !refreshing;
        if (refreshing)
        {
            _sourceStatus.Text = "Refreshing Windows Media players...";
        }
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

    private static Button CreateColorButton(string color, EventHandler click)
    {
        var button = new Button
        {
            Anchor = AnchorStyles.Left,
            AutoSize = false,
            Margin = Padding.Empty,
            MinimumSize = new Size(120, 0),
            Padding = new Padding(8, 0, 8, 0),
            UseVisualStyleBackColor = false,
        };
        SetColorButton(button, color);
        button.Click += click;
        return button;
    }

    private static TableLayoutPanel CreateAppearanceGroupLayout(IReadOnlyList<int> rowHeights)
    {
        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = rowHeights.Count,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        foreach (var rowHeight in rowHeights)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, rowHeight));
        }

        return layout;
    }

    private static int GetAppearanceRowHeight(params Control[] controls)
    {
        using var label = CreateLabel("Sample");
        label.Margin = new Padding(0, 2, 8, 4);
        var labelHeight = label.GetPreferredSize(Size.Empty).Height + label.Margin.Vertical;
        var controlHeight = controls.Max(control =>
            control.GetPreferredSize(Size.Empty).Height + 4);
        return Math.Max(labelHeight, controlHeight);
    }

    private static GroupBox CreateAppearanceGroup(string title, Control content)
    {
        var group = new GroupBox
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 8, 0),
            Padding = new Padding(10, 8, 10, 10),
            Text = title,
        };
        group.Controls.Add(content);
        return group;
    }

    private static NumericUpDown CreateNumericControl(int minimum, int maximum, int value)
    {
        return new NumericUpDown
        {
            Anchor = AnchorStyles.Left,
            Margin = Padding.Empty,
            Minimum = minimum,
            Maximum = maximum,
            MinimumSize = new Size(80, 0),
            Value = value,
        };
    }

    private static ComboBox CreateFontWeightControl(int value)
    {
        var control = new ComboBox
        {
            Anchor = AnchorStyles.Left,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FormattingEnabled = true,
            Margin = Padding.Empty,
            MinimumSize = new Size(80, 0),
        };
        control.Items.AddRange([400, 500, 600, 700]);
        SelectFontWeight(control, value);
        return control;
    }

    private static ComboBox CreateEnumChoiceControl<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        var control = new ComboBox
        {
            Anchor = AnchorStyles.Left,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FormattingEnabled = true,
            Margin = Padding.Empty,
            MinimumSize = new Size(100, 0),
        };
        control.Items.AddRange(Enum.GetValues<TEnum>().Cast<object>().ToArray());
        SelectEnum(control, value);
        return control;
    }

    private void PopulateFontFamilies(string? selectedFontFamily)
    {
        var options = new List<FontFamilyOption>
        {
            new(null, "Default"),
        };
        var installedFamilies = FontFamily.Families;
        try
        {
            options.AddRange(installedFamilies
                .Select(font => font.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .Select(name => new FontFamilyOption(name, name)));
        }
        finally
        {
            foreach (var installedFamily in installedFamilies)
            {
                installedFamily.Dispose();
            }
        }

        if (selectedFontFamily is not null
            && options.All(option => !string.Equals(
                option.FontFamily,
                selectedFontFamily,
                StringComparison.OrdinalIgnoreCase)))
        {
            options.Add(new FontFamilyOption(
                selectedFontFamily,
                $"{selectedFontFamily} (currently unavailable)"));
        }

        _fontFamily.Items.AddRange(options.Cast<object>().ToArray());
        SelectFontFamily(selectedFontFamily);
    }

    private void UpdateFontFamilyDropDownWidth()
    {
        var contentWidth = _fontFamily.Items
            .Cast<object>()
            .Select(item => TextRenderer.MeasureText(
                _fontFamily.GetItemText(item),
                _fontFamily.Font).Width)
            .DefaultIfEmpty(_fontFamily.Width)
            .Max();
        var desiredWidth = Math.Max(
            _fontFamily.Width,
            contentWidth + SystemInformation.VerticalScrollBarWidth + 24);
        var workingAreaWidth = Screen.FromControl(_fontFamily).WorkingArea.Width;
        var maximumWidth = Math.Max(_fontFamily.Width, workingAreaWidth - 32);
        _fontFamily.DropDownWidth = Math.Min(desiredWidth, maximumWidth);
    }

    private string? ReadEnteredFontFamily()
    {
        var option = FindEnteredFontFamily();
        return option is null ? _customAppearanceDraft.FontFamily : option.FontFamily;
    }

    private bool TrySelectEnteredFontFamily()
    {
        var option = FindEnteredFontFamily();
        if (option is null)
        {
            return false;
        }

        _fontFamily.SelectedItem = option;
        return true;
    }

    private FontFamilyOption? FindEnteredFontFamily()
    {
        var entered = _fontFamily.Text.Trim();
        return _fontFamily.Items
            .OfType<FontFamilyOption>()
            .FirstOrDefault(option =>
                string.Equals(option.Label, entered, StringComparison.CurrentCultureIgnoreCase)
                || string.Equals(
                    option.FontFamily,
                    entered,
                    StringComparison.CurrentCultureIgnoreCase));
    }

    private void SelectFontFamily(string? fontFamily)
    {
        for (var index = 0; index < _fontFamily.Items.Count; index++)
        {
            if (_fontFamily.Items[index] is FontFamilyOption option
                && string.Equals(
                    option.FontFamily,
                    fontFamily,
                    StringComparison.OrdinalIgnoreCase))
            {
                _fontFamily.SelectedIndex = index;
                return;
            }
        }

        _fontFamily.SelectedIndex = 0;
    }

    private static int GetSelectedFontWeight(ComboBox control)
    {
        return control.SelectedItem is int weight
            ? weight
            : throw new InvalidOperationException("A font weight must be selected.");
    }

    private static void SelectFontWeight(ComboBox control, int weight)
    {
        var index = control.Items.IndexOf(weight);
        if (index < 0)
        {
            throw new InvalidOperationException($"Unsupported font weight {weight}.");
        }

        control.SelectedIndex = index;
    }

    private static TEnum GetSelectedEnum<TEnum>(ComboBox control)
        where TEnum : struct, Enum
    {
        return control.SelectedItem is TEnum value
            ? value
            : throw new InvalidOperationException($"A {typeof(TEnum).Name} value must be selected.");
    }

    private static void SelectEnum<TEnum>(ComboBox control, TEnum value)
        where TEnum : struct, Enum
    {
        var index = control.Items.IndexOf(value);
        if (index < 0)
        {
            throw new InvalidOperationException($"Unsupported {typeof(TEnum).Name} value {value}.");
        }

        control.SelectedIndex = index;
    }

    private static void SetColorButton(Button button, string color)
    {
        var parsed = ColorTranslator.FromHtml(color);
        button.Text = color;
        button.BackColor = parsed;
        button.ForeColor = parsed.GetBrightness() < 0.5 ? Color.White : Color.Black;
    }

    private static string ToHexColor(Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static void AddArtworkField(
        TableLayoutPanel layout,
        int field,
        int row,
        string label,
        Control control,
        string? suffix = null)
    {
        var column = field * 3;
        var labelControl = CreateLabel(label);
        labelControl.Margin = new Padding(0, 2, 8, 4);
        control.Margin = new Padding(0, 0, 0, 4);
        if (control is CheckBox)
        {
            control.Anchor = AnchorStyles.Left;
        }
        else
        {
            control.Dock = DockStyle.Fill;
        }

        layout.Controls.Add(labelControl, column, row);
        layout.Controls.Add(control, column + 1, row);
        if (suffix is not null)
        {
            var suffixControl = CreateLabel(suffix);
            suffixControl.Margin = new Padding(6, 2, 0, 4);
            layout.Controls.Add(suffixControl, column + 2, row);
        }
    }

    private static void AddAppearanceRow(
        TableLayoutPanel layout,
        int row,
        string label,
        Control control,
        string? suffix = null)
    {
        var labelControl = CreateLabel(label);
        labelControl.Margin = new Padding(0, 2, 8, 4);
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(
            control.Margin.Left,
            control.Margin.Top,
            control.Margin.Right,
            4);
        layout.Controls.Add(labelControl, 0, row);
        layout.Controls.Add(control, 1, row);
        if (suffix is not null)
        {
            var suffixControl = CreateLabel(suffix);
            suffixControl.Margin = new Padding(6, 2, 0, 4);
            layout.Controls.Add(suffixControl, 2, row);
        }
    }

    private sealed record SourceOption(string? InstanceId, string Label);

    private sealed record FontFamilyOption(string? FontFamily, string Label);
}
