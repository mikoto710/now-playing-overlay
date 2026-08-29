using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using NowPlayingOverlay.Host.Shell;

namespace NowPlayingOverlay.Host.Tests.Shell;

public sealed class AboutDialogTests
{
    [Fact]
    public void ShowsProductVersionProjectAndAcknowledgementsWithoutClipping()
    {
        RunSta(() =>
        {
            using var dialog = new AboutDialog(() => { });
            dialog.Opacity = 0;
            dialog.Show();
            Application.DoEvents();

            Assert.Equal("About", dialog.Text);
            Assert.NotEqual("Unknown", AboutDialog.CurrentVersion);
            var labels = FindControls<Label>(dialog).Select(label => label.Text).ToArray();
            Assert.Contains("Now Playing Overlay", labels);
            Assert.Contains($"Version {AboutDialog.CurrentVersion}", labels);
            Assert.Contains("Inspired by Snip, Tuna, and Zyphen's Now Playing.", labels);
            Assert.Contains("GNU General Public License v3.0", labels);
            Assert.Contains(
                FindControls<LinkLabel>(dialog),
                link => link.Text == AboutDialog.ProjectUrl);
            AssertVisibleLeafControlsFit(dialog);

            var close = FindControls<Button>(dialog).Single(button => button.Text == "OK");
            close.PerformClick();
            Application.DoEvents();
            Assert.Equal(DialogResult.OK, dialog.DialogResult);
        });
    }

    private static void AssertVisibleLeafControlsFit(Control container)
    {
        foreach (var control in FindControls<Control>(container).Where(control =>
            control.Visible
            && control.Controls.Count == 0))
        {
            var bounds = container.RectangleToClient(
                control.RectangleToScreen(control.ClientRectangle));
            Assert.True(
                container.ClientRectangle.Contains(bounds),
                $"{control.GetType().Name} '{control.Text}' is clipped by the About window.");
        }
    }

    private static IEnumerable<T> FindControls<T>(Control root)
        where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T typed)
            {
                yield return typed;
            }

            foreach (var descendant in FindControls<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception caught)
            {
                error = caught;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}
