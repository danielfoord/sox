using Sox.Websocket.Rfc6455.Framing;
using System;
using System.Buffers;

namespace Sox.Websocket.Rfc6455.Messaging
{
    /// <summary>
    /// Reassembles a connection's stream of received <see cref="WebSocketFrame"/>s into complete
    /// <see cref="WebSocketMessage"/>s. One instance is owned per connection.
    /// </summary>
    /// <remarks>
    /// A message made of a single final frame borrows that frame's memory directly - no copy, no
    /// pooling. A fragmented message (an initial non-final frame followed by one or more
    /// continuations) is accumulated into a buffer rented from
    /// <see cref="ArrayPool{T}.Shared"/>, grown by re-renting and copying as needed, since each
    /// fragment's own memory becomes invalid as soon as the connection reads past it.
    /// </remarks>
    internal sealed class MessageAssembler
    {
        private readonly int _maxMessageBytes;

        private byte[] _buffer;
        private int _length;
        private OpCode _opCode;
        private bool _assembling;

        internal MessageAssembler(int maxMessageBytes)
        {
            _maxMessageBytes = maxMessageBytes;
        }

        /// <summary>
        /// Append a received data frame (Binary/Text/Continuation) to the message being
        /// assembled.
        /// </summary>
        /// <param name="frame">The frame to append</param>
        /// <param name="message">
        ///     The completed message, when this frame was the final frame of one. <c>null</c> if
        ///     more frames are still expected.
        /// </param>
        /// <returns>
        ///     <c>true</c> if the frame was accepted; <c>false</c> if accepting it would exceed
        ///     <c>maxMessageBytes</c> (any in-progress pooled buffer is returned to the pool
        ///     before returning)
        /// </returns>
        internal bool TryAppend(in WebSocketFrame frame, out WebSocketMessage message)
        {
            message = null;

            if (!_assembling)
            {
                if (frame.Headers.IsFinal)
                {
                    // Single frame message - just borrow its memory, nothing to accumulate.
                    if (frame.PayloadLength > _maxMessageBytes)
                    {
                        return false;
                    }

                    message = new WebSocketMessage(ToMessageType(frame.OpCode), frame.Data, rentedBuffer: null);
                    return true;
                }

                _opCode = frame.OpCode;
                _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(frame.PayloadLength, 1024));
                _length = 0;
                _assembling = true;
            }

            if (!TryAppendToBuffer(frame.Data.Span))
            {
                _assembling = false;
                return false;
            }

            if (!frame.Headers.IsFinal)
            {
                return true;
            }

            message = new WebSocketMessage(ToMessageType(_opCode), new ReadOnlyMemory<byte>(_buffer, 0, _length), rentedBuffer: _buffer);
            _assembling = false;
            _buffer = null;
            _length = 0;
            return true;
        }

        /// <summary>
        /// Return any pooled buffer for a message that never finished assembling (e.g. the
        /// connection was closed mid-fragment).
        /// </summary>
        internal void Dispose()
        {
            if (_buffer != null)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = null;
            }
        }

        private bool TryAppendToBuffer(ReadOnlySpan<byte> data)
        {
            var newLength = _length + data.Length;
            if (newLength > _maxMessageBytes)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = null;
                _length = 0;
                return false;
            }

            if (newLength > _buffer.Length)
            {
                var grown = ArrayPool<byte>.Shared.Rent(Math.Max(newLength, _buffer.Length * 2));
                _buffer.AsSpan(0, _length).CopyTo(grown);
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = grown;
            }

            data.CopyTo(_buffer.AsSpan(_length));
            _length = newLength;
            return true;
        }

        private static MessageType ToMessageType(OpCode opCode) => opCode switch
        {
            OpCode.Text => MessageType.Text,
            _ => MessageType.Binary
        };
    }
}
