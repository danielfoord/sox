using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading;
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
    // FIXME: Comments
    public sealed class Connection : IDisposable
    {
        // Guid used to Id the connection
        public readonly string Id;

        // The state of the connection
        public volatile ConnectionState State;

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

        // FIXME: Comments
        public Connection(Stream stream)
        {
            Id = Guid.NewGuid().ToString();
            State = ConnectionState.Connecting;
            _stream = stream;
            _stream.WriteTimeout = 2000;
            _pinger = new PingTimer
            {
                Enabled = true,
                Interval = PingIntervalMs,
                AutoReset = true
            };

            _pinger.Elapsed += Ping;
        }

        ~Connection()
        {
            Dispose(false);
        }

        // FIXME: Comments
        public async Task<Frame> ReadFrameAsync()
        {
            return await Frame.UnpackAsync(_stream);
        }

        // FIXME: Comments
        public void UpdateLastPong(DateTime dateTime)
        {
            PongRecieved = dateTime;
        }

        // FIXME: Comments
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

        // FIXME: Comments
        public async Task Send(string data)
        {
            (await new Message(data).Pack(MaxFramePayloadSize))
                .ForEach(async (frame) =>
                    await _stream.WriteAndFlushAsync(frame));
        }

        // FIXME: Comments
        public async Task Send(byte[] data)
        {
            (await new Message(data).Pack(MaxFramePayloadSize))
                .ForEach(async (frame) =>
                    await _stream.WriteAndFlushAsync(frame));
        }

        // FIXME: Comments
        public async Task Send(HttpResponse res)
        {
            await _stream.WriteAndFlushAsync(Encoding.UTF8.GetBytes(res.ToString()));
        }

        // FIXME: Comments
        public async Task Pong()
        {
            await _stream.WriteAndFlushAsync(await Frame.CreatePong().PackAsync());
        }

        // FIXME: Comments
        public async void Ping(object sender, ElapsedEventArgs elapsedEventArgs)
        {
            await _stream.WriteAndFlushAsync(await Frame.CreatePing().PackAsync());
        }

        // FIXME: Comments
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

        /// <summary>
        ///     This is called when the final message frame has been received
        /// </summary>
        internal async Task<Message> UnpackMessage()
        {
            var message = await Message.Unpack(Frames);
            Frames.Clear();
            return message;
        }
    }
}
