using System.Net;
using System.Net.Sockets;

namespace Jason.Runtime.Hosting;

/// <summary>Finds the port a runtime binds: the configured one, or the next free one above it within a bounded span.</summary>
public static class PortProbe
{
    public const int Span = 100;

    /// <summary>The first free loopback port in <c>[from, from + Span)</c>, or 0 when every one is taken.</summary>
    public static int FirstFree(int from)
    {
        for (var port = from; port < from + Span && port <= 65535; port++)
        {
            if (IsFree(port))
            {
                return port;
            }
        }

        return 0;
    }

    public static bool IsFree(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
