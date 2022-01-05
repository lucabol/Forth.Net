# Forth.Net

An implementation of the Forth programming language for the .Net platform. You can use it in interactively, interpret Forth files or compile Forth files to C#.

## Installation

```console
dotnet tool install -g forth.net.cli
```

## Usage

```console
# Start the CLI.
nforth

# Interpret some files and exit.
nforth Test1.fth Test2.fth -e bye

# Compile files to C#.
nforth Test1.fth Test2.fth -o Forth.cs
```

## Notes

. The CLI is implemented by compiling the Forth code to C# and executing it interactively.
. To see the generated C# code type `debug` at the console. That toggles the setting.
. The tool compiles a self-sufficient C# file containing a `Forth` static class with a static function for each compiled file named as the file passed at the command line.
