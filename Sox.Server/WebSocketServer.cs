using System;
using System.IO;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Sox.Core.Extensions;
using Sox.Core.Http;
using Sox.Core.Websocket.Rfc6455;
using Sox.Core.Websocket.Rfc6455.Framing;
using Sox.Core.Websocket.Rfc6455.Messaging;
using Sox.Server.Events;
using Sox.Server.State;
using HttpStatusCode = Sox.Core.Http.HttpStatusCode;
using System.Net.Security;
using System.Security.Authentication;

namespace Sox.Server
{
    /// <summary>
    /// Websocket Server
    /// </summary>
    public class WebSocketServer : IWebSocketServer, IDisposable
    {
        private const string WebsocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

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
        public readonly int MaxFramePayloadBytes;

        /// <summary>
        /// The amount of active connections
        /// </summary>
        public long ConnectionCount = 0;

        /// <summary>
        /// The server protocol
        /// </summary>
        public Protocol Protocol;

        private CancellationTokenSource _cancellationTokenSource;

        private TcpListener _server;

        private readonly ConcurrentDictionary<string, Connection> _connections = new ConcurrentDictionary<string, Connection>();

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <param name="ipAddress">The IPAddress to bind to</param>
        /// <param name="port">The port to bind to</param>
        /// <param name="maxFramePayloadBytes">The Maxmimum amount of bytes a frame can contain</param>
        /// <param name="x509Certificate">The SSL certificate for wss</param>
        public WebSocketServer(IPAddress ipAddress,
            int port,
            int? maxFramePayloadBytes = null,
            X509Certificate2 x509Certificate = null)
        {
            _cancellationTokenSource = new CancellationTokenSource();
            IpAddress = ipAddress;
            Port = port;
            MaxFramePayloadBytes = maxFramePayloadBytes ?? 50.Kilobytes();
            X509Certificate = x509Certificate;
            Protocol = X509Certificate == null ? Protocol.Ws : Protocol.Wss;
        }

        /// <summary>
        /// Destructor
        /// </summary>
        ~WebSocketServer()
        {
            Dispose(false);
        }

        /// <inhertidoc/>
        public async void Start()
        {
            _server = new TcpListener(IpAddress, Port);
            _server.Start();

            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                TcpClient client = null;

                try
                {
                    client = await _server.AcceptTcpClientAsync();
                    await HandleHttpUpgrade(client);
                }
                catch (Exception ex)
                {
                    if (!(ex is ObjectDisposedException))
                    {
                        Console.WriteLine(ex);
                        client?.Close();
                    }
                }
            }
        }

        /// <inhertidoc/>
        public async Task Stop()
        {
            _cancellationTokenSource.Cancel();
            var connectionIds = _connections.Keys.ToList();
            foreach (var id in connectionIds)
            {
                await CloseConnection(_connections[id], CloseStatusCode.GoingAway);
            }

            _server.Stop();
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
            if (disposing)
            {
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private async Task HandleHttpUpgrade(TcpClient client)
        {
            Stream stream = null;
            if (X509Certificate != null)
            {
                stream = new SslStream(client.GetStream());
                var secureStream = stream as SslStream;
                await secureStream.AuthenticateAsServerAsync(X509Certificate,
                    clientCertificateRequired: false,
                    enabledSslProtocols: SslProtocols.Tls12,
                    checkCertificateRevocation: true);
            }
            else
            {
                stream = client.GetStream();
            }


            try
            {
                var httpRequest = await HttpRequest.ReadAsync(stream);
                if (httpRequest.Headers.IsWebSocketUpgrade)
                {
                    await ProcessHandshake(stream, httpRequest);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Invalid Http Request: {ex}");
                stream.Close();
                stream.Dispose();
            }
        }

        private async Task ProcessHandshake(Stream stream, HttpRequest httpRequest)
        {
            var key = $"{httpRequest.Headers.SecWebSocketKey}{WebsocketGuid}";

            using (var sha1 = SHA1.Create())
            {
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
                var connection = new Connection(stream);

                // Begin sending the data to the remote device.  
                await connection.Send(response);

                connection.State = ConnectionState.Open;

                _connections[connection.Id] = connection;

                Interlocked.Increment(ref ConnectionCount);

                OnConnection?.Invoke(this, new OnConnectionEventArgs(connection));

                await StartClientHandler(connection);
            }
        }

        private async Task StartClientHandler(Connection connection)
        {
            await Task.Factory.StartNew(async (state) =>
            {
                while (connection.State == ConnectionState.Open)
                {
                    try
                    {
                        var frame = await connection.ReadFrameAsync();

                        OnFrame?.Invoke(this, new OnFrameEventArgs(connection, frame));

                        if (frame.PayloadLength > MaxFramePayloadBytes)
                        {
                            await CloseConnection(connection, CloseStatusCode.MessageTooBig);
                            continue;
                        }

                        await HandleFrame(connection, frame);
                    }
                    catch (Exception ex)
                    {
                        if (!_cancellationTokenSource.IsCancellationRequested)
                        {
                            OnError?.Invoke(this, new OnErrorEventArgs(connection, ex));
                            await connection.Close(CloseStatusCode.ProtocolError);
                        }
                    }
                }

            }, TaskCreationOptions.AttachedToParent, cancellationToken: _cancellationTokenSource.Token);
        }

        private async Task HandleFrame(Connection connection, Frame frame)
        {
            switch (frame.OpCode)
            {
                case OpCode.Binary:
                case OpCode.Text:
                case OpCode.Continuation:
                    await OnDataFrame(frame, connection);
                    break;
                case OpCode.Close:
                    await OnCloseFrame(connection);
                    break;
                case OpCode.Ping:
                    await OnPingFrame(connection);
                    break;
                case OpCode.Pong:
                    OnPongFrame(connection);
                    break;
                default:
                    await CloseConnection(connection, CloseStatusCode.ProtocolError);
                    break;
            }
        }

        private async Task OnDataFrame(Frame frame, Connection connection)
        {
            connection.Frames.Add(frame);
            if (frame.Headers.IsFinal)
            {
                var message = await connection.UnpackMessage();

                switch (message.Type)
                {
                    case MessageType.Binary:
                        OnBinaryMessage?.Invoke(this, new OnBinaryMessageEventArgs(connection, message.Data));
                        break;
                    case MessageType.Text:
                        OnTextMessage?.Invoke(this, new OnTextMessageEventArgs(connection, message.Data.GetString()));
                        break;
                    default:
                        await CloseConnection(connection, CloseStatusCode.ProtocolError);
                        break;
                }
            }
        }

        private async Task OnCloseFrame(Connection connection)
        {
            if (!_cancellationTokenSource.IsCancellationRequested)
            {
                await CloseConnection(connection, CloseStatusCode.Normal);
            }
        }

        private static async Task OnPingFrame(Connection connection)
        {
            await connection.Pong();
        }

        private static void OnPongFrame(Connection connection)
        {
            connection.UpdateLastPong(DateTime.Now);
        }

        private async Task CloseConnection(Connection connection, CloseStatusCode reason)
        {
            await connection.Close(reason);
            Interlocked.Decrement(ref ConnectionCount);
            OnDisconnection?.Invoke(this, new OnDisconnectionEventArgs(connection));
            RemoveConnection(connection.Id);
        }

        private void RemoveConnection(string id)
        {
            if (_connections.TryRemove(id, out var removed))
            {
                removed.Dispose();
            }
        }
    }
}
