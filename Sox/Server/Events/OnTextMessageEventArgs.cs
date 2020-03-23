using Sox.Server.State;
using System;

namespace Sox.Server.Events
{
    /// <summary>
    /// Arguments supplied to WebsocketServer.OnTextMessage
    /// </summary>
    public class OnTextMessageEventArgs : EventArgs
    {
        /// <summary>
        /// The subject Connection of the event
        /// </summary>
        public readonly Connection Connection;

        /// <summary>
        /// The message payload encoded in UTF8
        /// </summary>
        public readonly string Payload;

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <param name="connection">The subject Connection of the event</param>
        /// <param name="payload">The message payload</param>
        public OnTextMessageEventArgs(Connection connection, string payload)
        {
            Connection = connection;
            Payload = payload;
        }
    }
}
