# Sox

![Windows](https://github.com/danielfoord/sox/workflows/Windows/badge.svg?branch=master) ![Linux](https://github.com/danielfoord/sox/workflows/Linux/badge.svg?branch=master)

A pure websocket implementation for .NET Core

THIS IS A WORK IN PROGRESS. DO NOT USE IN PRODUCTION.

## Simple example

```csharp
var server = new WebSocketServer(ipAddress: _ipAddress, port: 80);

server.OnConnection += (sender, eventArgs) =>
{
    // ...
};

server.OnDisconnection += (sender, eventArgs) =>
{
    // ...
};

server.OnTextMessage += async (sender, eventArgs) =>
{
   // ...
};

server.OnBinaryMessage += (sender, eventArgs) =>
{
   // ...
};

server.OnError += (sender, eventArgs) =>
{
    // ...
};

server.OnFrame += (sender, eventArgs) =>
{
    // ...
};

// Start is non-blocking
await server.Start();
```

## Testing on your local machine

If you just want to run the tests:

`dotnet test`

If you want full coverage:

First install the coverage report generator:

`dotnet tool install -g dotnet-reportgenerator-globaltool`

Then run the test script which will run the tests and generate the coverage report:

`./test.sh`


## Get SSL (WSS) working on your local machine

The following instructions are for Debian/Linux:

First generate your CA key:

`openssl genrsa -des3 -out rootCA.key 2048`

Generate your root CA SSL certificate:

`openssl req -x509 -new -nodes -key rootCA.key -sha256 -days 1024 -out rootCA.pem`

Create a private key:

```
openssl req \
 -new -sha256 -nodes \
 -out localhost.csr \
 -newkey rsa:2048 -keyout localhost.key \
 -subj "/C=IN/ST=State/L=City/O=Organization/OU=OrganizationUnit/CN=demo/emailAddress=demo@example.com"
```

Create X509 V3 file called v3.ext:
```
authorityKeyIdentifier=keyid,issuer
basicConstraints=CA:FALSE
keyUsage = digitalSignature, nonRepudiation, keyEncipherment, dataEncipherment
subjectAltName = @alt_names

[alt_names]
DNS.1 = localhost
```

Generate your certificate signing request
```
openssl x509 \
 -req \
 -in localhost.csr \
 -CA rootCA.pem -CAkey rootCA.key -CAcreateserial \
 -out localhost.crt \
 -days 500 \
 -sha256 \
 -extfile v3.ext
```

Create a signed certificate with your key:
```
openssl pkcs12 -export -out sox.pfx -inkey localhost.key -in localhost.crt
```
## Reverse proxy on Nginx
Add the following to `/usr/local/etc/nginx/nginx.conf`
```
location / {
    proxy_set_header Host $host;
    proxy_set_header X-Real-IP $remote_addr;
    proxy_pass http://localhost:8080;
}
```
```
location /socket {
    proxy_pass http://localhost:8888;
    proxy_http_version 1.1;
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection "Upgrade";
    proxy_set_header Host $host;
}
```






