using NUnit.Framework;
using Sox.Core.Extensions;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Sox.Core.Tests.Extensions
{
    [TestFixture]
    public class StreamExtensionsTests
    {

        [TestCase(4096)]
        [TestCase(1024)]
        [TestCase(64)]
        [TestCase(8)]
        public async Task ReadBytesAsync_Reads_Bytes_Correctly(int byteAmount)
        {
            // Arrange
            var bytes = new byte[byteAmount];
            var random = new Random();
            random.NextBytes(bytes);
            using var stream = new MemoryStream(bytes);

            // Act
            var outBytes = await stream.ReadBytesAsync(byteAmount);

            // Assert
            Assert.AreEqual(bytes, outBytes);
        }
    }
}
