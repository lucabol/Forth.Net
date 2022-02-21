/** ## Abstract
This is a Forth for the .NET framework in one cs file. It is a token threaded implementation that can save its status to a very concise binary format.
Feel free to reuse this code as you wish.  **/

/** ## Preliminaries
The ambition was to write a Forth that can be recompiled for 32 or 64 bits. I just tested the 64 bits part.
The rest is just standard `using` stuff that I can't move to a `global.cs` file because I want to be able to simply copy
this cs file to a project and have my Forth there.**/

#if CELL32
using Cell      = System.Int32;
using Index     = System.Int32;
#else
using Cell      = System.Int64;
using Index     = System.Int32;
#endif

using Code    = System.Byte;
using AUnit   = System.Byte;
using AChar   = System.Byte;

using System.Text;
using System.Runtime.CompilerServices;
using System.Buffers.Binary;

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using static Forth.Utils;

using GitVarInt;

[assembly:InternalsVisibleTo("Forth.Net.Tests")]

namespace Forth;

public class Vm {

/** ## The outer interpret
In Forth everything starts at `Quit`. Yep. It is the line interpreter. It reads a line (with `Refill`) and interprets it. **/
    public void Quit()
    {
        rp = 0; Executing = true; ds[inp] = 0; // Don't reset the parameter stack as for ANS FORTH definition of QUIT.

        while(true)
        {
            Refill();
            if(Pop() != FORTH_TRUE) return;
            Interpret();
        }
    }

    /** Then comes the word interpret. It tries to interpret the text between spaces first as a word, then as a number **/
    void Interpret()
    {
        while(true)
        {
            Bl();
            Word(inKeyword: true);
            if(IsEmptyWord()) { Drop(); break;};

            // TODO: remove string allocation from main loop. It is not trivial to do, because some functions inside InterpretWord rely on it.
            Dup();
            var aword = ToDotNetStringC().ToLowerInvariant();

            if(InterpretWord(aword)) continue;

            if(TryParseNumber(aword, out Cell n))
                InterpretNumber(n);
            else
                Throw($"{aword} is not a recognized word or number.");
        }
    }

    /** ## Interpreting numbers **/
    /** Let's tackle parsing the number first. In Forth you can express numbers in different basis. We use the standard .NET conversion
        functions here, but those support just a few basis and throw exceptions on failure (bad design). We could consider writing our own to support all basis.
        Also the code can be compiled for 32 bits cell size and maybe it works. **/
    bool TryParseNumber(string s, out Cell n)
    {
        var b = ds[basep];

        try {
#if CELL32
            n = Convert.ToInt32(s, b);
#else
            n = Convert.ToInt64(s, b);
#endif
            return true;
        } catch (FormatException )
        {
            // This is not actually an exception case in Forth.
            n = 0;    

        } catch(OverflowException)
        {
            throw;
        }
        return false;
    }

    /** Now that we can parse a number, we can interpret it. This is a token interpreter. Word as described as tokens of various kind. You are either executing
        a token, or compiling it inside of another word (i.e., when using the defining word `:`). **/
    void InterpretNumber(Cell value)
    {
        if(Executing)
            Execute(Token.Numb, value);
        else
            PushOp(Token.Numb, value);
    }

    /** Executing a token is a bit complicated. Let's look first at compiling it. Compilation in this model means adding a token on top of the data stack.
        In Forth, the data stack is where user defined words live and we are in the middle of defining one. **/
    void PushOp(Token op) {
        ds[herep] = (Code)op;
        herep++;
    }
    /** Sometimes tokens have parameters. If they have a numeric one, we encode it so to minimize the overall size both in memory (for cache sake) and on disk.
        For that we use the [GitVarInt](https://www.nuget.org/packages/GitVarInt/) library. **/
    void PushOp(Token op, Cell value)
    {
        PushOp(op);
        Write7BitEncodedCell(ds, herep, value, out var howMany);
        herep += howMany;
    }

    /** ## Interpreting words **/
    /**
Now that we know how to compile numbers and that it exists a magic function that executes tokens, we can go back and look at interpreting words.
In this implementation, words are separated in user defined words, primitives and immediate primitive. This is likely a bad design brought about
by a desire of optimize prematurely. There should be an unified representation.

The logic is still relatively simple. If it is a user defined word, execute/compile a call token with its address. If it is a primitive,
execute/compile the corresponding token. If it is an immediate primitive, execute the code associated with it.

It is awkward that finding the user defined word relies on parameters on the stack, while the other cases use a .net string representation to find
the token in an hash table. Apart from style, this is an irritating allocation in the main loop that could be optimized away with some work.
    **/
    bool InterpretWord(string aword)
    {
        LowerCase();
        FindUserDefinedWord();

        var found   = Pop();
        var xt      = (Index)Pop();

        // Manage user defined word.
        if(found != FORTH_FALSE)
        {
            var immediate = found == 1; // There can be user defined immediate functions.
            if(Executing || immediate)
                Execute(Token.Call, xt);
            else
                PushOp(Token.Call, xt);
            return true;
        }
        // Manage simple primitives.
        if(WordToSimpleOp.TryGetValue(aword, out var op))
        {
            if(Executing)
                Execute(op, null);
            else
                PushOp(op);
            return true;
        }
        // Manage immediate primitives.
        if(ImmediatePrimitives.TryGetValue(aword, out var immediateWord))
        {
            immediateWord.Item2();
            return true;
        }
        return false;
    }

    /** Simple primitives map a word with the token defining it. **/
    readonly Dictionary<string, Token> WordToSimpleOp = new()
    {
        { "."           , Token.Prin },
        { "count"       , Token.Count },
        { "words"       , Token.Words },
        { "testsys"     , Token.TestSys },
        { "cells"       , Token.Cells },
        { "allot"       , Token.Allot },
        { "and"         , Token.And },
        { "or"          , Token.Or },
        { "base"        , Token.Base },
        { "refill"      , Token.Refill },
        { "interpret"   , Token.Interpret },
        { "quit"        , Token.Quit },
        { "word"        , Token.Word },
        { "parse"       , Token.Parse },
        { "save"        , Token.Save },
        { "load"        , Token.Load },
        { "savesys"     , Token.SaveSys },
        { "loadsys"     , Token.LoadSys },
        { "included"    , Token.Included },
        { ","           , Token.Comma },
        { "c,"          , Token.CComma },
        { "here"        , Token.Here },
        { "@"           , Token.At },
        { "c@"          , Token.CAt },
        { "pad"         , Token.Pad },
        { "!"           , Token.Store },
        { "state"       , Token.State },
        { "bl"          , Token.Bl },
        { ":"           , Token.Colo },
        { "bye"         , Token.Bye },
        { ".s"          , Token.DotS },
        { "+"           , Token.Plus },
        { "-"           , Token.Minu },
        { "*"           , Token.Mult },
        { "/"           , Token.Divi },
        { "<"           , Token.Less },
        { ">"           , Token.More },
        { "="           , Token.Equal },
        { "<>"          , Token.NotEqual },
        { "create"      , Token.Create },
        { "does>"       , Token.Does },
        { ">body"       , Token.Body },
        { "rdepth"      , Token.RDepth },
        { "swap"        , Token.Swap },
        { "depth"       , Token.Depth },
        { "over"        , Token.Over },
        { "dup"         , Token.Dup },
        { "dup2"        , Token.Dup2 },
        { "drop"        , Token.Drop },
        { "drop2"       , Token.Drop2 },
        { "*/mod"       , Token.MulDivRem },
        { "invert"      , Token.Invert },
        { "exit"        , Token.Exit },
        { "i"           , Token.I },
        { "j"           , Token.J },
        { ">r"          , Token.ToR },
        { "r>"          , Token.FromR },
        { "leave"       , Token.Leave },
        { "immediate"   , Token.Immediate },
        { "source"      , Token.Source },
        { "type"        , Token.Type },
        { "emit"        , Token.Emit },
        { "cr"          , Token.Cr },
        { "char"        , Token.Char },
        { ">in"         , Token.In },
        { "find"        , Token.Find },
        { "execute"     , Token.Exec },
        { ".net>type"   , Token.DType },
        { ".net>method" , Token.DMethod },
        { ".net>call"   , Token.DCall },
    };

    /** On the other hand, immediate actions need to be executed at compile time when their token is encountered.
        This is indexed by the string word, which is what we see in the interpreter. But later I discovered the need
        to index it by operator as well, so I bolted it on as a tuple. We'll look in more depth at how they work later. **/
    readonly Dictionary<string, (Token, Action)> ImmediatePrimitives = new();

    /** User defined words are stored in the data space as a linked list. Each word is in the format:
        -> Cell   <-> One Byte <-> Bytes    <-> Bytes
        | Next Link|  Len Word  | Word chars | Tokens |
        This function follow the links, staring at `dictHead`, until it finds the word on top of the parameter stack.
        As an optimization, the length field uses the higher bit to store if the word is an immediate one (save one byte, save the planet). **/
    internal void FindUserDefinedWord()
    {
        var caddr = (Index)Pop();
        var clen  = ds[caddr];
        var cspan = new Span<AChar>(ds, caddr + 1 * CHAR_SIZE, clen);

        var dp = dictHead;

        while(true)
        {
            if(dp == 0) break;

            var wordNameStart = dp + CELL_SIZE;
            var wordLenRaw    = ds[wordNameStart];
            var wordLen       = Utils.ResetHighBit(wordLenRaw); // Resets the high bit, so we get the real length.
            var wordSpan      = new Span<AChar>(ds, wordNameStart + 1 * CHAR_SIZE, wordLen);
            var found         = cspan.SequenceEqual(wordSpan);
            if(found)
            {
                Push(LinkToCode(dp, wordLen));                
                var isImmediate = Utils.HighBitValue(wordLenRaw) == 1;
                Push( isImmediate ? 1 : -1);
                return;
            }
            dp = (Index)ReadCell(ds, dp);
        }
        // Not found
        Push(caddr);
        Push(0);
    }

    /** These functions abstract out the details of the word structure. At least that was the idea. In practice that knowledge has leaked
        in other parts of the code base. **/
    static Index LinkToCode(Index link, Index wordLen)
        // Addr + Link size + len size  + word chars
        => link + CELL_SIZE + CHAR_SIZE + CHAR_SIZE * wordLen;

    static Index LinkToLen(Index link) => link + CELL_SIZE;

    /** ## Memory organization **/
    /** As we touched on the internal memory organization, it might now be time to describe it in more detail. Firstly the data space, parameter stack and
        return stack are represented as arrays of bytes. Some notes:
        * In Forth, you can access the stacks as Cells or bytes. I choose the lower denominator to simplify things.
        * I didn't use the .NET `Stack` class because it doesn't let you easily index into it.
        * One could use `unsafe` and pointers to avoid checking boundaries at each access, but then you could use the library just from unsafe code.
        **/

    Index sp     = 0;                   // Top of parameter stack.
    Index rp     = 0;                   // Top for return stack.
    Index herep  = 0;                   // Top of data space.
    AUnit[] ps;                         // Parameter stack.
    AUnit[] rs;                         // Return stack.
    AUnit[] ds;                         // Data space.

    /** Then come some pointers that map areas in the data space that are used by the system or point to some system cells.
        They get initialized in the Vm constructor. Also, some other random state used by the system. **/

    Index dictHead;                     // The last word added to the dictionary.

    readonly Index source;              // Input buffer.
    readonly Index inp;                 // Points char number (0...source_max_chars) to be read in the input buffer.
    readonly Index inputBufferSize;     // Size of the input buffer.
    readonly Index keyWord;             // Word read by the interpreter.
    readonly Index word;                // Text read by the Forth word `word`. It needs to be separated from keyWord, so as to not conflict.
    readonly Index wordBufferSize;      // Size of the `word` and `keyword` buffer.
    readonly Index pad;                 // Pad area. A temporary area usable by the user.
    readonly Index dotnetStrings;       // Area used to store string parameters passed from dotnet to Forth. 
    readonly Index code;                // Each token needs to be stored in the data space before execution, so that the instruction pointer can work.
    readonly Index basep;               // Store base for numbers.
    readonly Index state;               // Are we compiling or interpreting? See `Executing` below.
    readonly Index userStart;           // Start of the user part of the data space. `Save` starts saving from here.
    readonly Index savedDictHead;       // Used by `save` and `load` to fetch the index of the last word in the dictionary.

    Index input_len_chars = 0;          // How many chars in the input buffer.

    bool Executing { get => ReadCell (ds, state) == FORTH_FALSE;
                     set => WriteCell(ds, state, value ? FORTH_FALSE : FORTH_TRUE);}

    public bool Debug { get ; set; }    // Doesn't do much as of now, but we can extend it to print out a lot of useful thins (i.e., token disassembly).

    /** I decided for the system to use UTF8 internally to save space, despite .NET running on a 'kind of' UTF16. I convert at the boundary.
        A cell can be either 64 bits or 32 bits. I have not tested the latter. **/
    internal const Index CHAR_SIZE = 1;
    internal const Index CELL_SIZE = sizeof(Cell);

    const Cell FORTH_TRUE  = -1;
    const Cell FORTH_FALSE = 0;

    /** This is very important. By setting this field you can get the interpret to read the next line of text from wherever (typically Console or file). **/
    public Func<string>? NextLine = null;

    /** These cache the last dotnet type and method, so that you can call multiple methods on the same type ergonomically. **/
    Type lastType = typeof(Console);
    MethodInfo? lastMethod;

    /** Let's see how the pointers are initialized. First some debatable defaults for the most important
        data source areas. **/
    public Vm(
        Index parameterStackSize =  16 * 1_024,
        Index returnStackSize    =  16 * 1_024,
        Index dataStackSize      = 256 * 1_024,
        Index padSize            =       1_024,
        Index sourceSize         =       1_024,
        Index wordSize           =       1_024
        ) {

        /** These are the arrays storing the three most important memory areas. **/
        ps   = new AUnit[parameterStackSize];
        rs   = new AUnit[returnStackSize];
        ds   = new AUnit[dataStackSize];

        /** And we initialize all the pointers **/
        inputBufferSize  = sourceSize;
        wordBufferSize   = wordSize;

        code             = herep;
        herep           += CHAR_SIZE + CELL_SIZE; // Maximum size of an instruction.

        keyWord          = herep;
        herep           += wordSize * CHAR_SIZE;
        source           = herep;
        herep           += sourceSize * CHAR_SIZE;

        word             = herep;
        herep           += wordSize * CHAR_SIZE;

        pad              = herep;
        herep           += padSize;

        basep            = herep;
        herep           += CELL_SIZE;
        ds[basep]        = 10;

        dotnetStrings    = herep;
        herep           += 256 * Vm.CHAR_SIZE;

        inp              = herep;
        herep           += CELL_SIZE;

        state            = herep;
        herep           += CELL_SIZE;

        userStart        = herep;
        savedDictHead    = herep;
        herep           += CELL_SIZE;

/**
This table contains the immediate primitive words. These words are executed at compile time.
The management of conditionals and loop constructs is messy.

Take the `if` statement, at the point where the interpret encounters it, it doesn't yet know
where it has to jump to in case the condition is not satisfied.
It embeds a 'conditional' branching instruction, leaving two bytes
empty for the branching target. It also pushes the address of the empty bytes.
When the interpret encounters `else` or `then`, it backfill those empty bytes with
the right value so that the branch instruction jumps to the correct point.

Other flow control structures behave similarly. **/
        ImmediatePrimitives = new()
        {
            { "debug",      (Token.IDebug,     () => Debug = !Debug) },
            { "[char]",     (Token.IChar,      () => { Char(); PushOp(Token.Numb, Pop());}) },
            { "literal",    (Token.ILiteral,   () => { PushOp(Token.Numb, Pop());}) },
            { "sliteral",   (Token.ISLit,      EmbedSString(Token.ISLit)) },
            { "[",          (Token.IBrakO,     () => Executing = true) },
            { "]",          (Token.IBrakC,     () => Executing = false) },
            { ";",          (Token.ISemi,      () => { PushOp(Token.Exit);  Executing = true; }) },
            { "postpone",   (Token.IPostCall,  Postpone) },
            { "begin",      (Token.IBegin,     () => Push(herep)) },
            { "do",         (Token.IDo,        () => { PushOp(Token.Do); herep += CELL_SIZE; Push(herep); }) },
            { "loop",       (Token.ILoop,      EmbedHereJmp0Bck)},
            { "+loop",      (Token.ILoopP,     EmbedHereJmp0BckP)},
            { "again",      (Token.IAgain,     EmbedHereJmpBck)},    
            { "if",         (Token.IIf,        BranchAndMark) },
            { "else",       (Token.IElse,      EmbedInPoppedJmpFwd)  },    
            { "then",       (Token.IThen,      () => {
                var mark = (Index)Pop();
                short delta = (short)(herep - mark);
                WriteInt16(ds, mark, delta);
            }) },
            { "while",      (Token.IWhile,     BranchAndMark) },    
            { "repeat",     (Token.IRepeat,    () => {
                var whileMark = (Index)Pop();
                short delta   = (short)(herep + 3 - whileMark);
                WriteInt16(ds, whileMark, delta);
                EmbedHereJmpBck();
                }) },
            { "c\"",         (Token.ICStr,     EmbedString(Token.ICStr)) },
            { "s\"",         (Token.ISStr,     EmbedString(Token.ISStr)) },
        };

        /**
After everything is set up, we can now load the initialization files. In Forth, normally 
a good part of the interpret is written in Forth itself. Implementations vary with regard to
how many instructions are primitives. The obvious trade-offs apply.

Here we try to load from a binary file first, if present.
        
So, that was the idea, at least. In practice I got tired of trying to figure out where such a file should live to be
included correctly by the dll in all scenarios. So embedded as a constant instead. It also good so that you can just
copy this single file in a project and be done.
         **/
        EvaluateMultipleLines(INIT_FORTH);
        /*
        if(File.Exists("init.io"))
        {
            FromDotNetString("init.io");
            LoadSystem(true);
        } else if(File.Exists("init.fth")) {
            FromDotNetString("init.fth");
            Included();
        } else
        {
            Console.WriteLine("No init file loaded.");
        }
        */

        Reset();
    }
    /** In our token based interpret, loading and saving the system is a trivial operation. Just copy the bytes. **/
    void SaveSystem(bool all = false)
    {
        var start = all ? 0 : userStart ;
        WriteCell(ds, savedDictHead, dictHead);

        var fileName = ToDotNetString();
        File.WriteAllBytes(fileName, ds[start .. herep]);
        Console.WriteLine($"Saved in file {Path.Join(Environment.CurrentDirectory, fileName)} .");
    }
    void LoadSystem(bool all = false)
    {
        var start = all ? 0 : userStart ;
        var fileName = ToDotNetString();
        var buf      = File.ReadAllBytes(fileName);
        buf.CopyTo(ds, start);

        dictHead = (Index)ReadCell(ds, savedDictHead);
        herep = buf.Length;
        Console.WriteLine($"Loaded from file {Path.Join(Environment.CurrentDirectory, fileName)}");
    }

    /** ## Executing words **/
    /**
With all of that under our belt, we can now look at `Execute`. This is `switch threaded`.
It could be written as a table of `Token` to `Action`, but then I would lose possible
performance tricks that the compiler can play to speed it up (i.e., using function pointers).

It is a giant `do ... while` loop of getting the Token at the instruction pointer and performing
the associated action. The action might change the instruction pointer (i.e., jumps), so when
we get back to the top of the loop, we might be executing at a very different `ip` from where started.

We will comment on the most interesting cases.
    **/

    Index PushExecOp(Token op, Cell? value)
    {
        ds[code] = (Code)op;
        var howMany = 0;

        if(value is not null) Write7BitEncodedCell(ds, code + 1, (Cell)value, out howMany);
        return howMany + 1;
    }

    void Execute(Token op, Cell? data) {

        Index opLen = PushExecOp(op, data); // Store the token and data in the code area so IP can point to it.
        var ip      = code;

        do {
            var token = (Token)ds[ip];
            ip++;

            Cell n, flag, index, limit, incr ;
            Index count, idx;
            bool bflag;

            switch(token) {
                case Token.Words:
                    Console.WriteLine(string.Join(" ", Words()));
                    break;
                case Token.Source:
                    Source();
                    break;
                case Token.Base:
                    Push(basep);
                    break;
                case Token.Emit:
                    Console.Write((char)Pop());
                    break;
                case Token.Type:
                    Console.Write(ToDotNetString());
                    break;
                /** A string literal gets stored immediately after the ip. **/
                case Token.ISLit:
                    count = ds[ip];
                    Push(ip + 1);
                    Push(count);
                    ip += count + 1;
                    break;
                /** Postponing is a two step process. At compile time, we embed one of the two tokens below.
                    They allow postponing either user defined functions or primitives. For immediate primitives, 
                    postponing means embedding a token to execute the action. See description of `Postpone()` later.**/
                case Token.IPostCall:
                    n = Read7BitEncodedCell(ds, ip, out count);
                    PushOp(Token.Call, n);
                    ip += count;
                    break;
                case Token.IPostponeOp:
                    PushOp((Token)ds[ip]);
                    ip += 2; // There is a noop here to classify this as 2 bytes operation.
                    break;
                case Token.ImmCall:
                    var act = ImmediateAction((Token)ds[ip]);
                    if(act is null) Throw($"ImmCall with a non existing op: {(Token)ds[ip]}");
                    act();
                    ip++;
                    break;
                case Token.Allot:
                    herep += (Index)Pop();
                    break;
                /** These three tokens enable my rudimentary (static methods) dotnet integration. **/
                case Token.DType:
                    var typeName = ToDotNetString();
                    var aType = Type.GetType(typeName);
                    if(aType is null) Throw($"Cannot find type {typeName}");
                    lastType = aType;
                    break;
                case Token.DMethod:
                    var methodName = ToDotNetString();
                    lastMethod = lastType.GetMethod(methodName);
                    if(lastMethod is null) Throw($"No method {methodName} on type {lastType.Name}");
                    break;
                case Token.DCall:
                    DCall();
                    break;
                case Token.Cells:
                    Push(Pop() * CELL_SIZE);
                    break;
                case Token.Find:
                    Find();
                    break;
                case Token.Exec:
                    idx = (Index)Pop();
                    RPush(ip);
                    ip = (Index)idx;
                    break;
                case Token.Cr:
                    Console.WriteLine();
                    break;
                case Token.In:
                    Push(inp);
                    break;
                case Token.Char:
                    Char();
                    break;
                case Token.ICStr:
                    Push(ip);
                    ip += ds[ip] + 1;
                    break;
                case Token.ISStr:
                    Push(ip + 1);
                    idx = ds[ip];
                    Push(idx);
                    ip += idx + 1;
                    break;
                /** Numb and NumbEx are two ways to embed a number in the ip stream. The latter is needed 
                    because of the control structures (i.e., loop, if, ...). We need to use a full cell to store
                    the jumping point as we don't know upfront where we are going to jump to, hence how many bytes
                    we are going to need to store the address. **/
                case Token.Numb:
                    n = Read7BitEncodedCell(ds, ip, out count);
                    Push(n);
                    ip += count;
                    break;
                case Token.NumbEx:
                    n = ReadCell(ds, ip);
                    Push(n);
                    ip = (Index)RPop();
                    break;
                case Token.Prin:
                    n = Pop();
                    Console.Write($"{Convert.ToString(n, ds[basep])} ");
                    break;
                case Token.Count:
                    Count();
                    break;
                case Token.Refill:
                    Refill();
                    break;
                case Token.Word:
                    Word();
                    break;
                case Token.Parse:
                    Parse();
                    break;
                case Token.Comma:
                    Comma();
                    break;
                case Token.CComma:
                    ds[herep] = (byte) Pop();
                    herep++;
                    break;
                case Token.Save:
                    SaveSystem();
                    break;
                case Token.Load:
                    LoadSystem();
                    break;
                case Token.SaveSys:
                    SaveSystem(true);
                    break;
                case Token.LoadSys:
                    LoadSystem(true);
                    break;
                case Token.Included:
                    Included();
                    break;
                case Token.Here:
                    Here();
                    break;
                case Token.ToR:
                    PStoRS();
                    break;
                case Token.FromR:
                    FromR();
                    break;
                case Token.At:
                    At();
                    break;
                case Token.CAt:
                    CFetch();
                    break;
                case Token.Pad:
                    Push(pad);
                    break;
                case Token.Drop:
                    Drop();
                    break;
                case Token.Drop2:
                    Drop2();
                    break;
                case Token.MulDivRem:
                    MulDivRem();
                    break;
                case Token.Store:
                    Store();
                    break;
                case Token.State:
                    State();
                    break;
                case Token.Bl:
                    Bl();
                    break;
                case Token.Dup:
                    Dup();
                    break;
                case Token.Swap:
                    Swap();
                    break;
                case Token.Over:
                    Over();
                    break;
                case Token.Dup2:
                    Dup2();
                    break;
                case Token.Bye:
                    Environment.Exit(0);
                    break;
                case Token.TestSys:
                    EvaluateMultipleLines(PRELIM_TEST);
                    break;
                case Token.DotS:
                    Console.WriteLine(DotS());
                    break;
                case Token.Quit:
                    Quit();
                    break;
                case Token.Interpret:
                    Interpret();
                    break;
                case Token.And:
                    Push(Pop() & Pop());
                    break;
                case Token.Or:
                    Push(Pop() | Pop());
                    break;
                case Token.Plus:
                    Push(Pop() + Pop());
                    break;
                case Token.Minu:
                    Push(- Pop() + Pop());
                    break;
                case Token.Mult:
                    Push(Pop() * Pop());
                    break;
                case Token.Less:
                    Push(Pop() > Pop() ? FORTH_TRUE : FORTH_FALSE);
                    break;
                case Token.More:
                    Push(Pop() < Pop() ? FORTH_TRUE : FORTH_FALSE);
                    break;
                case Token.Equal:
                    Push(Pop() == Pop() ? FORTH_TRUE : FORTH_FALSE);
                    break;
                case Token.NotEqual:
                    Push(Pop() != Pop() ? FORTH_TRUE : FORTH_FALSE);
                    break;
                case Token.Depth:
                    Push(sp / CELL_SIZE);
                    break;
                case Token.RDepth:
                    Push(rp / CELL_SIZE);
                    break;
                case Token.Invert:
                    Push(~Pop());
                    break;
                case Token.Immediate:
                    Utils.SetHighBit(ref ds[LinkToLen(dictHead)]);
                    break;
                case Token.Divi:
                    var d = Pop();
                    var u = Pop();
                    Push(u / d);
                    break;
                case Token.Body:
                    Push(Pop() + 1 + CELL_SIZE); // See create below.
                    break;
                /** `create ... does>` is the pearl of Forth, but its implementation is complex. `create`
                    gives a byte address in the data space a name adding it to the dictionary. It also embed
                    a token in the instruction stream to push the created address when executed, so that
                    this works `create bob 10 , bob ?`. Here is a place when we need to use `NumbEx`. **/
                case Token.Create:
                    Push(' ');
                    Word();
                    if(ds[Peek()] == 0) Throw("'create' needs a subsequent word in the stream.");
                    LowerCase();
                    DictAdd();

                    PushOp(Token.NumbEx);
                    // Need to use full cell because it gets substituted by a jmp in does>
                    // and I don't know how many cells the number I need to jump to takes
                    WriteCell(ds, herep, herep + CELL_SIZE);
                    herep += CELL_SIZE;
                    break;
                /** Here stuff is tricky. First we get the address for the code of the last `created` word
                    (where `create` stored the `NumbEx addr`). We then override it with a jump to the current
                    `ip`. Then we store an instruction to push the address of the last created word. Finally,
                    we copy all the remaining tokens until we encounter Exit. This allows the following to
                    work and print `10`: `: var create  , does> ? ;   10 var bob  bob`**/
                case Token.Does:
                    // Allows adding code to the last `defined` word, not just last `created` one!!
                    // Even in gforth!!
                    idx = Lastxt();
                    var addrToPush = ReadCell(ds, idx + 1);
                    
                    var currentIp = herep;
                    herep = idx;
                    PushOp(Token.Jmp, currentIp);
                    herep = currentIp;
                    PushOp(Token.Numb, addrToPush);
                    PushUntilExit(ref ip);
                    ip = (Index)RPop();
                    break;
                /** Colon `:`, despite its importance, is utterly simple. Get the next word from the input
                    buffer, add it to the dictionary and move the interpret to compile mode. **/
                case Token.Colo:
                    Push(' ');
                    Word();
                    if(ds[Peek()] == 0) Throw("Colon needs a subsequent word in the stream.");
                    LowerCase();
                    DictAdd();
                    Executing = false;
                    break;
                /** A `call` stores the ip on the return stack, while `jmp` just moves the ip. **/
                case Token.Call:
                    n = Read7BitEncodedCell(ds, ip, out count);
                    ip += count;
                    RPush(ip);
                    ip = (Index)n;
                    break;
                case Token.Jmp:
                    n = Read7BitEncodedCell(ds, ip, out _);
                    ip = (Index) n;
                    break;
                /** `exit` gets the ip from the return stack **/
                case Token.Exit:
                    ip = (Index)RPop();
                    break;
                /** Here come all the instructions for control flow control in a stack based Vm.
                    I recommend stepping through a `do ... loop` to understand the tricky details.
                    Generally, the return stack contains `ip after the loop | limit | index`.
                    'ip after the loop` is needed by the `leave` instruction, which needs to know where
                    to go. There might be a way to pre-calculate this at compile time, but I didn't find it.
                **/
                case Token.Branch0:
                    flag = Pop();
                    ip += flag == FORTH_FALSE ? ReadInt16(ds, ip) : 2 ;
                    break;
                case Token.Do:
                    RPush(ReadCell(ds, ip));
                    ip += CELL_SIZE;
                    PStoRS();
                    PStoRS();
                    break;
                case Token.I:
                    Push(ReadCell(rs, rp - CELL_SIZE * 2));
                    break;
                case Token.J:
                    Push(ReadCell(rs, rp - CELL_SIZE * 5));
                    break;
                case Token.Leave:
                    RPop();
                    RPop();
                    ip = (Index)RPop();
                    break;
                case Token.Loop:
                    limit = RPop();
                    index = RPop();
                    index++;
                    bflag = index < limit; 
                    if(bflag) {
                        RPush(index);
                        RPush(limit);
                        ip += ReadInt16(ds, ip);
                    }  else {
                        RPop();
                        ip += 2;
                    }
                    break;
                case Token.LoopP:
                    incr  = Pop();
                    limit = RPop();
                    index = RPop();
                    index += incr;
                    bflag = incr > 0 ? index < limit : index >= limit; 
                    if(bflag) {
                        RPush(index);
                        RPush(limit);
                        ip += ReadInt16(ds, ip);
                    }  else {
                        RPop();
                        ip += 2;
                    }
                    break;
                /** For control structures we use relative jumps of 2 bytes, not to waste a full cell for them.
                    If you have an `if` statement longer than `FFFFFFFF` bytes, you are screwed. **/
                case Token.RelJmp:
                    ip += ReadInt16(ds, ip);
                    break;
                default:
                    /** We can treat all the immediate primitives in a generic way, by just calling their
                        associated action. **/
                    var a = ImmediateAction(token); 
                    if(a is not null)
                        a();
                    else 
                        Throw($"{(Token)op} bytecode not supported.");
                    break;
            }
        /** When do we stop looping? It took me a while to get this one right. The `ip` wonders around the
            data space following all the `call` and `jmp`,but, eventually, it comes back where it started. **/
        } while (ip != code + opLen);
    }

    /** Some details **/
    /** Now you should understand the how interpretation and compilation work. The rest is details.
        Let's look at a few of them.**/

    /** Forth uses `included` to interpret code in external files. Note the versatility of `NextLine`.
        We used it before as part of the interpret loop. Now we use the same mechanism to read from 
        a file and keep track of line numbers for the sake of error messages. **/
    void Included()
    {

        var lineNum  = 0;
        var lineText = "";

        var fileName     = ToDotNetString();
        using var stream = File.OpenRead(fileName);
        using var reader = new StreamReader(stream);
        var backupNL     = NextLine;

        try {
            if (Debug) Console.Write($"Interpreting file {fileName} ...");
            NextLine = () => { lineNum++; lineText = reader.ReadLine()! ; return lineText; };

            Quit();

            if (Debug) Console.WriteLine(" done.\n");
        } catch(Exception)
        {
            ColorLine(ConsoleColor.Red, $"File: {fileName} Line: {lineNum}\n{lineText}");
            throw; 
        } finally
        {
            NextLine = backupNL;
        }
    }
    /** This is awkward. When processing `does>`, I need to copy all the tokens until `Exit`. But how do I
        recognize it? There could be a byte with `Exit` value, which is part of some number. So I divide the
        tokens according to their lengths push each instruction one by one. There is certainly a better way,
        likely involving a more careful design of the token representation. **/
    void PushUntilExit(ref Index ip)
    {
        while(true)
        {
            var token = ds[ip];
            var count = 0;

            if(Utils.HasCellSize(token))
                count = CELL_SIZE;
            else if(Utils.HasVarNumberSize(token))
                Read7BitEncodedCell(ds, ip + 1, out count);
            else if(Utils.HasStringSize(token))
                count += ds[ip + 1] + 1; // Size of string + 1 for the len byte.

            Array.Copy(ds, ip, ds, herep, 1 + count);

            ip    += 1 + count;
            herep += 1 + count;

            if((Token)token == Token.Exit)
                break;
        }
    }


    /** This long and ugly beast navigates the dictionary linked list until it finds a word. If it doesn't, it
        looks for one in the primitives table, otherwise in the immediate primitive table. **/
    void Find()
    {
        // Look for user defined word
        var caddr = (Index)Peek();
        FindUserDefinedWord();

        if(Peek() != FORTH_FALSE) return;

        var clen  = ds[caddr];
        var cspan = new Span<AChar>(ds, caddr + 1 * CHAR_SIZE, clen);
        var sl    = Encoding.UTF8.GetString(cspan);

        // Look for simple statements
        if(WordToSimpleOp.TryGetValue(sl, out var op))
        {
            RetNewOp(op);
            return;
        }
        // Look for immediate words
        if(ImmediatePrimitives.TryGetValue(sl, out var imm))
        {
            RetNewImmediateOp(imm.Item1);
            return;
        }
        // Getting here, we return the result of FindUserDefinedWord. Below utility funcs.
        void RetNewImmediateOp(Token op)
        {
            var xt = herep;
            PushOp(Token.ImmCall);
            PushOp(op);
            PushOp(Token.Exit);
            Ret(xt, 1);
        }
        void RetNewOp(Token op)
        {
            var xt = herep;
            PushOp(op);
            PushOp(Token.Exit);
            Ret(xt, -1);
        }
        void Ret(Index xt, Cell f)
        {
            Drop(); Drop(); Push(xt); Push(f);
        }
    }
    Index Lastxt() => LinkToCode(dictHead, ds[dictHead + CELL_SIZE]);

    /** Adding to the dictionary means adding to a linked list. Just bytes twiddling. **/
    internal void DictAdd()
    {
        // First put the link
        Push(dictHead);             // Push last index
        dictHead = herep;           // DH is now here
        Comma();                    // Store last index in new dictHead (here)

        // Copy word to here
        var len = ds[(Index)Peek()];
        Push(herep);
        Push(len + 1);
        CMove();
        herep += len + 1;
    }
    /** `Postpone` gave me some grief. In summary, if it is a user defined word, postpone a call to its xt;
        if it is a primitive, postpone its token; if it is an immediate primitive, postpone its token. **/
    void Postpone()
    {
        Bl();
        Word();
        LowerCase();
        Dup();
        var sl = ToDotNetStringC();

        FindUserDefinedWord();

        var res = Pop();
        var xt    = (Index)Pop();

        if(res != FORTH_FALSE) {
            PushOp(Token.IPostCall, xt);
            return;
        }

        if(WordToSimpleOp.TryGetValue(sl, out var op)) {
            PushOp(Token.IPostponeOp);
            PushOp(op);
            PushOp(Token.Noop); // Need that because we don't have tokens with 2 bytes operands.
            return;
        }

        // TODO: confirm that all immediate tokens are one byte long.
        if(ImmediatePrimitives.TryGetValue(sl, out var imm)) {
            PushOp(imm.Item1);
            return;
        }
        Throw($"{sl: don't know this word.}");
    }
    /** ## List of Tokens **/
    /** In each implementation, each instruction is (Token, Operand) where Token is one byte, while operand
        can be 1 byts, 2 bytes, Cell size, a variable number or a string. There is vast literature, but no agreement
        on the most optimal number of primitives for a Forth system. It is a trade off between easy of porting vs 
        performance and compactness. I stayed somewhere in the middle. Whatever seemed to be highly optimizable
        as a single instruction became one. Having said that, if I had to do it again, I would probably have less primitives. **/
    internal enum Token {
        Error , Colo,  Does, Plus, Minu, Mult, Divi, Prin, Base, Noop,
        Count, Word, Parse, Refill, Comma, CComma, Here, At, Store, State, Bl, Dup, Exit, Immediate,
        Swap, Dup2, Drop, Drop2, Find, Bye, DotS, Interpret, Quit, Create, Body, RDepth, Depth,
        Less, Words, TestSys, More, Equal, NotEqual, Do, Loop, LoopP, ToR, FromR, I, J, Leave, Cr,
        Source, Type, Emit, Char, In, Over, And, Or, Allot, Cells, Exec, Invert, MulDivRem,
        Save, Load, SaveSys, LoadSys, Included, DType, DCall, DMethod, CAt, Pad,
        IDebug, ISemi,  IBegin, IDo, ILoop, ILoopP, IAgain, IIf, IElse, IThen,
        IWhile, IRepeat, IBrakO, IBrakC,   // End of 1 byte
        Branch0, RelJmp, ImmCall, IPostponeOp,// End of 2 byte size
        NumbEx, // End of CELL Size 
        Jmp , Numb, Call, IPostCall, ILiteral, IChar,// End of Var number
        ICStr, ISStr, ISLit, // End of string words
        FirstHasVarNumb = Jmp, FirstHas2Size = Branch0, FirstHasCellSize = NumbEx,
        FirstStringWord = ICStr,
    }

    /** ## Forth defined primitives **/
    /** Whatever is not a primitive, is implemented in Forth in the giant string below. Note that
        comments are implemented in Forth. What other language lets you do that? I guess Lisp, SmallTalk
        and derivatives ... **/
    const string INIT_FORTH = @"
: ( [char] ) parse drop drop ; immediate
: \ 0 word drop ; immediate

\ Some modified from https://theforth.net/package/minimal/current-view/README.md
: variable create 0 , ;
: constant create , does> @ ;

\ Arithmetic
: 1+ 1 + ;
: 2+ 2 + ;
: 1- 1 - ;
: 2- 2 - ;
: min ( n1 n2 -- n3 )  over over > if swap then drop ;
: max ( n1 n2 -- n3 )  over over < if swap then drop ;
: mod ( n n -- n )  1 swap */mod drop ;
: dec 10 base ! ;
: hex 16 base ! ;
: 2* 2 * ;
: negate -1 * ;
: d- - ;

\ Stack
: rot ( x1 x2 x3 -- x2 x3 x1 )  >r  swap r> swap ;
: -rot ( x1 x2 x3 -- x3 x2 x1 )  rot rot ;
: nip ( x1 x2 -- x2 )  swap drop ;
: tuck ( x1 x2 -- x2 x1 x2 )  swap over ;
: ?dup dup 0 <> if dup then ;
: bounds ( addr1 u -- addr2 addr3 )  over + swap ;
: 2dup ( d1 -- d1 d1 )  over over ;
: 2swap ( d1 d2 -- d2 d1 )  >r rot rot r> rot rot ;
: 2over ( d1 d2 -- d1 d2 d1 )  >r >r 2dup r> r> 2swap ;
: um/mod 2dup mod -rot / ;

\ Boolean
0 constant false
false invert constant true
: 0= 0 = ;
: 0< 0 < ;
: 0> 0 > ;
: or ( x x -- x )  invert swap invert and invert ; ( do morgan )
: xor ( x x -- x )  over over invert and >r swap invert and r> or ;
: lshift ( x1 u -- x2 )  begin dup while >r  2*  r> 1 - repeat drop ;
: endif postpone then ; immediate

\ Memory
: ? @ . ;
: +! ( x addr -- )  swap over @ + swap ! ;
: chars ;
: char+ ( c-addr1 -- c-addr2 )  1 chars + ;
: cell+ ( addr1 -- addr2 )  1 cells + ;
: aligned ( addr -- a-addr )  cell+ 1 -   1 cells 1 - invert  and ;
: 2! ( d addr -- )   SWAP OVER ! CELL+ ! ;
: 2@ ( addr -- d )  DUP CELL+ @ SWAP @ ;

\ Compiler
: ' bl word find drop ;
: ['] ' postpone literal ; immediate
: value ( -- )  create , does> @ ;
: defer ( ""<spaces>name"" -- )  create 0 , does> @ execute ;
: to ( x ""<spaces>name"" -- ) 
   state @ 
   if  postpone [']  postpone >body postpone !  
   else ' >body ! then ; immediate

: is ( x ""<spaces>name"" -- ) 
   state @ if  postpone to  else ['] to execute  then ; immediate

\ Strings
: space ( -- )  bl emit ;
: spaces ( u -- ) dup 0 > if  begin dup while  space 1 -  repeat  then  drop ;

\ .net inteop samples
: .net ( type-s-addr type-c methodName-s-addr method-name-c -- ** )
	2swap .net>type .net>method .net>call ;

: escape s"" System.Uri, System"" s"" EscapeDataString"" .net ;
: sqrt s"" System.Math"" s"" Sqrt"" .net ;
";

    /** The rest is either byte fiddling or control structures details. You should be able to understand it
        on your own, given what explained before. If you are highly motivated.**/
    void CMove()
    {
        var u  = Pop();
        var a2 = Pop();
        var a1 = Pop();
        Array.Copy(ds, a1, ds, a2, u);
    }
    void BranchAndMark() { PushOp(Token.Branch0); Push(herep); herep += 2;}
    void EmbedInPoppedJmpFwd() {
            PushOp(Token.RelJmp);
            var mark = (Index)Pop();
            short delta = (short)((herep + 2) - mark);
            WriteInt16(ds, mark, delta);
            Push(herep);
            herep += 2;
    }
    void EmbedHereJmpBck() {
            PushOp(Token.RelJmp);
            var mark = (Index)Pop();
            var delta = (short)(mark - herep);
            WriteInt16(ds, herep, delta); 
            herep += 2;
    }
    void EmbedHereJmp0Bck() {
            PushOp(Token.Loop);
            var mark = (Index)Pop();
            var delta = (short)(mark - herep);
            WriteInt16(ds, herep, delta); 
            herep += 2;

            var leaveTarget = mark - CELL_SIZE;
            WriteCell(ds, leaveTarget, herep);
    }
    void EmbedHereJmp0BckP() {
            PushOp(Token.LoopP);
            var mark = (Index)Pop();
            var delta = (short)(mark - herep);
            WriteInt16(ds, herep, delta); 
            herep += 2;
    }
    Action EmbedString(Token op) { return () =>
    {
        PushOp(op);

        Push('"');
        Parse();
        var len = (Index)Peek();
        Push(herep + 1);
        Swap();
        CMove();
        if(Executing && op == Token.ICStr) Push(herep);
        if(Executing && op == Token.ISStr) { Push(herep + 1); Push(len); }
        ds[herep] = (byte)len;
        herep += len + 1;
    }; }

    Action EmbedSString(Token op) { return () =>
    {
        PushOp(op);
        var len = (Index)Pop();
        Push(herep + 1);
        Push(len);
        CMove();
        ds[herep] = (byte)len;
        herep += len + 1;
    };}
    public void Reset()
    {
        sp = 0; rp = 0; Executing = true; ds[inp] = 0;
    }
    public void EvaluateSingleLine(string forthLine)
    {
        var oldLine = NextLine;
        try { 
            NextLine = () => forthLine;
            Refill();
            if(Pop() == FORTH_TRUE)
                Interpret();
        } finally
        {
            NextLine = oldLine;
        }
    }
    // Code duplication from EvaluateSingleLine on purpose for perf reason. TODO: Refactor EvaluateMultipleLines.
    public void EvaluateMultipleLines(string forthCode)
    {
        var oldLine = NextLine;
        try {
            var s = "";
            var forthLines = forthCode.Split('\n');
            var sLine   = () => s;
            NextLine = sLine;
            foreach (var l in forthLines)
            {
                s = l;
                Refill();
                if(Pop() == FORTH_TRUE)
                    Interpret();
            }
        } finally
        {
            NextLine = oldLine;
        }
    }
    public IEnumerable<string> Words()
    {
        List<string> userWords = new();

        var dp = dictHead;
        while(true)
        {
            if(dp == 0) break;

            var wordNameStart = dp + CELL_SIZE;
            var wordLenRaw    = ds[wordNameStart];
            var wordLen       = Utils.ResetHighBit(wordLenRaw);
            var wordSpan      = new Span<AChar>(ds, wordNameStart + 1 * CHAR_SIZE, wordLen);
            var wordName      = Encoding.UTF8.GetString(wordSpan);
            userWords.Add(wordName);

            dp = (Index)ReadCell(ds, dp);
        }
        return WordToSimpleOp.Keys.Concat(ImmediatePrimitives.Keys).Concat(userWords).OrderBy(s => s);
    }

    // TODO: clean this up, it now works because the default value of Op is 0 -> Error. It should use some kind of multidictionary here.
    Action? ImmediateAction(Token op) {
        var result =
            ImmediatePrimitives.FirstOrDefault(e => e.Value.Item1 == op);
        if(result.Value.Item1 != default)
            return result.Value.Item2;
        else
            return null;
    }

    void LowerCase()
    {
        var s  = (Index)Peek();
        var bs = new Span<byte>(ds, s + 1, ds[s]);
        foreach(ref byte b in bs)
            b = (byte) char.ToLowerInvariant((char)b); 
    }
    static void ColorLine(ConsoleColor color, string s) {
        var backupcolor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(s);
        Console.ForegroundColor = backupcolor;
    }
    internal bool IsEmptyWord() => ds[Peek()] == 0;

    public string DotS()
    {
        StringBuilder sb = new("Stack: ");
        //sb.Append($"<{sp / CELL_SIZE}> ");
        for (int i = 0; i < sp; i += CELL_SIZE)
        {
            sb.Append(ReadCell(ps, i)); sb.Append(' ');
        }
        if(sp == 0) sb.Append("empty");
        return sb.ToString();
    }
    internal void Bl()    => Push(' ');
    void State() => Push(state);

    /* These are internal to be able to test them. What a bother. */
    internal void Push(Cell n)  => ps = Utils.Add(ps, ref sp, n);
    internal Cell Pop()         => Utils.ReadBeforeIndex(ps, ref sp);
    internal Cell Peek()        => Utils.ReadCell(ps, sp - CELL_SIZE);

    internal void RPush(Cell n) => rs = Utils.Add(rs, ref rp, n);
    internal Cell RPop()        => Utils.ReadBeforeIndex(rs, ref rp);
    void PStoRS()                  => RPush(Pop());
    void FromR()                => Push(RPop());

    internal void DPush(Cell n) => ds = Utils.Add(ds, ref herep, n);

    internal void Comma()       => ds = Utils.Add(ds, ref herep, Pop());
    internal void Store()       => Utils.WriteCell(ds, (Index)Pop(), Pop());
    internal void At()          => Push(Utils.ReadCell(ds, (Index)Pop()));
    internal void Here()        => Push(herep);
    internal void Depth()       => Push(sp / CELL_SIZE);
    internal void Over()        => Push(ReadCell(ps, sp - CELL_SIZE * 2));
    internal void Swap()        { var a = Pop(); var b = Pop(); Push(a); Push(b); }

    void Char()
    {
        Bl();
        Word();
        var idx = (Index)Pop();
        if(ds[idx] == 0) Throw("'char' need more input.");
        Push(ds[idx + 1]);
    }
    internal void Refill()
    {
        if(NextLine == null) Throw("Trying to Refill, without having passed a NextLine func");

        var inputBuffer = NextLine();

        if (inputBuffer == null)
        {
            Push(FORTH_FALSE);
        }
        else
        {
            inputBuffer = inputBuffer.Trim();
            input_len_chars = Encoding.UTF8.GetByteCount(inputBuffer);
            if (input_len_chars > inputBufferSize)
                throw new Exception(
                $"Cannot parse a line longer than {inputBufferSize}. {inputBuffer} is {input_len_chars} chars long.");
            var inputCharSpan = ToChars(source, input_len_chars);
            var bytes = new Span<byte>(Encoding.UTF8.GetBytes(inputBuffer));
            bytes.CopyTo(inputCharSpan);
            WriteCell(ds, inp, 0);
            Push(FORTH_TRUE);
        }
    }
    internal void Source()
    {
        Push(source);
        Push(input_len_chars);
    }

    internal void Compare()
    {
        var u2 = (Index)Pop();
        var a2 = (Index)Pop();
        var u1 = (Index)Pop();
        var a1 = (Index)Pop();

        var s1 = new Span<AChar>(ds, a1, u1);
        var s2 = new Span<AChar>(ds, a2, u2);
        var r  = MemoryExtensions.SequenceCompareTo(s1, s2);
        Push(r < 0 ? -1 : r > 0 ? +1 : 0);
    }
    internal void Drop()  => sp -= CELL_SIZE;
    internal void Drop2() => sp -= CELL_SIZE * 2; 
    internal void Dup()   => Push(Peek());
    internal void Dup2()
    {
        var x2 = Pop();
        var x1 = Pop();
        Push(x1);
        Push(x2);
        Push(x1);
        Push(x2);
    }
    void MulDivRem()
    {
        var n3 = Pop();
        var n2 = Pop();
        var n1 = Pop();
        (var n5, var n4) = Math.DivRem(n1 * n2, n3);
        Push(n4);
        Push(n5);
    }

    void DCall()
    {
        if(lastMethod is null) Throw($"No method UNKNOWN on type {lastType.Name}");
        var args = lastMethod.GetParameters().Reverse();
        var objs =
            args.Select<ParameterInfo, object>(p => p.ParameterType == typeof(string) ? ToDotNetString() : (Cell) Pop());
        var res = lastMethod.Invoke(null, objs.Reverse().ToArray());
        if(res is null)
            Throw("null returned from this invocation.");
        else
            if(res.GetType() == typeof(string))
                FromDotNetString((string)res);
            else
                Push(Convert.ToInt64(res));
    }
    /* It is implemented like this to avoid endianess problems **/
    void CFetch()
    {
        var c = (Index)Pop();
        var sl = new Span<AUnit>(ds, c, 1);
        Push(sl[0]);
    }

    internal void Count()
    {
        var start = (Index) Pop();
        Push(start + 1);
        Push(ds[start]);
    }
    void FromDotNetString(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        bytes.CopyTo(ds, dotnetStrings);
        Push(dotnetStrings);
        Push(bytes.Length);
    }
    internal string ToDotNetStringC()
    {
        var a = (Index)Pop();
        var s = new Span<AChar>(ds, a + CHAR_SIZE, ds[a]);
        return Encoding.UTF8.GetString(s);
    }
    internal string ToDotNetString()
    {
        var c = (Index)Pop();
        var a = (Index)Pop();
        var s = new Span<AUnit>(ds, a, c);
        return Encoding.UTF8.GetString(s);
    }
    internal void Parse()
    {
        var delim = (byte) Pop();
        ref var off = ref ds[inp]; 
        var addr = source + off;
        var startOff = off;

        while(ds[source + off] != delim) off++;
        off++;

        Push(addr);
        Push(off - startOff - 1);
    }
    /* TODO: the delimiter in this implementation (and Forth) as to be one byte char, but UTF8 puts that into question */
    internal void Word(bool inKeyword = false)
    {
        var delim = (byte)Pop();
        var s = ToChars(source, input_len_chars);
        var toPtr = inKeyword ? keyWord : word;

        var w = ToChars(toPtr, wordBufferSize);

        var j = 1; // It is a counted string, the first byte contains the length

        ref var index = ref ds[this.inp];

        while (index < input_len_chars && s[(Index)index] == delim) { index++; }

        // If all spaces to the end of the input, return a string with length 0. 
        if (index >= input_len_chars)
        {
            w[0] = (byte)0;
            Push(toPtr);
            return;
        }

        // Copy chars until end of space allocated, end of buffer or delim.
        while (j < wordBufferSize && index < input_len_chars && s[(Index)index] != delim)
        {
            var c = s[(Index)index];
            index++;
            w[j++] = c;
        }
        // Points past the delimiter. Otherwise it would stay on last " of a string.
        if(index < input_len_chars) index++;
        if (j >= wordBufferSize) throw new Exception($"Word longer than {wordBufferSize}: {Encoding.UTF8.GetString(s)}");

        w[0] = (byte)(j - 1);  // len goes into the first char
        Push(toPtr);
    }
    Span<byte> ToChars(Index start, Index lenInBytes)
            => new(ds, start, lenInBytes);
    const string PRELIM_TEST = @"
CR CR SOURCE TYPE ( Preliminary test ) CR
SOURCE ( These lines test SOURCE, TYPE, CR and parenthetic comments ) TYPE CR
( The next line of output should be blank to test CR ) SOURCE TYPE CR CR

( It is now assumed that SOURCE, TYPE, CR and comments work. SOURCE and      )
( TYPE will be used to report test passes until something better can be      )
( defined to report errors. Until then reporting failures will depend on the )
( system under test and will usually be via reporting an unrecognised word   )
( or possibly the system crashing. Tests will be numbered by #n from now on  )
( to assist fault finding. Test successes will be indicated by               )
( 'Pass: #n ...' and failures by 'Error: #n ...'                             )

( Initial tests of >IN +! and 1+ )
( Check that n >IN +! acts as an interpretive IF, where n >= 0 )
( Pass #1: testing 0 >IN +! ) 0 >IN +! SOURCE TYPE CR
( Pass #2: testing 1 >IN +! ) 1 >IN +! xSOURCE TYPE CR
( Pass #3: testing 1+ ) 1 1+ >IN +! xxSOURCE TYPE CR

( Test results can now be reported using the >IN +! trick to skip )
( 1 or more characters )

( The value of BASE is unknown so it is not safe to use digits > 1, therefore )
( it will be set it to binary and then decimal, this also tests @ and ! )

( Pass #4: testing @ ! BASE ) 0 1+ 1+ BASE ! BASE @ >IN +! xxSOURCE TYPE CR
( Set BASE to decimal ) 1010 BASE !
( Pass #5: testing decimal BASE ) BASE @ >IN +! xxxxxxxxxxSOURCE TYPE CR

( Now in decimal mode and digits >1 can be used )

( A better error reporting word is needed, much like .( which can't  )
( be used as it is in the Core Extension word set, similarly PARSE can't be )
( used either, only WORD is available to parse a message and must be used   )
( in a colon definition. Therefore a simple colon definition is tested next )

( Pass #6: testing : ; ) : .SRC SOURCE TYPE CR ; 6 >IN +! xxxxxx.SRC
( Pass #7: testing number input ) 19 >IN +! xxxxxxxxxxxxxxxxxxx.SRC

( VARIABLE is now tested as one will be used instead of DROP e.g. Y ! )

( Pass #8: testing VARIABLE ) VARIABLE Y 2 Y ! Y @ >IN +! xx.SRC

: MSG 41 WORD COUNT ;  ( 41 is the ASCII code for right parenthesis )
( The next tests MSG leaves 2 items on the data stack )
( Pass #9: testing WORD COUNT ) 5 MSG abcdef) Y ! Y ! >IN +! xxxxx.SRC
( Pass #10: testing WORD COUNT ) MSG ab) >IN +! xxY ! .SRC

( For reporting success .MSG( is now defined )
: .MSG( MSG TYPE ; .MSG( Pass #11: testing WORD COUNT .MSG) CR

( To define an error reporting word, = 2* AND will be needed, test them first )
( This assumes 2's complement arithmetic )
1 1 = 1+ 1+ >IN +! x.MSG( Pass #12: testing = returns all 1's for true) CR
1 0 = 1+ >IN +! x.MSG( Pass #13: testing = returns 0 for false) CR
1 1 = -1 = 1+ 1+ >IN +! x.MSG( Pass #14: testing -1 interpreted correctly) CR

1 2* >IN +! xx.MSG( Pass #15: testing 2*) CR
-1 2* 1+ 1+ 1+ >IN +! x.MSG( Pass #16: testing 2*) CR

-1 -1 AND 1+ 1+ >IN +! x.MSG( Pass #17: testing AND) CR
-1  0 AND 1+ >IN +! x.MSG( Pass #18: testing AND) CR
6  -1 AND >IN +! xxxxxx.MSG( Pass #19: testing AND) CR

( Define ~ to use as a 'to end of line' comment. \ cannot be used as it a )
( Core Extension word )
: ~  ( -- )  SOURCE >IN ! Y ! ;

( Rather than relying on a pass message test words can now be defined to )
( report errors in the event of a failure. For convenience words ?T~ and )
( ?F~ are defined together with a helper ?~~ to test for TRUE and FALSE  )
( Usage is: <test> ?T~ Error #n: <message>                               )
( Success makes >IN index the ~ in ?T~ or ?F~ to skip the error message. )
( Hence it is essential there is only 1 space between ?T~ and Error      )

: ?~~  ( -1 | 0 -- )  2* >IN +! ;
: ?F~ ( f -- )   0 = ?~~ ;
: ?T~ ( f -- )  -1 = ?~~ ;

( Errors will be counted )
VARIABLE #ERRS 0 #ERRS !
: Error  1 #ERRS +! -6 >IN +! .MSG( CR ;
: Pass  -1 #ERRS +! 1 >IN +! Error ;  ~ Pass is defined solely to test Error

-1 ?F~ Pass #20: testing ?F~ ?~~ Pass Error
-1 ?T~ Error #1: testing ?T~ ?~~ ~

0  0 = 0= ?F~ Error #2: testing 0=
1  0 = 0= ?T~ Error #3: testing 0=
-1 0 = 0= ?T~ Error #4: testing 0=

0  0 = ?T~ Error #5: testing =
0  1 = ?F~ Error #6: testing =
1  0 = ?F~ Error #7: testing =
-1 1 = ?F~ Error #8: testing =
1 -1 = ?F~ Error #9: testing =

-1 0< ?T~ Error #10: testing 0<
0  0< ?F~ Error #11: testing 0<
1  0< ?F~ Error #12: testing 0<

 DEPTH 1+ DEPTH = ?~~ Error #13: testing DEPTH
 ~ Up to now whether the data stack was empty or not hasn't mattered as
 ~ long as it didn't overflow. Now it will be emptied - also
 ~ removing any unreported underflow
 DEPTH 0< 0= 1+ >IN +! ~ 0 0 >IN ! Remove any underflow
 DEPTH 0= 1+ >IN +! ~ Y !  0 >IN ! Empty the stack
 DEPTH 0= ?T~ Error #14: data stack not emptied 

 4 -5 SWAP 4 = SWAP -5 = = ?T~ Error #15: testing SWAP
 111 222 333 444
 DEPTH 4 = ?T~ Error #16: testing DEPTH
 444 = SWAP 333 = = DEPTH 3 = = ?T~ Error #17: testing SWAP DEPTH
 222 = SWAP 111 = = DEPTH 1 = = ?T~ Error #18: testing SWAP DEPTH
 DEPTH 0= ?T~ Error #19: testing DEPTH = 0

~ From now on the stack is expected to be empty after a test so
~ ?~ will be defined to include a check on the stack depth. Note
~ that ?~~ was defined and used earlier instead of ?~ to avoid
~ (irritating) redefinition messages that many systems display had
~ ?~ simply been redefined

: ?~  ( -1 | 0 -- )  DEPTH 1 = AND ?~~ ; ~ -1 test success, 0 test failure

123 -1 ?~ Pass #21: testing ?~
Y !   ~ equivalent to DROP

~ Testing the remaining Core words used in the Hayes tester, with the above
~ definitions these are straightforward

1 DROP DEPTH 0= ?~ Error #20: testing DROP
123 DUP  = ?~ Error #21: testing DUP
123 ?DUP = ?~ Error #22: testing ?DUP
0  ?DUP 0= ?~ Error #23: testing ?DUP
123  111  + 234  = ?~ Error #24: testing +
123  -111 + 12   = ?~ Error #25: testing +
-123 111  + -12  = ?~ Error #26: testing +
-123 -111 + -234 = ?~ Error #27: testing +
-1 NEGATE 1 = ?~ Error #28: testing NEGATE
0  NEGATE 0=  ?~ Error #29: testing NEGATE
987 NEGATE -987 = ?~ Error #30: testing NEGATE
HERE DEPTH SWAP DROP 1 = ?~ Error #31: testing HERE
CREATE TST1 HERE TST1 = ?~ Error #32: testing CREATE HERE
16  ALLOT HERE TST1 NEGATE + 16 = ?~ Error #33: testing ALLOT
-16 ALLOT HERE TST1 = ?~ Error #34: testing ALLOT
0 CELLS 0= ?~ Error #35: testing CELLS
1 CELLS ALLOT HERE TST1 NEGATE + VARIABLE CSZ CSZ !
CSZ @ 0= 0= ?~ Error #36: testing CELLS
3 CELLS CSZ @ DUP 2* + = ?~ Error #37: testing CELLS
-3 CELLS CSZ @ DUP 2* + + 0= ?~ Error #38: testing CELLS
: TST2  ( f -- n )  DUP IF 1+ THEN ;
0 TST2 0=  ?~ Error #39: testing IF THEN
1 TST2 2 = ?~ Error #40: testing IF THEN
: TST3  ( n1 -- n2 )  IF 123 ELSE 234 THEN ;
0 TST3 234 = ?~ Error #41: testing IF ELSE THEN
1 TST3 123 = ?~ Error #42: testing IF ELSE THEN
: TST4  ( -- n )  0 5 0 DO 1+ LOOP ;
TST4 5 = ?~ Error #43: testing DO LOOP
: TST5  ( -- n )  0 10 0 DO I + LOOP ;
TST5 45 = ?~ Error #44: testing I
: TST6  ( -- n )  0 10 0 DO DUP 5 = IF LEAVE ELSE 1+ THEN LOOP ;
TST6 5 = ?~ Error #45: testing LEAVE
: TST7  ( -- n1 n2 ) 123 >R 234 R> ;
TST7 NEGATE + 111 = ?~ Error #46: testing >R R>
: TST8  ( -- ch )  [CHAR] A ;
TST8 65 = ?~ Error #47: testing [CHAR]
: TST9  ( -- )  [CHAR] s [CHAR] s [CHAR] a [CHAR] P 4 0 DO EMIT LOOP ;
TST9 .MSG(  #22: testing EMIT) CR
: TST10  ( -- )  S"" Pass #23: testing S"" TYPE [CHAR] "" EMIT CR ; TST10

~ The Hayes core test core.fr uses CONSTANT before it is tested therefore
~ we test CONSTANT here

1234 CONSTANT CTEST
CTEST 1234 = ?~ Error #48: testing CONSTANT

~ The Hayes tester uses some words from the Core extension word set
~ These will be conditionally defined following definition of a
~ word called ?DEFINED to determine whether these are already defined

VARIABLE TIMM1 0 TIMM1 !
: TIMM2  123 TIMM1 ! ; IMMEDIATE
: TIMM3 TIMM2 ; TIMM1 @ 123 = ?~ Error #49: testing IMMEDIATE

: ?DEFINED  ( ""name"" -- 0 | -1 )  32 WORD FIND SWAP DROP 0= 0= ;
?DEFINED SWAP 0= ?~ Error #50: testing FIND ?DEFINED
?DEFINED <<no-such-word-hopefully>> 0= ?~ Error #51 testing FIND ?DEFINED

?DEFINED \ ?~ : \ ~ ; IMMEDIATE 
\ Error #52: testing \
: TIMM4  \ Error #53: testing \ is IMMEDIATE
;

~ TRUE and FALSE are defined as colon definitions as they have been used
~ more than CONSTANT above

?DEFINED TRUE  ?~ : TRUE 1 NEGATE ;
?DEFINED FALSE ?~ : FALSE 0 ;
?DEFINED HEX   ?~ : HEX 16 BASE ! ;

TRUE -1 = ?~ Error #54: testing TRUE
FALSE 0=  ?~ Error #55: testing FALSE
10 HEX 0A = ?~ Error #56: testing HEX
AB 0A BASE ! 171 = ?~ Error #57: testing hex number

~ Delete the ~ on the next 2 lines to check the final error report
~ Error #998: testing a deliberate failure
~ Error #999: testing a deliberate failure

~ Describe the messages that should be seen. The previously defined .MSG(
~ can be used for text messages

CR .MSG( Results: ) CR
CR .MSG( Pass messages #1 to #23 should be displayed above)
CR .MSG( and no error messages) CR

~ Finally display a message giving the number of tests that failed.
~ This is complicated by the fact that untested words including .( ."" and .
~ cannot be used. Also more colon definitions shouldn't be defined than are
~ needed. To display a number, note that the number of errors will have
~ one or two digits at most and an interpretive loop can be used to
~ display those.

CR
0 #ERRS @
~ Loop to calculate the 10's digit (if any)
DUP NEGATE 9 + 0< NEGATE >IN +! ( -10 + SWAP 1+ SWAP 0 >IN ! )
~ Display the error count
SWAP ?DUP 0= 1+ >IN +! ( 48 + EMIT ( ) 48 + EMIT

.MSG(  test) #ERRS @ 1 = 1+ >IN +! ~ .MSG( s)
.MSG(  failed out of 57 additional tests) CR

CR CR .MSG( --- End of Preliminary Tests --- ) CR

";
}

public class ForthException: Exception { public ForthException(string s): base(s) { } };

static class Utils {

    const AChar HighBit     = 0b10000000; 
    const AChar HighBitMask = 0b01111111;

    internal static bool  IsHighBitSet(AChar c)     => (HighBit & c) != 0;
    internal static AChar ResetHighBit(AChar c)     => (AChar)(HighBitMask & c);
    internal static void SetHighBit(ref AChar c)    => c = (AChar)(HighBit | c);
    internal static AChar HighBitValue(AChar c)     => (AChar) (c >> 7);

    internal static void WriteInt16(AUnit[] ar, Index i, Int16 c)
        => BinaryPrimitives.WriteInt16LittleEndian(new Span<byte>(ar, i, 2), c);
    internal static Int16 ReadInt16(AUnit[] ar, Index i)
        => BinaryPrimitives.ReadInt16LittleEndian(new Span<byte>(ar, i, 2));

    internal static void WriteCell(AUnit[] ar, Index i, Cell c)
#if CELL32
        => BinaryPrimitives.WriteInt32LittleEndian(new Span<byte>(ar, i, Vm.CELL_SIZE), c);
#else
        => BinaryPrimitives.WriteInt64LittleEndian(new Span<byte>(ar, i, Vm.CELL_SIZE), c);
#endif

    internal static Cell ReadCell(AUnit[] ar, Index i)
#if CELL32
        => BinaryPrimitives.ReadInt32LittleEndian(new Span<byte>(ar, i, Vm.CELL_SIZE));
#else
        => BinaryPrimitives.ReadInt64LittleEndian(new Span<byte>(ar, i, Vm.CELL_SIZE));
#endif

    internal static AUnit[] Add(AUnit[] a, ref Index i, Cell t) {
        if(i + Vm.CELL_SIZE >= a.Length) Array.Resize(ref a, i * 2);
        WriteCell(a, i, t);        
        i += Vm.CELL_SIZE;
        return a;
    }
    internal static Cell ReadBeforeIndex(AUnit[] a, ref Index i)
    {
        i -= Vm.CELL_SIZE;
        return ReadCell(a, i);
    }
    [DoesNotReturn]
    internal static void Throw(string message) => throw new ForthException(message);

    internal static long Read7BitEncodedCell(Code[] codes, Index index, out Index howMany) {
        using var stream = new MemoryStream(codes, index, 10);
        howMany = (Index)stream.Position;
#if CELL32
        var result = stream.ReadVarInt32();
#else
        var result = stream.ReadVarInt64();
#endif
        howMany = (Index)stream.Position - howMany;
        return result;
    }

    internal static void Write7BitEncodedCell(Code[] codes, Index index, Cell value, out Index howMany) {
        using var stream = new MemoryStream(codes, index, 10);
        howMany = (Index)stream.Position;
        stream.WriteVarInt(value);
        howMany = (Index)stream.Position - howMany;
    }

    internal static bool Has2NumberSize(byte b)
        => b >= (int)Vm.Token.FirstHas2Size && b < (int)Vm.Token.FirstHasCellSize;
    internal static bool HasCellSize(byte b)
        => b >= (int)Vm.Token.FirstHasCellSize && b < (int)Vm.Token.FirstHasVarNumb;
    internal static bool HasVarNumberSize(byte b)
        => b >= (int)Vm.Token.FirstHasVarNumb && b < (int) Vm.Token.FirstStringWord;
    internal static bool HasStringSize(byte b)
        => b >= (int)Vm.Token.FirstStringWord;
}

