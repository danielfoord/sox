using NUnit.Framework;
using Sox.Websocket.Rfc6455;
using Sox.Websocket.Rfc6455.Framing;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sox.Tests.Websocket.Rfc6455.Frames
{
    [TestFixture]
    public class FrameTests
    {
        [Test]
        public async Task Pack_Sets_Correct_Bit_For_Final_Flag_False()
        {
            // Arrange 
            var frame = Frame.CreateText("hello", isFinal: false);

            // Act
            var packed = await frame.PackAsync();

            // Assert
            Assert.AreEqual(0, (packed[0] & 0x80) >> 7); // 10000000 mask
        }

        [Test]
        public async Task Pack_Sets_Correct_Bit_For_Final_Flag_True()
        {
            // Arrange 
            var frame = Frame.CreateText("hello", isFinal: true);

            // Act
            var packed = await frame.PackAsync();

            // Assert
            Assert.AreEqual(1, (packed[0] & 0x80) >> 7); // 10000000 mask
        }

        [Test]
        public async Task Pack_Sets_Correct_Bit_For_Reserve1_Flag()
        {
            // Arrange 
            var frame = Frame.CreateText("hello");

            // Act
            var packed = await frame.PackAsync();

            // Assert
            Assert.AreEqual(0, (packed[0] & 0x40) >> 6); // 01000000 mask
        }

        [Test]
        public async Task Pack_Sets_Correct_Bit_For_Reserve2_Flag()
        {
            // Arrange 
            var frame = Frame.CreateText("hello");

            // Act
            var packed = await frame.PackAsync();

            // Assert
            Assert.AreEqual(0, (packed[0] & 0x20) >> 5); // 00100000 mask
        }

        [Test]
        public async Task Pack_Sets_Correct_Bit_For_Reserve3_Flag()
        {
            // Arrange 
            var frame = Frame.CreateText("hello");

            // Act
            var packed = await frame.PackAsync();

            // Assert
            Assert.AreEqual(0, (packed[0] & 0x10) >> 4); // 00010000 mask
        }

        [TestCase(8952)]
        [TestCase(568)]
        [TestCase(65535)]
        public async Task Pack_Sets_Correct_Bits_For_Length_Larger_Than_125_Less_Than_16BitMax(int length)
        {
            // Arrange
            var frame = await Frame.CreateBinary(new byte[length]).PackAsync();
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
        public async Task Pack_Sets_Correct_Bits_For_Length_Larger_Than_16BitMax(int length)
        {
            // Arrange
            var frame = await Frame.CreateBinary(new byte[length]).PackAsync();
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
        public async Task Pack_Sets_Correct_Bits_For_Length_Less_Than_Or_Equal_125(int length)
        {
            // Arrange
            var frame = await Frame.CreateBinary(new byte[length]).PackAsync();

            // Assert 
            Assert.AreEqual(length, frame[1] & 0x7F); // 01111111 mask
        }

        [Test]
        public async Task Pack_Sets_Correct_Bits_For_OpCode_Binary()
        {
            // Arrange 
            var frame = Frame.CreateBinary(new byte[0]);

            // Act
            var packed = await frame.PackAsync();

            // Assert
            Assert.AreEqual(OpCode.Binary, (OpCode)(packed[0] & 0xF)); // 00001111 mask
        }

        [Test]
        public async Task Pack_Sets_Correct_Bits_For_OpCode_Close()
        {
            // Arrange 
            var frame = Frame.CreateClose();

            // Act
            var packed = await frame.PackAsync();

            // Assert
            Assert.AreEqual(OpCode.Close, (OpCode)(packed[0] & 0xF)); // 00001111 mask
        }

        [Test]
        public async Task Pack_Sets_Correct_Bits_For_OpCode_Close_With_StatusCode()
        {
            // Arrange 
            var frame = Frame.CreateClose(CloseStatusCode.Normal);

            // Act
            var packed = await frame.PackAsync();
            var unpacked = await Frame.UnpackAsync(packed);

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(unpacked.Data);
            }

            // Assert
            Assert.Multiple(() =>
            {
                Assert.AreEqual(OpCode.Close, (OpCode)(packed[0] & 0xF)); // 00001111 mask
                Assert.AreEqual(CloseStatusCode.Normal, (CloseStatusCode)BitConverter.ToUInt16(unpacked.Data, 0));
            });
        }

        [Test]
        public async Task Pack_Sets_Correct_Bits_For_OpCode_Continuation()
        {
            // Arrange 
            var frame = Frame.CreateContinuation(new byte[0]);

            // Act
            var packed = await frame.PackAsync();

            // Assert
            Assert.AreEqual(OpCode.Continuation, (OpCode)(packed[0] & 0xF)); // 00001111 mask
        }

        [Test]
        public async Task Pack_Sets_Correct_Bits_For_OpCode_Ping()
        {
            // Arrange 
            var frame = Frame.CreatePing();

            // Act
            var packed = await frame.PackAsync();

            // Assert
            Assert.AreEqual(OpCode.Ping, (OpCode)(packed[0] & 0xF)); // 00001111 mask
        }

        [Test]
        public async Task Pack_Sets_Correct_Bits_For_OpCode_Pong()
        {
            // Arrange 
            var frame = Frame.CreatePong();

            // Act
            var packed = await frame.PackAsync();

            // Assert
            Assert.AreEqual(OpCode.Pong, (OpCode)(packed[0] & 0xF)); // 00001111 mask
        }

        [Test]
        public async Task Pack_Sets_Correct_Bits_For_OpCode_Text()
        {
            // Arrange 
            var frame = Frame.CreateText("hello");

            // Act
            var packed = await frame.PackAsync();

            // Assert
            Assert.AreEqual(OpCode.Text, (OpCode)(packed[0] & 0xF)); // 00001111 mask
        }

        [Test]
        public async Task Pack_Sets_Correct_Bits_For_ShouldMask_False()
        {
            // Arrange
            var frame = Frame.CreateText("hello", shouldMask: false);

            // Act
            var packed = await frame.PackAsync();

            // Assert
            Assert.AreEqual(0, (packed[1] & 0x80) >> 7); // 10000000 mask
        }

        [Test]
        public async Task Pack_Sets_Correct_Bits_For_ShouldMask_True()
        {
            // Arrange
            var frame = Frame.CreateText("hello", shouldMask: true);

            // Act
            var packed = await frame.PackAsync();

            // Assert
            Assert.AreEqual(1, (packed[1] & 0x80) >> 7); // 10000000 mask
        }

        [Test]
        public async Task Packs_Data_Correctly_Masked()
        {
            // Arrange
            var payload = "Hello World";
            var frame = Frame.CreateText(payload, shouldMask: true);

            var packed = await frame.PackAsync();

            // Assert correct length
            // 2 bytes - headers
            // 4 bytes - masking key
            // 11 bytes - payload
            Assert.AreEqual(17, packed.Length);

            var payloadBytes = packed.Skip(6).Take(payload.Length).ToArray();

            var decrypted = new byte[frame.Data.Length];
            for (var i = 0; i < frame.Data.Length; i++)
            {
                decrypted[i] = (byte)(frame.Data[i] ^ frame.MaskingKey[i % 4]);
            }

            // Assert
            Assert.AreEqual(decrypted, payloadBytes);
        }

        [Test]
        public async Task Packs_Data_Correctly_Unmasked()
        {
            // Arrange
            var payload = "Hello World";
            var frame = await Frame.CreateText(payload, shouldMask: false).PackAsync();

            // Assert correct length
            // 2 bytes - headers
            // 11 bytes - payload
            Assert.AreEqual(13, frame.Length);

            var payloadBytes = frame.Skip(2).Take(payload.Length).ToArray();

            // Assert
            Assert.AreEqual(payload, Encoding.UTF8.GetString(payloadBytes, 0, payload.Length));
        }

        [Test]
        public async Task Unpack_Reads_Correct_Bit_For_Final_Flag_False()
        {
            // Arrange 
            var frame = Frame.CreateText("hello", isFinal: false);

            // Act
            var packed = await frame.PackAsync();
            var unpacked = await Frame.UnpackAsync(packed);

            // Assert
            Assert.AreEqual(frame.Headers.IsFinal, unpacked.Headers.IsFinal); // 10000000 mask
        }

        [Test]
        public async Task Unpack_Reads_Correct_Bit_For_Final_Flag_True()
        {
            // Arrange 
            var frame = Frame.CreateText("hello", isFinal: true);

            // Act
            var packed = await frame.PackAsync();
            var unpacked = await Frame.UnpackAsync(packed);

            // Assert
            Assert.AreEqual(frame.Headers.IsFinal, unpacked.Headers.IsFinal); // 10000000 mask
        }

        [Test]
        public async Task Unpack_Reads_Correct_Bit_For_Reserve1_Flag()
        {
            // Arrange 
            var frame = Frame.CreateText("hello");

            // Act
            var packed = await frame.PackAsync();
            var unpacked = await Frame.UnpackAsync(packed);

            // Assert
            Assert.AreEqual(frame.Headers.Rsv1, unpacked.Headers.Rsv1);
        }

        [Test]
        public async Task Unpack_Reads_Correct_Bit_For_Reserve2_Flag()
        {
            // Arrange 
            var frame = Frame.CreateText("hello");

            // Act
            var packed = await frame.PackAsync();
            var unpacked = await Frame.UnpackAsync(packed);

            // Assert
            Assert.AreEqual(frame.Headers.Rsv2, unpacked.Headers.Rsv2);
        }

        [Test]
        public async Task Unpack_Reads_Correct_Bit_For_Reserve3_Flag()
        {
            // Arrange 
            var frame = Frame.CreateText("hello");

            // Act
            var packed = await frame.PackAsync();
            var unpacked = await Frame.UnpackAsync(packed);

            // Assert
            Assert.AreEqual(frame.Headers.Rsv3, unpacked.Headers.Rsv3);
        }

        [TestCase(12)]
        [TestCase(125)]
        [TestCase(67)]
        public async Task Unpack_Reads_Correct_Bits_For_Length_Less_Than_Or_Equal_125(int length)
        {
            // Arrange
            var frame = Frame.CreateBinary(new byte[length]);
            var unpacked = await Frame.UnpackAsync(await frame.PackAsync());

            // Assert 
            Assert.AreEqual(frame.PayloadLength, unpacked.PayloadLength);
        }

        [TestCase(8952)]
        [TestCase(568)]
        [TestCase(65535)]
        public async Task Unpack_Reads_Correct_Bits_For_Length_Larger_Than_125_Less_Than_16BitMax(int length)
        {
            // Arrange
            var frame = Frame.CreateBinary(new byte[length]);
            var unpacked = await Frame.UnpackAsync(await frame.PackAsync());

            // Assert 
            Assert.AreEqual(frame.PayloadLength, unpacked.PayloadLength);
        }

        [TestCase(65536)]
        [TestCase(120546)]
        [TestCase(85475)]
        public async Task Unpack_Reads_Correct_Bits_For_Length_Larger_Than_16BitMax(int length)
        {
            // Arrange
            var frame = Frame.CreateBinary(new byte[length]);
            var unpacked = await Frame.UnpackAsync(await frame.PackAsync());

            // Assert 
            Assert.AreEqual(frame.PayloadLength, unpacked.PayloadLength);
        }

        [Test]
        public async Task Unpack_Reads_Correct_Bits_For_OpCode_Binary()
        {
            // Arrange 
            var frame = Frame.CreateBinary(new byte[0]);

            // Act
            var packed = await frame.PackAsync();
            var unpacked = await Frame.UnpackAsync(packed);

            // Assert
            Assert.AreEqual(OpCode.Binary, unpacked.OpCode); // 00001111 mask
        }

        [Test]
        public async Task Unpack_Reads_Correct_Bits_For_OpCode_Close()
        {
            // Arrange 
            var frame = Frame.CreateClose();

            // Act
            var packed = await frame.PackAsync();
            var unpacked = await Frame.UnpackAsync(packed);

            // Assert
            Assert.AreEqual(OpCode.Close, unpacked.OpCode); // 00001111 mask
        }

        [Test]
        public async Task Unpack_Reads_Correct_Bits_For_OpCode_Close_With_Reason()
        {
            // Arrange 
            var frame = Frame.CreateClose(CloseStatusCode.Normal);

            // Act
            var packed = await frame.PackAsync();
            var unpacked = await Frame.UnpackAsync(packed);

            var isLittleEndian = BitConverter.IsLittleEndian;
            if (isLittleEndian)
            {
                Array.Reverse(unpacked.Data);
            }

            // Assert
            Assert.Multiple(() =>
            {
                Assert.AreEqual(OpCode.Close, unpacked.OpCode); // 00001111 mask
                Assert.AreEqual((ushort)CloseStatusCode.Normal, BitConverter.ToUInt16(unpacked.Data, 0));
            });
        }

        [Test]
        public async Task Unpack_Reads_Correct_Bits_For_OpCode_Continuation()
        {
            // Arrange 
            var frame = Frame.CreateContinuation(new byte[0]);

            // Act
            var packed = await frame.PackAsync();
            var unpacked = await Frame.UnpackAsync(packed);

            // Assert
            Assert.AreEqual(OpCode.Continuation, unpacked.OpCode); // 00001111 mask
        }

        [Test]
        public async Task Unpack_Reads_Correct_Bits_For_OpCode_Ping()
        {
            // Arrange 
            var frame = Frame.CreatePing();

            // Act
            var packed = await frame.PackAsync();
            var unpacked = await Frame.UnpackAsync(packed);

            // Assert
            Assert.AreEqual(OpCode.Ping, unpacked.OpCode); // 00001111 mask
        }

        [Test]
        public async Task Unpack_Reads_Correct_Bits_For_OpCode_Pong()
        {
            // Arrange 
            var frame = Frame.CreatePong();

            // Act
            var packed = await frame.PackAsync();
            var unpacked = await Frame.UnpackAsync(packed);

            // Assert
            Assert.AreEqual(OpCode.Pong, unpacked.OpCode); // 00001111 mask
        }

        [Test]
        public async Task Unpack_Reads_Correct_Bits_For_OpCode_Text()
        {
            // Arrange 
            var frame = Frame.CreateText("hello");

            // Act
            var packed = await frame.PackAsync();
            var unpacked = await Frame.UnpackAsync(packed);

            // Assert
            Assert.AreEqual(OpCode.Text, unpacked.OpCode); // 00001111 mask
        }

        [Test]
        public async Task Unpack_Reads_Correct_Bits_For_ShouldMask_False()
        {
            // Arrange
            var frame = Frame.CreateText("hello", shouldMask: false);

            // Act
            var packed = await frame.PackAsync();
            var unpacked = await Frame.UnpackAsync(packed);

            // Assert
            Assert.AreEqual(frame.Headers.ShouldMask, unpacked.Headers.ShouldMask);
        }

        [Test]
        public async Task Unpack_Reads_Correct_Bits_For_ShouldMask_True()
        {
            // Arrange
            var frame = Frame.CreateText("hello", shouldMask: true);

            // Act
            var packed = await frame.PackAsync();
            var unpacked = await Frame.UnpackAsync(packed);

            // Assert
            Assert.AreEqual(frame.Headers.ShouldMask, unpacked.Headers.ShouldMask);
        }

        [Test]
        public async Task Unpack_Reads_Correctly_Masked()
        {
            // Arrange
            var payload = "Hello World";
            var frame = Frame.CreateText(payload, shouldMask: true);

            var packed = await frame.PackAsync();
            var unpacked = await Frame.UnpackAsync(packed);

            // Assert
            Assert.AreEqual(frame.Data, unpacked.Data);
        }

        [Test]
        public async Task Unpack_Reads_Correctly_Unmasked()
        {
            // Arrange
            var payload = "Hello World";
            var frame = Frame.CreateText(payload, shouldMask: false);

            var packed = await frame.PackAsync();
            var unpacked = await Frame.UnpackAsync(packed);

            // Assert
            Assert.AreEqual(frame.Data, unpacked.Data);
        }
    }
}