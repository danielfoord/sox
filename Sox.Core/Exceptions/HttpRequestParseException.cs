using System;

namespace Sox.Core.Exceptions
{
    [Serializable]
    public class HttpRequestParseException : Exception
    {
        public HttpRequestParseException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
