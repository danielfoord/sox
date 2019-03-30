using System.Net;
using System.Threading.Tasks;

namespace Sox.Server
{
    // FIXME: Comments
    public interface IWebSocketServer
    {
        // FIXME: Comments
        void Start();

        // FIXME: Comments
        Task Stop();
    }
}
