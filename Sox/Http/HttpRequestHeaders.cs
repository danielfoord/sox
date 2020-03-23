using System;
using System.Collections.Generic;

namespace Sox.Http
{
    [Serializable]
    public class HttpRequestHeaders : Dictionary<string, string>
    {

        public HttpRequestHeaders()
        {
        }

        // General headers
        public string CacheControl => ReadValue("cache-control");

        public string Connection => ReadValue("connection");

        public string ContentType => ReadValue("content-type");

        public string ContentLength => ReadValue("content-length");

        public string Date => ReadValue("date");

        public string Pragma => ReadValue("pragma");

        public string Trailer => ReadValue("trailer");

        public string TransferEncoding => ReadValue("transfer-encoding");

        public string Upgrade => ReadValue("upgrade");

        public string Via => ReadValue("via");

        public string Warning => ReadValue("warning");

        // Request headers
        public string Accept => ReadValue("accept");

        public string AcceptCharset => ReadValue("accept-charset");

        public string AcceptEncoding => ReadValue("accept-encoding");

        public string AcceptLanguage => ReadValue("accept-language");

        public string Authorization => ReadValue("authorization");

        public string Expect => ReadValue("expect");

        public string From => ReadValue("from");

        public string Host => ReadValue("host");

        public string IfMatch => ReadValue("if-match");

        public string IfModifiedSince => ReadValue("if-modified-since");

        public string IfNoneMatch => ReadValue("if-none-match");

        public string IfRange => ReadValue("if-range");

        public string IfUnmodifiedSince => ReadValue("if-unmodified-since");

        public string MaxForwards => ReadValue("max-forwards");

        public string ProxyAuthorization => ReadValue("proxy-authorization");

        public string Range => ReadValue("range");

        public string Referer => ReadValue("referer");

        public string TE => ReadValue("te");

        public string UserAgent => ReadValue("user-agent");

        public string Origin => ReadValue("origin");

        // Websocket specific headers
        public string SecWebSocketKey => ReadValue("sec-websocket-key");

        public string SecWebSocketProtocol => ReadValue("sec-websocket-protocol");

        public string SecWebSocketVersion => ReadValue("sec-websocket-version");

        public bool IsWebSocketUpgrade => Upgrade == "websocket" &&
                                          Connection.ToLower().Contains("upgrade") &&
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
