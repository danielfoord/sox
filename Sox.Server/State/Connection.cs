using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Timers;
using Sox.Core.Websocket.Rfc6455;
using Sox.Core.Websocket.Rfc6455.Frames;
using Sox.Core.Websocket.Rfc6455.Messaging;

namespace Sox.Server.State
{
    public class Connection : IDisposable
    {
        // Guid used to Id the connection
        public readonly string Id;
        // The state of the connection
        public volatile ConnectionState State;
        // The status code used to close the websocket connection
        public volatile CloseStatusCode CloseStatusCode;
        // Last time a pong message was received
        public DateTime LastPongReceived { get; private set; }
        // Is the underlying Socket connected
        public bool IsConnected => _tcpClient.Connected;
        // Client socket.  
        private TcpClient _tcpClient;
        // TcpClient underlying stream
        private readonly NetworkStream _stream;
        // The socket address
        public string Address => _tcpClient.Client.RemoteEndPoint.ToString();
        // Size of receive buffer.  
        internal const int BufferSize = 4096;
        // How long to wait between pings to this connection
        internal const int PingIntervalMs = 60000;
        // Receive buffer.  
        internal byte[] Buffer = new byte[BufferSize];
        // Websocket frames.
        internal List<Frame> Frames = new List<Frame>();
        // The current message size in bytes
        internal ulong CurrentMessageSize => Convert.ToUInt64(Frames.Sum(frame => frame.PayloadLength));
        // Does the connection have the final frame of the message
        internal bool HasFinalFrame;
        // Scheduled pinger
        private Timer _pinger;

        public Connection(TcpClient tcpClient)
        {
            Id = Guid.NewGuid().ToString();
            State = ConnectionState.Connecting;
            LastPongReceived = DateTime.Now;
            _tcpClient = tcpClient;
            _stream = _tcpClient.GetStream();

            _pinger = new Timer
            {
                Enabled = true,
                Interval = PingIntervalMs,
                AutoReset = true
            };

            _pinger.Elapsed += Ping;
        }

        public void UpdateLastPong()
        {
            LastPongReceived = DateTime.Now;
        }

        public async Task Close(CloseStatusCode reason)
        {
            if (State == ConnectionState.Open || State == ConnectionState.Connecting)
            {
                State = ConnectionState.Closing;
                CloseStatusCode = reason;
                _pinger.Stop();
                var closeFrame = Frame.CreateClose(reason);
                var closeFrameBytes = closeFrame.Pack();
                await _stream.WriteAsync(closeFrameBytes, 0, closeFrameBytes.Length);
                _tcpClient?.Close();
                State = ConnectionState.Closed;
            }
        }

        public async Task<int> Receive(byte[] buffer)
        {
            return await _stream.ReadAsync(buffer, 0, buffer.Length);
        }

        public async Task Send(string data)
        {
            var message = new Message(data);
            var frames = message.Pack(WebSocketServer.MaxFramePayloadLen).Select(frame => frame.Pack());
            foreach (var frame in frames)
            {
                await _stream.WriteAsync(frame, 0, frame.Length);
                await _stream.FlushAsync();
            }
        }

        public async Task Send(byte[] data)
        {
            var message = new Message(data);
            var frames = message.Pack(WebSocketServer.MaxFramePayloadLen).Select(frame => frame.Pack());
            foreach (var frame in frames)
            {
                await _stream.WriteAsync(frame, 0, frame.Length);
                await _stream.FlushAsync();
            }
        }

        public async Task Pong()
        {
            var frame = Frame.CreatePong().Pack();
            await _stream.WriteAsync(frame, 0, frame.Length);
            await _stream.FlushAsync();
        }

        public void Ping(object sender, ElapsedEventArgs elapsedEventArgs)
        {
            var frame = Frame.CreatePing().Pack();
            _tcpClient.Client.Send(frame);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _tcpClient?.Dispose();
                _tcpClient = null;
                _pinger?.Dispose();
                _pinger = null;
            }
        }
    }
}
