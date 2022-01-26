namespace TranslatorUtils;

public static partial class TranslatorExt
{
    static Translator LoadStartingWords(this Translator tr)
    {
        Verbatim print = new(".", "vm.output.Write($\"{VmExt.pop(ref vm)} \");");
        Verbatim bye = new("bye", "System.Environment.Exit(0);");

        return tr with {
            Words = InitialWords.Add(print).Add(bye).Add(new Compile(":", Colon)).Add(new Immediate(";", new Compile(";", SemiColon)))
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

    static Word PrimitiveF(string name, string primitive) => new Primitive(name, primitive);

    static ImmutableList<Word> InitialWords = (new Word[]{
             PrimitiveF("dup", "dup"),
             PrimitiveF("dup2", "dup2"),
             PrimitiveF("drop", "drop"),
             PrimitiveF("drop2", "drop2"),
             PrimitiveF("cells", "cells"),
             PrimitiveF("cell+", "cellp"),
             PrimitiveF("chars", "chars"),
             PrimitiveF("char+", "charp"),
             PrimitiveF("unused", "unused"),
             PrimitiveF("here", "here"),
             PrimitiveF("over", "over"),
             PrimitiveF("@", "_fetch"),
             PrimitiveF("c@", "_cfetch"),
             PrimitiveF("!", "_store"),
             PrimitiveF("c!", "_cstore"),
             PrimitiveF(",", "_comma"),
             PrimitiveF("c,", "_ccomma"),
             PrimitiveF("allot", "allot"),
             PrimitiveF("align", "align"),
             PrimitiveF("aligned", "aligned"),
             PrimitiveF("type", "type"),
             PrimitiveF("source", "source"),
             PrimitiveF("count", "count"),
             PrimitiveF("refill", "refill"),
             PrimitiveF("bl", "bl"),
             PrimitiveF("nl", "nl"),
             PrimitiveF(".s", "_dots"),
             PrimitiveF("dump", "dump"),
             PrimitiveF("?dup", "_qdup"),
             PrimitiveF("depth", "depth"),
             PrimitiveF(">r", "toR"),
             PrimitiveF("r>", "fromR"),
             PrimitiveF("r@", "fetchR"),
             PrimitiveF("+!", "_fetchP"),
             PrimitiveF("move", "move"),
             PrimitiveF("word", "word"),
             PrimitiveF("cmove", "cmove"),
             PrimitiveF("cmove>", "cmove"),
             PrimitiveF("fill", "fill"),
             PrimitiveF("blank", "blank"),
             PrimitiveF("erase", "erase"),
             PrimitiveF("u.r", "urdot"),

             PrimitiveF("_labelHere", "_labelHere"),
             PrimitiveF("key", "_key"),
             PrimitiveF(">in", "inpp"),
   
    }).ToImmutableList();
}
