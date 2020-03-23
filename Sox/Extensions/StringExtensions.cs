using System.Text;

namespace Sox.Extensions
{
    /// <summary>
    /// Extension methods for Strings
    /// </summary>
    internal static class StringExtensions
    {
        /// <summary>
        /// Get the bytes for a String
        /// </summary>
        /// <param name="str">The string to convert</param>
        /// <returns>The <c>byte[]</c> representation of the supplied String</returns>
        internal static byte[] GetBytes(this string str)
        {
            return Encoding.UTF8.GetBytes(str);
        }
    }
}