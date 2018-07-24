using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sox.Core.Extensions;
using Sox.Core.Http;
using Sox.Core.Websocket.Rfc6455;
using Sox.Core.Websocket.Rfc6455.Frames;
using Sox.Core.Websocket.Rfc6455.Messaging;
using Sox.Server.Events;
using Sox.Server.State;
using HttpStatusCode = Sox.Core.Http.HttpStatusCode;

namespace Sox.Server
{
    public class WebSocketServer : IWebSocketServer, IDisposable
    {
        public const string WebsocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        public const int MaxFramePayloadLen = 4082;

        private CancellationTokenSource _cancellationTokenSource;

        private TcpListener _server;

        private readonly ConcurrentDictionary<string, Connection> _connections = new ConcurrentDictionary<string, Connection>();

        public EventHandler<OnConnectionEventArgs> OnConnection;

        public EventHandler<OnDisconnectionEventArgs> OnDisconnection;

        public EventHandler<OnTextMessageEventArgs> OnTextMessage;

        public EventHandler<OnBinaryMessageEventArgs> OnBinaryMessage;

        public WebSocketServer()
        {
            _cancellationTokenSource = new CancellationTokenSource();
        }

        public async void Start(IPAddress ipAddress, int port)
        {
            // TcpListener server = new TcpListener(port);
            _server = new TcpListener(ipAddress, port);
            _server.Start();

            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    Console.Write("#Waiting for a connection... ");
                    var client = await _server.AcceptTcpClientAsync();
                    await HandleHttpUpgrade(client);
                }
                catch (Exception ex)
                {
                    if (!(ex is ObjectDisposedException))
                    {
                        Console.WriteLine(ex);
                    }
                }
            }
        }

        public async Task HandleHttpUpgrade(TcpClient client)
        {
            try
            {
                var buffer = new byte[2048];
                var stream = client.GetStream();
                var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead > 0)
                {
                    var content = buffer.GetString();
                    if (HttpRequest.TryParse(content, out var httpRequest))
                    {
                        if (httpRequest.Headers.IsWebSocketUpgrade)
                        {
                            Console.WriteLine($"Received WS Upgrade request from : {httpRequest.Headers.Origin}");
                            await ProcessHandshake(client, httpRequest);
                        }
                        else
                        {
                            client.Close();
                        }
                    }
                    else
                    {
                        Console.WriteLine("Invalid HTTP request. Closing connection");
                        client.Close();
                    }
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception);
            }
        }

        public async Task ProcessHandshake(TcpClient client, HttpRequest httpRequest)
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

                var responseBytes = Encoding.UTF8.GetBytes(response.ToString());

                // Retrieve the socket from the state object.  
                var connection = new Connection(client);

                // Begin sending the data to the remote device.  
                await connection.Send(responseBytes);

                connection.State = ConnectionState.Open;

                _connections[connection.Id] = connection;

                OnConnection?.Invoke(this, new OnConnectionEventArgs(connection));

                Console.WriteLine($"CID: {connection.Id} Connected | {_connections.Count:N0} connections open");

                await StartClientHandler(connection);
            }
        }

        private async Task StartClientHandler(Connection connection)
        {
            await Task.Factory.StartNew(async () =>
            {
                while (connection.State == ConnectionState.Open)
                {
                    try
                    {
                        var bytesRead = await connection.Receive(connection.Buffer);

                        if (bytesRead > 0)
                        {
                            if (Frame.TryUnpack(connection.Buffer, out var frame))
                            {
                                Console.WriteLine($"CID: {connection.Id} | Received WS Frame ({frame.OpCode.ToString()}) | : {frame.PayloadLength:N0} bytes | {bytesRead:N0} bytes read");

                                if (frame.PayloadLength > MaxFramePayloadLen)
                                {
                                    Console.WriteLine($"CID: {connection.Id} | Frame payload lenth {frame.PayloadLength:N0} exceeds {MaxFramePayloadLen:N0}. Disconnecting client");
                                    await connection.Close(CloseStatusCode.MessageTooBig);
                                }

                                if (connection.State == ConnectionState.Open)
                                {
                                    connection.HasFinalFrame = frame.IsFinal;

                                    switch (frame.OpCode)
                                    {
                                        case OpCode.Continuation:
                                            connection.Frames.Add(frame);
                                            if (connection.HasFinalFrame)
                                            {
                                                var message = Message.Unpack(connection.Frames);
                                                connection.Frames.Clear();

                                                switch (message.Type)
                                                {
                                                    case MessageType.Binary:
                                                        OnBinaryMessage?.Invoke(this, new OnBinaryMessageEventArgs(connection, message.Data));
                                                        break;
                                                    case MessageType.Text:
                                                        OnTextMessage?.Invoke(this, new OnTextMessageEventArgs(connection, message.Data.GetString()));
                                                        break;
                                                    default:
                                                        await connection.Close(CloseStatusCode.ProtocolError);
                                                        break;
                                                }
                                            }
                                            break;

                                        case OpCode.Text:
                                            connection.Frames.Add(frame);
                                            if (connection.HasFinalFrame)
                                            {
                                                var message = Message.Unpack(connection.Frames);
                                                connection.Frames.Clear();
                                                OnTextMessage?.Invoke(this, new OnTextMessageEventArgs(connection, message.Data.GetString()));
                                            }
                                            break;

                                        case OpCode.Binary:
                                            connection.Frames.Add(frame);
                                            if (connection.HasFinalFrame)
                                            {
                                                var message = Message.Unpack(connection.Frames);
                                                connection.Frames.Clear();
                                                OnBinaryMessage?.Invoke(this, new OnBinaryMessageEventArgs(connection, message.Data));
                                            }
                                            break;

                                        case OpCode.Close:
                                            await connection.Close(CloseStatusCode.Normal);
                                            break;

                                        case OpCode.Ping:
                                            await connection.Pong();
                                            break;

                                        case OpCode.Pong:
                                            connection.UpdateLastPong();
                                            break;

                                        default:
                                            await connection.Close(CloseStatusCode.ProtocolError);
                                            break;
                                    }
                                }
                            }
                            else
                            {
                                await connection.Close(CloseStatusCode.ProtocolError);
                            }
                        }
                    }
                    catch (SocketException e)
                    {
                        Console.WriteLine(e);

                        if (_connections.TryRemove(connection.Id, out var removed))
                        {
                            removed.Dispose();
                            Console.WriteLine($"CID: {connection.Id} Disconnected | {_connections.Count:N0} connections open");
                        }

                        break;
                    }
                }

                if (_connections.TryRemove(connection.Id, out var cleanClose))
                {
                    await cleanClose.Close(CloseStatusCode.Normal);
                    Console.WriteLine($"CID: {connection.Id} Disconnected | {_connections.Count:N0} connections open");
                }

                OnDisconnection?.Invoke(this, new OnDisconnectionEventArgs(connection));

            }, TaskCreationOptions.AttachedToParent);
        }

        public async void Stop()
        {
            var connectionIds = _connections.Keys.ToList();
            foreach (var id in connectionIds)
            {
                if (!_connections.TryRemove(id, out var removed)) return;
                await removed.Close(CloseStatusCode.GoingAway);
                OnDisconnection?.Invoke(this, new OnDisconnectionEventArgs(removed));
                Console.WriteLine($"CID: {id} Disconnected | {_connections.Count:N0} connections open");
            }
           
            _server.Stop();

            _cancellationTokenSource.Cancel();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposing) return;
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
        }
    }
}
