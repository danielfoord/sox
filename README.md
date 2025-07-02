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





