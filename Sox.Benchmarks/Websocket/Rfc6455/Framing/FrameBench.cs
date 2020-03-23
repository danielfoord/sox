using BenchmarkDotNet.Attributes;
using Sox.Websocket.Rfc6455.Framing;
using System;

namespace Sox.Benchmarks.Websocket.Rfc6455.Framing
{
    public class FrameBench
    {
        private const int N = 1000;
        private readonly byte[] data;
        private readonly string stringData;

        public FrameBench()
        {
            data = new byte[N];
            new Random().NextBytes(data);

            var chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz ";
            var stringChars = new char[N];
            var random = new Random();

            for (int i = 0; i < stringChars.Length; i++)
            {
                stringChars[i] = chars[random.Next(chars.Length)];
            }

            stringData = new string(stringChars);
        }

        [Benchmark]
        public byte[] PackTextFrame1000Bytes() => Frame.CreateText(stringData, shouldMask: true).PackAsync().Result;

        [Benchmark]
        public byte[] PackBinaryFrame1000Bytes() => Frame.CreateBinary(data, shouldMask: true).PackAsync().Result;
    }
}
