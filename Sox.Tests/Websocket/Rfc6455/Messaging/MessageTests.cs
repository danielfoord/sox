using NUnit.Framework;
using System.Linq;
using System.Threading.Tasks;
using Sox.Websocket.Rfc6455.Messaging;
using Sox.Extensions;
using Sox.Websocket.Rfc6455.Framing;

namespace Sox.Tests.Websocket.Rfc6455.Messaging
{
    [TestFixture]
    public class MessageTests
    {
        [TestCase(2, 6)]
        [TestCase(11, 1)]
        [TestCase(3, 4)]
        [TestCase(1, 11)]
        public async Task Pack_Packs_The_Correct_Amount_Of_Frames(int maxFramePayloadBytes, int expectedFrameCount)
        {
            // Arrange
            var message = new Message(new byte[11]);

            // Act
            var frames = await message.Pack(maxFramePayloadBytes).AsEnumerable();

            // Assert
            Assert.AreEqual(expectedFrameCount, frames.Count());
        }

        [TestCase(0, "Hel", OpCode.Text, false)]
        [TestCase(1, "lo ", OpCode.Continuation, false)]
        [TestCase(2, "Wor", OpCode.Continuation, false)]
        [TestCase(3, "ld", OpCode.Continuation, true)]
        public async Task Pack_Packs_The_Data_Across_Frames_Correctly(int index, string data, OpCode opCode, bool isFinal)
        {
            // Arrange
            var message = new Message("Hello World");

            // Act
            var frames = (await message.Pack(3).AsEnumerable())
                .Select(bytes => Frame.UnpackAsync(bytes).Result)
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
        public async Task Pack_Returns_EmtpyList_If_Null_Data()
        {
            // Arrange
            var message = new Message((string)null);

            // Act
            var frames = await message.Pack(1).AsEnumerable();

            // Assert
            Assert.AreEqual(0, frames.Count());
        }

        [Test]
        public async Task Unpack_Unpacks_The_Data_From_All_Frames_Correctly()
        {
            // Arrange
            const string payload =
                "Lorem Ipsum is simply dummy text of the printing and typesetting industry. Lorem Ipsum has been the industry\'s standard dummy text ever since the 1500s, when an unknown printer took a galley of type and scrambled it to make a type specimen book. It has survived not only five centuries, but also the leap into electronic typesetting, remaining essentially unchanged. It was popularised in the 1960s with the release of Letraset sheets containing Lorem Ipsum passages, and more recently with desktop publishing software like Aldus PageMaker including versions of Lorem Ipsum.";
            var message = new Message(payload);

            // Act
            var frames = (await message.Pack(16).AsEnumerable()).Select(bytes => Frame.UnpackAsync(bytes).Result).ToArray();
            var unpacked = await Message.Unpack(frames);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.AreEqual(36, frames.Length);
                Assert.AreEqual(MessageType.Text, unpacked.Type);
                Assert.AreEqual(message.Data, unpacked.Data);

                Assert.AreEqual(OpCode.Text, frames.First().OpCode);
                Assert.IsFalse(frames.First().Headers.IsFinal);

                Assert.AreEqual(OpCode.Continuation, frames.Last().OpCode);
                Assert.IsTrue(frames.Last().Headers.IsFinal);
            });
        }
    }
}