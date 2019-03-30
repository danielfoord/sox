using System.Linq;
using NUnit.Framework;
using Sox.Core.Websocket.Rfc6455.Frames;
using Sox.Core.Websocket.Rfc6455.Messaging;


namespace Sox.Core.Tests.Websocket.Rfc6455.Messaging
{
    [TestFixture]
    public class MessageTests
    {
        [Test]
        public void Pack_Packs_The_Correct_Amount_Of_Frames()
        {
            // Arrange
            var message = new Message(new byte[11]);

            // Act
            var frames1 = message.Pack(2);
            var frames2 = message.Pack(11);
            var frames3 = message.Pack(3);
            var frames4 = message.Pack(1);
            var frames5 = message.Pack();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.AreEqual(6, frames1.Count());
                Assert.AreEqual(1, frames2.Count());
                Assert.AreEqual(4, frames3.Count());
                Assert.AreEqual(11, frames4.Count());
                Assert.AreEqual(1, frames5.Count());
            });
        }

        [Test]
        public void Pack_Packs_The_Data_Across_Frames_Correctly()
        {
            // Arrange
            var message = new Message("Hello World");

            // Act
            var frames = message.Pack(3).ToArray();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.AreEqual("Hel", frames[0].DataAsString());
                Assert.AreEqual(OpCode.Text, frames[0].OpCode);
                Assert.AreEqual(false, frames[0].IsFinal);

                Assert.AreEqual("lo ", frames[1].DataAsString());
                Assert.AreEqual(OpCode.Continuation, frames[1].OpCode);
                Assert.AreEqual(false, frames[1].IsFinal);

                Assert.AreEqual("Wor", frames[2].DataAsString());
                Assert.AreEqual(OpCode.Continuation, frames[2].OpCode);
                Assert.AreEqual(false, frames[2].IsFinal);

                Assert.AreEqual("ld", frames[3].DataAsString());
                Assert.AreEqual(OpCode.Continuation, frames[3].OpCode);
                Assert.AreEqual(true, frames[3].IsFinal);
            });
        }

        [Test]
        public void Pack_Returns_EmtpyList_If_Null_Data()
        {
            // Arrange
            var message = new Message((string) null);

            // Act
            var frames = message.Pack();

            // Assert
            Assert.AreEqual(0, frames.Count());
        }

        [Test]
        public void Unpack_Unpacks_The_Data_From_All_Frames_Correctly()
        {
            // Arrange
            const string payload =
                "Lorem Ipsum is simply dummy text of the printing and typesetting industry. Lorem Ipsum has been the industry\'s standard dummy text ever since the 1500s, when an unknown printer took a galley of type and scrambled it to make a type specimen book. It has survived not only five centuries, but also the leap into electronic typesetting, remaining essentially unchanged. It was popularised in the 1960s with the release of Letraset sheets containing Lorem Ipsum passages, and more recently with desktop publishing software like Aldus PageMaker including versions of Lorem Ipsum.";
            var message = new Message(payload);

            // Act
            var frames = message.Pack(16).ToArray();
            var unpacked = Message.Unpack(frames);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.AreEqual(36, frames.Length);
                Assert.AreEqual(MessageType.Text, unpacked.Type);
                Assert.AreEqual(message.Data, unpacked.Data);

                Assert.AreEqual(OpCode.Text, frames.First().OpCode);
                Assert.IsFalse(frames.First().IsFinal);

                Assert.AreEqual(OpCode.Continuation, frames.Last().OpCode);
                Assert.IsTrue(frames.Last().IsFinal);
            });
        }
    }
}