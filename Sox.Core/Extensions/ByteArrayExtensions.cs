using System.Text;

namespace Sox.Core.Extensions
{
    /// <summary>
    ///     Extensions for <c>byte[]</c>
    /// </summary>
    public static class ByteArrayExtensions
    {
        /// <summary>
        /// Get the UTF8 representation of a <c>byte[]</c>
        /// </summary>
        /// <param name="bytes">The bytes to stringify</param>
        /// <returns>A UTF8 encoded string</returns>
        public static string GetString(this byte[] bytes)
        {
            return Encoding.UTF8.GetString(bytes, 0, bytes.Length);
        }
    }
}