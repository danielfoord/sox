using System.Net;
using System.Threading.Tasks;

namespace Sox.Server
{
    /// <summary>
    /// A simple Websocket server
    /// </summary>
    public interface IWebSocketServer
    {
        /// <summary>
        /// Start the websocket server
        /// </summary>
        Task Start();

        /// <summary>
        /// Stop the websocket server
        /// </summary>
        /// <returns>A task that resolves when the server has finished shutting down</returns>
        Task Stop();
    }
}
