using Sox.Extensions;
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
        /// <remarks>
        /// This borrows the connection's read buffer (or a pooled reassembly buffer for a
        /// fragmented message) and is only valid for the duration of this event invocation -
        /// copy it, or call <see cref="GetString"/>, if you need to keep the data any longer.
        /// </remarks>
        public readonly ReadOnlyMemory<byte> Payload;

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <param name="connection">The subject Connection of the event</param>
        /// <param name="payload">The message payload</param>
        public OnTextMessageEventArgs(Connection connection, ReadOnlyMemory<byte> payload)
        {
            Connection = connection;
            Payload = payload;
        }

        /// <summary>
        /// Decode <see cref="Payload"/> as a UTF8 string
        /// </summary>
        /// <returns>The decoded payload</returns>
        public string GetString() => Payload.GetString();
    }
}
