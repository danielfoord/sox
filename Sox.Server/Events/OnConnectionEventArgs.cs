using System;
using Sox.Server.State;

namespace Sox.Server.Events
{
    public class OnConnectionEventArgs : EventArgs
    {
        public readonly Connection Connection;

        public OnConnectionEventArgs(Connection connection)
        {
            Connection = connection;
        }
    }
}
