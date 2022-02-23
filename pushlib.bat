del Forth.Net\nupkg\*.nupkg
dotnet pack Forth.Net\Forth.Net.csproj --configuration Release -o Forth.Net\nupkg
dotnet nuget push .\Forth.Net\nupkg\*.nupkg --source https://api.nuget.org/v3/index.json --api-key %NUGET_FORTH%