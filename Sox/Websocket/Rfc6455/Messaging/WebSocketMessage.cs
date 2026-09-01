using Sox.Extensions;
using Sox.Websocket.Rfc6455.Framing;
using System;
using System.Buffers;
using System.Collections.Generic;

namespace Sox.Websocket.Rfc6455.Messaging
{
    /// <summary>
    /// Complete message sent or received over a websocket connection
    /// </summary>
    /// <remarks>
    /// A <c>WebSocketMessage</c> produced by the <see cref="MessageAssembler"/> for an inbound,
    /// single-frame message borrows its <see cref="Data"/> directly from the network read
    /// buffer; one assembled from several fragments owns a buffer rented from
    /// <see cref="ArrayPool{T}.Shared"/>. Either way, <see cref="Data"/> is only valid until the
    /// message is disposed — dispatch code disposes it immediately after the corresponding
    /// <c>OnTextMessage</c>/<c>OnBinaryMessage</c> event handler returns. Copy <see cref="Data"/>
    /// inside the handler if you need to keep it any longer.
    /// </remarks>
    public sealed class WebSocketMessage : IDisposable
    {
        /// <summary>
        /// The type of message
        /// </summary>
        public readonly MessageType Type;

        /// <summary>
        /// The Message data
        /// </summary>
        public ReadOnlyMemory<byte> Data { get; }

        // Non-null only when Data was rented from ArrayPool<byte>.Shared and needs to be
        // returned once this message has been dispatched (see the lifetime note above).
        private readonly byte[] _rentedBuffer;

        /// <summary>
        /// Construct a text message
        /// </summary>
        /// <param name="data">The message data</param>
        public WebSocketMessage(string data) : this(MessageType.Text, data == null ? default : data.GetBytes(), rentedBuffer: null)
        {
        }

        /// <summary>
        /// Construct a binary message
        /// </summary>
        /// <param name="data">The message data</param>
        public WebSocketMessage(byte[] data) : this(MessageType.Binary, data, rentedBuffer: null)
        {
        }

        /// <summary>
        /// Construct a message that borrows or owns the given memory
        /// </summary>
        /// <param name="type">The type of message</param>
        /// <param name="data">The message payload</param>
        /// <param name="rentedBuffer">
        ///     The <c>ArrayPool&lt;byte&gt;.Shared</c> buffer backing <paramref name="data"/>, if
        ///     any, to be returned to the pool on <see cref="Dispose"/>
        /// </param>
        internal WebSocketMessage(MessageType type, ReadOnlyMemory<byte> data, byte[] rentedBuffer)
        {
            Type = type;
            Data = data;
            _rentedBuffer = rentedBuffer;
        }

        /// <summary>
        /// Pack this messages into it's frames
        /// </summary>
        /// <param name="frameMaxPayloadSizeBytes">The max payload size in bytes per frame</param>
        /// <param name="shouldMask">Should the frame be masked</param>
        /// <returns>The message split into it's frames</returns>
        internal IEnumerable<byte[]> Pack(int frameMaxPayloadSizeBytes, bool shouldMask = false)
        {
            if (Data.IsEmpty)
            {
                yield break;
            }

            var frameCount = GetFrameAmount(frameMaxPayloadSizeBytes);

            for (var i = 0; i < frameCount; i++)
            {
                yield return CreateFrame(i, frameCount, frameMaxPayloadSizeBytes, shouldMask).Pack();
            }
        }

        /// <summary>
        /// Return any pooled buffer backing this message to <c>ArrayPool&lt;byte&gt;.Shared</c>.
        /// A no-op for messages that borrow or own regular managed memory.
        /// </summary>
        public void Dispose()
        {
            if (_rentedBuffer != null)
            {
                ArrayPool<byte>.Shared.Return(_rentedBuffer);
            }
        }

        private int GetFrameAmount(int maxPayloadSizeBytes)
        {
            return (Data.Length / maxPayloadSizeBytes) + (Data.Length % maxPayloadSizeBytes == 0 ? 0 : 1);
        }

        private WebSocketFrame CreateFrame(int currentFrameIndex, int totalFrames, int frameMaxPayloadSizeBytes, bool shouldMask)
        {
            var isFirstFrame = currentFrameIndex == 0;
            var isLastFrame = currentFrameIndex == totalFrames - 1;

            var offset = currentFrameIndex * frameMaxPayloadSizeBytes;
            var chunkLength = Math.Min(frameMaxPayloadSizeBytes, Data.Length - offset);
            var chunk = Data.Slice(offset, chunkLength);

            return isFirstFrame
                ? WebSocketFrame.CreateInitiationFrame(type: Type, payload: chunk, shouldMask: shouldMask, isFinal: isLastFrame)
                : WebSocketFrame.CreateContinuation(payload: chunk, shouldMask: shouldMask, isFinal: isLastFrame);
        }
    }
}
