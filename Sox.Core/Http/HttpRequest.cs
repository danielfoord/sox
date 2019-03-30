using System;
using System.Linq;
using System.Net.Http;
using Sox.Core.Exceptions;

namespace Sox.Core.Http
{
    public class HttpRequest
    {
        public string Path { get; set; }

        public string Version { get; set; }

        public HttpMethod Method { get; set; }

        public HttpHeaders Headers { get; set; }

        public HttpRequest()
        {
            Headers = new HttpHeaders();
        }

        public static bool TryParse(string request, out HttpRequest httpRequest)
        {
            try
            {
                httpRequest = Parse(request);
                return true;
            }
            catch (HttpRequestParseException)
            {
                httpRequest = null;
                return false;
            }
        }

        public static HttpRequest Parse(string request)
        {
            try
            {
                var lines = request
                    .Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .ToArray();

                // Request heading line i.e. "GET / HTTP/1.1"
                var requestLine = lines[0];
                var headingParts = requestLine.Split(' ');
                var method = headingParts[0].Trim();
                var path = headingParts[1].Trim();
                var version = headingParts[2].Trim();

                var httpRequest = new HttpRequest
                {
                    Method = new HttpMethod(method),
                    Path = path,
                    Version = version,
                };

                var headers = lines.Skip(1);

                foreach (var header in headers)
                {
                    var colonIndex = header.IndexOf(':');
                    if (colonIndex == -1) continue;
                    var key = header.Substring(0, colonIndex).Trim();
                    var value = header.Substring(colonIndex + 1).Trim();
                    httpRequest.Headers.Add(key, value);
                }

                return httpRequest;
            }
            catch (Exception ex)
            {
                throw new HttpRequestParseException("Failed to parse HTTP request", ex);
            }
        }
    }
}
