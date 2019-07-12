using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Timers;
using PingTimer = System.Timers.Timer;
using Sox.Core.Http;
using Sox.Core.Websocket.Rfc6455;
using Sox.Core.Websocket.Rfc6455.Framing;
using Sox.Core.Websocket.Rfc6455.Messaging;
using Sox.Core.Extensions;

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

        // Size of receive buffer.  
        internal const int MaxFramePayloadSize = 4096;

        // How long to wait between pings to this connection
        internal const int PingIntervalMs = 60000;

        // Websocket frames.
        internal List<Frame> Frames = new List<Frame>();

        // Scheduled pinger
        private PingTimer _pinger;

        // How long shoud we wait for a stream write before timing out
        private int StreamWriteTimeoutMs = 2000;

        /// <summary>
        /// Contruct a connection
        /// </summary>
        /// <param name="stream">The underlying connection stream</param>
        public Connection(Stream stream)
        {
            Id = Guid.NewGuid().ToString();
            State = ConnectionState.Connecting;
            _stream = stream;
            _stream.WriteTimeout = StreamWriteTimeoutMs;
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
        /// Read a frame asynchronously from the connection
        /// </summary>
        /// <returns></returns>
        public async Task<Frame> ReadFrameAsync()
        {
            return await Frame.UnpackAsync(_stream);
        }

        /// <summary>
        /// Update the LastPongReceived field
        /// </summary>
        /// <param name="dateTime"></param>
        public void UpdateLastPong(DateTime dateTime)
        {
            PongRecieved = dateTime;
        }

        /// <summary>
        /// Close the connection
        /// </summary>
        /// <param name="reason">The reason the connection is being closed</param>
        /// <returns>A task that resolves when the connection has been closed</returns>
        public async Task Close(CloseStatusCode reason)
        {
            if (State == ConnectionState.Open || State == ConnectionState.Connecting)
            {
                State = ConnectionState.Closing;
                _pinger.Stop();
                await _stream.WriteAndFlushAsync(
                    await Frame.CreateClose(reason).PackAsync());
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
            (await new Message(data).Pack(MaxFramePayloadSize))
                .ForEach(async (frame) =>
                    await _stream.WriteAndFlushAsync(frame));
        }

        /// <summary>
        /// Send binary over the connection
        /// </summary>
        /// <param name="data">The binary to send</param>
        /// <returns>A task that resolves when the data has been sent</returns>
        public async Task Send(byte[] data)
        {
            (await new Message(data).Pack(MaxFramePayloadSize))
                .ForEach(async (frame) =>
                    await _stream.WriteAndFlushAsync(frame));
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
        public async Task Pong()
        {
            await _stream.WriteAndFlushAsync(await Frame.CreatePong().PackAsync());
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
            if (disposing)
            {
                _stream?.Dispose();
                _pinger?.Dispose();
            }
        }

        private async void Ping(object sender, ElapsedEventArgs elapsedEventArgs)
        {
            await _stream.WriteAndFlushAsync(await Frame.CreatePing().PackAsync());
        }

        internal async Task<Message> UnpackMessage()
        {
            var message = await Message.Unpack(Frames);
            Frames.Clear();
            return message;
        }
    }
}
