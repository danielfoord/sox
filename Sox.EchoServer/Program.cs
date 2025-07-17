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

        private static readonly ManualResetEventSlim ServerWaitHandle;

        private static readonly IPAddress IpAddress = IPAddress.Parse("127.0.0.1");

        private static int _messageCount;

        private static readonly object Locker = new();

        static Program()
        {
            ServerWaitHandle = new ManualResetEventSlim();
        }

        static void Main(string[] args)
        {
            _server = args.Length > 0 ? CreateServer(args[0]) : CreateServer();
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
                _server.OnError += OnError;
                _server.OnFrame += OnFrame;

                try
                {
                    Console.WriteLine($"Starting Sox server...");
                    _ = _server.Start();
                    Console.WriteLine($"Sox server listening on {_server.Protocol.ToString().ToLower()}://{_server.IpAddress}:{_server.Port}");
                    ServerWaitHandle.Wait();
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
                  ipAddress: IpAddress,
                  port: 8888),
            "wss" => new WebSocketServer(
                  ipAddress: IpAddress,
                  port: 443,
                  x509Certificate: new X509Certificate2($"sox.pfx", "sox")),
            _ => throw new NotSupportedException($"{protocol} not supported")
        };

        private static async Task StopServer()
        {
            await _server.Stop();
            ServerWaitHandle.Set();
        }

        private static void OnSigTerm(AssemblyLoadContext ctx)
        {
            StopServer().Wait();
        }

        private static void OnSigTerm(object sender, ConsoleCancelEventArgs e)
        {
            StopServer().Wait();
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

        private static void OnTextMessage(object sender, OnTextMessageEventArgs eventArgs)
        {
            var connection = eventArgs.Connection;
            var message = eventArgs.Payload;

            lock (Locker)
            {
                Interlocked.Increment(ref _messageCount);
                Console.WriteLine($"{connection.Id} sent {message} (message #{_messageCount})");
            }

            connection.Send($"{connection.Id} sent {message}").Wait();
        }

        private static void OnError(object sender, OnErrorEventArgs eventArgs)
        {
            Console.WriteLine(eventArgs.Exception);
        }

        private static void OnFrame(object sender, OnFrameEventArgs eventArgs)
        {
            Console.WriteLine($"CID: {eventArgs.Connection.Id} | Received Frame ({eventArgs.Frame.OpCode}) | : Plength - {eventArgs.Frame.PayloadLength:N0}");
        }
    }
}

