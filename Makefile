all:
	dotnet build --nologo -v minimal

check:
	dotnet test --nologo -v minimal

run:
	dotnet run -v minimal --project Forth.Net.Cli/Forth.Net.Cli.csproj
