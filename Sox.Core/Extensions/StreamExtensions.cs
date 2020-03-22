using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Sox.Core.Extensions
{
    /// <summary>
    /// Extension methods for <c>Stream</c>
    /// </summary>
    public static class StreamExtensions
    {
        /// <summary>
        /// Read a specific amount of bytes from a <c>Stream</c>
        /// </summary>
        /// <param name="stream">The stream to read from</param>
        /// <param name="bytesToRead">The amount of bytes to read</param>
        /// <returns>A <c>Task</c> that resolves a <c>byte[]</c> when the amount of bytes has been read</returns>
        public static async Task<byte[]> ReadBytesAsync(this Stream stream, int bytesToRead, int bufferSize = 1024)
        {
            var actualBufferSize = bufferSize > bytesToRead ? bytesToRead : bufferSize;
            var buffer = new byte[actualBufferSize];
            var read = await stream.ReadAsync(buffer, 0, actualBufferSize);

            if (read == 0)
            {
                return new byte[0];
            }

            if (read < bytesToRead)
            {
                var readBuffer = await stream.ReadBytesAsync(bytesToRead - read, bufferSize);
                return buffer.Concat(readBuffer).ToArray();
            }
            return buffer;
        }

        /// <summary>
        /// Write a specific amount of bytes to a <c>Stream</c>
        /// </summary>
        /// <param name="stream">The stream to write to</param>
        /// <param name="bytes">The bytes to write to the <c>Stream</c></param>
        /// <returns>A <c>Task</c> that resolves when the bytes have been written</returns>
        public static async Task WriteBytesAsync(this Stream stream, byte[] bytes)
        {
            await stream.WriteAsync(bytes, 0, bytes.Length);
        }

        /// <summary>
        /// Write bytes to a Stream and flush it
        /// </summary>
        /// <param name="stream">The Stream to write the bytes to</param>
        /// <param name="bytes">The bytes to write to the Stream</param>
        /// <returns>A Task that resolves when the bytes have been written and the stream has been flushed</returns>
        public static async Task WriteAndFlushAsync(this Stream stream, byte[] bytes)
        {
            await stream.WriteBytesAsync(bytes);
            await stream.FlushAsync();
        }
    }
}