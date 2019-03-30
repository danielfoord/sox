using System;
using Sox.Server.State;

namespace Sox.Server.Events
{
    public class OnErrorEventArgs : EventArgs
    {
        public readonly Connection Connection;
        public readonly Exception Exception;

        public OnErrorEventArgs(Connection connection, Exception ex)
        {
            Connection = connection;
            Exception = ex;
        }
    }
}
