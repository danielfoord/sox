namespace Sox.Server.State
{
    /// <summary>
    /// Shows the state of a Connection
    /// </summary>
    public enum ConnectionState
    {
        /// <summary>
        /// The connection is being opened, and cannot receive messages
        /// </summary>
        Connecting,

        /// <summary>
        /// The connection is open, and is able to receive messages
        /// </summary>
        Open,

        /// <summary>
        /// The connection is closing, and cannot receive messages
        /// </summary>
        Closing,

        /// <summary>
        /// The connection is closed, and can be disposed of
        /// </summary>
        Closed
    }
}
