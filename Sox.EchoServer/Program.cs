using System;
using System.Net;
using Sox.Server;

namespace Sox.EchoServer
{
    public class Program
    {
        static void Main(string[] args)
        {
            var s = new WebSocketServer();

            try
            {
                s.OnConnection += (sender, eventArgs) =>
                {
                    Console.WriteLine($"{eventArgs.Connection.Id} connected!");
                };

                s.OnDisconnection += (sender, eventArgs) =>
                {
                    Console.WriteLine($"{eventArgs.Connection.Id} disconnected!");
                };

                s.OnTextMessage += async (sender, eventArgs) =>
                {
                    var connection = eventArgs.Connection;
                    var message = eventArgs.Payload;
                    Console.WriteLine($"{connection.Id} sent {message}");
                    await connection.Send($"{connection.Id} sent {message}");
                };

                s.OnBinaryMessage += (sender, eventArgs) =>
                {
                    var connection = eventArgs.Connection;
                    var message = eventArgs.Payload;
                    Console.WriteLine($"{connection.Id} sent {message}");
                };

                s.Start(IPAddress.Loopback, 8088);

                Console.ReadLine();

                Console.WriteLine("Stopping...");

                s.Stop();

                Console.WriteLine("Stopped...");

                Console.ReadLine();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                Console.ReadLine();
            }
        }
    }
}

