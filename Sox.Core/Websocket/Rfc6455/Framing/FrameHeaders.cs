
using System;
using System.IO;
using System.Threading.Tasks;
using Sox.Core.Extensions;

namespace Sox.Core.Websocket.Rfc6455.Framing
{
    /// <summary>
    ///     Websocket frame headers
    /// </summary>
    public struct FrameHeaders
    {
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
        /// Unpack the <c>Frame headers from bytes</c>
        /// </summary>
        /// <param name="stream">The byte input stream</param>
        /// <returns>A unpacked <c>FrameHeaders</c> instance</returns>
        public static async Task<FrameHeaders> Unpack(Stream stream)
        {
            var bytes = await stream.ReadBytesAsync(2);
            var payloadLength = (bytes[1] & 0x7F);

            if (payloadLength == 126)
            {
                var lenBytes = await stream.ReadBytesAsync(2);
                EnsureBigEndian(lenBytes);
                payloadLength = BitConverter.ToUInt16(lenBytes, 0);
            }
            else if (payloadLength == 127)
            {
                var lenBytes = await stream.ReadBytesAsync(8);
                EnsureBigEndian(lenBytes);
                var length = BitConverter.ToUInt64(lenBytes, 0);
                if (length > Int32.MaxValue)
                {
                    throw new OverflowException("Frame payload length cannot exceed maximum supported Array dimensions");
                }
                payloadLength = (int)length;
            }

            return new FrameHeaders(
                isFinal: (bytes[0] & 0x80) >> 7 == 1,
                rsv1: (bytes[0] & 0x40) >> 6 == 1,
                rsv2: (bytes[0] & 0x20) >> 5 == 1,
                rsv3: (bytes[0] & 0x10) >> 4 == 1,
                opCode: (OpCode)(bytes[0] & 0xF),
                shouldMask: (bytes[1] & 0x80) >> 7 == 1,
                payloadLength: payloadLength);
        }

        private static void EnsureBigEndian(byte[] bytes)
        {
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
        }

        public async Task<byte[]> PackAsync()
        {
            using var stream = new MemoryStream { Position = 0 };

            // First byte values
            var finalMask = IsFinal ? 0x80 : 0x0;
            var rsv1Mask = Rsv1 ? 0x40 : 0x0;
            var rsv2Mask = Rsv2 ? 0x20 : 0x0;
            var rsv3Mask = Rsv3 ? 0x10 : 0x0;
            var opCode = (int)OpCode;

            // Second byte values
            var mask = ShouldMask ? 0x80 : 0x0;
            stream.WriteByte((byte)(finalMask | rsv1Mask | rsv2Mask | rsv3Mask | opCode));

            // Pack the shouldMask and payload length (1bit+7bit | 1bit+7bit+16bit | 1bit+7bit+64bit)
            if (PayloadLength < 126)
            {
                stream.WriteByte((byte)(mask | (ushort)PayloadLength));
            }
            else if (PayloadLength >= 126 && PayloadLength <= ushort.MaxValue)
            {
                stream.WriteByte((byte)(mask | 126));
                var lengthBytes = BitConverter.GetBytes((ushort)PayloadLength);
                EnsureBigEndian(lengthBytes);
                await stream.WriteBytesAsync(lengthBytes);
            }
            else if (PayloadLength > ushort.MaxValue)
            {
                stream.WriteByte((byte)(mask | 127));
                var lengthBytes = BitConverter.GetBytes((ulong)PayloadLength);
                EnsureBigEndian(lengthBytes);
                await stream.WriteBytesAsync(lengthBytes);
            }

            await stream.FlushAsync();

            return stream.ToArray();
        }
    }
}
