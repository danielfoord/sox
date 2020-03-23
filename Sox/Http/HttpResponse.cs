using System;
using System.Linq;

namespace Sox.Http
{
    public class HttpResponse
    {
        public int MajorVersion { get; set; }

        public int MinorVersion { get; set; }

        public HttpStatusCode StatusCode { get; set; }

        public HttpResponseHeaders Headers { get; set; }

        public string Body { get; set; }

        public HttpResponse()
        {
            Headers = new HttpResponseHeaders();
            MajorVersion = 1;
            MinorVersion = 1;
        }

        public override string ToString()
        {
            return $"HTTP/{MajorVersion}.{MinorVersion} {StatusCode.Code} {StatusCode.ReasonPhrase}\r\n" +
                   $"{string.Join("\r\n", Headers.Select(kvp => $"{kvp.Key}: {kvp.Value}"))}\r\n\r\n";
        }

        internal static bool TryParse(string raw, out HttpResponse response)
        {
            try
            {
                var lines = raw
                    .Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .ToArray();

                // Status line i.e. "HTTP/1.1 200 OK"
                var statusLine = lines[0];

                var spaceIndex = statusLine.IndexOf(' ');
                var version = statusLine.Substring(0, spaceIndex).Trim();
                version = version.Substring(0, version.IndexOf("/"));
                var majorVersion = version.Split('.')[0];
                var minorVersion = version.Split('.')[1];

                // TODO: Finish implementation
                spaceIndex = statusLine.IndexOf(' ', spaceIndex + 1);
                var statusCode = statusLine.Substring(spaceIndex, statusLine.IndexOf(' ', spaceIndex + 1));

                response = new HttpResponse
                {
                    //StatusCode = new HttpStatusCode(method),
                    MajorVersion = Int32.Parse(majorVersion),
                    MinorVersion = Int32.Parse(minorVersion)
                };

                var headers = lines.Skip(1);

                foreach (var header in headers)
                {
                    var colonIndex = header.IndexOf(':');
                    var key = header.Substring(0, colonIndex).Trim();
                    var value = header.Substring(colonIndex + 1).Trim();
                    response.Headers.Add(key, value);
                }

            }
            catch (Exception)
            {
                response = null;
                return false;
            }

            return true;
        }
    }
}
