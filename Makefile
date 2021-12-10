all:
	dotnet build --nologo -v minimal

check:
	dotnet test --nologo -v minimal
