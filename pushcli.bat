del Forth.Net.Program\nupkg\*.nupkg
dotnet pack Forth.Net.Program/Forth.Net.Program.csproj --configuration Release
dotnet nuget push .\Forth.Net.Program\nupkg\*.nupkg --source https://api.nuget.org/v3/index.json --api-key %NUGET_FORTH%