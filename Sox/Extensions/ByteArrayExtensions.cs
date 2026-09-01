using System;
using System.Text;

namespace Sox.Extensions
{
    /// <summary>
    ///     Extensions for <c>byte[]</c>
    /// </summary>
    internal static class ByteArrayExtensions
    {
        /// <summary>
        /// Get the UTF8 representation of a <c>byte[]</c>
        /// </summary>
        /// <param name="bytes">The bytes to stringify</param>
        /// <returns>A UTF8 encoded string</returns>
        internal static string GetString(this byte[] bytes)
        {
            return Encoding.UTF8.GetString(bytes, 0, bytes.Length);
        }

        /// <summary>
        /// Get the UTF8 representation of a <c>ReadOnlySpan&lt;byte&gt;</c>
        /// </summary>
        /// <param name="bytes">The bytes to stringify</param>
        /// <returns>A UTF8 encoded string</returns>
        internal static string GetString(this ReadOnlySpan<byte> bytes)
        {
            return Encoding.UTF8.GetString(bytes);
        }

        /// <summary>
        /// Get the UTF8 representation of a <c>ReadOnlyMemory&lt;byte&gt;</c>
        /// </summary>
        /// <param name="bytes">The bytes to stringify</param>
        /// <returns>A UTF8 encoded string</returns>
        internal static string GetString(this ReadOnlyMemory<byte> bytes)
        {
            return bytes.Span.GetString();
        }
    }
}