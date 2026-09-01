using Sox.Extensions;
using Sox.Http;
using Sox.Server.Events;
using Sox.Server.State;
using Sox.Websocket.Rfc6455;
using Sox.Websocket.Rfc6455.Framing;
using Sox.Websocket.Rfc6455.Messaging;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using HttpStatusCode = Sox.Http.HttpStatusCode;

namespace Sox.Server
{
    /// <summary>
    /// Websocket Server
    /// </summary>
    public class WebSocketServer : IWebSocketServer, IDisposable
    {
        private const string WebsocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        // SslProtocols.Tls13 isn't part of the netstandard2.1 reference assembly's SslProtocols
        // enum (it was added to the runtime after netstandard2.1 was frozen) - this numeric value
        // matches the real enum member and resolves correctly at runtime on any target that does
        // support it.
        private const SslProtocols Tls13 = (SslProtocols)12288;

        /// <summary>
        /// The IP Address that the server should bind to
        /// </summary>
        public readonly IPAddress IpAddress;

        /// <summary>
        /// The port that the server should bind to
        /// </summary>
        public readonly int Port;

        /// <summary>
        /// The SSL Certificate if the server needs to use the wss protocol
        /// </summary>
        public readonly X509Certificate2 X509Certificate;

        /// <summary>
        /// Fires when a new connection is established
        /// </summary>
        public EventHandler<OnConnectionEventArgs> OnConnection;

        /// <summary>
        /// Fires when a connection is severed
        /// </summary>
        public EventHandler<OnDisconnectionEventArgs> OnDisconnection;

        /// <summary>
        /// Fired when a Message containing a text body is received
        /// </summary>
        public EventHandler<OnTextMessageEventArgs> OnTextMessage;

        /// <summary>
        /// Fires when a Message containing a binary body is received
        /// </summary>
        public EventHandler<OnBinaryMessageEventArgs> OnBinaryMessage;

        /// <summary>
        /// Fires when an unhandled exception occours
        /// </summary>
        public EventHandler<OnErrorEventArgs> OnError;

        /// <summary>
        /// Fires when a Websocket frame is received
        /// </summary>
        public EventHandler<OnFrameEventArgs> OnFrame;

        /// <summary>
        /// The Maxmimum amount of bytes a frame can contain
        /// Note: A larger message can be sent by using more than one Frame
        /// </summary>
        public readonly int MaxFrameBytes;

        /// <summary>
        /// The Maxmimum amount of bytes a message can contain
        /// </summary>
        public readonly int MaxMessageBytes;

        /// <summary>
        /// The amount of active connections
        /// </summary>
        public long ConnectionCount => _connections.Count;

        /// <summary>
        /// The server protocol
        /// </summary>
        public readonly Protocol Protocol;

        /// <summary>
        /// The period of time before a connection will timeout on read
        /// </summary>
        public readonly int ConnectionReadTimeoutMs;

        private CancellationTokenSource _cancellationTokenSource;

        private TcpListener _server;

        private readonly ConcurrentDictionary<string, Connection> _connections = new ConcurrentDictionary<string, Connection>();

        // Every task spawned per accepted client (HandleHttpUpgrade -> ProcessHandshake ->
        // StartClientHandler is one awaited call chain, so tracking the outer task here covers
        // all of it), so Stop() can wait for a full drain - including a client still mid-handshake
        // that never made it into _connections.
        private readonly ConcurrentDictionary<Task, byte> _clientTasks = new();

        private bool _disposed;

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <param name="ipAddress">The IPAddress to bind to</param>
        /// <param name="port">The port to bind to</param>
        /// <param name="maxMessageBytes">The Maxmimum amount of bytes a message can contain</param>
        /// <param name="x509Certificate">The SSL certificate for wss</param>
        /// <param name="connectionReadTimeoutMs"></param>
        public WebSocketServer(IPAddress ipAddress,
            int port,
            int? maxMessageBytes = default,
            X509Certificate2 x509Certificate = default,
            int connectionReadTimeoutMs = 5000)
        {
            _cancellationTokenSource = new CancellationTokenSource();
            IpAddress = ipAddress;
            Port = port;
            MaxMessageBytes = maxMessageBytes ?? 10.Megabytes();
            X509Certificate = x509Certificate;
            Protocol = X509Certificate == null ? Protocol.Ws : Protocol.Wss;
            ConnectionReadTimeoutMs = connectionReadTimeoutMs;
        }

        /// <summary>
        /// Destructor
        /// </summary>
        ~WebSocketServer()
        {
            Dispose(false);
        }

        /// <inheritdoc/>
        public async Task Start()
        {
            _server = new TcpListener(IpAddress, Port);
            _server.Start();

            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                TcpClient client = null;

                try
                {
                    client = await _server.AcceptTcpClientAsync();
                    TrackClientTask(Task.Run(() => HandleHttpUpgrade(client)));
                }
                catch (Exception ex)
                {
                    client?.Close();
                    if (!_cancellationTokenSource.IsCancellationRequested)
                    {
                        OnError?.Invoke(this, new OnErrorEventArgs(null, ex));
                    }
                }
            }
        }

        /// <inheritdoc/>
        public async Task Stop()
        {
            // Capture, and guard against Stop() being called again after Dispose() already ran
            // (e.g. a consumer wiring both Console.CancelKeyPress and
            // AssemblyLoadContext.Unloading to the same shutdown routine - the latter also fires
            // on ordinary process exit, after a first Stop()+Dispose() already completed).
            var cancellationTokenSource = _cancellationTokenSource;
            if (cancellationTokenSource == null)
            {
                return;
            }

            // Cancel and stop accepting before draining, so no new connection can land mid-shutdown
            // - this also unblocks a pending AcceptTcpClientAsync() in the loop above.
            cancellationTokenSource.Cancel();
            _server?.Stop();

            foreach (var connection in _connections.Values.ToArray())
            {
                await CloseConnection(connection, CloseStatusCode.GoingAway);
            }

            // Wait for every in-flight client task - including one still mid-handshake that never
            // made it into _connections - so Stop() only returns once fully drained.
            await Task.WhenAll(_clientTasks.Keys.ToArray());
        }

        private void TrackClientTask(Task task)
        {
            _clientTasks[task] = 0;
            task.ContinueWith(t => _clientTasks.TryRemove(t, out _), TaskScheduler.Default);
        }

        private async Task HandleHttpUpgrade(TcpClient client)
        {
            Stream stream = null;

            try
            {
                stream = X509Certificate != null
                    ? new SslStream(client.GetStream())
                    : client.GetStream();

                if (stream is SslStream sslStream)
                {
                    await sslStream.AuthenticateAsServerAsync(X509Certificate,
                        clientCertificateRequired: false,
                        enabledSslProtocols: SslProtocols.Tls12 | Tls13,
                        checkCertificateRevocation: true);
                }

                stream.ReadTimeout = ConnectionReadTimeoutMs;

                var httpRequest = await HttpRequest.ReadAsync(stream);
                if (httpRequest != null)
                {
                    if (httpRequest.Headers.IsWebSocketUpgrade)
                    {
                        await ProcessHandshake(stream, httpRequest);
                    }
                    else
                    {
                        // TODO: Not websocket request, return HTTP response (https://github.com/danielfoord/sox/issues/6)
                    }
                }
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, new OnErrorEventArgs(null, ex));
                if (stream != null)
                {
                    stream.Close();
                    await stream.DisposeAsync();
                }
            }
        }

        private async Task ProcessHandshake(Stream stream, HttpRequest httpRequest)
        {
            using var sha1 = SHA1.Create();
            var key = $"{httpRequest.Headers.SecWebSocketKey}{WebsocketGuid}";
            var hash = sha1.ComputeHash(key.GetBytes());
            var acceptKey = Convert.ToBase64String(hash);

            var response = new HttpResponse
            {
                StatusCode = HttpStatusCode.SwitchingProtocols,
                Headers = new HttpResponseHeaders
                    {
                        { "Upgrade", "websocket"},
                        { "Connection", "Upgrade" },
                        { "Sec-WebSocket-Accept", acceptKey }
                    }
            };

            // Retrieve the socket from the state object.
            var connection = new Connection(stream, MaxMessageBytes);
            // Begin sending the data to the remote device.
            await connection.Send(response);
            connection.State = ConnectionState.Open;

            _connections[connection.Id] = connection;
            OnConnection?.Invoke(this, new OnConnectionEventArgs(connection));

            await StartClientHandler(connection);
        }

        private async Task StartClientHandler(Connection connection)
        {
            // Should be Task.Run, Task.Factory.StartNew doesn't handle async properly
            await Task.Run(async () =>
            {
                try
                {
                    await foreach (var frame in connection.ReadFramesAsync(_cancellationTokenSource.Token))
                    {
                        OnFrame?.Invoke(this, new OnFrameEventArgs(connection, frame));
                        await HandleFrame(connection, frame);

                        if (connection.State != ConnectionState.Open)
                        {
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (!_cancellationTokenSource.IsCancellationRequested)
                    {
                        OnError?.Invoke(this, new OnErrorEventArgs(connection, ex));
                        await CloseConnection(connection, CloseStatusCode.ProtocolError);
                    }
                }
            }, cancellationToken: _cancellationTokenSource.Token);
        }

        private async Task HandleFrame(Connection connection, WebSocketFrame frame)
        {
            // Close connection if not masked 
            // See: https://tools.ietf.org/html/rfc6455#section-5.1
            if (!frame.Headers.ShouldMask)
            {
                await CloseConnection(connection, CloseStatusCode.ProtocolError);
                return;
            }

            switch (frame.OpCode)
            {
                case OpCode.Binary or OpCode.Text or OpCode.Continuation when connection.State == ConnectionState.Open:
                    await HandleDataFrame(frame, connection);
                    break;
                case OpCode.Close:
                    await HandleCloseFrame(connection);
                    break;
                case OpCode.Ping:
                    await HandlePingFrame(connection);
                    break;
                case OpCode.Pong:
                    HandlePongFrame(connection);
                    break;
                default:
                    await CloseConnection(connection, CloseStatusCode.ProtocolError);
                    break;
            }
        }

        private async Task HandleDataFrame(WebSocketFrame frame, Connection connection)
        {
            using var message = await connection.TryCompleteMessage(frame);
            if (message == null)
            {
                // Either more fragments are expected, or the message was rejected for exceeding
                // the max message size (connection.TryCompleteMessage already closed it).
                return;
            }

            switch (message.Type)
            {
                case MessageType.Binary:
                    OnBinaryMessage?.Invoke(this, new OnBinaryMessageEventArgs(connection, message.Data));
                    break;
                case MessageType.Text:
                    OnTextMessage?.Invoke(this, new OnTextMessageEventArgs(connection, message.Data));
                    break;
                default:
                    await CloseConnection(connection, CloseStatusCode.ProtocolError);
                    break;
            }
        }

        private async Task HandleCloseFrame(Connection connection)
        {
            if (!_cancellationTokenSource.IsCancellationRequested)
            {
                await CloseConnection(connection, CloseStatusCode.Normal);
            }
        }

        private static async Task HandlePingFrame(Connection connection)
        {
            await connection.Pong();
        }

        private static void HandlePongFrame(Connection connection)
        {
            connection.UpdateLastPong(DateTime.Now);
        }

        private async Task CloseConnection(Connection connection, CloseStatusCode reason)
        {
            await connection.Close(reason);
            RemoveConnection(connection.Id);
            OnDisconnection?.Invoke(this, new OnDisconnectionEventArgs(connection));
        }

        private void RemoveConnection(string id)
        {
            if (_connections.TryRemove(id, out var removed))
            {
                removed.Dispose();
            }
        }

        /// <summary>
        /// Dispose of all unmanaged resources
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
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                _server?.Stop();
                X509Certificate?.Dispose();
            }

            _disposed = true;
        }
    }
}
