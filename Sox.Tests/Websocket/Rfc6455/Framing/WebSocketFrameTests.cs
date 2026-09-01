using NUnit.Framework;
using Sox.Websocket.Rfc6455;
using Sox.Websocket.Rfc6455.Framing;
using System;
using System.Buffers;
using System.Linq;
using System.Text;

namespace Sox.Tests.Websocket.Rfc6455.Framing
{
    [TestFixture]
    public class WebSocketFrameTests
    {
        private static WebSocketFrame Unpack(byte[] bytes)
        {
            var buffer = new ReadOnlySequence<byte>(bytes);
            Assert.IsTrue(WebSocketFrame.TryParse(ref buffer, out var frame));
            return frame;
        }

        [Test]
        public void Pack_Sets_Correct_Bit_For_Final_Flag_False()
        {
            // Arrange
            var frame = WebSocketFrame.CreateText("hello", isFinal: false);

            // Act
            var packed = frame.Pack();

            // Assert
            Assert.AreEqual(0, (packed[0] & 0x80) >> 7); // 10000000 mask
        }

        [Test]
        public void Pack_Sets_Correct_Bit_For_Final_Flag_True()
        {
            // Arrange
            var frame = WebSocketFrame.CreateText("hello", isFinal: true);

            // Act
            var packed = frame.Pack();

            // Assert
            Assert.AreEqual(1, (packed[0] & 0x80) >> 7); // 10000000 mask
        }

        [Test]
        public void Pack_Sets_Correct_Bit_For_Reserve1_Flag()
        {
            // Arrange
            var frame = WebSocketFrame.CreateText("hello");

            // Act
            var packed = frame.Pack();

            // Assert
            Assert.AreEqual(0, (packed[0] & 0x40) >> 6); // 01000000 mask
        }

        [Test]
        public void Pack_Sets_Correct_Bit_For_Reserve2_Flag()
        {
            // Arrange
            var frame = WebSocketFrame.CreateText("hello");

            // Act
            var packed = frame.Pack();

            // Assert
            Assert.AreEqual(0, (packed[0] & 0x20) >> 5); // 00100000 mask
        }

        [Test]
        public void Pack_Sets_Correct_Bit_For_Reserve3_Flag()
        {
            // Arrange
            var frame = WebSocketFrame.CreateText("hello");

            // Act
            var packed = frame.Pack();

            // Assert
            Assert.AreEqual(0, (packed[0] & 0x10) >> 4); // 00010000 mask
        }

        [TestCase(8952)]
        [TestCase(568)]
        [TestCase(65535)]
        public void Pack_Sets_Correct_Bits_For_Length_Larger_Than_125_Less_Than_16BitMax(int length)
        {
            // Arrange
            var frame = WebSocketFrame.CreateBinary(new byte[length]).Pack();
            var frameLengthBytes = frame.Skip(2).Take(2).ToArray();

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(frameLengthBytes);
            }

            // Assert
            Assert.Multiple(() =>
            {
                Assert.AreEqual(126, frame[1] & 0x7F); // 01111111 mask
                Assert.AreEqual(length, BitConverter.ToUInt16(frameLengthBytes, 0));
            });
        }

        [TestCase(65536)]
        [TestCase(120546)]
        [TestCase(85475)]
        public void Pack_Sets_Correct_Bits_For_Length_Larger_Than_16BitMax(int length)
        {
            // Arrange
            var frame = WebSocketFrame.CreateBinary(new byte[length]).Pack();
            var frameLengthBytes = frame.Skip(2).Take(8).ToArray();

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(frameLengthBytes);
            }

            // Assert
            Assert.Multiple(() =>
            {
                Assert.AreEqual(127, frame[1] & 0x7F); // 01111111 mask
                Assert.AreEqual(length, BitConverter.ToUInt64(frameLengthBytes, 0));
            });
        }

        [TestCase(12)]
        [TestCase(125)]
        [TestCase(67)]
        public void Pack_Sets_Correct_Bits_For_Length_Less_Than_Or_Equal_125(int length)
        {
            // Arrange
            var frame = WebSocketFrame.CreateBinary(new byte[length]).Pack();

            // Assert
            Assert.AreEqual(length, frame[1] & 0x7F); // 01111111 mask
        }

        [Test]
        public void Pack_Sets_Correct_Bits_For_OpCode_Binary()
        {
            // Arrange
            var frame = WebSocketFrame.CreateBinary(new byte[0]);

            // Act
            var packed = frame.Pack();

            // Assert
            Assert.AreEqual(OpCode.Binary, (OpCode)(packed[0] & 0xF)); // 00001111 mask
        }

        [Test]
        public void Pack_Sets_Correct_Bits_For_OpCode_Close()
        {
            // Arrange
            var frame = WebSocketFrame.CreateClose();

            // Act
            var packed = frame.Pack();

            // Assert
            Assert.AreEqual(OpCode.Close, (OpCode)(packed[0] & 0xF)); // 00001111 mask
        }

        [Test]
        public void Pack_Sets_Correct_Bits_For_OpCode_Close_With_StatusCode()
        {
            // Arrange
            var frame = WebSocketFrame.CreateClose(CloseStatusCode.Normal);

            // Act
            var packed = frame.Pack();
            var unpacked = Unpack(packed);
            var data = unpacked.Data.ToArray();

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(data);
            }

            // Assert
            Assert.Multiple(() =>
            {
                Assert.AreEqual(OpCode.Close, (OpCode)(packed[0] & 0xF)); // 00001111 mask
                Assert.AreEqual(CloseStatusCode.Normal, (CloseStatusCode)BitConverter.ToUInt16(data, 0));
            });
        }

        [Test]
        public void Pack_Sets_Correct_Bits_For_OpCode_Continuation()
        {
            // Arrange
            var frame = WebSocketFrame.CreateContinuation(new byte[0]);

            // Act
            var packed = frame.Pack();

            // Assert
            Assert.AreEqual(OpCode.Continuation, (OpCode)(packed[0] & 0xF)); // 00001111 mask
        }

        [Test]
        public void Pack_Sets_Correct_Bits_For_OpCode_Ping()
        {
            // Arrange
            var frame = WebSocketFrame.CreatePing();

            // Act
            var packed = frame.Pack();

            // Assert
            Assert.AreEqual(OpCode.Ping, (OpCode)(packed[0] & 0xF)); // 00001111 mask
        }

        [Test]
        public void Pack_Sets_Correct_Bits_For_OpCode_Pong()
        {
            // Arrange
            var frame = WebSocketFrame.CreatePong();

            // Act
            var packed = frame.Pack();

            // Assert
            Assert.AreEqual(OpCode.Pong, (OpCode)(packed[0] & 0xF)); // 00001111 mask
        }

        [Test]
        public void Pack_Sets_Correct_Bits_For_OpCode_Text()
        {
            // Arrange
            var frame = WebSocketFrame.CreateText("hello");

            // Act
            var packed = frame.Pack();

            // Assert
            Assert.AreEqual(OpCode.Text, (OpCode)(packed[0] & 0xF)); // 00001111 mask
        }

        [Test]
        public void Pack_Sets_Correct_Bits_For_ShouldMask_False()
        {
            // Arrange
            var frame = WebSocketFrame.CreateText("hello", shouldMask: false);

            // Act
            var packed = frame.Pack();

            // Assert
            Assert.AreEqual(0, (packed[1] & 0x80) >> 7); // 10000000 mask
        }

        [Test]
        public void Pack_Sets_Correct_Bits_For_ShouldMask_True()
        {
            // Arrange
            var frame = WebSocketFrame.CreateText("hello", shouldMask: true);

            // Act
            var packed = frame.Pack();

            // Assert
            Assert.AreEqual(1, (packed[1] & 0x80) >> 7); // 10000000 mask
        }

        [Test]
        public void Packs_Data_Correctly_Masked()
        {
            // Arrange
            var payload = "Hello World";
            var frame = WebSocketFrame.CreateText(payload, shouldMask: true);

            var packed = frame.Pack();

            // Assert correct length
            // 2 bytes - headers
            // 4 bytes - masking key
            // 11 bytes - payload
            Assert.AreEqual(17, packed.Length);

            var payloadBytes = packed.Skip(6).Take(payload.Length).ToArray();

            var data = frame.Data.Span;
            var maskingKey = frame.MaskingKey.Span;
            var decrypted = new byte[data.Length];
            for (var i = 0; i < data.Length; i++)
            {
                decrypted[i] = (byte)(data[i] ^ maskingKey[i % 4]);
            }

            // Assert
            Assert.AreEqual(decrypted, payloadBytes);
        }

        [Test]
        public void Packs_Data_Correctly_Unmasked()
        {
            // Arrange
            var payload = "Hello World";
            var frame = WebSocketFrame.CreateText(payload, shouldMask: false).Pack();

            // Assert correct length
            // 2 bytes - headers
            // 11 bytes - payload
            Assert.AreEqual(13, frame.Length);

            var payloadBytes = frame.Skip(2).Take(payload.Length).ToArray();

            // Assert
            Assert.AreEqual(payload, Encoding.UTF8.GetString(payloadBytes, 0, payload.Length));
        }

        [Test]
        public void Unpack_Reads_Correct_Bit_For_Final_Flag_False()
        {
            // Arrange
            var frame = WebSocketFrame.CreateText("hello", isFinal: false);

            // Act
            var unpacked = Unpack(frame.Pack());

            // Assert
            Assert.AreEqual(frame.Headers.IsFinal, unpacked.Headers.IsFinal); // 10000000 mask
        }

        [Test]
        public void Unpack_Reads_Correct_Bit_For_Final_Flag_True()
        {
            // Arrange
            var frame = WebSocketFrame.CreateText("hello", isFinal: true);

            // Act
            var unpacked = Unpack(frame.Pack());

            // Assert
            Assert.AreEqual(frame.Headers.IsFinal, unpacked.Headers.IsFinal); // 10000000 mask
        }

        [Test]
        public void Unpack_Reads_Correct_Bit_For_Reserve1_Flag()
        {
            // Arrange
            var frame = WebSocketFrame.CreateText("hello");

            // Act
            var unpacked = Unpack(frame.Pack());

            // Assert
            Assert.AreEqual(frame.Headers.Rsv1, unpacked.Headers.Rsv1);
        }

        [Test]
        public void Unpack_Reads_Correct_Bit_For_Reserve2_Flag()
        {
            // Arrange
            var frame = WebSocketFrame.CreateText("hello");

            // Act
            var unpacked = Unpack(frame.Pack());

            // Assert
            Assert.AreEqual(frame.Headers.Rsv2, unpacked.Headers.Rsv2);
        }

        [Test]
        public void Unpack_Reads_Correct_Bit_For_Reserve3_Flag()
        {
            // Arrange
            var frame = WebSocketFrame.CreateText("hello");

            // Act
            var unpacked = Unpack(frame.Pack());

            // Assert
            Assert.AreEqual(frame.Headers.Rsv3, unpacked.Headers.Rsv3);
        }

        [TestCase(12)]
        [TestCase(125)]
        [TestCase(67)]
        public void Unpack_Reads_Correct_Bits_For_Length_Less_Than_Or_Equal_125(int length)
        {
            // Arrange
            var frame = WebSocketFrame.CreateBinary(new byte[length]);
            var unpacked = Unpack(frame.Pack());

            // Assert
            Assert.AreEqual(frame.PayloadLength, unpacked.PayloadLength);
        }

        [TestCase(8952)]
        [TestCase(568)]
        [TestCase(65535)]
        public void Unpack_Reads_Correct_Bits_For_Length_Larger_Than_125_Less_Than_16BitMax(int length)
        {
            // Arrange
            var frame = WebSocketFrame.CreateBinary(new byte[length]);
            var unpacked = Unpack(frame.Pack());

            // Assert
            Assert.AreEqual(frame.PayloadLength, unpacked.PayloadLength);
        }

        [TestCase(65536)]
        [TestCase(120546)]
        [TestCase(85475)]
        public void Unpack_Reads_Correct_Bits_For_Length_Larger_Than_16BitMax(int length)
        {
            // Arrange
            var frame = WebSocketFrame.CreateBinary(new byte[length]);
            var unpacked = Unpack(frame.Pack());

            // Assert
            Assert.AreEqual(frame.PayloadLength, unpacked.PayloadLength);
        }

        [Test]
        public void Unpack_Reads_Correct_Bits_For_OpCode_Binary()
        {
            // Arrange
            var frame = WebSocketFrame.CreateBinary(new byte[0]);

            // Act
            var unpacked = Unpack(frame.Pack());

            // Assert
            Assert.AreEqual(OpCode.Binary, unpacked.OpCode); // 00001111 mask
        }

        [Test]
        public void Unpack_Reads_Correct_Bits_For_OpCode_Close()
        {
            // Arrange
            var frame = WebSocketFrame.CreateClose();

            // Act
            var unpacked = Unpack(frame.Pack());

            // Assert
            Assert.AreEqual(OpCode.Close, unpacked.OpCode); // 00001111 mask
        }

        [Test]
        public void Unpack_Reads_Correct_Bits_For_OpCode_Close_With_Reason()
        {
            // Arrange
            var frame = WebSocketFrame.CreateClose(CloseStatusCode.Normal);

            // Act
            var unpacked = Unpack(frame.Pack());
            var data = unpacked.Data.ToArray();

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(data);
            }

            // Assert
            Assert.Multiple(() =>
            {
                Assert.AreEqual(OpCode.Close, unpacked.OpCode); // 00001111 mask
                Assert.AreEqual((ushort)CloseStatusCode.Normal, BitConverter.ToUInt16(data, 0));
            });
        }

        [Test]
        public void Unpack_Reads_Correct_Bits_For_OpCode_Continuation()
        {
            // Arrange
            var frame = WebSocketFrame.CreateContinuation(new byte[0]);

            // Act
            var unpacked = Unpack(frame.Pack());

            // Assert
            Assert.AreEqual(OpCode.Continuation, unpacked.OpCode); // 00001111 mask
        }

        [Test]
        public void Unpack_Reads_Correct_Bits_For_OpCode_Ping()
        {
            // Arrange
            var frame = WebSocketFrame.CreatePing();

            // Act
            var unpacked = Unpack(frame.Pack());

            // Assert
            Assert.AreEqual(OpCode.Ping, unpacked.OpCode); // 00001111 mask
        }

        [Test]
        public void Unpack_Reads_Correct_Bits_For_OpCode_Pong()
        {
            // Arrange
            var frame = WebSocketFrame.CreatePong();

            // Act
            var unpacked = Unpack(frame.Pack());

            // Assert
            Assert.AreEqual(OpCode.Pong, unpacked.OpCode); // 00001111 mask
        }

        [Test]
        public void Unpack_Reads_Correct_Bits_For_OpCode_Text()
        {
            // Arrange
            var frame = WebSocketFrame.CreateText("hello");

            // Act
            var unpacked = Unpack(frame.Pack());

            // Assert
            Assert.AreEqual(OpCode.Text, unpacked.OpCode); // 00001111 mask
        }

        [Test]
        public void Unpack_Reads_Correct_Bits_For_ShouldMask_False()
        {
            // Arrange
            var frame = WebSocketFrame.CreateText("hello", shouldMask: false);

            // Act
            var unpacked = Unpack(frame.Pack());

            // Assert
            Assert.AreEqual(frame.Headers.ShouldMask, unpacked.Headers.ShouldMask);
        }

        [Test]
        public void Unpack_Reads_Correct_Bits_For_ShouldMask_True()
        {
            // Arrange
            var frame = WebSocketFrame.CreateText("hello", shouldMask: true);

            // Act
            var unpacked = Unpack(frame.Pack());

            // Assert
            Assert.AreEqual(frame.Headers.ShouldMask, unpacked.Headers.ShouldMask);
        }

        [Test]
        public void Unpack_Reads_Correctly_Masked()
        {
            // Arrange
            var payload = "Hello World";
            var frame = WebSocketFrame.CreateText(payload, shouldMask: true);

            var unpacked = Unpack(frame.Pack());

            // Assert
            Assert.AreEqual(frame.Data.ToArray(), unpacked.Data.ToArray());
        }

        [Test]
        public void Unpack_Reads_Correctly_Unmasked()
        {
            // Arrange
            var payload = "Hello World";
            var frame = WebSocketFrame.CreateText(payload, shouldMask: false);

            var unpacked = Unpack(frame.Pack());

            // Assert
            Assert.AreEqual(frame.Data.ToArray(), unpacked.Data.ToArray());
        }

        [Test]
        public void TryParse_Returns_False_And_Leaves_Buffer_Untouched_When_Incomplete()
        {
            // Arrange - one byte short of a complete frame
            var packed = WebSocketFrame.CreateText("hello").Pack();
            var incomplete = new ReadOnlySequence<byte>(packed.Take(packed.Length - 1).ToArray());
            var beforeStart = incomplete.Start;
            var beforeEnd = incomplete.End;

            // Act
            var parsed = WebSocketFrame.TryParse(ref incomplete, out _);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.IsFalse(parsed);
                Assert.AreEqual(beforeStart, incomplete.Start);
                Assert.AreEqual(beforeEnd, incomplete.End);
            });
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(6)]
        [TestCase(16)]
        public void TryParse_Handles_A_Frame_Split_Across_Multiple_Sequence_Segments(int splitAt)
        {
            // Arrange
            var payload = "Hello World";
            var packed = WebSocketFrame.CreateText(payload, shouldMask: true).Pack();
            var buffer = ToMultiSegmentSequence(packed, splitAt);

            // Act
            var parsed = WebSocketFrame.TryParse(ref buffer, out var frame);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.IsTrue(parsed);
                Assert.AreEqual(OpCode.Text, frame.OpCode);
                Assert.AreEqual(payload, frame.DecodedData);
            });
        }

        private static ReadOnlySequence<byte> ToMultiSegmentSequence(byte[] bytes, int splitAt)
        {
            var first = new BufferSegment(bytes.AsMemory(0, splitAt));
            var last = first.Append(bytes.AsMemory(splitAt));
            return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
        }

        private sealed class BufferSegment : ReadOnlySequenceSegment<byte>
        {
            public BufferSegment(ReadOnlyMemory<byte> memory)
            {
                Memory = memory;
            }

            public BufferSegment Append(ReadOnlyMemory<byte> memory)
            {
                var segment = new BufferSegment(memory)
                {
                    RunningIndex = RunningIndex + Memory.Length
                };
                Next = segment;
                return segment;
            }
        }
    }
}
