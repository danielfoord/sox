using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Sox.Core.Extensions
{
    /// <summary>
    /// Extension methods for <c>Stream</c>
    /// </summary>
    public static class StreamReaderExtensions
    {
        /// <summary>
        /// Read a specific amount of bytes from a <c>StreamReader</c>
        /// </summary>
        /// <param name="stream">The stream to read from</param>
        /// <param name="bytesToRead">The amount of bytes to read</param>
        /// <returns>A <c>Task</c> that resolves a <c>byte[]</c> when the amount of bytes has been read</returns>
        public static async Task<char[]> ReadBytesAsync(this StreamReader sr, int bytesToRead)
        {
            var buffer = new char[bytesToRead];
            var read = await sr.ReadAsync(buffer, 0, bytesToRead);

            if (read == 0)
            {
                return new char[0];
            }

            if (read < bytesToRead)
            {
                var readBuffer = await sr.ReadBytesAsync(bytesToRead - read);
                return buffer.Concat(readBuffer).ToArray();
            }
            return buffer;
        }
    }
}