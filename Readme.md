# Forth.Net

An implementation of the [Forth](https://en.wikipedia.org/wiki/Forth_(programming_language)) programming language for the [.Net Framework](https://en.wikipedia.org/wiki/.NET_Framework).
You can use it as a C# library, interactively with a CLI or compile the code to a compact binary format.

## Installation

```console
dotnet tool install -g forth.net.program
```

## Usage

```console
# Start the CLI.
nforth

# Interpret some files and exit.
nforth Test1.fth Test2.fth -e bye

# Compile files to binary
nforth Test1.fth Test2.fth -e 's\" myfile.io" save'
```

## Notes

* You save the user portion of the dictionary with `S" myfile.io" save`. You load it with `S" myfile.io" load`.
* You save the whole dictionary, system included, with `S" myfile.io" savesys`. You load it with `S" myfile.io" loadsys`.
* Calling into .NET APIs is achieved with these simple words. (fyi just static methods with string or numbers for now).

```factor
: escape s" System.Uri, System" s" EscapeDataString" .net ;
: sqrt   s" System.Math"        s" Sqrt"             .net ;
```

* Calling from .NET to Forth is achieved using the public APIs on the `Vm` class. Look at the `Program` folder for a simple example. 
* Type `debug` if curious.
* Type `nforth --help` for less used options.