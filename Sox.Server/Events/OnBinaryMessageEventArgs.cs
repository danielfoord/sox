using Sox.Server.State;
using System;

namespace Sox.Server.Events
{
    /// <summary>
    /// Arguments supplied to WebsocketServer.OnBinaryMessage
    /// </summary>
    public class OnBinaryMessageEventArgs : EventArgs
    {
        /// <summary>
        /// The subject Connection of the event
        /// </summary>
        public readonly Connection Connection;

        /// <summary>
        /// The message payload
        /// </summary>
        public readonly byte[] Payload;

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <param name="connection">The subject Connection of the event</param>
        /// <param name="payload">The message payload</param>
        public OnBinaryMessageEventArgs(Connection connection, byte[] payload)
        {
            Connection = connection;
            Payload = payload;
        }
    }
}
