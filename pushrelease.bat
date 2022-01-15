del ./Forth.Net.Cli/nupkg/*.nupkg
dotnet pack Forth.Net.Cli/Forth.Net.Cli.csproj --configuration Release
dotnet nuget push ./Forth.Net.Cli/nupkg/*.nupkg --source https://api.nuget.org/v3/index.json --api-key %NUGET_FORTH%
