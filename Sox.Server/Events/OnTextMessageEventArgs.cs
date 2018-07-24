using System;
using Sox.Server.State;

namespace Sox.Server.Events
{
    public class OnTextMessageEventArgs : EventArgs
    {
        public readonly Connection Connection;
        public readonly string Payload;

        public OnTextMessageEventArgs(Connection connection, string payload)
        {
            Connection = connection;
            Payload = payload;
        }
    }
}
