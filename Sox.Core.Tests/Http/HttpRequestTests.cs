
using System;
using Sox.Core.Http;
using NUnit.Framework;
using Sox.Core.Exceptions;

namespace Sox.Core.Tests.Http
{
    [TestFixture]
    public class HttpRequestTests
    {
        [Test]
        public void Can_Parse_RequestLine()
        {
            // Arrange
            var method = HttpMethod.Get;
            var uri = "/";
            var majorVersion = 1;
            var minorVersion = 1;

            var raw = $"{method} {uri} HTTP/{majorVersion}.{minorVersion}\r\n" +
                "content-length: 0\r\n" +
                "\r\n";

            // Assert
            var req = HttpRequest.Parse(raw);

            Assert.AreEqual(method, req.Method);
            Assert.AreEqual(uri, req.Uri);
            Assert.AreEqual(majorVersion, req.MajorVersion);
            Assert.AreEqual(minorVersion, req.MinorVersion);
        }

        [TestCase("GET / \r\n")]
        [TestCase("GET / HTTP/1.1 abc\r\n")]
        public void Parse_Throws_Exception_If_Invalid_Request_Line(string requestLine)
        {
            // Assert
            Assert.Throws<HttpRequestParseException>(() =>
            {
                HttpRequest.Parse(requestLine);
            });
        }

        [Test]
        public void Try_Parse_Outs_Null_And_Returns_False_On_Invalid_Request()
        {
            // Arrange
            var raw = "GET / HTTP/1.1 abc" +
                "\r\n";

            // Assert
            var canParse = HttpRequest.TryParse(raw, out var req);
            Assert.IsFalse(canParse);
            Assert.IsNull(req);
        }


        [Test]
        public void TryParse_Outs_Request_And_Returns_True()
        {
            // Arrange
            var method = HttpMethod.Get;
            var uri = "/";
            var majorVersion = 1;
            var minorVersion = 1;

            var cacheControl = "none";
            var connection = "keep-alive";
            var date = DateTime.Now.ToUniversalTime().ToShortTimeString();
            var pragma = "some-value";
            var trailer = "trailer";


            var raw = $"{method} {uri} HTTP/{majorVersion}.{minorVersion}\r\n" +
                $"Cache-Control: {cacheControl}\r\n" +
                $"Connection: {connection}\r\n" +
                "content-length: 0\r\n" +
                $"Date: {date}\r\n" +
                $"Pragma: {pragma}\r\n" +
                $"Trailer: {trailer}\r\n" +
                "\r\n";

            // Act
            var canParse = HttpRequest.TryParse(raw, out var req);

            // Assert
            Assert.IsTrue(canParse);
            Assert.NotNull(req);
        }

        [TestCase(null)]
        [TestCase("-1")]
        public void Parse_Throws_Exception_On_Invalid_ContentLength(string contentLength)
        {
            // Arrange
            var method = HttpMethod.Get;
            var uri = "/";
            var majorVersion = 1;
            var minorVersion = 1;

            var contentType = "text/plain";
            var date = DateTime.Now.ToUniversalTime().ToShortTimeString();

            var raw = $"{method} {uri} HTTP/{majorVersion}.{minorVersion}\r\n" +
                $"Content-Length: {contentLength}\r\n" +
                $"Content-Type: {contentType}\r\n" +
                "\r\n" +
                "Hello \n" +
                "World";

            // Assert
            Assert.Throws<HttpRequestParseException>(() =>
            {
                HttpRequest.Parse(raw);
            });
        }


        [Test]
        public void Parse_Can_Parse_Headers()
        {
            // Arrange
            var method = HttpMethod.Get;
            var uri = "/";
            var majorVersion = 1;
            var minorVersion = 1;

            var cacheControl = "none";
            var connection = "keep-alive";
            var date = DateTime.Now.ToUniversalTime().ToShortTimeString();
            var pragma = "some-value";
            var trailer = "trailer";


            var raw = $"{method} {uri} HTTP/{majorVersion}.{minorVersion}\r\n" +
                $"cache-control: {cacheControl}\r\n" +
                $"connection: {connection}\r\n" +
                "content-length: 0\r\n" +
                $"date: {date}\r\n" +
                $"pragma: {pragma}\r\n" +
                $"trailer: {trailer}\r\n" +
                "\r\n";

            // Act
            var req = HttpRequest.Parse(raw);

            // Assert
            Assert.AreEqual(method, req.Method);
            Assert.AreEqual(uri, req.Uri);
            Assert.AreEqual(majorVersion, req.MajorVersion);
            Assert.AreEqual(minorVersion, req.MinorVersion);

            Assert.AreEqual(cacheControl, req.Headers.CacheControl);
            Assert.AreEqual(connection, req.Headers.Connection);
            Assert.AreEqual(date, req.Headers.Date);
            Assert.AreEqual(pragma, req.Headers.Pragma);
            Assert.AreEqual(trailer, req.Headers.Trailer);
        }

        [Test]
        public void Parse_Can_Read_ContentLength_Body()
        {
            // Arrange
            var method = HttpMethod.Get;
            var uri = "/";
            var majorVersion = 1;
            var minorVersion = 1;

            var contentType = "text/plain";
            var body = "Hello \nWorld";

            var raw = $"{method} {uri} HTTP/{majorVersion}.{minorVersion}\r\n" +
                $"Content-Length: {body.Length}\r\n" +
                $"Content-Type: {contentType}\r\n" +
                "\r\n" +
                $"{body}";

            // Act
            var req = HttpRequest.Parse(raw);

            // Assert
            Assert.AreEqual(method, req.Method);
            Assert.AreEqual(uri, req.Uri);
            Assert.AreEqual(majorVersion, req.MajorVersion);
            Assert.AreEqual(minorVersion, req.MinorVersion);

            Assert.AreEqual(contentType, req.Headers.ContentType);
            Assert.AreEqual(body.Length.ToString(), req.Headers.ContentLength);
            Assert.AreEqual("Hello \nWorld", System.Text.Encoding.UTF8.GetString(req.Body));
        }

        [Test]
        public void ToString_Returns_Correct_Representation()
        {
            // Arrange
            var method = HttpMethod.Get;
            var uri = "/";
            var majorVersion = 1;
            var minorVersion = 1;

            var contentLength = 12;
            var contentType = "text/plain";

            var raw = $"{method} {uri} HTTP/{majorVersion}.{minorVersion}\r\n" +
                $"content-length: {contentLength}\r\n" +
                $"content-type: {contentType}\r\n" +
                "\r\n" +
                "Hello \n" +
                "World";

            // Act
            var req = HttpRequest.Parse(raw);

            // Assert
            Assert.AreEqual(raw, req.ToString());
        }

        [Test]
        public void Parse_Can_Read_Chunked_Body()
        {
            // Arrange
            // Act
            // Assert
        }
    }
}