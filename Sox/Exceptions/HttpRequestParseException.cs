using System;

namespace Sox.Exceptions
{
    [Serializable]
    internal class HttpRequestParseException : Exception
    {
        internal HttpRequestParseException(string message) : base(message)
        {
        }

        internal HttpRequestParseException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
