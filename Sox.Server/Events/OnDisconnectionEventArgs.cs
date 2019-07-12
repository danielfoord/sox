using Sox.Server.State;

namespace Sox.Server.Events
{
    /// <summary>
    /// Arguments supplied to WebsocketServer.OnDisconnection
    /// </summary>
    public class OnDisconnectionEventArgs
    {
        /// <summary>
        /// The subject Connection of the event
        /// </summary>
        public readonly Connection Connection;

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <param name="connection">The subject Connection of the event</param>
        public OnDisconnectionEventArgs(Connection connection)
        {
            Connection = connection;
        }
    }
}
