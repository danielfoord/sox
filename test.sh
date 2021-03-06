#!/bin/sh
dotnet build
dotnet test /p:CollectCoverage=true /p:Threshold=80 /p:ThresholdType=line /p:ThresholdStat=total /p:CoverletOutputFormat=opencover
reportgenerator -reports:Sox.Tests/coverage.opencover.xml -targetdir:Sox.Tests/coverage