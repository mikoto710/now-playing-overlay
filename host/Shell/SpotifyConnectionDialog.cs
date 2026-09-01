using System.Windows.Forms;
using NowPlayingOverlay.Host.Media.Spotify.Authorization;

namespace NowPlayingOverlay.Host.Shell;

/// <summary>
/// Runs immediate Spotify authorization and credential removal from the Settings UI.
/// </summary>
internal sealed class SpotifyConnectionDialog : Form
{
    private readonly Func<string, bool, CancellationToken, Task<SpotifyConnectionSnapshot>> _authorize;
    private readonly Func<CancellationToken, Task<SpotifyConnectionSnapshot>> _disconnect;
    private readonly Action<string> _setClipboardText;
    private readonly TextBox _clientId;
    private readonly Label _status;
    private readonly Button _connect;
    private readonly Button _reauthorize;
    private readonly Button _disconnectButton;
    private readonly Button _copyRedirectUri;
    private readonly Button _close;
    private readonly CancellationTokenSource _shutdown = new();
    private bool _operationActive;

    public SpotifyConnectionDialog(
        SpotifyConnectionSnapshot connection,
        int callbackPort,
        Func<string, bool, CancellationToken, Task<SpotifyConnectionSnapshot>> authorize,
        Func<CancellationToken, Task<SpotifyConnectionSnapshot>> disconnect,
        Action<string>? setClipboardText = null)
    {
        CurrentConnection = connection ?? throw new ArgumentNullException(nameof(connection));
        _authorize = authorize ?? throw new ArgumentNullException(nameof(authorize));
        _disconnect = disconnect ?? throw new ArgumentNullException(nameof(disconnect));
        _setClipboardText = setClipboardText ?? new ClipboardTextWriter().SetText;
        var redirectUri = SpotifyAuthorizationRequest.CreateLoopbackRedirectUri(callbackPort);

        Text = "Spotify Connection";
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        var explanation = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            MaximumSize = new Size(560, 0),
            Text = "Use your own Spotify Developer application Client ID. Register the redirect URI below in Spotify Dashboard; authorization opens in the system browser.",
        };
        var redirectUriValue = new TextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            TabStop = false,
            Text = redirectUri.AbsoluteUri,
        };
        _copyRedirectUri = new Button
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(12, 0, 0, 0),
            MinimumSize = new Size(85, 0),
            Padding = new Padding(8, 2, 8, 2),
            Text = "Copy",
        };
        _copyRedirectUri.Click += (_, _) => CopyRedirectUri(redirectUri.AbsoluteUri);
        var redirectUriRow = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 1,
        };
        redirectUriRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        redirectUriRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        redirectUriRow.Controls.Add(redirectUriValue, 0, 0);
        redirectUriRow.Controls.Add(_copyRedirectUri, 1, 0);
        _clientId = new TextBox
        {
            Dock = DockStyle.Fill,
            MaxLength = 256,
            Text = connection.ClientId ?? string.Empty,
        };
        _clientId.TextChanged += (_, _) => UpdateButtons(clientIdEdited: true);
        _status = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            MaximumSize = new Size(560, 0),
        };

        _connect = CreateButton("Connect", async () => await AuthorizeAsync(reauthorize: false));
        _reauthorize = CreateButton(
            "Reauthorize",
            async () => await AuthorizeAsync(reauthorize: true));
        _disconnectButton = CreateButton("Disconnect", DisconnectAsync);
        _close = new Button
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            DialogResult = DialogResult.OK,
            MinimumSize = new Size(85, 0),
            Padding = new Padding(8, 2, 8, 2),
            Text = "Close",
        };

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = Padding.Empty,
            WrapContents = false,
        };
        actions.Controls.AddRange([_connect, _reauthorize, _disconnectButton]);

        var closeRow = new FlowLayoutPanel
        {
            Anchor = AnchorStyles.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 16, 0, 0),
            WrapContents = false,
        };
        closeRow.Controls.Add(_close);

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Padding = new Padding(16),
            RowCount = 6,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(explanation, 0, 0);
        layout.SetColumnSpan(explanation, 2);
        layout.Controls.Add(CreateLabel("Redirect URI:"), 0, 1);
        layout.Controls.Add(redirectUriRow, 1, 1);
        layout.Controls.Add(CreateLabel("Client ID:"), 0, 2);
        layout.Controls.Add(_clientId, 1, 2);
        layout.Controls.Add(_status, 0, 3);
        layout.SetColumnSpan(_status, 2);
        layout.Controls.Add(actions, 0, 4);
        layout.SetColumnSpan(actions, 2);
        layout.Controls.Add(closeRow, 0, 5);
        layout.SetColumnSpan(closeRow, 2);

        AcceptButton = _connect;
        CancelButton = _close;
        Controls.Add(layout);
        Shown += (_, _) =>
        {
            if (string.IsNullOrEmpty(_clientId.Text))
            {
                _clientId.Select();
            }
            else
            {
                _close.Select();
            }
        };
        UpdateStatus();
        UpdateButtons(clientIdEdited: false);
    }

    public SpotifyConnectionSnapshot CurrentConnection { get; private set; }

    public bool ConnectionRemoved { get; private set; }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _shutdown.Cancel();
            _shutdown.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task AuthorizeAsync(bool reauthorize)
    {
        string clientId;
        try
        {
            clientId = new SpotifyClientId(_clientId.Text).Value;
        }
        catch (ArgumentException error)
        {
            MessageBox.Show(
                this,
                error.Message,
                "Invalid Spotify Client ID",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            _clientId.Focus();
            return;
        }

        SetOperationState(active: true, reauthorize ? "Waiting for reauthorization..." : "Waiting for authorization...");
        try
        {
            CurrentConnection = await _authorize(
                clientId,
                reauthorize,
                _shutdown.Token);
            _clientId.Text = CurrentConnection.ClientId ?? clientId;
            UpdateStatus();
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            MessageBox.Show(
                this,
                error.Message,
                "Spotify Connection Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            if (!IsDisposed && !_shutdown.IsCancellationRequested)
            {
                SetOperationState(active: false);
            }
        }
    }

    private async Task DisconnectAsync()
    {
        if (MessageBox.Show(
                this,
                "Disconnect Spotify and delete the locally protected refresh credential?",
                "Disconnect Spotify",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        SetOperationState(active: true, "Disconnecting...");
        try
        {
            CurrentConnection = await _disconnect(_shutdown.Token);
            ConnectionRemoved = true;
            _clientId.Clear();
            UpdateStatus();
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            MessageBox.Show(
                this,
                error.Message,
                "Spotify Disconnect Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            if (!IsDisposed && !_shutdown.IsCancellationRequested)
            {
                SetOperationState(active: false);
            }
        }
    }

    private void CopyRedirectUri(string redirectUri)
    {
        _copyRedirectUri.Enabled = false;
        try
        {
            _setClipboardText(redirectUri);
            _copyRedirectUri.Text = "Copied";
        }
        catch (Exception error)
        {
            MessageBox.Show(
                this,
                $"The redirect URI could not be copied. {error.Message}",
                "Copy Redirect URI Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            if (!IsDisposed && !_shutdown.IsCancellationRequested)
            {
                _copyRedirectUri.Enabled = true;
            }
        }
    }

    private void SetOperationState(bool active, string? status = null)
    {
        _operationActive = active;
        _clientId.Enabled = !active;
        _close.Enabled = !active;
        if (status is not null)
        {
            _status.Text = status;
        }

        UpdateButtons(clientIdEdited: false);
    }

    private void UpdateStatus()
    {
        _status.Text = CurrentConnection.State.Status switch
        {
            SpotifyConnectionStatus.Disconnected => "Spotify is not connected.",
            SpotifyConnectionStatus.Connected => "Spotify is connected.",
            SpotifyConnectionStatus.ClientIdMismatch =>
                "The stored credential belongs to a different Client ID. Connect again.",
            SpotifyConnectionStatus.CredentialUnavailable =>
                "The stored Spotify credential cannot be read. Disconnect or connect again.",
            _ => throw new ArgumentOutOfRangeException(nameof(CurrentConnection)),
        };
    }

    private void UpdateButtons(bool clientIdEdited)
    {
        var hasClientId = !string.IsNullOrWhiteSpace(_clientId.Text);
        var connected = CurrentConnection.State.Status == SpotifyConnectionStatus.Connected
            && string.Equals(CurrentConnection.ClientId, _clientId.Text.Trim(), StringComparison.Ordinal);
        _connect.Enabled = !_operationActive && hasClientId && (!connected || clientIdEdited);
        _reauthorize.Enabled = !_operationActive && hasClientId && connected;
        _disconnectButton.Enabled = !_operationActive
            && CurrentConnection.State.Status != SpotifyConnectionStatus.Disconnected;
        if (clientIdEdited && !_operationActive && !connected)
        {
            _status.Text = "Connect to authorize this Client ID.";
        }
    }

    private static Button CreateButton(string text, Func<Task> action)
    {
        var button = new Button
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 8, 0),
            MinimumSize = new Size(85, 0),
            Padding = new Padding(8, 2, 8, 2),
            Text = text,
        };
        button.Click += async (_, _) => await action();
        return button;
    }

    private static Label CreateLabel(string text)
    {
        return new Label
        {
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            Margin = new Padding(0, 4, 12, 4),
            Text = text,
        };
    }
}
