using NUnit.Framework;
using Sox.Websocket.Rfc6455.Framing;
using Sox.Websocket.Rfc6455.Messaging;
using System.Buffers;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Sox.Tests.Websocket.Rfc6455.Messaging
{
    [TestFixture]
    public class MessageAssemblerTests
    {
        private static WebSocketFrame Unpack(byte[] bytes)
        {
            var sequence = new ReadOnlySequence<byte>(bytes);
            Assert.IsTrue(WebSocketFrame.TryParse(ref sequence, out var frame));
            return frame;
        }

        [Test]
        public void TryAppend_Borrows_Memory_For_A_Single_Frame_Message()
        {
            // Arrange
            var frame = WebSocketFrame.CreateText("hi", shouldMask: false, isFinal: true);
            var assembler = new MessageAssembler(1024);

            // Act
            var accepted = assembler.TryAppend(frame, out var message);

            // Assert
            using (message)
            {
                Assert.Multiple(() =>
                {
                    Assert.IsTrue(accepted);
                    Assert.IsNotNull(message);
                    Assert.AreEqual(MessageType.Text, message.Type);
                    // Same underlying memory as the frame's payload - not a copy.
                    Assert.IsTrue(message.Data.Equals(frame.Data));
                });
            }
        }

        [Test]
        public void TryAppend_Reassembles_A_Fragmented_Message_From_Multiple_Frames()
        {
            // Arrange
            const string payload =
                "Lorem Ipsum is simply dummy text of the printing and typesetting industry. Lorem Ipsum has been the industry\'s standard dummy text ever since the 1500s, when an unknown printer took a galley of type and scrambled it to make a type specimen book. It has survived not only five centuries, but also the leap into electronic typesetting, remaining essentially unchanged. It was popularised in the 1960s with the release of Letraset sheets containing Lorem Ipsum passages, and more recently with desktop publishing software like Aldus PageMaker including versions of Lorem Ipsum.";
            var frames = new WebSocketMessage(payload).Pack(16).Select(Unpack).ToArray();
            var assembler = new MessageAssembler(int.MaxValue);

            // Act
            WebSocketMessage assembled = null;
            foreach (var frame in frames)
            {
                var accepted = assembler.TryAppend(frame, out var result);
                Assert.IsTrue(accepted);

                if (frame.Headers.IsFinal)
                {
                    assembled = result;
                }
                else
                {
                    Assert.IsNull(result);
                }
            }

            // Assert
            using (assembled)
            {
                Assert.Multiple(() =>
                {
                    Assert.AreEqual(36, frames.Length);
                    Assert.IsNotNull(assembled);
                    Assert.AreEqual(MessageType.Text, assembled.Type);
                    Assert.AreEqual(payload, Encoding.UTF8.GetString(assembled.Data.ToArray()));

                    Assert.AreEqual(OpCode.Text, frames.First().OpCode);
                    Assert.IsFalse(frames.First().Headers.IsFinal);

                    Assert.AreEqual(OpCode.Continuation, frames.Last().OpCode);
                    Assert.IsTrue(frames.Last().Headers.IsFinal);
                });
            }
        }

        [Test]
        public void TryAppend_Rejects_A_Single_Frame_Exceeding_MaxMessageBytes()
        {
            // Arrange
            var frame = WebSocketFrame.CreateBinary(new byte[100], isFinal: true);
            var assembler = new MessageAssembler(maxMessageBytes: 50);

            // Act
            var accepted = assembler.TryAppend(frame, out var message);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.IsFalse(accepted);
                Assert.IsNull(message);
            });
        }

        [Test]
        public void TryAppend_Rejects_A_Fragmented_Message_Exceeding_MaxMessageBytes_Across_Fragments()
        {
            // Arrange
            var assembler = new MessageAssembler(maxMessageBytes: 10);

            // Act
            var accepted1 = assembler.TryAppend(WebSocketFrame.CreateBinary(new byte[6], isFinal: false), out var message1);
            var accepted2 = assembler.TryAppend(WebSocketFrame.CreateContinuation(new byte[6], isFinal: true), out var message2);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.IsTrue(accepted1);
                Assert.IsNull(message1);
                Assert.IsFalse(accepted2);
                Assert.IsNull(message2);
            });
        }

        [Test]
        public void Dispose_Returns_The_Pooled_Buffer_To_The_ArrayPool()
        {
            // Arrange
            var assembler = new MessageAssembler(1024);
            assembler.TryAppend(WebSocketFrame.CreateText("Hel", shouldMask: false, isFinal: false), out _);
            assembler.TryAppend(WebSocketFrame.CreateContinuation(Encoding.UTF8.GetBytes("lo"), shouldMask: false, isFinal: true), out var message);

            var rentedBufferField = typeof(WebSocketMessage).GetField("_rentedBuffer", BindingFlags.NonPublic | BindingFlags.Instance);
            var rentedBuffer = (byte[])rentedBufferField.GetValue(message);
            Assert.IsNotNull(rentedBuffer, "Expected a fragmented message to own a rented buffer");

            // Act
            message.Dispose();

            // Assert - the pool hands the exact same array straight back out, proving it was returned
            var reRented = ArrayPool<byte>.Shared.Rent(rentedBuffer.Length);
            try
            {
                Assert.AreSame(rentedBuffer, reRented);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(reRented);
            }
        }
    }
}
