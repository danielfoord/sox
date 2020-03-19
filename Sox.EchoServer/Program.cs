using System;
using System.Net;
using System.Runtime.Loader;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Sox.Server;

namespace Sox.EchoServer
{
    public class Program
    {
        private static WebSocketServer _server;

        private static readonly ManualResetEventSlim _serverWaitHandle;

        private static readonly IPAddress _ipAddress = IPAddress.Loopback;

        private static int MessageCount;

        static Program()
        {
            _serverWaitHandle = new ManualResetEventSlim();
        }

        static void Main(string[] args)
        {
            if (args[0] == "ws")
            {
                _server = new WebSocketServer(
                  ipAddress: _ipAddress,
                  port: 80);
            }
            else 
            {
                _server = new WebSocketServer(
                  ipAddress: _ipAddress,
                  port: 44315,
                  x509Certificate: new X509Certificate2($"sox.pfx", "sox"));
            }
            AssemblyLoadContext.Default.Unloading += OnSigTerm;
            Console.CancelKeyPress += OnSigTerm;
            Console.WriteLine($"Sox.EchoServer exited with code: {StartServer()}");
        }

        public static int StartServer()
        {
            using (_server)
            {
                try
                {
                    _server.OnConnection += (sender, eventArgs) =>
                    {
                        var s = (WebSocketServer)sender;
                        Console.WriteLine($"{eventArgs.Connection.Id} connected! | {s.ConnectionCount}");
                    };

                    _server.OnDisconnection += (sender, eventArgs) =>
                    {
                        var s = (WebSocketServer)sender;
                        Console.WriteLine($"{eventArgs.Connection.Id} disconnected! | {s.ConnectionCount}");
                    };

                    _server.OnTextMessage += async (sender, eventArgs) =>
                    {
                        var connection = eventArgs.Connection;
                        var message = eventArgs.Payload;
                        Interlocked.Increment(ref MessageCount);
                        Console.WriteLine($"{connection.Id} sent {message} (message #{MessageCount})");
                        await connection.Send($"{connection.Id} sent {message}");
                    };

                    _server.OnBinaryMessage += (sender, eventArgs) =>
                    {
                        var connection = eventArgs.Connection;
                        var message = eventArgs.Payload;
                        Console.WriteLine($"{connection.Id} sent {message}");
                    };

                    _server.OnError += (sender, eventArgs) =>
                    {
                        Console.WriteLine(eventArgs.Exception);
                    };

                    _server.OnFrame += (sender, eventArgs) =>
                    {
                        Console.WriteLine($"CID: {eventArgs.Connection.Id} | Received WS Frame ({eventArgs.Frame.OpCode}) | : Plength - {eventArgs.Frame.PayloadLength:N0}");
                    };

                    Console.WriteLine($"Starting Sox server...");
                    _server.Start();
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
    }
}

