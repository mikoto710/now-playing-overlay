using System.Reflection;
using System.Windows.Forms;

namespace NowPlayingOverlay.Host.Shell;

internal sealed class AboutDialog : Form
{
    public const string ProjectUrl = "https://github.com/mikoto710/now-playing-overlay";

    public AboutDialog(Action openProjectPage)
    {
        ArgumentNullException.ThrowIfNull(openProjectPage);
        Text = "About";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(500, 330);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;

        var title = CreateCenteredLabel("Now Playing Overlay");
        var version = CreateCenteredLabel($"Version {CurrentVersion}");
        var description = CreateCenteredLabel(
            "Shows now-playing information from Windows Media, Spotify, browser players, and window titles.");
        var project = new LinkLabel
        {
            Anchor = AnchorStyles.None,
            AutoSize = true,
            Margin = new Padding(0, 12, 0, 12),
            Text = ProjectUrl,
        };
        project.LinkClicked += (_, _) => openProjectPage();
        var acknowledgements = CreateCenteredLabel(
            "Inspired by Snip, Tuna, and Zyphen's Now Playing.");
        var license = CreateCenteredLabel("GNU General Public License v3.0");
        var close = new Button
        {
            Anchor = AnchorStyles.None,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            DialogResult = DialogResult.OK,
            Margin = new Padding(0, 18, 0, 0),
            Padding = new Padding(16, 3, 16, 3),
            Text = "OK",
        };
        AcceptButton = close;
        CancelButton = close;

        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
            RowCount = 7,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var index = 0; index < layout.RowCount; index++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(version, 0, 1);
        layout.Controls.Add(description, 0, 2);
        layout.Controls.Add(project, 0, 3);
        layout.Controls.Add(acknowledgements, 0, 4);
        layout.Controls.Add(license, 0, 5);
        layout.Controls.Add(close, 0, 6);
        Controls.Add(layout);
    }

    internal static string CurrentVersion
    {
        get
        {
            var assembly = typeof(AboutDialog).Assembly;
            return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                    .InformationalVersion
                ?? assembly.GetName().Version?.ToString(3)
                ?? "Unknown";
        }
    }

    private static Label CreateCenteredLabel(string text)
    {
        return new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 5, 0, 5),
            MaximumSize = new Size(440, 0),
            Text = text,
            TextAlign = ContentAlignment.MiddleCenter,
        };
    }
}
