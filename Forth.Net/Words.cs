namespace TranslatorUtils;

public static partial class TranslatorExt
{
    static Translator LoadStartingWords(this Translator tr) =>  tr with { Words = InitialWords };

    static Translator ColonDef(Translator tr) {
        var v = tr.NextWord(' ');
        return (tr with
            {
                LastDefinedWord = v,
                Defs = ImmutableList.Create<Word>()
            })
            .ToExecuting()
            .EmitPrimitive(new Primitive("", "_labelHere", v))
            .ToCompiling();
    }
    static Translator CreateDef(Translator tr) {
        var v = tr.NextWord(' ');
        return (tr with
            {
                LastCreatedWord = v,
                Words = tr.Words.Add(new Primitive(v, $"_pushLabel", v))
            })
            .EmitWord(new Verbatim("", "VmExt._pushLabel(ref vm, vm.lastCreatedWord);"))
            .ToExecuting()
            .EmitPrimitive(new Primitive("", "_labelHere", v))
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
             PrimitiveF(".", "_dot"),
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
             PrimitiveF("bye", "bye"),

             new Compile(":", ColonDef),
             new Compile("create", CreateDef),
             new Immediate(";", new Compile(";", SemiColon)),  

    }).ToImmutableList();
}
