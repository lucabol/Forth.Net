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

# Execute a binary file
nforth -e 's\" myfile.io" load aword'
```

## Notes

* Save the user portion of the dictionary with `S" myfile.io" save`. You load it with `S" myfile.io" load`.
* Save the whole dictionary, system included, with `S" myfile.io" savesys`. You load it with `S" myfile.io" loadsys`.
* Call the .NET APIs with these words. (FYI just static methods with strings or numbers for now).

```factor
: escape s" System.Uri, System" s" EscapeDataString" .net ;
: sqrt   s" System.Math"        s" Sqrt"             .net ;
```

* Call from .NET to Forth with the public APIs on the `Vm` class. Look at the `Forth.Net.Program` folder for a simple example. 
* To include the library, copy the file `Vm.cs` in your project. It is lightly literate programmed. See [here](Forth.Net/Readme.md)
* See all the supported words with `words`.
* Perform a system test with `testsys`. Your results should look like the text at the bottom of this doc.
* Type `debug` if curious.
* Type `nforth --help` for less used options.

```console
CR CR SOURCE TYPE ( Preliminary test ) CR
SOURCE ( These lines test SOURCE, TYPE, CR and parenthetic comments ) TYPE CR
( The next line of output should be blank to test CR ) SOURCE TYPE CR CR

( Pass #1: testing 0 >IN +! ) 0 >IN +! SOURCE TYPE CR
( Pass #2: testing 1 >IN +! ) 1 >IN +! xSOURCE TYPE CR
( Pass #3: testing 1+ ) 1 1+ >IN +! xxSOURCE TYPE CR
( Pass #4: testing @ ! BASE ) 0 1+ 1+ BASE ! BASE @ >IN +! xxSOURCE TYPE CR
( Pass #5: testing decimal BASE ) BASE @ >IN +! xxxxxxxxxxSOURCE TYPE CR
( Pass #6: testing : ; ) : .SRC SOURCE TYPE CR ; 6 >IN +! xxxxxx.SRC
( Pass #7: testing number input ) 19 >IN +! xxxxxxxxxxxxxxxxxxx.SRC
( Pass #8: testing VARIABLE ) VARIABLE Y 2 Y ! Y @ >IN +! xx.SRC
( Pass #9: testing WORD COUNT ) 5 MSG abcdef) Y ! Y ! >IN +! xxxxx.SRC
( Pass #10: testing WORD COUNT ) MSG ab) >IN +! xxY ! .SRC
Pass #11: testing WORD COUNT .MSG
Pass #12: testing = returns all 1's for true
Pass #13: testing = returns 0 for false
Pass #14: testing -1 interpreted correctly
Pass #15: testing 2*
Pass #16: testing 2*
Pass #17: testing AND
Pass #18: testing AND
Pass #19: testing AND
Pass #20: testing ?F~ ?~~ Pass Error
Pass #21: testing ?~
Pass #22: testing EMIT
Pass #23: testing S"

Results:

Pass messages #1 to #23 should be displayed above
and no error messages

0 tests failed out of 57 additional tests
```