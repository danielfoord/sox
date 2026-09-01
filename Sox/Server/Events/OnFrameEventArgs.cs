using Sox.Server.State;
using Sox.Websocket.Rfc6455.Framing;
using System;

namespace Sox.Server.Events
{
    /// <summary>
    /// Arguments supplied to WebsocketServer.OnConnection
    /// </summary>
    public class OnFrameEventArgs : EventArgs
    {
        /// <summary>
        /// The subject Connection of the event
        /// </summary>
        public readonly Connection Connection;

        /// <summary>
        /// The websocket frame that was received
        /// </summary>
        /// <remarks>
        /// <see cref="WebSocketFrame.Data"/> borrows the connection's read buffer and is only
        /// valid for the duration of this event invocation - copy it if you need to keep it.
        /// </remarks>
        public readonly WebSocketFrame Frame;

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <param name="connection">The subject Connection of the event</param>
        /// <param name="frame">The websocket frame that was received</param>
        public OnFrameEventArgs(Connection connection, WebSocketFrame frame)
        {
            Connection = connection;
            Frame = frame;
        }
    }
}
