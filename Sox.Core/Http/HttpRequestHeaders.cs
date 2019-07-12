using System;
using System.Collections.Generic;

namespace Sox.Core.Http
{
    [Serializable]
    public class HttpRequestHeaders : Dictionary<string, string>
    {

        public HttpRequestHeaders()
        {
        }

        // General headers
        public string CacheControl => ReadValue("Cache-Control");

        public string Connection => ReadValue("Connection");

        public string ContentType => ReadValue("Content-Type");

        public string ContentLength => ReadValue("Content-Length");

        public string Date => ReadValue("Date");

        public string Pragma => ReadValue("Pragma");

        public string Trailer => ReadValue("Trailer");

        public string TransferEncoding => ReadValue("Transfer-Encoding");

        public string Upgrade => ReadValue("Upgrade");

        public string Via => ReadValue("Via");

        public string Warning => ReadValue("Warning");

        // Request headers
        public string Accept => ReadValue("Accept");

        public string AcceptCharset => ReadValue("Accept-Charset");

        public string AcceptEncoding => ReadValue("Accept-Encoding");

        public string AcceptLanguage => ReadValue("Accept-Language");

        public string Authorization => ReadValue("Authorization");

        public string Expect => ReadValue("Expect");

        public string From => ReadValue("From");

        public string Host => ReadValue("Host");

        public string IfMatch => ReadValue("If-Match");

        public string IfModifiedSince => ReadValue("If-Modified-Since");

        public string IfNoneMatch => ReadValue("If-None-Match");

        public string IfRange => ReadValue("If-Range");

        public string IfUnmodifiedSince => ReadValue("If-Unmodified-Since");

        public string MaxForwards => ReadValue("Max-Forwards");

        public string ProxyAuthorization => ReadValue("Proxy-Authorization");

        public string Range => ReadValue("Range");

        public string Referer => ReadValue("Referer");

        public string TE => ReadValue("TE");

        public string UserAgent => ReadValue("User-Agent");

        public string Origin => ReadValue("Origin");

        // Websocket specific headers
        public string SecWebSocketKey => ReadValue("Sec-WebSocket-Key");

        public string SecWebSocketProtocol => ReadValue("Sec-WebSocket-Protocol");

        public string SecWebSocketVersion => ReadValue("Sec-WebSocket-Version");

        public bool IsWebSocketUpgrade => Upgrade == "websocket" &&
                                          Connection.Contains("Upgrade") &&
                                          Host != null &&
                                          SecWebSocketKey != null &&
                                          SecWebSocketVersion != null;

        private string ReadValue(string name)
        {
            TryGetValue(name, out var value);
            return value;
        }
    }
}
