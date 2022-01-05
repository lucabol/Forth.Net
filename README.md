# Forth.Net

An implementation of the [Forth](https://en.wikipedia.org/wiki/Forth_(programming_language)) programming language for the [.Net Framework](https://en.wikipedia.org/wiki/.NET_Framework). You can use it in interactively, interpret Forth files or compile Forth files to C#.

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

* The CLI is implemented by compiling the Forth code to C# and executing it interactively.
* To see the generated C# code type `debug` at the console. That toggles the setting.
* The tool compiles a self-sufficient C# file containing a `Forth` static class with a static function for each compiled file named as the file passed at the command line.
* The CLI loads a `vm.cs.kernel` file located in the same directory as the executable. That is the `Forth` vm. You can switch it off with your own implementation (if you are careful and adventurous).
