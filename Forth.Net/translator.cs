using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;

namespace Translator;

public static class TranslatorExt
{ 
    public record Code(string Declarations, string Statements);
    public record Translator(
        ImmutableList<Word> Words,Code Code, ImmutableList<Word>? Defs, Func<char, string> NextWord, int FuncId);

    public abstract record Word (string Name);
    public record Immediate     (string Name, Word Word)                    : Word(Name);
    public record Operation     (string Name, string CodeText)              : Word(Name);
    public record Grabbing      (string Name, char Separator)               : Word(Name);
    public record FuncDef       (string Name, ImmutableList<Word> Defs)     : Word(Name);
    public record FuncCall      (string Name, int FuncId)                   : Word(Name);

    private enum Status { Compiling, Executing }
    static Status StatusQ(this Translator tr) => tr.Defs is null ? Status.Executing : Status.Compiling;

    static Translator Translate(Translator tr) => tr.NextWord(' ') switch
    {
        var w when string.IsNullOrEmpty(w) => tr,
        var w                              => Translate(TranslateWord(w, tr)) 
    };

    static Translator TranslateWord(string ws, Translator tr)
        => tr.Words.FindLast(w => w.Name == ws.ToLowerInvariant()) switch
    {
        var w when w is not null      => Perform(w, tr),
        _ when IsANumberInAnyBase(ws) => PerformNumber(ws, tr),
        _                             => Throw<Translator>($"The word '{ws}' was not found in the dictionary.")
    };

    static Translator PerformNumber(string ws, Translator tr) => Perform(NumberWord(ws), tr);

    static Translator Perform(Word word, Translator tr) => tr.StatusQ() switch
    {
        Status.Executing => EmitWord(word, tr),
        Status.Compiling => CompileWord(word, tr),
        _ => throw new NotImplementedException(),
    };

    static Translator EmitWord(Word word, Translator tr) => word switch
    {
        Operation op => EmitString   (op.CodeText, tr), 
        FuncDef   fd => EmitFuncDef  (fd, tr),
        FuncCall  fc => EmitFuncCall (fc, tr),
        Immediate im => EmitImmediate(im, tr),
        Grabbing  gr => EmitGrabbing (gr, tr),

        _ => Throw<Translator>($"Not known word type for {word.Name}"),
    };

    public static string ToCsharpId(string forthId) {
        StringBuilder sb = new();
        foreach(var c in forthId)
            if(sym.TryGetValue(c, out var v)) sb.Append($"_{v}");
            else sb.Append(c);
        return sb.ToString();
    }

    static Code GenerateFuncCode(FuncDef fd, Code code, int funcId) => code with
    {
        
    };
    static Translator EmitFuncDef  (FuncDef word, Translator tr) => tr with
    {
        Code   = GenerateFuncCode(word, tr.Code, tr.FuncId),
        Words  = tr.Words.Add(new FuncCall(word.Name, tr.FuncId)),
        FuncId = tr.FuncId + 1
    };
    static Translator EmitFuncCall (FuncCall word, Translator tr) => tr;
    static Translator EmitImmediate(Immediate word, Translator tr) => tr;
    static Translator EmitGrabbing (Grabbing word, Translator tr) => tr;

    static Translator CompileWord(Word word, Translator tr) => word switch
    {
        _ => Throw<Translator>($"Not known word type for {word.Name}"),
    };
    
    static T Throw<T>(string v) => throw new Exception(v);

    static Translator EmitString(string s, Translator tr) => tr.StatusQ() switch
    {
        Status.Compiling => tr with { Code = tr.Code with { Declarations = tr.Code.Declarations + s } },
        Status.Executing => tr with { Code = tr.Code with { Statements = tr.Code.Statements + s } },
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

    static Word NumberWord(string aNumber) => new Operation("",
    #if NOBASE
            $"VmExt.push(ref vm, {aNumber});";
    #else
            $"VmExt.pushs(ref vm, \"{aNumber}\");"
    #endif
        );

}
