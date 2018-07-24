using System;
using Sox.Server.State;

namespace Sox.Server.Events
{
    public class OnBinaryMessageEventArgs : EventArgs
    {
        public readonly Connection Connection;
        public readonly byte[] Payload;

        public OnBinaryMessageEventArgs(Connection connection, byte[] payload)
        {
            Connection = connection;
            Payload = payload;
        }
    }
}
