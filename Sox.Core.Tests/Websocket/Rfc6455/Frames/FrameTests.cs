using System;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Sox.Core.Websocket.Rfc6455;
using Sox.Core.Websocket.Rfc6455.Frames;

namespace Sox.Core.Tests.Websocket.Rfc6455.Frames
{
    [TestFixture]
    public class FrameTests
    {
        [Test]
        public void Pack_Sets_Correct_Bit_For_Final_Flag_False()
        {
            // Arrange 
            var frame = Frame
                .CreateText("hello")
                .WithIsFinal(false);

            // Act
            var packed = frame.Pack();

            // Assert
            Assert.AreEqual(0, (packed[0] & 0x80) >> 7); // 10000000 mask
        }

        [Test]
        public void Pack_Sets_Correct_Bit_For_Final_Flag_True()
        {
            // Arrange 
            var frame = Frame
                .CreateText("hello")
                .WithIsFinal(true);

            // Act
            var packed = frame.Pack();

            // Assert
            Assert.AreEqual(1, (packed[0] & 0x80) >> 7); // 10000000 mask
        }

        [Test]
        public void Pack_Sets_Correct_Bit_For_Reserve1_Flag()
        {
            // Arrange 
            var frame = Frame.CreateText("hello");

            // Act
            var packed = frame.Pack();

            // Assert
            Assert.AreEqual(0, (packed[0] & 0x40) >> 6); // 01000000 mask
        }

        [Test]
        public void Pack_Sets_Correct_Bit_For_Reserve2_Flag()
        {
            // Arrange 
            var frame = Frame.CreateText("hello");

            // Act
            var packed = frame.Pack();

            // Assert
            Assert.AreEqual(0, (packed[0] & 0x20) >> 5); // 00100000 mask
        }

        [Test]
        public void Pack_Sets_Correct_Bit_For_Reserve3_Flag()
        {
            // Arrange 
            var frame = Frame.CreateText("hello");

            // Act
            var packed = frame.Pack();

            // Assert
            Assert.AreEqual(0, (packed[0] & 0x10) >> 4); // 00010000 mask
        }

        [Test]
        public void Pack_Sets_Correct_Bits_For_Length_Larger_Than_125_Less_Than_16BitMax()
        {
            // Arrange
            var frame1 = Frame.CreateBinary(new byte[8952]).Pack();
            var frame2 = Frame.CreateBinary(new byte[568]).Pack();
            var frame3 = Frame.CreateBinary(new byte[65535]).Pack();

            var frame1LengthBytes = frame1.Skip(2).Take(2).ToArray();
            var frame2LengthBytes = frame2.Skip(2).Take(2).ToArray();
            var frame3LengthBytes = frame3.Skip(2).Take(2).ToArray();

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(frame1LengthBytes);
                Array.Reverse(frame2LengthBytes);
                Array.Reverse(frame3LengthBytes);
            }

            // Assert 
            Assert.Multiple(() => // 01111111 mask
            {
                Assert.AreEqual(126, frame1[1] & 0x7F);
                Assert.AreEqual(126, frame2[1] & 0x7F);
                Assert.AreEqual(126, frame3[1] & 0x7F);

                Assert.AreEqual(8952, BitConverter.ToUInt16(frame1LengthBytes, 0));
                Assert.AreEqual(568, BitConverter.ToUInt16(frame2LengthBytes, 0));
                Assert.AreEqual(65535, BitConverter.ToUInt16(frame3LengthBytes, 0));
            });
        }

        [Test]
        public void Pack_Sets_Correct_Bits_For_Length_Larger_Than_16BitMax()
        {
            // Arrange
            var frame1 = Frame.CreateBinary(new byte[65536]).Pack();
            var frame2 = Frame.CreateBinary(new byte[120546]).Pack();
            var frame3 = Frame.CreateBinary(new byte[85475]).Pack();

            var frame1LengthBytes = frame1.Skip(2).Take(8).ToArray();
            var frame2LengthBytes = frame2.Skip(2).Take(8).ToArray();
            var frame3LengthBytes = frame3.Skip(2).Take(8).ToArray();

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(frame1LengthBytes);
                Array.Reverse(frame2LengthBytes);
                Array.Reverse(frame3LengthBytes);
            }

            // Assert 
            Assert.Multiple(() => // 01111111 mask
            {
                Assert.AreEqual(127, frame1[1] & 0x7F);
                Assert.AreEqual(127, frame2[1] & 0x7F);
                Assert.AreEqual(127, frame3[1] & 0x7F);

                Assert.AreEqual(65536, BitConverter.ToUInt64(frame1LengthBytes, 0));
                Assert.AreEqual(120546, BitConverter.ToUInt64(frame2LengthBytes, 0));
                Assert.AreEqual(85475, BitConverter.ToUInt64(frame3LengthBytes, 0));
            });
        }

        [Test]
        public void Pack_Sets_Correct_Bits_For_Length_Less_Than_Or_Equal_125()
        {
            // Arrange
            var frame1 = Frame.CreateBinary(new byte[12]).Pack();
            var frame2 = Frame.CreateBinary(new byte[125]).Pack();
            var frame3 = Frame.CreateBinary(new byte[67]).Pack();

            // Assert 
            Assert.Multiple(() => // 01111111 mask
            {
                Assert.AreEqual(12, frame1[1] & 0x7F);
                Assert.AreEqual(125, frame2[1] & 0x7F);
                Assert.AreEqual(67, frame3[1] & 0x7F);
            });
        }

        [Test]
        public void Pack_Sets_Correct_Bits_For_OpCode_Binary()
        {
            // Arrange 
            var frame = Frame.CreateBinary(new byte[0]);

            // Act
            var packed = frame.Pack();

            // Assert
            Assert.AreEqual(OpCode.Binary, (OpCode) (packed[0] & 0xF)); // 00001111 mask
        }

        [Test]
        public void Pack_Sets_Correct_Bits_For_OpCode_Close()
        {
            // Arrange 
            var frame = Frame.CreateClose();

            // Act
            var packed = frame.Pack();

            // Assert
            Assert.AreEqual(OpCode.Close, (OpCode) (packed[0] & 0xF)); // 00001111 mask
        }

        [Test]
        public void Pack_Sets_Correct_Bits_For_OpCode_Close_With_StatusCode()
        {
            // Arrange 
            var frame = Frame.CreateClose(CloseStatusCode.Normal);

            // Act
            var packed = frame.Pack();
            var unpacked = Frame.Unpack(packed);

            var isLittleEndian = BitConverter.IsLittleEndian;
            if (isLittleEndian)
            {
                Array.Reverse(unpacked.Data);
            }

            // Assert
            Assert.AreEqual(OpCode.Close, (OpCode) (packed[0] & 0xF)); // 00001111 mask
            Assert.AreEqual(CloseStatusCode.Normal, (CloseStatusCode) BitConverter.ToUInt16(unpacked.Data, 0));
        }

        [Test]
        public void Pack_Sets_Correct_Bits_For_OpCode_Continuation()
        {
            // Arrange 
            var frame = Frame.CreateContinuation(new byte[0]);

            // Act
            var packed = frame.Pack();

            // Assert
            Assert.AreEqual(OpCode.Continuation, (OpCode) (packed[0] & 0xF)); // 00001111 mask
        }

        [Test]
        public void Pack_Sets_Correct_Bits_For_OpCode_Ping()
        {
            // Arrange 
            var frame = Frame.CreatePing();

            // Act
            var packed = frame.Pack();

            // Assert
            Assert.AreEqual(OpCode.Ping, (OpCode) (packed[0] & 0xF)); // 00001111 mask
        }

        [Test]
        public void Pack_Sets_Correct_Bits_For_OpCode_Pong()
        {
            // Arrange 
            var frame = Frame.CreatePong();

            // Act
            var packed = frame.Pack();

            // Assert
            Assert.AreEqual(OpCode.Pong, (OpCode) (packed[0] & 0xF)); // 00001111 mask
        }

        [Test]
        public void Pack_Sets_Correct_Bits_For_OpCode_Text()
        {
            // Arrange 
            var frame = Frame.CreateText("hello");

            // Act
            var packed = frame.Pack();

            // Assert
            Assert.AreEqual(OpCode.Text, (OpCode) (packed[0] & 0xF)); // 00001111 mask
        }

        [Test]
        public void Pack_Sets_Correct_Bits_For_ShouldMask_False()
        {
            // Arrange
            var frame = Frame
                .CreateText("hello")
                .WithShouldMask(false);

            // Act
            var packed = frame.Pack();

            // Assert
            Assert.AreEqual(0, (packed[1] & 0x80) >> 7); // 10000000 mask
        }

        [Test]
        public void Pack_Sets_Correct_Bits_For_ShouldMask_True()
        {
            // Arrange
            var frame = Frame
                .CreateText("hello")
                .WithShouldMask(true);

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
            var frame = Frame.CreateText(payload)
                .WithShouldMask(true)
                .WithIsFinal(true);

            var packed = frame.Pack();

            // Assert correct length
            // 2 bytes - headers
            // 4 bytes - masking key
            // 11 bytes - payload
            Assert.AreEqual(17, packed.Length);

            var payloadBytes = packed.Skip(6).Take(payload.Length).ToArray();

            var decrypted = new byte[frame.Data.Length];
            for (var i = 0; i < frame.Data.Length; i++) decrypted[i] = (byte) (frame.Data[i] ^ frame.MaskingKey[i % 4]);

            // Assert
            Assert.AreEqual(decrypted, payloadBytes);
        }

        [Test]
        public void Packs_Data_Correctly_Unmasked()
        {
            // Arrange
            var payload = "Hello World";
            var frame = Frame.CreateText(payload)
                .WithShouldMask(false)
                .WithIsFinal(true)
                .Pack();

            // Assert correct length
            // 2 bytes - headers
            // 11 bytes - payload
            Assert.AreEqual(13, frame.Length);

            var payloadBytes = frame.Skip(2).Take(payload.Length).ToArray();

            // Assert
            Assert.AreEqual(payload, Encoding.UTF8.GetString(payloadBytes, 0, payload.Length));
        }

        [Test]
        public void Packs_Data_Correctly_Ping()
        {
            // Arrange
            var payload = "Hello World";
            var frame = Frame.CreatePing(Encoding.UTF8.GetBytes(payload))
                .WithShouldMask(false)
                .WithIsFinal(true)
                .Pack();

            // Assert correct length
            // 2 bytes - headers
            // 11 bytes - payload
            Assert.AreEqual(13, frame.Length);

            var payloadBytes = frame.Skip(2).Take(payload.Length).ToArray();

            // Assert
            Assert.AreEqual(payload, Encoding.UTF8.GetString(payloadBytes, 0, payload.Length));
        }

        [Test]
        public void Packs_Data_Correctly_Pong()
        {
            // Arrange
            var payload = "Hello World";
            var frame = Frame.CreatePong(Encoding.UTF8.GetBytes(payload))
                .WithShouldMask(false)
                .WithIsFinal(true)
                .Pack();

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
            var frame = Frame
                .CreateText("hello")
                .WithIsFinal(false);

            // Act
            var packed = frame.Pack();
            var unpacked = Frame.Unpack(packed);

            // Assert
            Assert.AreEqual(frame.IsFinal, unpacked.IsFinal); // 10000000 mask
        }

        [Test]
        public void Unpack_Reads_Correct_Bit_For_Final_Flag_True()
        {
            // Arrange 
            var frame = Frame
                .CreateText("hello")
                .WithIsFinal(true);

            // Act
            var packed = frame.Pack();
            var unpacked = Frame.Unpack(packed);

            // Assert
            Assert.AreEqual(frame.IsFinal, unpacked.IsFinal); // 10000000 mask
        }

        [Test]
        public void Unpack_Reads_Correct_Bit_For_Reserve1_Flag()
        {
            // Arrange 
            var frame = Frame.CreateText("hello");

            // Act
            var packed = frame.Pack();
            var unpacked = Frame.Unpack(packed);

            // Assert
            Assert.AreEqual(frame.Rsv1, unpacked.Rsv1);
        }

        [Test]
        public void Unpack_Reads_Correct_Bit_For_Reserve2_Flag()
        {
            // Arrange 
            var frame = Frame.CreateText("hello");

            // Act
            var packed = frame.Pack();
            var unpacked = Frame.Unpack(packed);

            // Assert
            Assert.AreEqual(frame.Rsv2, unpacked.Rsv2);
        }

        [Test]
        public void Unpack_Reads_Correct_Bit_For_Reserve3_Flag()
        {
            // Arrange 
            var frame = Frame.CreateText("hello");

            // Act
            var packed = frame.Pack();
            var unpacked = Frame.Unpack(packed);

            // Assert
            Assert.AreEqual(frame.Rsv3, unpacked.Rsv3);
        }

        [Test]
        public void Unpack_Reads_Correct_Bits_For_Length_Larger_Than_125_Less_Than_16BitMax()
        {
            // Arrange
            var frame1 = Frame.CreateBinary(new byte[8952]);
            var frame2 = Frame.CreateBinary(new byte[568]);
            var frame3 = Frame.CreateBinary(new byte[65535]);

            var unpacked1 = Frame.Unpack(frame1.Pack());
            var unpacked2 = Frame.Unpack(frame2.Pack());
            var unpacked3 = Frame.Unpack(frame3.Pack());

            // Assert 
            Assert.Multiple(() =>
            {
                Assert.AreEqual(frame1.PayloadLength, unpacked1.PayloadLength);
                Assert.AreEqual(frame2.PayloadLength, unpacked2.PayloadLength);
                Assert.AreEqual(frame3.PayloadLength, unpacked3.PayloadLength);
            });
        }

        [Test]
        public void Unpack_Reads_Correct_Bits_For_Length_Larger_Than_16BitMax()
        {
            // Arrange
            var frame1 = Frame.CreateBinary(new byte[65536]);
            var frame2 = Frame.CreateBinary(new byte[120546]);
            var frame3 = Frame.CreateBinary(new byte[85475]);

            var unpacked1 = Frame.Unpack(frame1.Pack());
            var unpacked2 = Frame.Unpack(frame2.Pack());
            var unpacked3 = Frame.Unpack(frame3.Pack());

            // Assert 
            Assert.Multiple(() =>
            {
                Assert.AreEqual(frame1.PayloadLength, unpacked1.PayloadLength);
                Assert.AreEqual(frame2.PayloadLength, unpacked2.PayloadLength);
                Assert.AreEqual(frame3.PayloadLength, unpacked3.PayloadLength);
            });
        }

        [Test]
        public void Unpack_Reads_Correct_Bits_For_Length_Less_Than_Or_Equal_125()
        {
            // Arrange
            var frame1 = Frame.CreateBinary(new byte[12]);
            var frame2 = Frame.CreateBinary(new byte[125]);
            var frame3 = Frame.CreateBinary(new byte[67]);

            var unpacked1 = Frame.Unpack(frame1.Pack());
            var unpacked2 = Frame.Unpack(frame2.Pack());
            var unpacked3 = Frame.Unpack(frame3.Pack());

            // Assert 
            Assert.Multiple(() =>
            {
                Assert.AreEqual(frame1.PayloadLength, unpacked1.PayloadLength);
                Assert.AreEqual(frame2.PayloadLength, unpacked2.PayloadLength);
                Assert.AreEqual(frame3.PayloadLength, unpacked3.PayloadLength);
            });
        }

        [Test]
        public void Unpack_Reads_Correct_Bits_For_OpCode_Binary()
        {
            // Arrange 
            var frame = Frame.CreateBinary(new byte[0]);

            // Act
            var packed = frame.Pack();
            var unpacked = Frame.Unpack(packed);

            // Assert
            Assert.AreEqual(OpCode.Binary, unpacked.OpCode); // 00001111 mask
        }

        [Test]
        public void Unpack_Reads_Correct_Bits_For_OpCode_Close()
        {
            // Arrange 
            var frame = Frame.CreateClose();

            // Act
            var packed = frame.Pack();
            var unpacked = Frame.Unpack(packed);

            // Assert
            Assert.AreEqual(OpCode.Close, unpacked.OpCode); // 00001111 mask
        }

        [Test]
        public void Unpack_Reads_Correct_Bits_For_OpCode_Close_With_Reason()
        {
            // Arrange 
            var frame = Frame.CreateClose(CloseStatusCode.Normal);

            // Act
            var packed = frame.Pack();
            var unpacked = Frame.Unpack(packed);

            var isLittleEndian = BitConverter.IsLittleEndian;
            if (isLittleEndian)
            {
                Array.Reverse(unpacked.Data);
            }

            // Assert
            Assert.AreEqual(OpCode.Close, unpacked.OpCode); // 00001111 mask
            Assert.AreEqual((ushort) CloseStatusCode.Normal, BitConverter.ToUInt16(unpacked.Data, 0));
        }

        [Test]
        public void Unpack_Reads_Correct_Bits_For_OpCode_Continuation()
        {
            // Arrange 
            var frame = Frame.CreateContinuation(new byte[0]);

            // Act
            var packed = frame.Pack();
            var unpacked = Frame.Unpack(packed);

            // Assert
            Assert.AreEqual(OpCode.Continuation, unpacked.OpCode); // 00001111 mask
        }

        [Test]
        public void Unpack_Reads_Correct_Bits_For_OpCode_Ping()
        {
            // Arrange 
            var frame = Frame.CreatePing();

            // Act
            var packed = frame.Pack();
            var unpacked = Frame.Unpack(packed);

            // Assert
            Assert.AreEqual(OpCode.Ping, unpacked.OpCode); // 00001111 mask
        }

        [Test]
        public void Unpack_Reads_Correct_Bits_For_OpCode_Pong()
        {
            // Arrange 
            var frame = Frame.CreatePong();

            // Act
            var packed = frame.Pack();
            var unpacked = Frame.Unpack(packed);

            // Assert
            Assert.AreEqual(OpCode.Pong, unpacked.OpCode); // 00001111 mask
        }

        [Test]
        public void Unpack_Reads_Correct_Bits_For_OpCode_Text()
        {
            // Arrange 
            var frame = Frame.CreateText("hello");

            // Act
            var packed = frame.Pack();
            var unpacked = Frame.Unpack(packed);

            // Assert
            Assert.AreEqual(OpCode.Text, unpacked.OpCode); // 00001111 mask
        }

        [Test]
        public void Unpack_Reads_Correct_Bits_For_ShouldMask_False()
        {
            // Arrange
            var frame = Frame
                .CreateText("hello")
                .WithShouldMask(false);

            // Act
            var packed = frame.Pack();
            var unpacked = Frame.Unpack(packed);

            // Assert
            Assert.AreEqual(frame.ShouldMask, unpacked.ShouldMask);
        }

        [Test]
        public void Unpack_Reads_Correct_Bits_For_ShouldMask_True()
        {
            // Arrange
            var frame = Frame
                .CreateText("hello")
                .WithShouldMask(true);

            // Act
            var packed = frame.Pack();
            var unpacked = Frame.Unpack(packed);

            // Assert
            Assert.AreEqual(frame.ShouldMask, unpacked.ShouldMask);
        }

        [Test]
        public void Unpack_Reads_Correctly_Masked()
        {
            // Arrange
            var payload = "Hello World";
            var frame = Frame.CreateText(payload)
                .WithShouldMask(true)
                .WithIsFinal(true);

            var packed = frame.Pack();
            var unpacked = Frame.Unpack(packed);

            // Assert
            Assert.AreEqual(frame.Data, unpacked.Data);
        }

        [Test]
        public void Unpack_Reads_Correctly_Unmasked()
        {
            // Arrange
            var payload = "Hello World";
            var frame = Frame.CreateText(payload)
                .WithShouldMask(false)
                .WithIsFinal(true);

            var packed = frame.Pack();
            var unpacked = Frame.Unpack(packed);

            // Assert
            Assert.AreEqual(frame.Data, unpacked.Data);
        }
    }
}