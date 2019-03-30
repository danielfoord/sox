using System.Net;

namespace Sox.Server
{
    public interface IWebSocketServer
    {
        void Start(IPAddress ipAddress, int port);

        void Stop();
    }
}
