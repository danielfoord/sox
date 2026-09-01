using NUnit.Framework;
using Sox.Websocket.Rfc6455.Framing;
using Sox.Websocket.Rfc6455.Messaging;
using System.Buffers;
using System.Linq;

namespace Sox.Tests.Websocket.Rfc6455.Messaging
{
    [TestFixture]
    public class WebSocketMessageTests
    {
        private static WebSocketFrame Unpack(byte[] bytes)
        {
            var buffer = new ReadOnlySequence<byte>(bytes);
            Assert.IsTrue(WebSocketFrame.TryParse(ref buffer, out var frame));
            return frame;
        }

        [TestCase(2, 6)]
        [TestCase(11, 1)]
        [TestCase(3, 4)]
        [TestCase(1, 11)]
        public void Pack_Packs_The_Correct_Amount_Of_Frames(int maxFramePayloadBytes, int expectedFrameCount)
        {
            // Arrange
            var message = new WebSocketMessage(new byte[11]);

            // Act
            var frames = message.Pack(maxFramePayloadBytes).ToArray();

            // Assert
            Assert.AreEqual(expectedFrameCount, frames.Length);
        }

        [TestCase(0, "Hel", OpCode.Text, false)]
        [TestCase(1, "lo ", OpCode.Continuation, false)]
        [TestCase(2, "Wor", OpCode.Continuation, false)]
        [TestCase(3, "ld", OpCode.Continuation, true)]
        public void Pack_Packs_The_Data_Across_Frames_Correctly(int index, string data, OpCode opCode, bool isFinal)
        {
            // Arrange
            var message = new WebSocketMessage("Hello World");

            // Act
            var frames = message.Pack(3)
                .Select(Unpack)
                .ToArray();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.AreEqual(data, frames[index].DecodedData);
                Assert.AreEqual(opCode, frames[index].OpCode);
                Assert.AreEqual(isFinal, frames[index].Headers.IsFinal);
            });
        }

        [Test]
        public void Pack_Returns_EmtpyList_If_Null_Data()
        {
            // Arrange
            var message = new WebSocketMessage((string)null);

            // Act
            var frames = message.Pack(1).ToArray();

            // Assert
            Assert.AreEqual(0, frames.Length);
        }
    }
}
