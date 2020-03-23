using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace Sox.Http
{
    [Serializable]
    public class HttpHeaders : Dictionary<string, string>
    {

        public HttpHeaders()
        {
        }

        protected HttpHeaders(SerializationInfo info, StreamingContext ctx) : base(info, ctx)
        {
        }

        public string Upgrade
        {
            get
            {
                TryGetValue("Upgrade", out var upgrade);
                return upgrade;
            }
        }

        public IEnumerable<string> Connection
        {
            get
            {
                TryGetValue("Connection", out var connection);
                return connection?.Split(',').AsEnumerable().Select(v => v.Trim());
            }
        }

        public string Host
        {
            get
            {
                TryGetValue("Host", out var host);
                return host;
            }
        }

        public string Origin
        {
            get
            {
                TryGetValue("Origin", out var origin);
                return origin;
            }
        }

        public string SecWebSocketKey
        {
            get
            {
                TryGetValue("Sec-WebSocket-Key", out var secWebsocketKey);
                return secWebsocketKey;
            }
        }

        public string SecWebSocketProtocol
        {
            get
            {
                TryGetValue("Sec-WebSocket-Protocol", out var secWebSocketProtocol);
                return secWebSocketProtocol;
            }
        }

        public string SecWebSocketVersion
        {
            get
            {
                TryGetValue("Sec-WebSocket-Version", out var secWebSocketVersion);
                return secWebSocketVersion;
            }
        }

        public bool IsWebSocketUpgrade => Upgrade == "websocket" &&
                                          Connection.Contains("Upgrade") &&
                                          Host != null &&
                                          SecWebSocketKey != null &&
                                          SecWebSocketVersion != null;
    }
}
