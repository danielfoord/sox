using Sox.Extensions;
using Sox.Http;
using Sox.Websocket.Rfc6455;
using Sox.Websocket.Rfc6455.Framing;
using Sox.Websocket.Rfc6455.Messaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Timers;
using PingTimer = System.Timers.Timer;

namespace Sox.Server.State
{
    /// <summary>
    /// A Websocket connection
    /// </summary>
    public sealed class Connection : IDisposable
    {
        /// <summary>
        /// The Id of the connection, used for context
        /// </summary>
        public readonly string Id;

        /// <summary>
        /// The current state of the connection
        /// </summary>
        public volatile ConnectionState State;

        /// <summary>
        /// When the last Pong frame was recieved
        /// </summary>
        public DateTime PongRecieved { get; private set; }

        // TcpClient underlying stream
        private readonly Stream _stream;

        // Reads frames off the stream without allocating/copying unless a frame straddles more
        // than one of the Pipe's internal buffer segments
        private readonly PipeReader _pipeReader;

        // Reassembles received frames into complete messages
        private readonly MessageAssembler _messageAssembler;

        // Size of receive buffer.
        internal const int MaxFrameBytes = 4096;

        // How long to wait between pings to this connection
        internal const int PingIntervalMs = 60000;

        // Scheduled pinger
        private readonly PingTimer _pinger;

        // How long shoud we wait for a stream write before timing out
        private readonly int StreamWriteTimeoutMs = 2000;

        private readonly Channel<byte[]> _channel;

        // Guards DrainAsync so only one drain loop owns writing to the stream at a time - see
        // EnqueueAsync/DrainAsync.
        private int _draining;

        // Guards Close() so it only ever runs its body once, even if called concurrently from
        // multiple paths (e.g. a ping-timeout and an idle-timeout both deciding to close at once).
        private int _closeInitiated;

        private bool _disposed;

        /// <summary>
        /// Contruct a connection
        /// </summary>
        /// <param name="stream">The underlying connection stream</param>
        /// <param name="maxMessageBytes">The maximum amount of bytes a message can contain</param>
        public Connection(Stream stream, int maxMessageBytes)
        {
            Id = Guid.NewGuid().ToString();
            State = ConnectionState.Connecting;
            _channel = Channel.CreateUnbounded<byte[]>();
            _stream = stream;
            _stream.WriteTimeout = StreamWriteTimeoutMs;
            _pipeReader = PipeReader.Create(_stream);
            _messageAssembler = new MessageAssembler(maxMessageBytes);
            _pinger = new PingTimer
            {
                Enabled = true,
                Interval = PingIntervalMs,
                AutoReset = true
            };

            _pinger.Elapsed += Ping;
        }

        /// <summary>
        /// Destructor
        /// </summary>
        ~Connection()
        {
            Dispose(false);
        }

        /// <summary>
        /// Close the connection
        /// </summary>
        /// <param name="reason">The reason the connection is being closed</param>
        /// <returns>A task that resolves when the connection has been closed</returns>
        public async Task Close(CloseStatusCode reason)
        {
            // Single-shot guard: State's check-then-set below isn't atomic on its own, and more
            // than one path can decide to close a connection concurrently (today: a received
            // Close frame vs. the server tearing every connection down on Stop(); future timeout
            // paths add more). Only the first caller should ever run the body.
            if (Interlocked.CompareExchange(ref _closeInitiated, 1, 0) != 0)
            {
                return;
            }

            if (State == ConnectionState.Open || State == ConnectionState.Connecting)
            {
                State = ConnectionState.Closing;
                _pinger.Stop();
                await EnqueueAsync(WebSocketFrame.CreateClose(reason));
                State = ConnectionState.Closed;
            }
        }

        /// <summary>
        /// Send a string over the connection
        /// </summary>
        /// <param name="data">The string to send</param>
        /// <returns>A task that resolves when the data has been sent</returns>
        public async Task Send(string data)
        {
            using var message = new WebSocketMessage(data);
            foreach (var frame in message.Pack(MaxFrameBytes))
            {
                await EnqueueAsync(frame);
            }
        }

        /// <summary>
        /// Send binary over the connection
        /// </summary>
        /// <param name="data">The binary to send</param>
        /// <returns>A task that resolves when the data has been sent</returns>
        public async Task Send(byte[] data)
        {
            using var message = new WebSocketMessage(data);
            foreach (var frame in message.Pack(MaxFrameBytes))
            {
                await EnqueueAsync(frame);
            }
        }

        /// <summary>
        /// Send a HTTP response over the connection
        /// </summary>
        /// <param name="res">The HTTP response object</param>
        /// <returns>A task that resolves when the response has been sent</returns>
        public async Task Send(HttpResponse res)
        {
            await _stream.WriteAndFlushAsync(res.ToString().GetBytes());
        }

        /// <summary>
        /// Send a pong frame over the connection
        /// </summary>
        /// <returns>A task that resolves when the frame has been sent</returns>
        internal async Task Pong()
        {
            await EnqueueAsync(WebSocketFrame.CreatePong());
        }

        /// <summary>
        /// Read frames asynchronously from the connection as they arrive, without allocating or
        /// copying the payload unless a frame straddles more than one of the Pipe's internal
        /// buffer segments.
        /// </summary>
        /// <remarks>
        /// Each yielded <c>WebSocketFrame</c>'s memory is only valid until the enumerator is
        /// advanced again (i.e. for the duration of a single loop iteration) - it may borrow the
        /// Pipe's internal read buffer, which can be reused/overwritten once more data is read.
        /// </remarks>
        /// <param name="cancellationToken">Cancels the read loop</param>
        internal async IAsyncEnumerable<WebSocketFrame> ReadFramesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await _pipeReader.ReadAsync(cancellationToken);
                var buffer = result.Buffer;

                while (WebSocketFrame.TryParse(ref buffer, out var frame))
                {
                    yield return frame;
                }

                _pipeReader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted || result.IsCanceled)
                {
                    yield break;
                }
            }
        }

        /// <summary>
        /// Append a received data frame to the message currently being assembled
        /// </summary>
        /// <param name="frame">The frame to append</param>
        /// <returns>
        ///     The completed message once <paramref name="frame"/> is the final frame of one;
        ///     <c>null</c> if more fragments are expected, or if the message was rejected for
        ///     exceeding the connection's max message size (in which case the connection is
        ///     closed with <see cref="CloseStatusCode.MessageTooBig"/>)
        /// </returns>
        internal async Task<WebSocketMessage> TryCompleteMessage(WebSocketFrame frame)
        {
            if (!_messageAssembler.TryAppend(frame, out var message))
            {
                await Close(CloseStatusCode.MessageTooBig);
                return null;
            }

            return message;
        }

        /// <summary>
        /// Update the LastPongReceived field
        /// </summary>
        /// <param name="dateTime"></param>
        internal void UpdateLastPong(DateTime dateTime)
        {
            PongRecieved = dateTime;
        }

        /// <summary>
        /// Dispose the connection
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                _messageAssembler.Dispose();
                _pipeReader.Complete();
                _stream?.Dispose();
                _pinger?.Dispose();
            }
            _disposed = true;
        }

        private async void Ping(object sender, ElapsedEventArgs elapsedEventArgs)
        {
            try
            {
                await EnqueueAsync(WebSocketFrame.CreatePing());
            }
            catch (IOException)
            {
                Dispose();
            }
        }

        private async Task EnqueueAsync(WebSocketFrame frame)
        {
            await EnqueueAsync(frame.Pack());
        }

        private async Task EnqueueAsync(byte[] frame)
        {
            await _channel.Writer.WriteAsync(frame);

            // Try to become the drainer. If another EnqueueAsync call already owns the drain
            // loop, trust it to pick up this frame - DrainAsync always rechecks the channel
            // immediately after releasing the guard, before it actually stops, so a frame
            // written in the handoff window is never stranded (see DrainAsync).
            if (Interlocked.CompareExchange(ref _draining, 1, 0) == 0)
            {
                await DrainAsync();
            }
        }

        // Writes every currently-queued frame to the stream, in order. Only one DrainAsync runs
        // at a time.
        //
        // The previous version of this used "if (_channel.Reader.Count == 1) drain()" as its
        // handoff signal, which raced under concurrent Send() calls: two writers could both
        // observe a count > 1 right after each other's write and neither would start a drain,
        // leaving frames queued indefinitely. This instead uses an atomic owner flag
        // (_draining), and - critically - re-checks the channel after releasing that flag before
        // exiting, so a frame written in the gap between "the drain loop found the channel empty"
        // and "the flag was reset" still gets picked up, either by this loop continuing or by
        // whichever writer wins the race to reclaim the flag.
        private async Task DrainAsync()
        {
            do
            {
                while (_channel.Reader.TryRead(out var frame))
                {
                    await _stream.WriteAndFlushAsync(frame);
                }

                Interlocked.Exchange(ref _draining, 0);
            }
            while (_channel.Reader.Count > 0 && Interlocked.CompareExchange(ref _draining, 1, 0) == 0);
        }
    }
}
