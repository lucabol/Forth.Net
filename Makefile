all:
	dotnet build --nologo -v minimal

check:
	dotnet test --nologo -v minimal

run:
	dotnet run -v minimal --project Forth.Net.Program/Forth.Net.Program.csproj

pack:
	dotnet pack Forth.Net.Program/Forth.Net.Program.csproj --configuration Release --version-suffix alpha+$(shell date +%s)

packrelease:
	dotnet pack Forth.Net.Program/Forth.Net.Program.csproj --configuration Release

push: clean_pkg pack
	dotnet nuget push ./Forth.Net.Program/nupkg/*.nupkg --source https://api.nuget.org/v3/index.json --api-key $(NUGET_FORTH)

pushrelease: clean_pkg packrelease
	dotnet nuget push ./Forth.Net.Program/nupkg/*.nupkg --source https://api.nuget.org/v3/index.json --api-key $(NUGET_FORTH)

clean_pkg:
	trash -f ./Forth.Net.Program/nupkg/*.nupkg || true