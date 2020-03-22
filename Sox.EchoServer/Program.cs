using Sox.Server;
using Sox.Server.Events;
using System;
using System.Net;
using System.Runtime.Loader;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace Sox.EchoServer
{
    public class Program
    {
        private static WebSocketServer _server;

        private static readonly ManualResetEventSlim _serverWaitHandle;

        private static readonly IPAddress _ipAddress = IPAddress.Loopback;

        private static int MessageCount;

        private static readonly object locker = new object();

        static Program()
        {
            _serverWaitHandle = new ManualResetEventSlim();
        }

        static void Main(string[] args)
        {
            if (args.Length > 0)
            {
                _server = CreateServer(args[0]);
            }
            else
            {
                _server = CreateServer();
            }
            AssemblyLoadContext.Default.Unloading += OnSigTerm;
            Console.CancelKeyPress += OnSigTerm;
            Console.WriteLine($"Sox.EchoServer exited with code: {StartServer()}");
        }

        public static int StartServer()
        {
            using (_server)
            {
                _server.OnConnection += OnConnect;
                _server.OnDisconnection += OnDisconnect;
                _server.OnTextMessage += OnTextMessage;
                _server.OnBinaryMessage += OnBinaryMessage;
                _server.OnError += OnError;
                _server.OnFrame += OnFrame;

                try
                {
                    Console.WriteLine($"Starting Sox server...");
                    _ = _server.Start();
                    Console.WriteLine($"Sox server listening on {_server.Protocol.ToString().ToLower()}://{_server.IpAddress}:{_server.Port}");
                    _serverWaitHandle.Wait();
                    return 0;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    return -1;
                }
            }
        }

        private static WebSocketServer CreateServer(string protocol = "ws") => protocol switch
        {
            "ws" => new WebSocketServer(
                  ipAddress: _ipAddress,
                  port: 80),
            "wss" => new WebSocketServer(
                  ipAddress: _ipAddress,
                  port: 443,
                  x509Certificate: new X509Certificate2($"sox.pfx", "sox")),
            _ => throw new NotSupportedException($"{protocol} not supported")
        };

        private static async Task StopServer()
        {
            await _server.Stop();
            _serverWaitHandle.Set();
        }

        private static async void OnSigTerm(AssemblyLoadContext ctx)
        {
            await StopServer();
        }

        private static async void OnSigTerm(object sender, ConsoleCancelEventArgs e)
        {
            await StopServer();
        }

        private static void OnConnect(object sender, OnConnectionEventArgs eventArgs)
        {
            var s = (WebSocketServer)sender;
            Console.WriteLine($"{eventArgs.Connection.Id} connected! | {s.ConnectionCount}");
        }

        private static void OnDisconnect(object sender, OnDisconnectionEventArgs eventArgs)
        {
            var s = (WebSocketServer)sender;
            Console.WriteLine($"{eventArgs.Connection.Id} disconnected! | {s.ConnectionCount}");
        }

        private static async void OnTextMessage(object sender, OnTextMessageEventArgs eventArgs)
        {
            var connection = eventArgs.Connection;
            var message = eventArgs.Payload;
            lock (locker)
            {
                Interlocked.Increment(ref MessageCount);
                Console.WriteLine($"{connection.Id} sent {message} (message #{MessageCount})");
            }
            await connection.Send($"{connection.Id} sent {message}");
        }

        private static void OnBinaryMessage(object sender, OnBinaryMessageEventArgs eventArgs)
        {
            var connection = eventArgs.Connection;
            var message = eventArgs.Payload;
            Console.WriteLine($"{connection.Id} sent {message}");
        }

        private static void OnError(object sender, OnErrorEventArgs eventArgs)
        {
            Console.WriteLine(eventArgs.Exception);
        }

        private static void OnFrame(object sender, OnFrameEventArgs eventArgs)
        {
            Console.WriteLine($"CID: {eventArgs.Connection.Id} | Received WS Frame ({eventArgs.Frame.OpCode}) | : Plength - {eventArgs.Frame.PayloadLength:N0}");
        }
    }
}

