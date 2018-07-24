using System.Text;

namespace Sox.Core.Extensions
{
    public static class ByteArrayExtensions
    {
        public static string GetString(this byte[] bytes)
        {
            return Encoding.UTF8.GetString(bytes, 0, bytes.Length);
        }
    }
}