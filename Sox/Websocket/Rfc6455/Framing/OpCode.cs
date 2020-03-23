namespace Sox.Websocket.Rfc6455.Framing
{
    /// <summary>
    /// Represents a Websocket Frame Opcode
    /// </summary>
    /// <remarks>
    /// See: https://tools.ietf.org/html/rfc6455#section-11.8
    /// </remarks>
    public enum OpCode
    {
        Continuation = 0x0,
        Text = 0x1,
        Binary = 0x2,
        Close = 0x8,
        Ping = 0x9,
        Pong = 0xA
    }
}