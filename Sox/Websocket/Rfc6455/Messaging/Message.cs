using Sox.Extensions;
using Sox.Websocket.Rfc6455.Framing;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Sox.Websocket.Rfc6455.Messaging
{
    /// <summary>
    /// Complete message sent over a websocket connection
    /// </summary>
    public class Message
    {
        /// <summary>
        /// The type of message
        /// </summary>
        public readonly MessageType Type;

        /// <summary>
        /// Construct a text message
        /// </summary>
        /// <param name="data">The message data</param>
        public Message(string data)
        {
            Type = MessageType.Text;
            Data = data?.GetBytes();
        }

        /// <summary>
        /// Construct a binary message
        /// </summary>
        /// <param name="data">The message data</param>
        public Message(byte[] data)
        {
            Type = MessageType.Binary;
            Data = data;
        }

        /// <summary>
        /// The Message data
        /// </summary>
        public byte[] Data { get; set; }

        /// <summary>
        /// Pack this messages into it's frames
        /// </summary>
        /// <param name="frameMaxPayloadSizeBytes">The max payload size in bytes per frame</param>
        /// <param name="shouldMask">Should the frame be masked</param>
        /// <returns>The message split into it's frames</returns>
        internal async IAsyncEnumerable<byte[]> Pack(int frameMaxPayloadSizeBytes, bool shouldMask = false)
        {
            if (Data == null)
            {
                yield break;
            }

            var frameCount = GetFrameAmount(frameMaxPayloadSizeBytes);
            using var stream = new MemoryStream(Data) { Position = 0 };

            for (var i = 0; i < frameCount; i++)
            {
                yield return await CreateFrameFromDataStream(stream, i, frameCount, frameMaxPayloadSizeBytes, shouldMask);
            }
        }

        /// <summary>
        /// Unpack a message from it's frames
        /// </summary>
        /// <param name="frames">The packed message</param>
        /// <returns>The unpacked message</returns>
        internal static async Task<Message> Unpack(IEnumerable<Frame> frames)
        {
            if (frames.First().PayloadLength == 0)
            {
                return new Message(string.Empty);
            }

            using var stream = new MemoryStream { Position = 0 };
            frames.ForEach(async frame => await stream.WriteBytesAsync(frame.Data));
            await stream.FlushAsync();

            return frames.First().OpCode == OpCode.Text
                ? new Message(stream.ToArray().GetString())
                : new Message(stream.ToArray());
        }

        private int GetFrameAmount(int maxPayloadSizeBytes)
        {
            return Data.Length / maxPayloadSizeBytes + (Data.Length % maxPayloadSizeBytes == 0 ? 0 : 1);
        }

        private async Task<byte[]> CreateFrameFromDataStream(Stream stream, int currentFrameIndex, int totalFrames, int frameMaxPayloadSizeBytes, bool shouldMask)
        {
            var isFirstFrame = currentFrameIndex == 0;
            var isLastFrame = currentFrameIndex == totalFrames - 1;

            if (isFirstFrame)
            {
                return await Frame.CreateInitiationFrame(
                    type: Type,
                    payload: isLastFrame ? Data : await stream.ReadBytesAsync(frameMaxPayloadSizeBytes),
                    shouldMask: shouldMask,
                    isFinal: isLastFrame).PackAsync();
            }
            else if (isLastFrame)
            {
                return await Frame.CreateContinuation(
                    payload: Data.Length % frameMaxPayloadSizeBytes == 0
                        ? await stream.ReadBytesAsync(frameMaxPayloadSizeBytes)
                        : await stream.ReadBytesAsync(Data.Length % frameMaxPayloadSizeBytes),
                    shouldMask: shouldMask,
                    isFinal: isLastFrame).PackAsync();
            }

            return await Frame.CreateContinuation(
                payload: await stream.ReadBytesAsync(frameMaxPayloadSizeBytes),
                shouldMask: shouldMask,
                isFinal: isLastFrame).PackAsync();
        }
    }
}