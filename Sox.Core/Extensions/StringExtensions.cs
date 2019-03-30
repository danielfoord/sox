using System.Text;

namespace Sox.Core.Extensions
{
    /// <summary>
    /// Extension methods for Strings
    /// </summary>
    public static class StringExtensions
    {
        /// <summary>
        /// Get the bytes for a String
        /// </summary>
        /// <param name="str">The string to convert</param>
        /// <returns>The <c>byte[]</c> representation of the supplied String</returns>
        public static byte[] GetBytes(this string str)
        {
            return Encoding.UTF8.GetBytes(str);
        }
    }
}