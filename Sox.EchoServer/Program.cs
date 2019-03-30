using System;
using System.Net;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Sox.Server;

namespace Sox.EchoServer
{
    public class Program
    {
        private static WebSocketServer _server;

        private static ManualResetEventSlim _serverWaitHandle;

        private static IPAddress _ipAddress = IPAddress.Loopback;

        private static int _port = 44315;

        // private static int _port = 8088; 

        static Program()
        {
            _server = new WebSocketServer(
                ipAddress:_ipAddress,
                port: _port,
                x509CertificateFile: $"{AppDomain.CurrentDomain.BaseDirectory}sox.pfx",
                x509CertificatePassword: "sox_secret");
            _serverWaitHandle = new ManualResetEventSlim();
        }

        static void Main(string[] args)
        {
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
                        Console.WriteLine($"{connection.Id} sent {message}");
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


                    Console.WriteLine($"Starting Sox server...");
                    _server.Start();
                    Console.WriteLine($"Sox server listening on {_ipAddress}:{_port}");

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

