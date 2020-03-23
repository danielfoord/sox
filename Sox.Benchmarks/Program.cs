using BenchmarkDotNet.Running;
using Sox.Benchmarks.Websocket.Rfc6455.Framing;

namespace Sox.Benchmarks
{
    public class Program
    {
        public static void Main()
        {
            BenchmarkRunner.Run<FrameBench>();
        }
    }
}