using Sox.Extensions;
using Sox.Websocket.Rfc6455.Messaging;
using System;
using System.Buffers;
using System.Runtime.InteropServices;

/*
  https://tools.ietf.org/html/rfc6455#section-5.2

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
    /// <remarks>
    /// A <c>WebSocketFrame</c> parsed off the wire via <see cref="TryParse"/> does not own its
    /// <see cref="Data"/>/<see cref="MaskingKey"/> memory in the common case — it borrows a slice
    /// of the underlying <c>PipeReader</c> buffer. That memory is only valid until the reader is
    /// advanced past it, which happens as soon as the frame has been handed off (to the
    /// <c>OnFrame</c> event, and to the message assembler). Do not retain a <c>WebSocketFrame</c>
    /// or its memory beyond that synchronous handoff.
    /// </remarks>
    public readonly struct WebSocketFrame
    {
        /// <summary>
        ///     The frames headers
        /// </summary>
        public readonly FrameHeaders Headers;

        /// <summary>
        ///     Frame opcode
        /// </summary>
        public OpCode OpCode => Headers.OpCode;

        /// <summary>
        ///     The key used to shouldMask the data
        /// </summary>
        public readonly ReadOnlyMemory<byte> MaskingKey;

        /// <summary>
        ///     The payload data
        /// </summary>
        public readonly ReadOnlyMemory<byte> Data;

        /// <summary>
        ///     The length of the payloads data
        /// </summary>
        public int PayloadLength => Headers.PayloadLength;

        /// <summary>
        ///     Decode the payload as a string
        /// </summary>
        /// <returns>The decoded payload</returns>
        public string DecodedData => Data.GetString();

        internal WebSocketFrame(bool isFinal, bool rsv1, bool rsv2, bool rsv3,
            OpCode opCode, bool shouldMask, ReadOnlyMemory<byte> maskingKey = default,
            int payloadLength = 0, ReadOnlyMemory<byte> data = default)
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
            Data = data;
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="headers">The <c>FrameHeaders</c> for this <c>Frame</c></param>
        /// <param name="maskingKey">The data masking key</param>
        /// <param name="data">The frame payload</param>
        internal WebSocketFrame(FrameHeaders headers, ReadOnlyMemory<byte> maskingKey, ReadOnlyMemory<byte> data)
        {
            Headers = headers;
            MaskingKey = maskingKey;
            Data = data;
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
        internal static WebSocketFrame CreateInitiationFrame(MessageType type, ReadOnlyMemory<byte> payload, bool isFinal = true, bool shouldMask = true) => type switch
        {
            MessageType.Binary => CreateBinary(payload: payload, shouldMask: shouldMask, isFinal: isFinal),
            MessageType.Text => CreateText(payload: payload.GetString(), shouldMask: shouldMask, isFinal: isFinal),
            _ => throw new ($"MessageType {type} is not a valid data frame type")
        };

        /// <summary>
        /// Create a text frame
        /// </summary>
        /// <param name="payload">The frame payload</param>
        /// <param name="shouldMask">True if the frame payload should be masked</param>
        /// <param name="isFinal">True if the frame is the last frame of the message</param>
        /// <returns>An instance of a Frame</returns>
        internal static WebSocketFrame CreateText(string payload,
            bool shouldMask = false,
            bool isFinal = true)
        {
            var bytes = payload.GetBytes();
            return new(
                isFinal: isFinal,
                rsv1: false,
                rsv2: false,
                rsv3: false,
                opCode: OpCode.Text,
                shouldMask: shouldMask,
                payloadLength: bytes.Length,
                maskingKey: CreateMaskingKey(),
                data: bytes);
        }

        /// <summary>
        ///     Create a binary frame
        /// </summary>
        /// <param name="payload">The frame payload</param>
        /// <param name="shouldMask"></param>
        /// <param name="isFinal"></param>
        /// <returns>A Binary Websocket Frame</returns>
        internal static WebSocketFrame CreateBinary(ReadOnlyMemory<byte> payload,
            bool shouldMask = false,
            bool isFinal = true) => new(
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
        /// <param name="shouldMask"></param>
        /// <param name="isFinal"></param>
        /// <returns>A Binary Websocket Frame</returns>
        internal static WebSocketFrame CreateContinuation(ReadOnlyMemory<byte> payload,
            bool shouldMask = false,
            bool isFinal = false) => new(
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
        internal static WebSocketFrame CreatePing() => new(
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
        internal static WebSocketFrame CreatePong() => new(
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
        internal static WebSocketFrame CreateClose() => new(
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
        internal static WebSocketFrame CreateClose(CloseStatusCode closeCode) => new(
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
        ///     Attempt to parse a <c>WebSocketFrame</c> directly out of a <c>ReadOnlySequence&lt;byte&gt;</c>
        ///     without allocating or copying, unless the frame straddles more than one of the
        ///     sequence's underlying segments (rare — only near a Pipe buffer boundary), in which
        ///     case just that frame's bytes are copied into a small contiguous buffer.
        /// </summary>
        /// <param name="buffer">
        ///     The byte sequence to parse from (typically a <c>PipeReader</c>'s read buffer). On a
        ///     successful parse this is advanced past the consumed frame; on failure it is left
        ///     untouched so the caller can wait for more data and try again.
        /// </param>
        /// <param name="frame">The parsed frame</param>
        /// <returns><c>true</c> if a complete frame was parsed</returns>
        internal static bool TryParse(ref ReadOnlySequence<byte> buffer, out WebSocketFrame frame)
        {
            frame = default;
            var reader = new SequenceReader<byte>(buffer);

            if (!FrameHeaders.TryParse(ref reader, out var headers))
            {
                return false;
            }

            var maskingKey = ReadOnlyMemory<byte>.Empty;
            if (headers.ShouldMask && !TryReadContiguous(ref reader, 4, out maskingKey))
            {
                return false;
            }

            var data = ReadOnlyMemory<byte>.Empty;
            if (headers.PayloadLength > 0)
            {
                if (!TryReadContiguous(ref reader, headers.PayloadLength, out data))
                {
                    return false;
                }

                if (headers.ShouldMask)
                {
                    Mask(AsMutableSpan(data), maskingKey.Span);
                }
            }

            frame = new WebSocketFrame(headers, maskingKey, data);
            buffer = buffer.Slice(reader.Position);
            return true;
        }

        /// <summary>
        ///     Encode this frame as bytes ready to be written to a socket
        /// </summary>
        /// <returns>The packed websocket frame bytes</returns>
        internal byte[] Pack()
        {
            Span<byte> headerBuffer = stackalloc byte[FrameHeaders.MaxSize];
            var headerLength = Headers.WriteTo(headerBuffer);

            var maskLength = Headers.ShouldMask ? MaskingKey.Length : 0;
            var packed = new byte[headerLength + maskLength + Data.Length];

            headerBuffer.Slice(0, headerLength).CopyTo(packed);

            var payloadDestination = packed.AsSpan(headerLength + maskLength);
            Data.Span.CopyTo(payloadDestination);

            if (Headers.ShouldMask)
            {
                MaskingKey.Span.CopyTo(packed.AsSpan(headerLength, maskLength));
                Mask(payloadDestination, MaskingKey.Span);
            }
            else if (Headers.OpCode == OpCode.Close)
            {
                // Close status codes are BigEndian on the wire
                EnsureBigEndian(payloadDestination);
            }

            return packed;
        }
        #endregion

        /// <summary>
        ///     Read exactly <paramref name="length"/> bytes off the reader as one contiguous
        ///     <c>ReadOnlyMemory&lt;byte&gt;</c>. Zero-copy when those bytes already sit in a
        ///     single segment of the underlying sequence; otherwise copies just that span into a
        ///     freshly allocated buffer.
        /// </summary>
        private static bool TryReadContiguous(ref SequenceReader<byte> reader, int length, out ReadOnlyMemory<byte> memory)
        {
            if (reader.Remaining < length)
            {
                memory = default;
                return false;
            }

            var slice = reader.Sequence.Slice(reader.Position, length);
            reader.Advance(length);

            if (slice.IsSingleSegment)
            {
                memory = slice.First;
                return true;
            }

            var copy = new byte[length];
            slice.CopyTo(copy);
            memory = copy;
            return true;
        }

        /// <summary>
        ///     Reclaim mutable access to memory that is known to actually be writable (a slice of
        ///     a Pipe's internal buffer, or a buffer we just allocated ourselves) but is only
        ///     exposed to us as a <c>ReadOnlyMemory&lt;byte&gt;</c>.
        /// </summary>
        private static Span<byte> AsMutableSpan(ReadOnlyMemory<byte> memory) => MemoryMarshal.AsMemory(memory).Span;

        private static void Mask(Span<byte> data, ReadOnlySpan<byte> maskingKey)
        {
            for (var i = 0; i < data.Length; i++)
            {
                data[i] ^= maskingKey[i % 4];
            }
        }

        private static byte[] CreateMaskingKey()
        {
            var maskingKey = new byte[4];
            new Random().NextBytes(maskingKey);
            return maskingKey;
        }

        private static void EnsureBigEndian(Span<byte> bytes)
        {
            if (BitConverter.IsLittleEndian)
            {
                bytes.Reverse();
            }
        }

        /// <inheritdoc/>
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
