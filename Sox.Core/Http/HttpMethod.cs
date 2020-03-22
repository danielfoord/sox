
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sox.Core.Http
{
    public sealed class HttpMethod
    {
        public static HttpMethod Options = new HttpMethod("OPTIONS");
        public static HttpMethod Get = new HttpMethod("GET");
        public static HttpMethod Head = new HttpMethod("HEAD");
        public static HttpMethod Post = new HttpMethod("POST");
        public static HttpMethod Put = new HttpMethod("PUT");
        public static HttpMethod Delete = new HttpMethod("DELETE");
        public static HttpMethod Trace = new HttpMethod("TRACE");
        public static HttpMethod Connect = new HttpMethod("CONNECT");

        public readonly string Value;

        private static readonly IEnumerable<HttpMethod> allMethods = new List<HttpMethod>
        {
            Options,
            Get,
            Head,
            Post,
            Put,
            Delete,
            Trace,
            Connect
        };

        private HttpMethod(string value) => Value = value;

        public static HttpMethod Parse(string value)
        {
            var method = allMethods.FirstOrDefault(m => m.Value == value);
            if (method != null)
            {
                return method;
            }
            throw new NotSupportedException($"Method {value} not recognized");
        }

        public static bool TryParse(string value, out HttpMethod method)
        {
            try
            {
                method = Parse(value);
                return true;
            }
            catch (NotSupportedException)
            {
                method = null;
                return false;
            }
        }

        public override String ToString()
        {
            return Value;
        }
    }
}