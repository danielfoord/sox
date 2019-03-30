using System;
using System.IO;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
    // FIXME: Comments
    public class WebSocketServer : IWebSocketServer, IDisposable
    {
        private const string WebsocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        // FIXME: Comments
        public long ConnectionCount = 0;

        public EventHandler<OnConnectionEventArgs> OnConnection;

        // FIXME: Comments
        public EventHandler<OnDisconnectionEventArgs> OnDisconnection;

        // FIXME: Comments
        public EventHandler<OnTextMessageEventArgs> OnTextMessage;

        // FIXME: Comments
        public EventHandler<OnBinaryMessageEventArgs> OnBinaryMessage;

        // FIXME: Comments
        public EventHandler<OnErrorEventArgs> OnError;

        // FIXME: Comments
        public readonly int MaxFramePayloadBytes;

        // FIXME: Comments
        public IEnumerable<string> Connections => _connections.Keys;

        private CancellationTokenSource _cancellationTokenSource;

        private TcpListener _server;

        private readonly ConcurrentDictionary<string, Connection> _connections = new ConcurrentDictionary<string, Connection>();

        // FIXME: Comments
        public readonly IPAddress IpAddress;

        // FIXME: Comments
        public readonly int Port;

        private X509Certificate2 _x509CertificateFile = null;

        // FIXME: Comments
        public WebSocketServer(IPAddress ipAddress, 
            int port, 
            int? maxFramePayloadBytes = null, 
            string x509CertificateFile = null,
            string x509CertificatePassword = null)
        {
            _cancellationTokenSource = new CancellationTokenSource();
            IpAddress = ipAddress;
            Port = port;
            MaxFramePayloadBytes = maxFramePayloadBytes ?? 50.Kilobytes();
            LoadX509Certificate(x509CertificateFile, x509CertificatePassword);
        }

        ~WebSocketServer()
        {
            Dispose(false);
        }

        // FIXME: Comments
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

        // FIXME: Comments
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
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private void LoadX509Certificate(string x509CertificateFile, string x509CertificatePassword)
        {
            if (x509CertificateFile != null)
            {
                if (!File.Exists(x509CertificateFile))
                {
                    throw new FileNotFoundException(x509CertificateFile);
                }

                this._x509CertificateFile = new X509Certificate2(x509CertificateFile, x509CertificatePassword);
            }
        }

        private async Task HandleHttpUpgrade(TcpClient client)
        {
            Stream stream = null;
            if (_x509CertificateFile != null)
            {
                stream = new SslStream(client.GetStream());
                var secureStream = stream as SslStream;
                await secureStream.AuthenticateAsServerAsync(_x509CertificateFile,
                    clientCertificateRequired: false,
                    enabledSslProtocols: SslProtocols.Tls12,
                    checkCertificateRevocation: true);
#if DEBUG
                Console.WriteLine($"Is Encrypted: {secureStream.IsEncrypted}");
                Console.WriteLine($"Cipher: {secureStream.CipherAlgorithm} strength {secureStream.CipherStrength}");
                Console.WriteLine($"Hash: {secureStream.HashAlgorithm} strength {secureStream.HashStrength}");
                Console.WriteLine($"Key exchange: {secureStream.KeyExchangeAlgorithm} strength { secureStream.KeyExchangeStrength}");
                Console.WriteLine($"Protocol: {secureStream.SslProtocol}");
#endif
            }
            else
            {
                stream = client.GetStream();
            }

            // FIXME: This should be streamed
            var buffer = new byte[2048];
            var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);

            if (bytesRead > 0)
            {
                var content = buffer.GetString();
                if (HttpRequest.TryParse(content, out var httpRequest))
                {
                    if (httpRequest.Headers.IsWebSocketUpgrade)
                    {
                        await ProcessHandshake(stream, httpRequest);
                    }
                }
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
                    Headers = new HttpHeaders
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

                        // TODO: Remove this/add frame listener
#if DEBUG
                        Console.WriteLine($"CID: {connection.Id} | Received WS Frame ({frame.OpCode.ToString()}) | : Plength - {frame.PayloadLength:N0}");
#endif

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
