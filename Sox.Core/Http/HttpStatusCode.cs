namespace Sox.Core.Http
{
    public class HttpStatusCode
    {
        public static readonly HttpStatusCode Accepted = new HttpStatusCode(202, "Accepted");
        public static readonly HttpStatusCode Ambiguous = new HttpStatusCode(300, "Ambiguous");
        public static readonly HttpStatusCode BadGateway = new HttpStatusCode(502, "Bad Gateway");
        public static readonly HttpStatusCode BadRequest = new HttpStatusCode(400, "Bad Request");
        public static readonly HttpStatusCode Conflict = new HttpStatusCode(409, "Conflict");
        public static readonly HttpStatusCode Continue = new HttpStatusCode(100, "Continue");
        public static readonly HttpStatusCode Created = new HttpStatusCode(201, "Created");
        public static readonly HttpStatusCode ExpectationFailed = new HttpStatusCode(417, "Expectation Failed");
        public static readonly HttpStatusCode Forbidden = new HttpStatusCode(403, "Forbidden");
        public static readonly HttpStatusCode Found = new HttpStatusCode(302, "Found");
        public static readonly HttpStatusCode GatewayTimeout = new HttpStatusCode(504, "Gateway Timeout");
        public static readonly HttpStatusCode Gone = new HttpStatusCode(410, "Gone");
        public static readonly HttpStatusCode HttpVersionNotSupported = new HttpStatusCode(505, "Http Version Not Supported");
        public static readonly HttpStatusCode InternalServerError = new HttpStatusCode(500, "Internal Server Error");
        public static readonly HttpStatusCode LengthRequired = new HttpStatusCode(411, "Length Required");
        public static readonly HttpStatusCode MethodNotAllowed = new HttpStatusCode(405, "Method Not Allowed");
        public static readonly HttpStatusCode Moved = new HttpStatusCode(301, "Moved");
        public static readonly HttpStatusCode MovedPermanently = new HttpStatusCode(301, "Moved Permanently");
        public static readonly HttpStatusCode MultipleChoices = new HttpStatusCode(300, "Multiple Choices");
        public static readonly HttpStatusCode NoContent = new HttpStatusCode(204, "No Content");
        public static readonly HttpStatusCode NonAuthoritativeInformation = new HttpStatusCode(203, "Non Authoritative Information");
        public static readonly HttpStatusCode NotAcceptable = new HttpStatusCode(406, "Not Acceptable");
        public static readonly HttpStatusCode NotFound = new HttpStatusCode(404, "Not Found");
        public static readonly HttpStatusCode NotImplemented = new HttpStatusCode(501, "Not Implemented");
        public static readonly HttpStatusCode NotModified = new HttpStatusCode(304, "Not Modified");
        public static readonly HttpStatusCode Ok = new HttpStatusCode(200, "OK");
        public static readonly HttpStatusCode PartialContent = new HttpStatusCode(206, "Partial Content");
        public static readonly HttpStatusCode PaymentRequired = new HttpStatusCode(402, "Payment Required");
        public static readonly HttpStatusCode PreconditionFailed = new HttpStatusCode(412, "Precondition Failed");
        public static readonly HttpStatusCode ProxyAuthenticationRequired = new HttpStatusCode(407, "Proxy Authentication Required");
        public static readonly HttpStatusCode Redirect = new HttpStatusCode(302, "Redirect");
        public static readonly HttpStatusCode RedirectKeepVerb = new HttpStatusCode(307, "Redirect Keep Verb");
        public static readonly HttpStatusCode RedirectMethod = new HttpStatusCode(303, "Redirect Method");
        public static readonly HttpStatusCode RequestedRangeNotSatisfiable = new HttpStatusCode(416, "Requested Range Not Satisfiable");
        public static readonly HttpStatusCode RequestEntityTooLarge = new HttpStatusCode(413, "Request Entity Too Large");
        public static readonly HttpStatusCode RequestTimeout = new HttpStatusCode(408, "Request Timeout");
        public static readonly HttpStatusCode RequestUriTooLong = new HttpStatusCode(414, "Request Uri Too Long");
        public static readonly HttpStatusCode ResetContent = new HttpStatusCode(205, "Reset Content");
        public static readonly HttpStatusCode SeeOther = new HttpStatusCode(303, "See Other");
        public static readonly HttpStatusCode ServiceUnavailable = new HttpStatusCode(503, "Service Unavailable");
        public static readonly HttpStatusCode SwitchingProtocols = new HttpStatusCode(101, "Switching Protocols");
        public static readonly HttpStatusCode TemporaryRedirect = new HttpStatusCode(307, "Temporary Redirect");
        public static readonly HttpStatusCode Unauthorized = new HttpStatusCode(401, "Unauthorized");
        public static readonly HttpStatusCode UnsupportedMediaType = new HttpStatusCode(415, "Unsupported Media Type");
        public static readonly HttpStatusCode Unused = new HttpStatusCode(306, "Unused");
        public static readonly HttpStatusCode UpgradeRequired = new HttpStatusCode(426, "Upgrade Required");
        public static readonly HttpStatusCode UseProxy = new HttpStatusCode(305, "Use Proxy");

        public readonly string ReasonPhrase;
        public readonly int StatusCode;

        public HttpStatusCode(int statusCode, string reasonPhrase)
        {
            StatusCode = statusCode;
            ReasonPhrase = reasonPhrase;
        }

        public bool TryParse(int code, string reason, out HttpStatusCode statusCode)
        {
            var r = reason.ToLower();
            switch(code) 
            {
                case 202: statusCode = Accepted; return true;
                case 300: statusCode = Ambiguous; return true;
                case 502: statusCode = BadGateway; return true;
                case 400: statusCode = BadRequest; return true;
                case 409: statusCode = Conflict; return true;
                case 100: statusCode = Continue; return true;
                case 201: statusCode = Created; return true;
                case 417: statusCode = ExpectationFailed; return true;
                case 403: statusCode = Forbidden; return true;
                case 302:
                    if (r == Found.ReasonPhrase.ToLower()) { statusCode = Found; return true; }
                    else if (r == Redirect.ReasonPhrase.ToLower()) { statusCode = Redirect; return true; }
                    statusCode = null;
                    return false;
                case 504: statusCode = GatewayTimeout; return true;
                case 410: statusCode = Gone; return true;
                case 505: statusCode = HttpVersionNotSupported; return true;
                case 500: statusCode = InternalServerError; return true;
                case 411: statusCode = LengthRequired; return true;
                case 405: statusCode = MethodNotAllowed; return true;
                case 301: statusCode = Moved; return true;
                case 204: statusCode = NoContent; return true;
                case 203: statusCode = NonAuthoritativeInformation; return true;
                case 406: statusCode = NotAcceptable; return true;
                case 404: statusCode = NotFound; return true;
                case 501: statusCode = NotImplemented; return true;
                case 304: statusCode = NotModified; return true;
                case 200: statusCode = Ok; return true;
                case 206: statusCode = PartialContent; return true;
                case 402: statusCode = PaymentRequired; return true;
                case 412: statusCode = PreconditionFailed; return true;
                case 407: statusCode = ProxyAuthenticationRequired; return true;
                case 303:
                    if (r == RedirectMethod.ReasonPhrase.ToLower()) { statusCode = RedirectMethod; return true; }
                    else if (r == SeeOther.ReasonPhrase.ToLower()) { statusCode = SeeOther; return true; }
                    statusCode = null;
                    return false;
                case 307:
                    if (r == TemporaryRedirect.ReasonPhrase.ToLower()) { statusCode = TemporaryRedirect; return true; }
                    else if (r == RedirectKeepVerb.ReasonPhrase.ToLower()) { statusCode = RedirectKeepVerb; return true; }
                    statusCode = null;
                    return false;
                case 416: statusCode = RequestedRangeNotSatisfiable; return true;
                case 413: statusCode = RequestEntityTooLarge; return true;
                case 408: statusCode = RequestTimeout; return true;
                case 414: statusCode = RequestUriTooLong; return true;
                case 205: statusCode = ResetContent; return true;
                case 503: statusCode = ServiceUnavailable; return true;
                case 101: statusCode = SwitchingProtocols; return true;
                case 401: statusCode = Unauthorized; return true;
                case 415: statusCode = UnsupportedMediaType; return true;
                case 306: statusCode = Unused; return true;
                case 426: statusCode = UpgradeRequired; return true;
                case 305: statusCode = UseProxy; return true;
                default: statusCode = null; return false;
            }
        }
    }
}
