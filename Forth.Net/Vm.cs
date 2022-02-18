namespace Forth;

public enum Op {
    Error , Colo,  Does, Plus, Minu, Mult, Divi, Prin, Base, Noop,
    Count, Word, Parse, Refill, Comma, CComma, Here, At, Store, State, Bl, Dup, Exit, Immediate,
    Swap, Dup2, Drop, Drop2, Find, Bye, DotS, Interpret, Quit, Create, Body, RDepth, Depth,
    Less, More, Equal, NotEqual, Do, Loop, LoopP, ToR, FromR, I, J, Leave, Cr,
    Source, Type, Emit, Char, In, Over, And, Or, Allot, Cells, Exec, Invert, MulDivRem,
    Save, Load, SaveSys, LoadSys, Included, DType, DCall, DMethod, CAt, Pad,
    IDebug, ISemi,  IBegin, IDo, ILoop, ILoopP, IAgain, IIf, IElse, IThen,
    IWhile, IRepeat, IBrakO, IBrakC,   // End of 1 byte
    Branch0, RelJmp, ImmCall, IPostponeOp,// End of 2 byte size
    NumbEx, // End of CELL Size 
    Jmp , Numb, Call, IPostponeCall, ILiteral, IChar,// End of Var number
    ICStr, ISStr, ISLit, // End of string words
    FirstHasVarNumb = Jmp, FirstHas2Size = Branch0, FirstHasCellSize = NumbEx,
    FirstStringWord = ICStr,
}

public class Vm {
    /** Enable debug to see the generated opcodes **/
    public bool Debug { get ; set; }

    const Cell FORTH_TRUE  = -1;
    const Cell FORTH_FALSE = 0;

    internal const Index CHAR_SIZE = 1;
    internal const Index CELL_SIZE = sizeof(Cell);

    readonly Index source;
    readonly Index keyWord;
    readonly Index word;
    readonly Index inp;
    readonly Index source_max_chars;
    readonly Index word_max_chars;
    readonly Index pad;
    readonly Index strings;

    private readonly int userStart;

    Index input_len_chars = 0;

    /** This is the base to interpret numbers. **/
    readonly Index base_p;

    /** State TRUE when compiling, FALSE when interpreting. **/
    readonly Index state;
    bool Executing { get => ReadCell (ds, state) == FORTH_FALSE;
                     set => WriteCell(ds, state, value ? FORTH_FALSE : FORTH_TRUE);}

    Index sp     = 0;
    Index rp     = 0;
    Index herep  = 0;
    AUnit[] ps;
    AUnit[] rs;
    AUnit[] ds;
    readonly Index savedDictHead;
    readonly Index code = 0;

    Index dictHead;

    /** There are some words that directly maps to single opcodes **/
    readonly Dictionary<string, Op> WordToSimpleOp = new()
    {
        { "."           , Op.Prin },
        { "count"       , Op.Count },
        { "cells"       , Op.Cells },
        { "allot"       , Op.Allot },
        { "and"         , Op.And },
        { "or"          , Op.Or },
        { "base"        , Op.Base },
        { "refill"      , Op.Refill },
        { "interpret"   , Op.Interpret },
        { "quit"        , Op.Quit },
        { "word"        , Op.Word },
        { "parse"       , Op.Parse },
        { "save"        , Op.Save },
        { "load"        , Op.Load },
        { "savesys"     , Op.SaveSys },
        { "loadsys"     , Op.LoadSys },
        { "included"    , Op.Included },
        { ","           , Op.Comma },
        { "c,"          , Op.CComma },
        { "here"        , Op.Here },
        { "@"           , Op.At },
        { "c@"          , Op.CAt },
        { "pad"         , Op.Pad },
        { "!"           , Op.Store },
        { "state"       , Op.State },
        { "bl"          , Op.Bl },
        { ":"           , Op.Colo },
        { "bye"         , Op.Bye },
        { ".s"          , Op.DotS },
        { "+"           , Op.Plus },
        { "-"           , Op.Minu },
        { "*"           , Op.Mult },
        { "/"           , Op.Divi },
        { "<"           , Op.Less },
        { ">"           , Op.More },
        { "="           , Op.Equal },
        { "<>"          , Op.NotEqual },
        { "create"      , Op.Create },
        { "does>"       , Op.Does },
        { ">body"       , Op.Body },
        { "rdepth"      , Op.RDepth },
        { "swap"        , Op.Swap },
        { "depth"       , Op.Depth },
        { "over"        , Op.Over },
        { "dup"         , Op.Dup },
        { "dup2"        , Op.Dup2 },
        { "drop"        , Op.Drop },
        { "drop2"       , Op.Drop2 },
        { "*/mod"       , Op.MulDivRem },
        { "invert"      , Op.Invert },
        { "exit"        , Op.Exit },
        { "i"           , Op.I },
        { "j"           , Op.J },
        { ">r"          , Op.ToR },
        { "r>"          , Op.FromR },
        { "leave"       , Op.Leave },
        { "immediate"   , Op.Immediate },
        { "source"      , Op.Source },
        { "type"        , Op.Type },
        { "emit"        , Op.Emit },
        { "cr"          , Op.Cr },
        { "char"        , Op.Char },
        { ">in"         , Op.In },
        { "find"        , Op.Find },
        { "execute"     , Op.Exec },
        { ".net>type"   , Op.DType },
        { ".net>method" , Op.DMethod },
        { ".net>call"   , Op.DCall },
    };

    /** While other words need to perfom more complicated actions at compile time **/
    readonly Dictionary<string, (Op, Action)> ImmediateWords = new();
    public Func<string>? NextLine = null;

    Type lastType = typeof(Console);
    MethodInfo? lastMethod;

    public Vm(
        Index parameterStackSize = Config.SmallStack,
        Index returnStackSize    = Config.SmallStack,
        Index dataStackSize      = Config.MediumStack,
        Index stringsSize        = 1_024,
        Index padSize            = 1_024,
        Index sourceSize         = 1_024,
        Index wordSize           = 1_024
        ) {

        ps   = new AUnit[parameterStackSize];
        rs   = new AUnit[returnStackSize];
        ds   = new AUnit[dataStackSize];

        code          = herep;
        herep        += CHAR_SIZE + CELL_SIZE;

        keyWord       = herep;
        herep        += wordSize * CHAR_SIZE;
        source        = herep;
        herep        += sourceSize * CHAR_SIZE;

        word          = herep;
        herep        += wordSize * CHAR_SIZE;

        pad              = herep;
        herep           += padSize;
        source_max_chars = sourceSize;
        word_max_chars   = wordSize;

        base_p     = herep;
        herep    += CELL_SIZE;
        ds[base_p] = 10;

        strings = herep;
        herep += stringsSize * Vm.CHAR_SIZE;

        inp     = herep;
        herep += CELL_SIZE;

        state   = herep;
        herep += CELL_SIZE;

        void Mark()          => Push(herep);
        void BranchAndMark() { PushOp(Op.Branch0); Mark(); herep += 2;}
        void EmbedInPoppedJmpFwd() {
                PushOp(Op.RelJmp);
                var mark = (Index)Pop();
                short delta = (short)((herep + 2) - mark);
                WriteInt16(ds, mark, delta);
                Push(herep);
                herep += 2;
        }
        void EmbedHereJmpBck() {
                PushOp(Op.RelJmp);
                var mark = (Index)Pop();
                var delta = (short)(mark - herep);
                WriteInt16(ds, herep, delta); 
                herep += 2;
        }
        void EmbedHereJmp0Bck() {
                PushOp(Op.Loop);
                var mark = (Index)Pop();
                var delta = (short)(mark - herep);
                WriteInt16(ds, herep, delta); 
                herep += 2;

                var leaveTarget = mark - CELL_SIZE;
                WriteCell(ds, leaveTarget, herep);
        }
        void EmbedHereJmp0BckP() {
                PushOp(Op.LoopP);
                var mark = (Index)Pop();
                var delta = (short)(mark - herep);
                WriteInt16(ds, herep, delta); 
                herep += 2;
        }
        Action EmbedString(Op op) { return () =>
            {
                PushOp(op);

                Push('"');
                Parse();
                var len = (Index)Peek();
                Push(herep + 1);
                Swap();
                CMove();
                if(Executing && op == Op.ICStr) Push(herep);
                if(Executing && op == Op.ISStr) { Push(herep + 1); Push(len); }
                ds[herep] = (byte)len;
                herep += len + 1;
            };
        }
        Action SEmbedString(Op op) { return () =>
        {
            PushOp(op);
            var len = (Index)Pop();
            Push(herep + 1);
            Push(len);
            CMove();
            ds[herep] = (byte)len;
            herep += len + 1;
        };}

        ImmediateWords = new()
        {
            { "debug",      (Op.IDebug,     () => Debug = !Debug) },
            { "[char]",     (Op.IChar,      () => { Char(); PushOp(Op.Numb, Pop());}) },
            { "literal",    (Op.ILiteral,       () => { PushOp(Op.Numb, Pop());}) },
            { "sliteral",   (Op.ISLit,      SEmbedString(Op.ISLit)) },
            { "[",          (Op.IBrakO,     () => Executing = true) },
            { "]",          (Op.IBrakC,     () => Executing = false) },
            { ";",          (Op.ISemi,      () => { PushOp(Op.Exit);  Executing = true; }) },
            { "postpone",   (Op.IPostponeCall,  Postpone) },
            { "begin",      (Op.IBegin,     Mark) },
            { "do",         (Op.IDo,        () => { PushOp(Op.Do); herep += CELL_SIZE; Mark(); }) },
            { "loop",       (Op.ILoop,      EmbedHereJmp0Bck)},
            { "+loop",      (Op.ILoopP,     EmbedHereJmp0BckP)},
            { "again",      (Op.IAgain,     EmbedHereJmpBck)},    
            { "if",         (Op.IIf,        BranchAndMark) },
            { "else",       (Op.IElse,      EmbedInPoppedJmpFwd)  },    
            { "then",       (Op.IThen,      () => {
                var mark = (Index)Pop();
                short delta = (short)(herep - mark);
                WriteInt16(ds, mark, delta);
            }) },
            { "while",      (Op.IWhile,     BranchAndMark) },    
            { "repeat",     (Op.IRepeat,    () => {
                var whileMark = (Index)Pop();
                short delta   = (short)(herep + 3 - whileMark);
                WriteInt16(ds, whileMark, delta);
                EmbedHereJmpBck();
                }) },
            { "c\"",         (Op.ICStr, EmbedString(Op.ICStr)) },
            { "s\"",         (Op.ISStr, EmbedString(Op.ISStr)) },
        };

        userStart = herep;
        savedDictHead = herep;
        herep        += CELL_SIZE;

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
        Reset();

    }
    public void Reset()
    {
        sp = 0; rp = 0; Executing = true; ds[inp] = 0;
    }
    public void EvaluateSingleLine(string s)
    {
        var oldLine = NextLine;
        try { 
            NextLine = () => s;
            Refill();
            if(Pop() == FORTH_TRUE)
                Interpret();
        } finally
        {
            NextLine = oldLine;
        }

    }
    public void Quit()
    {
        rp = 0; Executing = true; ds[inp] = 0; // Don't reset the parameter stack as for ANS FORTH definition of QUIT.

        if(NextLine is null) Throw("Trying to Quit with a null readline.");

        while(true)
        {
            Refill();
            if(Pop() == FORTH_TRUE)
                Interpret();
            else
                break;
        }
    }
    public IEnumerable<string> Words()
    {
        return WordToSimpleOp.Keys.Concat(ImmediateWords.Keys);
    }

    // TODO: clean this up, it now works because the default value of Op is 0 -> Error. It should use some kind of multidictionary here.
    Action? ImmediateAction(Op op) {
        var result =
            ImmediateWords.FirstOrDefault(e => e.Value.Item1 == op);
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
    void PushOp(Op op) {
        if(Debug) Console.Write($"C:{op} ");
        ds[herep] = (Code)op;
        herep++;
    }
    Index PushOp(Op op, Cell value)
    {
        if(Debug) Console.Write($"C:{op}:{value} ");
        PushOp(op);
        Write7BitEncodedCell(ds, herep, value, out var howMany);
        herep += howMany;
        return howMany;
    }
    Index PushExecOp(Op op, Cell? value)
    {
        ds[code] = (Code)op;
        var howMany = 0;

        if(value is not null) Write7BitEncodedCell(ds, code + 1, (Cell)value, out howMany);
        return howMany + 1;
    }
    static void ColorLine(ConsoleColor color, string s) {
        var backupcolor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(s);
        Console.ForegroundColor = backupcolor;
    }
    void Included()
    {

        var lineNum = 0;
        var lineText = "";

        var fileName = ToDotNetString();
        using var stream = File.OpenRead(fileName);
        using var reader = new StreamReader(stream);
        var backNext = NextLine;

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
            NextLine = backNext;
        }
    }
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
    void PushUntilExit(ref Index ip)
    {
        while(true)
        {
            var op = ds[ip];
            var count = 0;
            if(Utils.HasCellSize(op))
                count = CELL_SIZE;
            else if(Utils.HasVarNumberSize(op))
                Read7BitEncodedCell(ds, ip + 1, out count);
            else if(Utils.HasStringSize(op))
                count += ds[ip + 1] + 1; // Size of string + 1 for the len byte.

            Array.Copy(ds, ip, ds, herep, 1 + count);
            ip += 1 + count;
            herep += 1 + count;
            if((Op)op == Op.Exit)
                break;
        }
    }

    static Index LinkToCode(Index link, Index wordLen)
        // Addr + Link size + len size  + word chars
        => link + CELL_SIZE + CHAR_SIZE + CHAR_SIZE * wordLen;
    static Index LinkToLen(Index link) => link + CELL_SIZE;

    void Find()
    {
        // Look for user defined word
        var caddr = (Index)Peek();
        FindUserDefinedWord();
        var found = Peek() != FORTH_FALSE;
        if(found) return;

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
        if(ImmediateWords.TryGetValue(sl, out var imm))
        {
            RetNewImmediateOp(imm.Item1);
            return;
        }
        // Getting here, we return the result of FindUserDefinedWord. Below utility funcs.
        void RetNewImmediateOp(Op op)
        {
            var xt = herep;
            PushOp(Op.ImmCall);
            PushOp(op);
            PushOp(Op.Exit);
            Ret(xt, 1);
        }
        void RetNewOp(Op op)
        {
            var xt = herep;
            PushOp(op);
            PushOp(Op.Exit);
            Ret(xt, -1);
        }
        void Ret(Index xt, Cell f)
        {
            Drop(); Drop(); Push(xt); Push(f);
        }
    }
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
            var wordLen       = Utils.ResetHighBit(wordLenRaw);
            var wordSpan      = new Span<AChar>(ds, wordNameStart + 1 * CHAR_SIZE, wordLen);
            var res           = cspan.SequenceEqual(wordSpan);
            if(res) // Found
            {
                Push(LinkToCode(dp, wordLen));                
                Push(Utils.HighBitValue(wordLenRaw) == 1 ? 1 : -1);
                return;
            }
            dp = (Index)ReadCell(ds, dp);
        }
        // Not found
        Push(caddr);
        Push(0);
    }
    Index Lastxt() => LinkToCode(dictHead, ds[dictHead + CELL_SIZE]);

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
    void CMove()
    {
        var u  = Pop();
        var a2 = Pop();
        var a1 = Pop();
        Array.Copy(ds, a1, ds, a2, u);
    }
    void Postpone()
    {
        Bl();
        WordW();
        LowerCase();
        Dup();
        var sl = ToDotNetStringC();

        FindUserDefinedWord();

        var res = Pop();
        var xt    = (Index)Pop();

        if(res != FORTH_FALSE) {
            PushOp(Op.IPostponeCall, xt);
            return;
        }

        if(WordToSimpleOp.TryGetValue(sl, out var op)) {
            PushOp(Op.IPostponeOp);
            PushOp(op);
            PushOp(Op.Noop);
            return;
        }

        if(ImmediateWords.TryGetValue(sl, out var imm)) {
            PushOp(imm.Item1);
            return;
        }
        Throw($"{sl: don't know this word.}");
    }
    bool InterpretWord()
    {
        LowerCase();

        // TODO: optimize away string creation, requires not storing word to opcode/definition data in hashtables (I think).
        Dup();
        var sl = ToDotNetStringC().ToLowerInvariant();

        FindUserDefinedWord();
        var res   = Pop();
        var xt    = (Index)Pop();

        // Manage user defined word.
        if(res != FORTH_FALSE)
        {
            var immediate = res == 1;
            if(Executing || immediate)
                Execute(Op.Call, xt);
            else
                PushOp(Op.Call, xt);
            return true;
        }
        // Manage simple primitives.
        if(WordToSimpleOp.TryGetValue(sl, out var op))
        {
            if(Executing)
                Execute(op, null);
            else
                PushOp(op);
            return true;
        }
        // Manage complex primitives.
        if(ImmediateWords.TryGetValue(sl, out var imm))
        {
            imm.Item2();
            return true;
        }
        return false;
    }
    void InterpretNumber(Cell value)
    {
        if(Executing)
            Execute(Op.Numb, value);
        else
            PushOp(Op.Numb, value);
    }
    internal bool IsEmptyWordC() => ds[Peek()] == 0;

    bool TryParseNumber(string s, out Cell n)
    {
        var res = false;
        var b = ds[base_p];
        // TODO: is there a way to speed this up without recoding the whole thing?
        // Recoding might not be bad as we could support bases other than
        // 
        try {
#if CELL32
            n = Convert.ToInt32(s, b);
#else
            n = Convert.ToInt64(s, b);
#endif
            res = true;
        } catch (FormatException )
        {
            // This is not acturally an exception case in Forth.
            n = 0;    

        } catch(OverflowException)
        {
            throw;
        }
        return res;
    }
    void Interpret()
    {
        while(true)
        {
            Bl();
            WordW(inKeyword: true);
            if(IsEmptyWordC()) { Drop(); break;};

            // TODO: remove string allocation from main loop.
            Dup();
            var s = ToDotNetStringC();
            if(!InterpretWord()) {
                if(TryParseNumber(s, out Cell n))
                    InterpretNumber(n);
                else
                    Throw($"{s} is not a recognized word or number.");
            }
        }
    }
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

    void Execute(Op op, Cell? data) {

        Index opLen = PushExecOp(op, data);
        var ip      = code;

        do {
            var currentOp = (Op)ds[ip];
            if(Debug) Console.Write($"E: {currentOp} ");
            ip++;
            Cell n, flag, index, limit, incr ;
            Index count, idx;
            bool bflag;
            switch(currentOp) {
                case Op.Source:
                    Source();
                    break;
                case Op.Base:
                    Push(base_p);
                    break;
                case Op.Emit:
                    Console.Write((char)Pop());
                    break;
                case Op.Type:
                    Console.Write(ToDotNetString());
                    break;
                case Op.ISLit:
                    count = ds[ip];
                    Push(ip + 1);
                    Push(count);
                    ip += count + 1;
                    break;
                case Op.IPostponeCall:
                    n = Read7BitEncodedCell(ds, ip, out count);
                    PushOp(Op.Call, n);
                    ip += count;
                    break;
                case Op.IPostponeOp:
                    PushOp((Op)ds[ip]);
                    ip += 2; // There is a noop here to classify this as 2 bytes operation.
                    break;
                case Op.ImmCall:
                    var act = ImmediateAction((Op)ds[ip]);
                    if(act is null) Throw($"ImmCall with a non existing op: {(Op)ds[ip]}");
                    act();
                    ip++;
                    break;
                case Op.Allot:
                    herep += (Index)Pop();
                    break;
                case Op.DType:
                    var typeName = ToDotNetString();
                    var aType = Type.GetType(typeName);
                    if(aType is null) Throw($"Cannot find type {typeName}");
                    lastType = aType;
                    break;
                case Op.DMethod:
                    var methodName = ToDotNetString();
                    lastMethod = lastType.GetMethod(methodName);
                    if(lastMethod is null) Throw($"No method {methodName} on type {lastType.Name}");
                    break;
                case Op.DCall:
                    DCall();
                    break;
                case Op.Cells:
                    Push(Pop() * CELL_SIZE);
                    break;
                case Op.Find:
                    Find();
                    break;
                case Op.Exec:
                    idx = (Index)Pop();
                    RPush(ip);
                    ip = (Index)idx;
                    break;
                case Op.Cr:
                    Console.WriteLine();
                    break;
                case Op.In:
                    Push(inp);
                    break;
                case Op.Char:
                    Char();
                    break;
                case Op.ICStr:
                    Push(ip);
                    ip += ds[ip] + 1;
                    break;
                case Op.ISStr:
                    Push(ip + 1);
                    idx = ds[ip];
                    Push(idx);
                    ip += idx + 1;
                    break;
                case Op.Numb:
                    n = Read7BitEncodedCell(ds, ip, out count);
                    Push(n);
                    ip += count;
                    break;
                case Op.NumbEx:
                    n = ReadCell(ds, ip);
                    Push(n);
                    ip = (Index)RPop();
                    break;
                case Op.Prin:
                    n = Pop();
                    Console.Write($"{Convert.ToString(n, ds[base_p])} ");
                    break;
                case Op.Count:
                    Count();
                    break;
                case Op.Refill:
                    Refill();
                    break;
                case Op.Word:
                    WordW();
                    break;
                case Op.Parse:
                    Parse();
                    break;
                case Op.Comma:
                    Comma();
                    break;
                case Op.CComma:
                    ds[herep] = (byte) Pop();
                    herep++;
                    break;
                case Op.Save:
                    SaveSystem();
                    break;
                case Op.Load:
                    LoadSystem();
                    break;
                case Op.SaveSys:
                    SaveSystem(true);
                    break;
                case Op.LoadSys:
                    LoadSystem(true);
                    break;
                case Op.Included:
                    Included();
                    break;
                case Op.Here:
                    Here();
                    break;
                case Op.ToR:
                    ToR();
                    break;
                case Op.FromR:
                    FromR();
                    break;
                case Op.At:
                    At();
                    break;
                case Op.CAt:
                    CFetch();
                    break;
                case Op.Pad:
                    Push(pad);
                    break;
                case Op.Drop:
                    Drop();
                    break;
                case Op.Drop2:
                    Drop2();
                    break;
                case Op.MulDivRem:
                    MulDivRem();
                    break;
                case Op.Store:
                    Store();
                    break;
                case Op.State:
                    State();
                    break;
                case Op.Bl:
                    Bl();
                    break;
                case Op.Dup:
                    Dup();
                    break;
                case Op.Swap:
                    Swap();
                    break;
                case Op.Over:
                    Over();
                    break;
                case Op.Dup2:
                    Dup2();
                    break;
                case Op.Bye:
                    Environment.Exit(0);
                    break;
                case Op.DotS:
                    Console.WriteLine(DotS());
                    break;
                case Op.Quit:
                    Quit();
                    break;
                case Op.Interpret:
                    Interpret();
                    break;
                case Op.And:
                    Push(Pop() & Pop());
                    break;
                case Op.Or:
                    Push(Pop() | Pop());
                    break;
                case Op.Plus:
                    Push(Pop() + Pop());
                    break;
                case Op.Minu:
                    Push(- Pop() + Pop());
                    break;
                case Op.Mult:
                    Push(Pop() * Pop());
                    break;
                case Op.Less:
                    Push(Pop() > Pop() ? FORTH_TRUE : FORTH_FALSE);
                    break;
                case Op.More:
                    Push(Pop() < Pop() ? FORTH_TRUE : FORTH_FALSE);
                    break;
                case Op.Equal:
                    Push(Pop() == Pop() ? FORTH_TRUE : FORTH_FALSE);
                    break;
                case Op.NotEqual:
                    Push(Pop() != Pop() ? FORTH_TRUE : FORTH_FALSE);
                    break;
                case Op.Depth:
                    Push(sp / CELL_SIZE);
                    break;
                case Op.RDepth:
                    Push(rp / CELL_SIZE);
                    break;
                case Op.Invert:
                    Push(~Pop());
                    break;
                case Op.Immediate:
                    Utils.SetHighBit(ref ds[LinkToLen(dictHead)]);
                    break;
                case Op.Divi:
                    var d = Pop();
                    var u = Pop();
                    Push(u / d);
                    break;
                case Op.Body:
                    Push(Pop() + 1 + CELL_SIZE); // See create below.
                    break;
                case Op.Create:
                    Push(' ');
                    WordW();
                    if(ds[Peek()] == 0) Throw("Make needs a subsequent word in the stream.");
                    LowerCase();
                    DictAdd();

                    PushOp(Op.NumbEx);
                    // Need to use full cell because it gets substituted by a jmp in does>
                    // and I don't know how many cells the number I need to jump to takes
                    WriteCell(ds, herep, herep + CELL_SIZE);
                    herep += CELL_SIZE;
                    break;
                case Op.Does:
                    // Allows redefinition of last defined word!!
                    // Even in gforth!!
                    idx = Lastxt();
                    var addrToPush = ReadCell(ds, idx + 1);
                    
                    var tmpHerep = herep;
                    herep = idx;
                    PushOp(Op.Jmp, tmpHerep);
                    herep = tmpHerep;
                    PushOp(Op.Numb, addrToPush);
                    PushUntilExit(ref ip);
                    ip = (Index)RPop();
                    break;
                case Op.Colo:
                    Push(' ');
                    WordW();
                    if(ds[Peek()] == 0) Throw("Colon needs a subsequent word in the stream.");
                    LowerCase();
                    DictAdd();
                    Executing = false;
                    break;
                case Op.Call:
                    n = Read7BitEncodedCell(ds, ip, out count);
                    ip += count;
                    RPush(ip);
                    ip = (Index)n;
                    break;
                case Op.Jmp:
                    n = Read7BitEncodedCell(ds, ip, out _);
                    ip = (Index) n;
                    break;
                case Op.Exit:
                    ip = (Index)RPop();
                    break;
                case Op.Branch0:
                    flag = Pop();
                    ip += flag == FORTH_FALSE ? ReadInt16(ds, ip) : 2 ;
                    break;
                case Op.Do:
                    RPush(ReadCell(ds, ip));
                    ip += CELL_SIZE;
                    ToR();
                    ToR();
                    break;
                case Op.I:
                    Push(ReadCell(rs, rp - CELL_SIZE * 2));
                    break;
                case Op.J:
                    Push(ReadCell(rs, rp - CELL_SIZE * 5));
                    break;
                case Op.Leave:
                    RPop();
                    RPop();
                    ip = (Index)RPop();
                    break;
                case Op.Loop:
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
                case Op.LoopP:
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
                case Op.RelJmp:
                    ip += ReadInt16(ds, ip);
                    break;
                default:
                    var a = ImmediateAction(currentOp); 
                    if(a is not null)
                    {
                        a();
                    } else {
                        Throw($"{(Op)op} bytecode not supported.");
                    }
                    break;
            }
        } while (ip != code + opLen);
    }
    /** These are internal to be able to test them. What a bother. **/
    internal void Push(Cell n)  => ps = Utils.Add(ps, ref sp, n);
    internal Cell Pop()         => Utils.ReadBeforeIndex(ps, ref sp);
    internal Cell Peek()        => Utils.ReadCell(ps, sp - CELL_SIZE);

    internal void RPush(Cell n) => rs = Utils.Add(rs, ref rp, n);
    internal Cell RPop()        => Utils.ReadBeforeIndex(rs, ref rp);
    void ToR()                  => RPush(Pop());
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
        WordW();
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
            if (input_len_chars > source_max_chars)
                throw new Exception(
                $"Cannot parse a line longer than {source_max_chars}. {inputBuffer} is {input_len_chars} chars long.");
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
    /** It is implemented like this to avoid endianess problems **/
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
        bytes.CopyTo(ds, strings);
        Push(strings);
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
    /** TODO: the delimiter in this implemenation (and Forth) as to be one byte char, but UTF8 puts that into question **/
    internal void WordW(bool inKeyword = false)
    {
        var delim = (byte)Pop();
        var s = ToChars(source, input_len_chars);
        var toPtr = inKeyword ? keyWord : word;

        var w = ToChars(toPtr, word_max_chars);

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
        while (j < word_max_chars && index < input_len_chars && s[(Index)index] != delim)
        {
            var c = s[(Index)index];
            index++;
            w[j++] = c;
        }
        // Points past the delimiter. Otherwise it would stay on last " of a string.
        if(index < input_len_chars) index++;
        if (j >= word_max_chars) throw new Exception($"Word longer than {word_max_chars}: {Encoding.UTF8.GetString(s)}");

        w[0] = (byte)(j - 1);  // len goes into the first char
        Push(toPtr);
    }
    Span<byte> ToChars(Index start, Index lenInBytes)
            => new(ds, start, lenInBytes);
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
        => b >= (int)Op.FirstHas2Size && b < (int)Op.FirstHasCellSize;
    internal static bool HasCellSize(byte b)
        => b >= (int)Op.FirstHasCellSize && b < (int)Op.FirstHasVarNumb;
    internal static bool HasVarNumberSize(byte b)
        => b >= (int)Op.FirstHasVarNumb && b < (int) Op.FirstStringWord;
    internal static bool HasStringSize(byte b)
        => b >= (int)Op.FirstStringWord;
}
