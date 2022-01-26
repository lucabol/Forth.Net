namespace TranslatorUtils;

public static partial class TranslatorExt
{
    static Translator LoadStartingWords(this Translator tr)
    {
        Primitive drop = new("drop", "drop");
        Verbatim print = new(".", "vm.output.Write($\"{VmExt.pop(ref vm)} \");");
        Verbatim bye = new("bye", "System.Environment.Exit(0);");

        return tr with {
            Words = tr.Words.Add(drop).Add(print).Add(bye).Add(new Compile(":", Colon)).Add(new Immediate(";", new Compile(";", SemiColon)))
        };
    }
    static Translator Colon(Translator tr) {
        var v = tr.NextWord(' ');
        return (tr.ToCompiling() with
            {
                LastDefinedWord = v,
                Defs = ImmutableList.Create<Word>()
            })
            .ToExecuting()
            .EmitPrimitive(new Primitive("", "_labelHere"), v)
            .ToOldStatus();
    }
    static Translator SemiColon(Translator tr) => (tr with
    {
        Words = tr.Words.Add(new FuncDef(tr.LastDefinedWord, tr.Defs))
    }).ToOldStatus();

    static Word[] InitialWords = new Word[]{
    
    };
}
