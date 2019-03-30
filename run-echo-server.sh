#!/bin/sh
xterm -e dotnet build && 
xterm -hold -e dotnet run --project Sox.EchoServer &
xterm -hold -e "cd Sox.TestWebClient && yarn start"
