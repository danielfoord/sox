using Sox.Extensions;
using Sox.Websocket.Rfc6455.Messaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

/*
  https://tools.ietf.org/html/rfc6455

  0                   1                   2                   3
  0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
  +-+-+-+-+-------+-+-------------+-------------------------------+
  |F|R|R|R| opcode|M| Payload len |    Extended payload length    |
  |I|S|S|S|  (4)  |A|     (7)     |             (16/64)           |
  |N|V|V|V|       |S|             |   (if payload len==126/127)   |
  | |1|2|3|       |K|             |                               |
  +-+-+-+-+-------+-+-------------+ - - - - - - - - - - - - - - - +
  |     Extended payload length continued, if payload len == 127  |
  + - - - - - - - - - - - - - - - +-------------------------------+
  |                               |Masking-key, if MASK set to 1  |
  +-------------------------------+-------------------------------+
  | Masking-key (continued)       |          Payload Data         |
  +-------------------------------- - - - - - - - - - - - - - - - +
  :                     Payload Data continued ...                :
  + - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - +
  |                     Payload Data continued ...                |
  +---------------------------------------------------------------+
*/

namespace Sox.Websocket.Rfc6455.Framing
{
    /// <summary>
    /// Represents a Rfc6455 websocket frame
    /// see: https://tools.ietf.org/html/rfc6455#section-5.2
    /// </summary>
    public class Frame
    {
        /// <summary>
        ///     The frames headers
        /// </summary>
        public FrameHeaders Headers;

        /// <summary>
        ///     Frame opcode
        /// </summary>
        public OpCode OpCode => Headers.OpCode;

        /// <summary>
        ///     The key used to shouldMask the data
        /// </summary>
        public readonly byte[] MaskingKey;

        /// <summary>
        ///     The payload data
        /// </summary>
        public readonly byte[] Data;

        /// <summary>
        ///     The length of the payloads data
        /// </summary>
        public int PayloadLength => Headers.PayloadLength;

        /// <summary>
        ///     Decode the payload as a string
        /// </summary>
        /// <returns>The decoded payload</returns>
        public string DecodedData => Data.GetString();

        internal Frame(bool isFinal, bool rsv1, bool rsv2, bool rsv3,
            OpCode opCode, bool shouldMask, byte[] maskingKey = null,
            int payloadLength = 0, byte[] data = null)
        {
            Headers = new FrameHeaders(
                isFinal,
                rsv1,
                rsv2,
                rsv3,
                opCode,
                shouldMask,
                payloadLength);

            MaskingKey = maskingKey;
            Data = data ?? new byte[0];
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="headers">The <c>FrameHeaders</c> for this <c>Frame</c></param>
        /// <param name="maskingKey">The data masking key</param>
        /// <param name="data">The frame payload</param>
        internal Frame(FrameHeaders headers, byte[] maskingKey, byte[] data)
        {
            Headers = headers;
            MaskingKey = maskingKey;
            Data = data ?? new byte[0];
        }

        #region Factory Methods
        /// <summary>
        /// Create an initiation frame for a websocket message
        /// </summary>
        /// <param name="type">The type of message</param>
        /// <param name="payload">The frame payload</param>
        /// <param name="isFinal">Flag to indicate if this is the final frame of the message</param>
        /// <param name="shouldMask">True if the frame payload should be masked</param>
        /// <returns>A <c>Frame</c> instance</returns>
        internal static Frame CreateInitiationFrame(MessageType type, byte[] payload, bool isFinal = true, bool shouldMask = true) => type switch
        {
            MessageType.Binary => CreateBinary(payload: payload, shouldMask: shouldMask, isFinal: isFinal),
            MessageType.Text => CreateText(payload: payload.GetString(), shouldMask: shouldMask, isFinal: isFinal),
            _ => throw new Exception($"MessageType {type} is not a valid data frame type")
        };

        /// <summary>
        /// Create a text frame
        /// </summary>
        /// <param name="payload">The frame payload</param>
        /// <param name="shouldMask">True if the frame payload should be masked</param>
        /// <param name="isFinal">True if the frame is the last frame of the message</param>
        /// <returns>An instance of a Frame</returns>
        internal static Frame CreateText(string payload,
            bool shouldMask = false,
            bool isFinal = true) => new Frame(
            isFinal: isFinal,
            rsv1: false,
            rsv2: false,
            rsv3: false,
            opCode: OpCode.Text,
            shouldMask: shouldMask,
            payloadLength: payload.Length,
            maskingKey: CreateMaskingKey(),
            data: payload.GetBytes());

        /// <summary>
        ///     Create a binary frame
        /// </summary>
        /// <param name="payload">The frame payload</param>
        /// <returns>A Binary Websocket Frame</returns>
        internal static Frame CreateBinary(byte[] payload,
            bool shouldMask = false,
            bool isFinal = true) => new Frame(
            isFinal: isFinal,
            rsv1: false,
            rsv2: false,
            rsv3: false,
            opCode: OpCode.Binary,
            shouldMask: shouldMask,
            payloadLength: payload.Length,
            maskingKey: CreateMaskingKey(),
            data: payload);

        /// <summary>
        ///     Create a continuation frame
        /// </summary>
        /// <param name="payload">The frame payload</param>
        /// <returns>A Binary Websocket Frame</returns>
        internal static Frame CreateContinuation(byte[] payload,
            bool shouldMask = false,
            bool isFinal = false) => new Frame(
            isFinal: isFinal,
            rsv1: false,
            rsv2: false,
            rsv3: false,
            opCode: OpCode.Continuation,
            shouldMask: shouldMask,
            payloadLength: payload.Length,
            maskingKey: CreateMaskingKey(),
            data: payload);

        /// <summary>
        ///     Create a ping frame
        /// </summary>
        /// <returns>A Ping Websocket Frame</returns>
        internal static Frame CreatePing() => new Frame(
            isFinal: true,
            rsv1: false,
            rsv2: false,
            rsv3: false,
            opCode: OpCode.Ping,
            shouldMask: false);

        /// <summary>
        ///     Create a pong frame
        /// </summary>
        /// <returns>A Ping Websocket Frame</returns>
        internal static Frame CreatePong() => new Frame(
            isFinal: true,
            rsv1: false,
            rsv2: false,
            rsv3: false,
            opCode: OpCode.Pong,
            shouldMask: false);

        /// <summary>
        ///     Create a close frame
        /// </summary>
        /// <returns>A Ping Websocket Frame</returns>
        internal static Frame CreateClose() => new Frame(
            isFinal: true,
            rsv1: false,
            rsv2: false,
            rsv3: false,
            opCode: OpCode.Close,
            shouldMask: false);

        /// <summary>
        ///     Create a close frame
        /// </summary>
        /// <returns>A Ping Websocket Frame</returns>
        internal static Frame CreateClose(CloseStatusCode closeCode) => new Frame(
            isFinal: true,
            rsv1: false,
            rsv2: false,
            rsv3: false,
            opCode: OpCode.Close,
            payloadLength: 2,
            shouldMask: false,
            data: BitConverter.GetBytes((short)closeCode));
        #endregion

        #region IO Methods
        /// <summary>
        ///     Read a <c>Frame</c> directly from a <c>System.IO.Stream</c>
        /// </summary>
        /// <param name="stream">The input stream</param>
        /// <returns>
        ///     A <c>System.Threading.Task</c> that resolves when a complete <c>Frame</c> has been read
        /// </returns>
        internal static async Task<Frame> UnpackAsync(Stream stream)
        {
            var headers = await FrameHeaders.Unpack(stream);

            byte[] maskingKey = null;
            byte[] data = null;

            if (headers.ShouldMask)
            {
                maskingKey = await stream.ReadBytesAsync(4);
            }

            if (headers.PayloadLength > 0)
            {
                data = headers.ShouldMask
                    ? Xor(maskingKey, await stream.ReadBytesAsync(headers.PayloadLength))
                    : await stream.ReadBytesAsync(headers.PayloadLength);
            }

            return new Frame(headers, maskingKey, data);
        }

        /// <summary>
        ///     Decode a byte array to a <c>Frame</c>
        /// </summary>
        /// <param name="bytes">The byte array to decode</param>
        /// <returns>A WebSocket message frame</returns>
        internal static async Task<Frame> UnpackAsync(byte[] bytes)
        {
            using var stream = new MemoryStream(bytes);
            return await UnpackAsync(stream);
        }

        /// <summary>
        ///     Encode this frame as a byte array
        /// </summary>
        /// <returns>A WebSocket message frame bytes</returns>
        internal async Task<byte[]> PackAsync()
        {
            using var stream = new MemoryStream { Position = 0 };
            await stream.WriteBytesAsync(await Headers.PackAsync());

            if (Headers.ShouldMask)
            {
                await stream.WriteBytesAsync(MaskingKey);
                // Mask the payload data with a simple XOR using the masking key
                await stream.WriteBytesAsync(Xor(MaskingKey, Data));
            }
            else
            {
                // If this is a close frame status code, it needs to be BigEndian
                if (Headers.OpCode == OpCode.Close)
                {
                    EnsureBigEndian(Data);
                }
                await stream.WriteBytesAsync(Data);
            }

            await stream.FlushAsync();

            return stream.ToArray();
        }
        #endregion

        private static byte[] Xor(IReadOnlyList<byte> maskingKey, IReadOnlyList<byte> data)
        {
            var output = new byte[data.Count];
            for (var i = 0; i < data.Count; i++)
            {
                output[i] = (byte)(data[i] ^ maskingKey[i % 4]);
            }

            return output;
        }

        private static byte[] CreateMaskingKey()
        {
            var random = new Random();
            var maskingKey = new byte[4];

            for (var i = 0; i < 4; i++)
            {
                maskingKey[i] = (byte)random.Next(255);
            }

            return maskingKey;
        }

        private static void EnsureBigEndian(byte[] bytes)
        {
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
        }

        public override string ToString()
        {
            return $"FIN: {Headers.IsFinal} | " +
                   $"RSV1: {Headers.Rsv1} | " +
                   $"RSV2: {Headers.Rsv2} | " +
                   $"RSV3: {Headers.Rsv3} | " +
                   $"Mask: {Headers.ShouldMask} | " +
                   $"OpCode: {Headers.OpCode} | " +
                   $"PayloadLen: {PayloadLength}";
        }
    }
}