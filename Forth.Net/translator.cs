namespace TranslatorUtils;

public static partial class TranslatorExt
{ 
    public record Code(string Declarations, string Statements)
    {
        public override string ToString() =>
            (string.IsNullOrEmpty(Declarations) ? "" : Declarations) +
            (string.IsNullOrEmpty(Statements)   ? "" : Statements);
    }
    public record Translator(
        ImmutableList<Word> Words,
        Code Code,
        ImmutableList<Word> Defs,
        Func<char, string> NextWord,
        Action<Code> ExecuteWord,
        Status Status = Status.Executing,
        Status LastStatus = Status.Executing,
        int FuncId = 0,
        string LastDefinedWord = "");

    public abstract record Word (string Name);
    public record Immediate     (string Name, Word Word)                    : Word(Name);
    public record Verbatim      (string Name, string CodeText)              : Word(Name);
    public record Primitive     (string Name, string PrimitiveName)         : Word(Name);
    public record Grabbing      (string Name, char Separator)               : Word(Name);
    public record FuncDef       (string Name, ImmutableList<Word> Defs)     : Word(Name);
    public record FuncCall      (string Name, int FuncId)                   : Word(Name);

    public record Compile       (string Name, Func<Translator, Translator> CompileFunction) : Word(Name);

    public enum Status { Compiling, Executing }

    public static Translator Translate(this Translator tr) => tr.NextWord(' ') switch
    {
        var w when string.IsNullOrEmpty(w) => tr,
        var w                              =>
            tr.TranslateWord(w).Execute().Translate() 
    };

    public static Translator Reset(this Translator tr) => tr with { Code = new Code("", ""), Defs = ImmutableList.Create<Word>() };
    public static Translator Create(Func<char, string> nextWord, Action<Code> execute) => 
        new Translator(ImmutableList.Create<Word>(), new Code("", ""), ImmutableList.Create<Word>(), nextWord, execute)
        .LoadStartingWords();

    public static bool IsEmpty(this Code code)
        => string.IsNullOrEmpty(code.Statements) && string.IsNullOrEmpty(code.Declarations);

    static Translator TranslateWord(this Translator tr, string ws)
        => tr.Words.FindLast(w => w.Name == ws.ToLowerInvariant()) switch
    {
        var w when w is not null      => tr.Perform(w),
        _ when IsANumberInAnyBase(ws) => tr.PerformNumber(ws),
        _                             => Throw<Translator>($"The word '{ws}' was not found in the dictionary.")
    };

    static Translator Execute(this Translator tr)
    {
        if(!tr.Code.IsEmpty())
            tr.ExecuteWord(tr.Code);
        return tr with { Code = tr.Code with { Declarations = "", Statements = ""} };
    }
    static Translator PerformNumber(this Translator tr, string ws) => tr.Perform(NumberWord(ws));

    static Translator Perform(this Translator tr, Word word) => tr.Status switch
    {
        Status.Executing => tr.EmitWord(word),
        Status.Compiling => tr.CompileWord(word),
        _ => throw new NotImplementedException(),
    };

    static Translator EmitWord(this Translator tr, Word word) => word switch
    {
        Verbatim  op => tr.EmitString       (op.CodeText), 
        Primitive pr => tr.EmitPrimitive    (pr), 
        FuncDef   fd => tr.EmitFuncDef      (fd),
        FuncCall  fc => tr.EmitFuncCall     (fc),
        Immediate im => tr.EmitImmediate    (im),
        Grabbing  gr => tr.EmitGrabbing     (gr),
        Compile   co => co.CompileFunction  (tr),

        _ => Throw<Translator>($"Not known word type for {word.Name}"),
    };

    static string ToCsharpId(string forthId, int funcId) =>
        forthId
            .ToCharArray()
            .Select(c => char.IsLetterOrDigit(c) ? c.ToString() : Convert.ToByte(c).ToString())
            .Append("__").Append(funcId.ToString())
            .Aggregate("__", (string s1, string s2) => s1 + s2);

    static Translator ToCompiling(this Translator tr) => tr with { Status = Status.Compiling, LastStatus = tr.Status};
    static Translator ToExecuting(this Translator tr) => tr with { Status = Status.Executing, LastStatus = tr.Status};
    static Translator ToOldStatus(this Translator tr) => tr with { Status = tr.LastStatus,    LastStatus = tr.Status};

    static Translator EmitFuncPre(this Translator tr, FuncDef fd) =>
        tr.EmitString($"\npublic static void {ToCsharpId(fd.Name, tr.FuncId)}(ref Vm vm) {{\n");

    static Translator EmitFuncBody(this Translator tr, FuncDef fd) => fd.Defs.Aggregate(tr, (tr1, w) => tr1.EmitWord(w));

    static Translator EmitFuncEnd(this Translator tr, FuncDef fd) => tr.EmitString("\n}\n");

    static Translator EmitPrimitive(this Translator tr, Primitive pr)
        => tr.EmitString($"VmExt.{pr.PrimitiveName}(ref vm);");
    static Translator EmitPrimitive(this Translator tr, Primitive pr, string str)
        => tr.EmitString($"VmExt.{pr.PrimitiveName}(ref vm, \"{str}\");");

    static Translator EmitFuncDef  (this Translator tr, FuncDef fd)
        => (tr.ToCompiling().EmitFuncPre(fd).EmitFuncBody(fd).EmitFuncEnd(fd).ToOldStatus() with
        {
            Words  = tr.Words.Add(new FuncCall(fd.Name, tr.FuncId)),
            FuncId = tr.FuncId + 1
        })
        .EmitFuncCall(new FuncCall(fd.Name, tr.FuncId));

    static Translator EmitFuncCall (this Translator tr, FuncCall fc) =>
        tr.EmitString($"{ToCsharpId(fc.Name, fc.FuncId)}(ref vm);");

    static Translator EmitImmediate(this Translator tr, Immediate word) =>
        tr.ToExecuting().EmitWord(word.Word).ToOldStatus();
    static Translator EmitGrabbing (this Translator tr, Grabbing word) => tr;

    static Translator CompileWord(this Translator tr, Word word) => word switch {
        Immediate im => tr.EmitImmediate(im),
        _ => tr with { Defs = tr.Defs.Add(word)}
        };
    
    static T Throw<T>(string v) => throw new Exception(v);

    static Translator EmitString(this Translator tr, string s) => tr.Status switch
    {
        Status.Compiling => tr with { Code = tr.Code with { Declarations = tr.Code.Declarations + s + "\n" } },
        Status.Executing => tr with { Code = tr.Code with { Statements = tr.Code.Statements + s  + "\n"} },
        _ => throw new NotImplementedException(),
    };
    // At compile time we don't know the base and the size of the cell (nint) for the Forth VM.
    // We try them all in order of likely and get a runtime exception if we guess wrong.
    static bool IsANumberInAnyBase(string? s) {
        foreach(var b in new int[] { 10, 16, 2, 8 }) { // Screw you, base 9.
            try { var _ = (nint)Convert.ToInt64(s, b); return true;} catch(Exception) { }
            try { var _ = (nint)Convert.ToInt32(s, b); return true;} catch(Exception) { }
            try { var _ = (nint)Convert.ToInt16(s, b); return true;} catch(Exception) { }
            try { var _ = (nint)Convert.ToSByte(s, b); return true;} catch(Exception) { }
        }
        return false;
    }

    static Word NumberWord(string aNumber) => new Verbatim("",
    #if NOBASE
            $"VmExt.push(ref vm, {aNumber});";
    #else
            $"VmExt.pushs(ref vm, \"{aNumber}\");"
    #endif
        );

}
