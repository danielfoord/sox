
using System;
using System.Buffers;
using System.Buffers.Binary;

namespace Sox.Websocket.Rfc6455.Framing
{
    /// <summary>
    ///     Websocket frame headers
    /// </summary>
    public struct FrameHeaders
    {
        /// <summary>
        ///     The maximum amount of bytes a packed <c>FrameHeaders</c> can occupy on the wire
        ///     (1 byte + 1 byte + up to 8 bytes of extended payload length)
        /// </summary>
        internal const int MaxSize = 10;

        /// <summary>
        ///     Frame opcode
        /// </summary>
        public OpCode OpCode;

        /// <summary>
        ///     Reserve bit 1 for extensions
        /// </summary>
        public readonly bool Rsv1;

        /// <summary>
        ///     Reserve bit 2 for extensions
        /// </summary>
        public readonly bool Rsv2;

        /// <summary>
        ///     Reserve bit 3 for extensions
        /// </summary>
        public readonly bool Rsv3;

        /// <summary>
        ///     Is this the Final frame. Defaulted to true unless modified by WithIsFinal
        /// </summary>
        public readonly bool IsFinal;

        /// <summary>
        ///     Is the frame masked. Defaulted to true unless modified by WithShouldMask
        /// </summary>
        public readonly bool ShouldMask;

        /// <summary>
        ///     The length of the payloads data
        /// </summary>
        public int PayloadLength;

        /// <summary>
        /// Contstructor
        /// </summary>
        /// <param name="isFinal">Is the frame the last frame of the message</param>
        /// <param name="rsv1">First reserved bit</param>
        /// <param name="rsv2">Second reserved bit</param>
        /// <param name="rsv3">Thrid reserved bit</param>
        /// <param name="opCode">The type of Frame</param>
        /// <param name="shouldMask">Should the Frame payload be masked</param>
        /// <param name="payloadLength">The length of the payload</param>
        public FrameHeaders(bool isFinal, bool rsv1, bool rsv2, bool rsv3,
            OpCode opCode, bool shouldMask, int payloadLength)
        {
            IsFinal = isFinal;
            Rsv1 = rsv1;
            Rsv2 = rsv2;
            Rsv3 = rsv3;
            OpCode = opCode;
            ShouldMask = shouldMask;
            PayloadLength = payloadLength;
        }

        /// <summary>
        /// Attempt to parse <c>FrameHeaders</c> from a <c>SequenceReader&lt;byte&gt;</c> without
        /// allocating. If the reader does not yet contain a full set of headers, the reader is
        /// rewound to its original position and <c>false</c> is returned so the caller can wait
        /// for more data.
        /// </summary>
        /// <param name="reader">The byte sequence reader, advanced past the headers on success</param>
        /// <param name="headers">The parsed headers</param>
        /// <returns><c>true</c> if a complete set of headers was parsed</returns>
        internal static bool TryParse(ref SequenceReader<byte> reader, out FrameHeaders headers)
        {
            var checkpoint = reader;
            headers = default;

            if (!reader.TryRead(out var byte0) || !reader.TryRead(out var byte1))
            {
                reader = checkpoint;
                return false;
            }

            var payloadLength = byte1 & 0x7F;

            if (payloadLength == 126)
            {
                if (!reader.TryReadBigEndian(out short len16))
                {
                    reader = checkpoint;
                    return false;
                }
                payloadLength = unchecked((ushort)len16);
            }
            else if (payloadLength == 127)
            {
                if (!reader.TryReadBigEndian(out long len64))
                {
                    reader = checkpoint;
                    return false;
                }
                var length = unchecked((ulong)len64);
                if (length > int.MaxValue)
                {
                    throw new OverflowException("Frame payload length cannot exceed maximum supported Array dimensions");
                }
                payloadLength = (int)length;
            }

            headers = new FrameHeaders(
                isFinal: (byte0 & 0x80) >> 7 == 1,
                rsv1: (byte0 & 0x40) >> 6 == 1,
                rsv2: (byte0 & 0x20) >> 5 == 1,
                rsv3: (byte0 & 0x10) >> 4 == 1,
                opCode: (OpCode)(byte0 & 0xF),
                shouldMask: (byte1 & 0x80) >> 7 == 1,
                payloadLength: payloadLength);

            return true;
        }

        /// <summary>
        /// Pack the frame headers into bytes, writing directly into the supplied buffer
        /// </summary>
        /// <param name="destination">
        ///     The buffer to write into. Must be at least <see cref="MaxSize"/> bytes long
        /// </param>
        /// <returns>The amount of bytes written</returns>
        internal int WriteTo(Span<byte> destination)
        {
            // First byte values
            var finalMask = IsFinal ? 0x80 : 0x0;
            var rsv1Mask = Rsv1 ? 0x40 : 0x0;
            var rsv2Mask = Rsv2 ? 0x20 : 0x0;
            var rsv3Mask = Rsv3 ? 0x10 : 0x0;
            var opCode = (int)OpCode;

            // Second byte values
            var mask = ShouldMask ? 0x80 : 0x0;

            destination[0] = (byte)(finalMask | rsv1Mask | rsv2Mask | rsv3Mask | opCode);

            // Pack the shouldMask and payload length (1bit+7bit | 1bit+7bit+16bit | 1bit+7bit+64bit)
            if (PayloadLength < 126)
            {
                destination[1] = (byte)(mask | PayloadLength);
                return 2;
            }

            if (PayloadLength <= ushort.MaxValue)
            {
                destination[1] = (byte)(mask | 126);
                BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(2, 2), (ushort)PayloadLength);
                return 4;
            }

            destination[1] = (byte)(mask | 127);
            BinaryPrimitives.WriteUInt64BigEndian(destination.Slice(2, 8), (ulong)PayloadLength);
            return 10;
        }
    }
}
