using System.Drawing;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using NowPlayingOverlay.Host.Shell;

namespace NowPlayingOverlay.Host.Tests.Shell;

public sealed class PortConfigurationDialogTests
{
    [Fact]
    public void LayoutExpandsForLargeSystemFontsWithoutClippingControls()
    {
        RunInSta(() =>
        {
            using var dialog = new PortConfigurationDialog(13130)
            {
                Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 18),
            };

            dialog.PerformLayout();

            Assert.Equal("Now Playing Overlay Port", dialog.Text);
            var layout = Assert.Single(dialog.Controls.OfType<TableLayoutPanel>());
            var controls = Descendants(dialog).ToArray();
            var explanation = Assert.Single(
                controls.OfType<Label>(),
                label => label.Text.StartsWith("Choose"));
            var portLabel = Assert.Single(
                controls.OfType<Label>(),
                label => label.Text == "Port:");
            var port = Assert.Single(controls.OfType<NumericUpDown>());
            var buttons = controls.OfType<Button>().ToArray();

            Assert.True(dialog.ClientRectangle.Contains(layout.Bounds));
            Assert.Equal(explanation.PreferredHeight, explanation.Height);
            Assert.True(portLabel.Right <= port.Left);
            Assert.True(port.Width >= port.MinimumSize.Width);
            Assert.All(buttons, button =>
            {
                Assert.True(button.Width >= button.PreferredSize.Width);
                Assert.True(button.Height >= button.PreferredSize.Height);
            });
        });
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static void RunInSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                error = exception;
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
