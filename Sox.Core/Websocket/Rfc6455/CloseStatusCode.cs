namespace Sox.Core.Websocket.Rfc6455
{   
    /// <summary>
    ///     The codes for closing a websocket connection
    /// </summary>
    public enum CloseStatusCode : ushort
    {
        Normal = 1000,
        GoingAway = 1001,
        ProtocolError = 1002,
        WontAccept = 1003,
        Reserved = 1004,
        // Do not send over the wire
        NoStatusSet = 1005,
        // Do not send over the wire
        AbnormalClosure = 1006,
        NotConsistent = 1007,
        PolicyViolation = 1008,
        MessageTooBig = 1009,
        MissingExtensions = 1010,
        UnexpectedError = 1011,
        ServiceRestart = 1012,
        TryAgainLater = 1013,
        BadGateway = 1014,
        TlsHandshakeFail = 1015
    }
}