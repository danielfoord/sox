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
        /// <remarks>
        /// This borrows the connection's read buffer (or a pooled reassembly buffer for a
        /// fragmented message) and is only valid for the duration of this event invocation -
        /// copy it (e.g. <c>Payload.ToArray()</c>) if you need to keep the data any longer.
        /// </remarks>
        public readonly ReadOnlyMemory<byte> Payload;

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <param name="connection">The subject Connection of the event</param>
        /// <param name="payload">The message payload</param>
        public OnBinaryMessageEventArgs(Connection connection, ReadOnlyMemory<byte> payload)
        {
            Connection = connection;
            Payload = payload;
        }
    }
}
