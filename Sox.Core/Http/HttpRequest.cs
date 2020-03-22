using Sox.Core.Exceptions;
using Sox.Core.Extensions;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
            if (line == null)
            {
                return null;
            }

            var (method, uri, majorVersion, minorVersion) = ParseRequestLine(line);

            var httpRequest = new HttpRequest
            {
                Method = method,
                Uri = uri,
                MajorVersion = majorVersion,
                MinorVersion = minorVersion
            };

            // Read headers
            while (!string.IsNullOrWhiteSpace((line = await sr.ReadLineAsync())))
            {
                var colonIndex = line.IndexOf(':');
                if (colonIndex == -1) continue;
                var key = line.Substring(0, colonIndex).Trim().ToLower();
                var value = line.Substring(colonIndex + 1).Trim();
                httpRequest.Headers.Add(key, value);
            }

            // Read content-length body
            if (httpRequest.IsBodyPermitted() && !httpRequest.Headers.IsWebSocketUpgrade)
            {
                if (!string.IsNullOrEmpty(httpRequest.Headers.ContentLength))
                {
                    if (int.TryParse(httpRequest.Headers.ContentLength, out var contentLength))
                    {
                        if (contentLength < 0)
                        {
                            throw new HttpRequestParseException("content-length cannot be smaller than 0");
                        }
                        var bytesToRead = int.Parse(httpRequest.Headers.ContentLength);
                        httpRequest.Body = Encoding.UTF8.GetBytes(await sr.ReadBytesAsync(bytesToRead));
                    }
                }
                else
                {
                    if (!httpRequest.Headers.TransferEncoding.Contains("chunked"))
                    {
                        throw new HttpRequestParseException("transfer-encoding header must be set to 'chunked' in content-length header is not present");
                    }
                }
            }

            //19.4.6 Introduction of Transfer - Encoding

            //HTTP / 1.1 introduces the Transfer - Encoding header field(section
            //14.41). Proxies / gateways MUST remove any transfer-coding prior to
            //forwarding a message via a MIME-compliant protocol.

            //A process for decoding the "chunked" transfer - coding(section 3.6)
            //can be represented in pseudo - code as:

            //    length := 0
            //    read chunk - size, chunk - extension(if any) and CRLF
            //    while (chunk - size > 0)
            //         {
            //             read chunk-data and CRLF
            //       append chunk-data to entity-body
            //       length:= length + chunk - size
            //       read chunk-size and CRLF
            //    }
            //         read entity-header
            //    while (entity - header not empty) {
            //             append entity-header to existing header fields
            //         read entity-header
            //      }
            //         Content - Length := length
            //    Remove "chunked" from Transfer-Encoding

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
            str += string.Join(string.Empty, Headers.Select(header => $"{header.Key}: {header.Value}\r\n"));
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
            return Method != HttpMethod.Trace;
        }
    }
}
