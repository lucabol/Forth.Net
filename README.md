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
