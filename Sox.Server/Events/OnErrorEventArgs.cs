using System;
using Sox.Server.State;

namespace Sox.Server.Events
{
    /// <summary>
    /// Arguments supplied to WebsocketServer.OnError
    /// </summary>
    public class OnErrorEventArgs : EventArgs
    {
        /// <summary>
        /// The subject Connection of the event
        /// </summary>
        public readonly Connection Connection;

        /// <summary>
        /// The Exception that was raised
        /// </summary>
        public readonly Exception Exception;

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <param name="connection">The subject Connection of the event</param>
        /// <param name="ex">The Exception that was raised</param>
        public OnErrorEventArgs(Connection connection, Exception ex)
        {
            Connection = connection;
            Exception = ex;
        }
    }
}
