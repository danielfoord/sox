using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sox.Core.Exceptions;
using Sox.Core.Extensions;

namespace Sox.Core.Http
{
    /// <summary>
    /// HTTP 1.1 Request implementation as per https://tools.ietf.org/html/rfc2616
    /// </summary>
    public class HttpRequest
    {
        public string Uri { get; set; }

        public int MajorVersion { get; set; }

        public int MinorVersion { get; set; }

        public HttpMethod Method { get; set; }

        public HttpRequestHeaders Headers { get; set; }

        public byte[] Body { get; set; }

        public HttpRequest()
        {
            Headers = new HttpRequestHeaders();
        }

        public static async Task<HttpRequest> ReadAsync(Stream stream)
        {
            using var sr = new StreamReader(stream, Encoding.UTF8, true, 1024, true);

            string line = await sr.ReadLineAsync();
            var (method, uri, majorVersion, minorVersion) = ParseRequestLine(line);

            var httpRequest = new HttpRequest
            {
                Method = method,
                Uri = uri,
                MajorVersion = majorVersion,
                MinorVersion = minorVersion
            };

            // Read headers
            while (!string.IsNullOrWhiteSpace(line = await sr.ReadLineAsync()))
            {
                var colonIndex = line.IndexOf(':');
                if (colonIndex == -1) continue;
                var key = line.Substring(0, colonIndex).Trim();
                var value = line.Substring(colonIndex + 1).Trim();
                httpRequest.Headers.Add(key, value);
            }

            // Read content-length body
            if (httpRequest.IsBodyPermitted())
            {
                if (!string.IsNullOrEmpty(httpRequest.Headers.ContentLength))
                {
                    var contentLength = int.Parse(httpRequest.Headers.ContentLength);
                    if (contentLength <= 0)
                    {
                        throw new HttpRequestParseException("Content-Length cannot be smaller or equal to 0");
                    }

                    var bytesToRead = int.Parse(httpRequest.Headers.ContentLength);
                    httpRequest.Body = Encoding.UTF8.GetBytes(await sr.ReadBytesAsync(bytesToRead));
                }
            }

            // TODO: Read chunked body

            return httpRequest;
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
                return ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes(request))).Result;
            }
            catch (Exception ex)
            {
                throw new HttpRequestParseException("Failed to parse HTTP request", ex);
            }
        }

        public override string ToString()
        {
            var str = $"{Method} {Uri} HTTP/{MajorVersion}.{MinorVersion}\r\n";
            Headers.ForEach((kvp) =>
            {
                str += $"{kvp.Key}: {kvp.Value}\r\n";
            });
            str += "\r\n";
            str += Encoding.UTF8.GetString(Body);
            return str;
        }

        private static (HttpMethod method, string uri, int majorVersion, int minorVersion) ParseRequestLine(string line)
        {
            var parts = line.Split(' ').Select(part => part.Trim()).ToArray();

            if (parts.Length != 3)
            {
                throw new HttpRequestParseException("Invalid request line");
            }

            var method = parts[0];
            var uri = parts[1];
            var version = parts[2].Split('/')[1];
            var majorVersion = int.Parse(version.Split('.')[0]);
            var minorVersion = int.Parse(version.Split('.')[1]);

            return (HttpMethod.Parse(method), uri, majorVersion, minorVersion);
        }

        private bool IsBodyPermitted()
        {
            return this.Method != HttpMethod.Trace;
        }
    }
}
