using Sox.Server.State;

namespace Sox.Server.Events
{
    public class OnDisconnectionEventArgs
    {
        public readonly Connection Connection;

        public OnDisconnectionEventArgs(Connection connection)
        {
            Connection = connection;
        }
    }
}
