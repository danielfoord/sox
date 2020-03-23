using Sox.Server.State;
using System;

namespace Sox.Server.Events
{
    /// <summary>
    /// Arguments supplied to WebsocketServer.OnConnection
    /// </summary>
    public class OnConnectionEventArgs : EventArgs
    {
        /// <summary>
        /// The subject Connection of the event
        /// </summary>
        public readonly Connection Connection;

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <param name="connection">The subject Connection of the event</param>
        public OnConnectionEventArgs(Connection connection)
        {
            Connection = connection;
        }
    }
}
