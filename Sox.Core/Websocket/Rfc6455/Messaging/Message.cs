using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sox.Core.Extensions;
using Sox.Core.Websocket.Rfc6455.Frames;

namespace Sox.Core.Websocket.Rfc6455.Messaging
{
    public class Message
    {
        /// <summary>
        ///     The type of message
        /// </summary>
        public readonly MessageType Type;

        public Message(string data)
        {
            Type = MessageType.Text;
            Data = data?.GetBytes();
        }

        public Message(byte[] data)
        {
            Type = MessageType.Binary;
            Data = data;
        }

        /// <summary>
        ///     The Message data
        /// </summary>
        public byte[] Data { get; set; }

        /// <summary>
        ///     Pack this messages into it's frames
        /// </summary>
        /// <param name="frameMaxPayloadSizeBytes">The max payload size in bytes per frame</param>
        /// <param name="shouldMask">Should the frame be masked</param>
        /// <returns>The message split into it's frames</returns>
        public IEnumerable<Frame> Pack(int frameMaxPayloadSizeBytes = 0, bool shouldMask = false)
        {
            var frames = new List<Frame>();

            if (Data == null) return frames;

            var frameCount = GetFrameAmount(frameMaxPayloadSizeBytes);

            using (var ms = new MemoryStream(Data))
            {
                ms.Position = 0;
                var buffer = new byte[frameMaxPayloadSizeBytes];

                for (var i = 0; i < frameCount; i++)
                    if (i == 0)
                    {
                        if (i == frameCount - 1)
                        {
                            frames.Add(CreateInitiationFrame(Data)
                                .WithShouldMask(shouldMask)
                                .WithIsFinal(true));
                        }
                        else
                        {
                            ms.Read(buffer, 0, buffer.Length);
                            frames.Add(CreateInitiationFrame(buffer)
                                .WithShouldMask(shouldMask)
                                .WithIsFinal(false));
                        }
                    }
                    else if (i == frameCount - 1)
                    {
                        buffer = new byte[frameMaxPayloadSizeBytes];
                        if (Data.Length % frameMaxPayloadSizeBytes == 0)
                        {
                            ms.Read(buffer, 0, buffer.Length);
                        }
                        else
                        {
                            buffer = new byte[Data.Length % frameMaxPayloadSizeBytes];
                            ms.Read(buffer, 0, buffer.Length);
                        }

                        frames.Add(Frame.CreateContinuation(buffer)
                            .WithIsFinal(true)
                            .WithShouldMask(shouldMask));
                    }
                    else
                    {
                        buffer = new byte[frameMaxPayloadSizeBytes];
                        ms.Read(buffer, 0, buffer.Length);
                        frames.Add(Frame.CreateContinuation(buffer)
                            .WithIsFinal(false)
                            .WithShouldMask(shouldMask));
                    }
            }

            return frames;
        }

        private int GetFrameAmount(int maxPayloadSizeBytes)
        {
            int frameCount;
            if (maxPayloadSizeBytes == 0)
            {
                frameCount = 1;
            }
            else
            {
                if (Data.Length % maxPayloadSizeBytes == 0) frameCount = Data.Length / maxPayloadSizeBytes;
                else frameCount = Data.Length / maxPayloadSizeBytes + 1;
            }

            return frameCount;
        }

        private Frame CreateInitiationFrame(byte[] data, bool isFinal = true, bool shouldMask = true)
        {
            return Type == MessageType.Text
                ? Frame.CreateText(data.GetString())
                    .WithIsFinal(isFinal)
                    .WithShouldMask(shouldMask)
                : Frame.CreateBinary(data)
                    .WithIsFinal(isFinal)
                    .WithShouldMask(shouldMask);
        }

        /// <summary>
        ///     Unpack a message from it's frames
        /// </summary>
        /// <param name="frames">The packed message</param>
        /// <returns>The unpacked message</returns>
        public static Message Unpack(IEnumerable<Frame> frames)
        {
            using (var ms = new MemoryStream())
            {
                ms.Position = 0;

                var enumerable = frames as Frame[] ?? frames.ToArray();
                foreach (var frame in enumerable) ms.Write(frame.Data, 0, frame.Data.Length);

                ms.Flush();

                return enumerable[0].OpCode == OpCode.Text
                    ? new Message(ms.ToArray().GetString())
                    : new Message(ms.ToArray());
            }
        }
    }
}