using System.Net;
using System.Net.Sockets;

namespace NowPlayingOverlay.Host.Shell;

internal static class LoopbackPortProbe
{
    public static bool IsAvailable(int port)
    {
        if (port is < 1 or > 65535)
        {
            return false;
        }

        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        finally
        {
            listener?.Stop();
        }
    }
}
