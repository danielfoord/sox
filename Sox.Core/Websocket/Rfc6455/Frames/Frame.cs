using System;
using System.Collections.Generic;
using System.IO;
using Sox.Core.Extensions;

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

namespace Sox.Core.Websocket.Rfc6455.Frames
{
    public class Frame
    {
        /// <summary>
        ///     The payload data
        /// </summary>
        public readonly byte[] Data;

        /// <summary>
        ///     The key used to shouldMask the data
        /// </summary>
        public readonly byte[] MaskingKey;

        /// <summary>
        ///     Frame opcode
        /// </summary>
        public readonly OpCode OpCode;

        /// <summary>
        ///     The length of the payloads data
        /// </summary>
        public readonly uint PayloadLength;

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

        public Frame(bool isFinal, bool rsv1, bool rsv2, bool rsv3,
            OpCode opCode, bool shouldMask, uint payloadLength, byte[] maskingKey, byte[] data)
        {
            IsFinal = isFinal;
            Rsv1 = rsv1;
            Rsv2 = rsv2;
            Rsv3 = rsv3;
            OpCode = opCode;
            ShouldMask = shouldMask;
            PayloadLength = payloadLength;
            MaskingKey = maskingKey;
            Data = data;
        }

        /// <summary>
        ///     Is this the Final frame. Defaulted to true unless modified by WithIsFinal
        /// </summary>
        public bool IsFinal { get; private set; }

        /// <summary>
        ///     Is the frame masked. Defaulted to true unless modified by WithShouldMask
        /// </summary>
        public bool ShouldMask { get; private set; }

        /// <summary>
        ///     Decode the payload as a string
        /// </summary>
        /// <returns>The decoded payload</returns>
        public string DataAsString() => Data.GetString();

        /// <summary>
        ///     Create a text frame
        /// </summary>
        /// <param name="payload">The frame payload</param>
        /// <returns>A Text Websocket Frame</returns>
        public static Frame CreateText(string payload)
        {
            return new Frame(true,
                false,
                false,
                false,
                OpCode.Text,
                true,
                (uint) payload.Length,
                CreateMaskingKey(),
                payload.GetBytes());
        }

        /// <summary>
        ///     Create a binary frame
        /// </summary>
        /// <param name="payload">The frame payload</param>
        /// <returns>A Binary Websocket Frame</returns>
        public static Frame CreateBinary(byte[] payload)
        {
            return new Frame(true,
                false,
                false,
                false,
                OpCode.Binary,
                true,
                (uint) payload.Length,
                CreateMaskingKey(),
                payload);
        }

        /// <summary>
        ///     Create a continuation frame
        /// </summary>
        /// <param name="payload">The frame payload</param>
        /// <returns>A Binary Websocket Frame</returns>
        public static Frame CreateContinuation(byte[] payload)
        {
            return new Frame(true,
                false,
                false,
                false,
                OpCode.Continuation,
                true,
                (uint) payload.Length,
                CreateMaskingKey(),
                payload);
        }

        /// <summary>
        ///     Create a ping frame
        /// </summary>
        /// <returns>A Ping Websocket Frame</returns>
        public static Frame CreatePing()
        {
            return new Frame(true,
                false,
                false,
                false,
                OpCode.Ping,
                false,
                0,
                CreateMaskingKey(),
                string.Empty.GetBytes());
        }

        /// <summary>
        ///     Create a ping frame
        /// </summary>
        /// <returns>A Ping Websocket Frame</returns>
        public static Frame CreatePing(byte[] data)
        {
            return new Frame(true,
                false,
                false,
                false,
                OpCode.Ping,
                false,
                (uint) data.Length,
                CreateMaskingKey(),
                data);
        }

        /// <summary>
        ///     Create a pong frame
        /// </summary>
        /// <returns>A Ping Websocket Frame</returns>
        public static Frame CreatePong()
        {
            return new Frame(true,
                false,
                false,
                false,
                OpCode.Pong,
                false,
                0,
                CreateMaskingKey(),
                string.Empty.GetBytes());
        }

        /// <summary>
        ///     Create a pong frame
        /// </summary>
        /// <returns>A Ping Websocket Frame</returns>
        public static Frame CreatePong(byte[] data)
        {
            return new Frame(true,
                false,
                false,
                false,
                OpCode.Pong,
                false,
                (uint) data.Length,
                CreateMaskingKey(),
                data);
        }

        /// <summary>
        ///     Create a close frame
        /// </summary>
        /// <returns>A Ping Websocket Frame</returns>
        public static Frame CreateClose()
        {
            return new Frame(true,
                false,
                false,
                false,
                OpCode.Close,
                false,
                0,
                CreateMaskingKey(),
                string.Empty.GetBytes());
        }

        /// <summary>
        ///     Create a close frame
        /// </summary>
        /// <returns>A Ping Websocket Frame</returns>
        public static Frame CreateClose(CloseStatusCode statusCode)
        {
            return new Frame(true,
                false,
                false,
                false,
                OpCode.Close,
                false,
                2,
                CreateMaskingKey(),
                BitConverter.GetBytes((short) statusCode));
        }

        /// <summary>
        ///     Set IsFinal flag for this message frame
        /// </summary>
        /// <param name="isFinal">Indicates whether this is the final frame</param>
        /// <returns>This frame instance</returns>
        public Frame WithIsFinal(bool isFinal)
        {
            IsFinal = isFinal;
            return this;
        }

        /// <summary>
        ///     Set ShouldMask flag for this message frame
        /// </summary>
        /// <param name="shouldMask">Indicates whether this frames payload should be masked</param>
        /// <returns>This frame instance</returns>
        public Frame WithShouldMask(bool shouldMask)
        {
            ShouldMask = shouldMask;
            return this;
        }

        /// <summary>
        ///     Decode a byte array to a <c>Frame</c>
        /// </summary>
        /// <param name="bytes">The byte array to decode</param>
        /// <returns>A WebSocket message frame</returns>
        public static Frame Unpack(byte[] bytes)
        {
            // Unpack Headers - First byte
            var isFin = (bytes[0] & 0x80) >> 7 == 1;
            var rsv1 = (bytes[0] & 0x40) >> 6 == 1;
            var rsv2 = (bytes[0] & 0x20) >> 5 == 1;
            var rsv3 = (bytes[0] & 0x10) >> 4 == 1;
            var opCode = (OpCode) (bytes[0] & 0xF);

            // Second byte
            var shouldMask = (bytes[1] & 0x80) >> 7 == 1;
            var payloadLength = (uint) (bytes[1] & 0x7F);

            using (var ms = new MemoryStream(bytes))
            {
                ms.Position = 2;

                if (payloadLength == 126)
                {
                    var lenBytes = ReadBytes(ms, 2, 2);

                    if (BitConverter.IsLittleEndian)
                    {
                        Array.Reverse(lenBytes);
                    }

                    payloadLength = BitConverter.ToUInt16(lenBytes, 0);
                }
                else if (payloadLength == 127)
                {
                    var lenBytes = ReadBytes(ms, 8, 8);

                    if (BitConverter.IsLittleEndian)
                    {
                        Array.Reverse(lenBytes);
                    }
                
                    payloadLength = BitConverter.ToUInt32(lenBytes, 0);
                }

                byte[] maskingKey = null;
                byte[] data;

                if (shouldMask)
                {
                    maskingKey = ReadBytes(ms, 4, 4);
                    data = Xor(maskingKey, ReadBytes(ms, (int)payloadLength, (int)payloadLength));
                }
                else
                {
                    data = ReadBytes(ms, (int)payloadLength, (int)payloadLength);
                }

                return new Frame(isFin, rsv1, rsv2, rsv3, opCode, shouldMask, payloadLength, maskingKey, data);
            }
        }

        /// <summary>
        /// Try unpack a frame
        /// </summary>
        /// <param name="bytes">The bytes to unpack</param>
        /// <param name="frame">The frame that is output if it unpacked successfully</param>
        /// <returns>True if the frame has been unpacked</returns>
        public static bool TryUnpack(byte[] bytes, out Frame frame)
        {
            try
            {
                frame = Unpack(bytes);
                return true;
            }
            catch (Exception)
            {
                frame = null;
                return false;
            }
        }

        private static byte[] ReadBytes(Stream ms, int bufferSize, int count)
        {
            var buffer = new byte[bufferSize];
            ms.Read(buffer, 0, count);
            return buffer;
        }

        /// <summary>
        ///     Encode this frame as a byte array
        /// </summary>
        /// <returns>A WebSocket message frame bytes</returns>
        public byte[] Pack()
        {
            // First byte values
            var finalMask = IsFinal ? 0x80 : 0x0;
            var rsv1Mask = Rsv1 ? 0x40 : 0x0;
            var rsv2Mask = Rsv2 ? 0x20 : 0x0;
            var rsv3Mask = Rsv3 ? 0x10 : 0x0;
            var opCode = (int) OpCode;

            // Second byte values
            var mask = ShouldMask ? 0x80 : 0x0;

            // Setting the headers length depending on the payload length
            var headerSize = 2;

            if (PayloadLength >= 126 && PayloadLength <= ushort.MaxValue) headerSize += 2;
            else if (PayloadLength > ushort.MaxValue) headerSize += 8;

            // Pack the first byte
            var headers = new byte[headerSize];

            headers[0] = (byte) (finalMask | rsv1Mask | rsv2Mask | rsv3Mask | opCode);

            using (var buffer = new MemoryStream {Position = 0})
            {
                // Pack the shouldMask and payload length (1bit+7bit | 1bit+7bit+16bit | 1bit+7bit+64bit)
                if (PayloadLength < 126)
                {
                    headers[1] = (byte) (mask | (ushort) PayloadLength);
                    buffer.Write(headers, 0, headers.Length);
                }
                else if (PayloadLength >= 126 && PayloadLength <= ushort.MaxValue)
                {
                    headers[1] = (byte) (mask | 126);
                    var length = (ushort) PayloadLength;
                    var lengthBytes = BitConverter.GetBytes(length);
                    if (BitConverter.IsLittleEndian)
                    {
                        Array.Reverse(lengthBytes);
                    }
                    Array.Copy(lengthBytes, 0, headers, 2, 2);
                    buffer.Write(headers, 0, headers.Length);
                }
                else if (PayloadLength > ushort.MaxValue)
                {
                    headers[1] = (byte) (mask | 127);
                    var length = (ulong) PayloadLength;
                    var lengthBytes = BitConverter.GetBytes(length);
                    if (BitConverter.IsLittleEndian)
                    {
                        Array.Reverse(lengthBytes);
                    }
                    Array.Copy(lengthBytes, 0, headers, 2, 8);
                    buffer.Write(headers, 0, headers.Length);
                }

                if (ShouldMask)
                {
                    buffer.Write(MaskingKey, 0, 4);

                    // Mask the payload data with a simple XOR using the masking key
                    var masked = Xor(MaskingKey, Data);
                    buffer.Write(masked, 0, masked.Length);
                }
                else
                {   
                    // If this is a close frame status code, it needs to be BigEndian
                    if (OpCode == OpCode.Close)
                    {
                        if (BitConverter.IsLittleEndian)
                        {
                            Array.Reverse(Data);
                        }
                    }
                    buffer.Write(Data, 0, Data.Length);
                }

                buffer.Flush();

                return buffer.ToArray();
            }
        }

        private static byte[] Xor(IReadOnlyList<byte> maskingKey, IReadOnlyList<byte> data)
        {
            var output = new byte[data.Count];
            for (var i = 0; i < data.Count; i++) output[i] = (byte) (data[i] ^ maskingKey[i % 4]);
            return output;
        }

        private static byte[] CreateMaskingKey()
        {
            var random = new Random();
            var maskingKey = new byte[4];

            for (var i = 0; i < 4; i++) maskingKey[i] = (byte) random.Next(255);

            return maskingKey;
        }

        public override string ToString()
        {
            return $"FIN: {IsFinal} | " +
                   $"RSV1: {Rsv1} | " +
                   $"RSV2: {Rsv2} | " +
                   $"RSV3: {Rsv3} | " +
                   $"Mask: {ShouldMask} | " +
                   $"PayloadLen: {PayloadLength} | " +
                   $"OpCode: {OpCode}";
        }
    }
}