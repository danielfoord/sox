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
        public readonly Frame Frame;

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <param name="connection">The subject Connection of the event</param>
        /// <param name="frame">The websocket frame that was received</param>
        public OnFrameEventArgs(Connection connection, Frame frame)
        {
            Connection = connection;
            Frame = frame;
        }
    }
}
