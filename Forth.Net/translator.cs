#define NOBASEOFF
#define NOFASTCONSTANT
using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

public delegate void Definition(Word w, Translator tr);

public class Word {
    public Definition? def;
    public bool       immediate;
    public bool       export;
    public bool       tickdefined;
    public string?     name;
}

public class Translator {

    // Input and outputs.
    public StringBuilder output;

    // Manage definition words.
    public bool InDefinition  = false;
    public string lastWord    = "";
    public string lastCreated = "";

    // Actions for a word definition starting with :
    List<Word>? defActions;
    // Actions for each word to add their behavior to. It can be defActions or does> actions.
    List<Word>? actions;
    // Calls to recurse inside a colon definition.
    List<Word>? recurseActions;

    // Iteration count for for loops.
    public int nested = 0;
    public Stack<int> loopStack = new();

    // Create unique names for goto statements.
    public int nameCount = 0;

    // Number of immediates in the definition;
    public int literalCount = 0;

    // The input buffer needs to be accessible at run time (aka from a running Forth vm).
    // Insted of passing the vm in, we generalize a little by passing callbacks.
    public Action<string> setLine;
    public Func<char, string> getNextWord;

    public Translator(StringBuilder output,
                      Action<string> setLine,
                      Func<char, string> getNextWord
                      ) {

        this.output      = output;
        this.setLine     = setLine;
        this.getNextWord = getNextWord;

        // TODO: this is a hack. The word constructing words should set the name.
        foreach(var (key, value) in words)
            value.name = key;
    }

    public string? NextWord(char sep = ' ') {
        var s = this.getNextWord(sep);
        if(string.IsNullOrWhiteSpace(s)) return null;
        return s;
    }

    public void Reset() {  InDefinition = false; nested = 0;}

    public static void CommentP(Word w, Translator tr) {
        string? word;
        do {
            word = tr.NextWord();
        } while(word != null && word != ")");
    }
    public static void CommentS(Word w, Translator tr) {
        string? word;
        do {
            word = tr.NextWord();
        } while(word != null);
    }

    public void Emit(string s) => output.AppendLine(s);

    private static void ExecuteDef(Word w, Translator tr) {
        if(w.def == null) throw new Exception("Trying to execute a word with a null definition.");
        w.def(w, tr);
    }
    public static void CompileOrEmit(Word w, Translator tr) {
        if (tr.InDefinition) Compile(w, tr); else ExecuteDef(w, tr);
    }
    public static void Compile(Word w, Translator tr) {
        if(w.immediate)
            ExecuteDef(w, tr);
        else if(tr.actions == null)
            throw new Exception($"Trying to compile {w} outside a definition");
        else
            tr.actions.Add(w);
    }
    public static void Perform(Word w, Translator tr) {
        if(tr.InDefinition) Compile(w, tr);
        else ExecuteDef(w, tr);
    }
    private static string NextWordLower(Word w, Translator tr) {
        var s = tr.NextWord();
        if(s == null) throw new Exception($"Unexpected end of input stream while processing {w.name}.");

        s = s.ToLowerInvariant();
        return s;
    }
    public static void ColonDef(Word w, Translator tr) {
        var s = NextWordLower(w, tr);

        tr.lastWord   = s;
        tr.defActions = new();
        tr.recurseActions = new();
        tr.actions    = tr.defActions;

        tr.Emit(ToCsharpInst("_labelHere", $"\"{s}\""));

        tr.InDefinition = true;
    }
    public static void CreateDef(Word w, Translator tr) {
        var s = NextWordLower(w, tr);

        tr.lastCreated = s;
        tr.Emit(ToCsharpInst("_labelHere", $"\"{s}\""));

        // By default, calling a created word pushes the address on the stack.
        // This behavior can be overridden by does>, which is managed below.
        tr.words[s] = inline(ToCsharpInst("_pushLabel", $"\"{s}\""));
    }
    public static void DoesDef(Word w, Translator tr) {
        // Encountering does> finishes the definition of the word and starts the definition of does.
        List<Word> doesActions = new();

       doesActions.Add(
                function((Word w, Translator tr1) => // tr1 is at time of does> execution.
                    tr1.Emit(ToCsharpInst("_pushLabel", $"\"{tr1.lastCreated}\"")), false));


       if(tr.actions == null) throw new Exception("does> outside a definition perhaps?");

       tr.actions.Add(function((Word w, Translator tr1) =>
                    tr1.words[tr1.lastCreated] = new Word { immediate = false,
                    export = false, def = ExecuteWords(doesActions) }, false));

       tr.actions = doesActions;
    }
    public static void SemiColonDef(Word w, Translator tr) {
        if(tr.defActions == null) throw new Exception("Semicolon (;) seen in interpret mode");


        var wordName = tr.lastWord;

        // Attach the definition actions to the defining word (: array)
        var word =  new Word { name = wordName, immediate = false,
            export = false, def = ExecuteWords(tr.defActions)};
        tr.words[wordName] = word;

        RecurseSubst(tr, ref word);

        tr.literalCount = 0;
        tr.InDefinition   = false;
    }
    // Tried to use a c# constant instead of the dictionary for constants.
    // It didn't work because, to support Exit, a new definition is wrapped in a do {} while loop.
    // This makes the defined constant local to that scope, not visible outside it.
    // But it is faster, so making it a compile time flag.
    public static void ConstantDef(Word w, Translator tr) {
        var s = NextWordLower(w, tr);

        tr.Emit($"readonly nint {s} = VmExt.pop(ref vm);");
        tr.words[s] = inline($"var a = {s};pusha;");
    }
    public static void ConstantDef1(Word w, Translator tr) {
        var s = NextWordLower(w, tr);
        tr.Emit(ToCsharpInst("_labelHere", $"\"{s}\""));
        tr.Emit(ToCsharpInst("_comma"));
        var op1 = ToCsharpInst("_pushLabel",  $"\"{s}\"");
        var op2 = ToCsharpInst("_fetch");

        if(tr.words.TryGetValue(s, out var _)) throw new Exception($"Trying to reassign the constant {s}");
        tr.words[s] = inline($"{op1};{op2};");
    }

    public static void EmitFunc(Word w, Translator tr, string funcName) {
        if(string.IsNullOrWhiteSpace(w.name))
            throw new Exception("Try to generate a function for an empty word");

        tr.Emit($"static void {funcName}(ref Vm vm) {{\n");
        tr.Emit("do {\n");
        ExecuteDef(w, tr);
        tr.Emit("\n} while(false);");
        tr.Emit("\n}");

    }
    public static void RegisterTick(Word w, Translator tr, string funcName) {
            tr.Emit($"vm.xts[vm.xtsp] = {funcName};vm.wordToXts[\"{w.name}\"] = vm.xtsp; vm.xtsp++;");
            w.tickdefined = true;

            if(w.name == null) throw new Exception("Trying to tick a word without a name");
            tr.words[w.name] = w;
    }

    public static void TickDef(Word w, Translator tr) {
        var s = NextWordLower(w, tr);
        var funcName = ToCsharpId(s);

        if(!tr.words.TryGetValue(s, out var word))
            throw new Exception($"Executing tick ('), cannot find word {s}");

        if(!word.tickdefined) {
            EmitFunc(word, tr, funcName);
            RegisterTick(word, tr, funcName);
        }
        var op = $"VmExt.push(ref vm, vm.wordToXts[\"{word.name}\"]);";
        if(w.immediate)
            Compile(verbatim(op), tr);
        else
            tr.Emit(op);
    }
    public static void immediateDef(Word w, Translator tr) {
        tr.words[tr.lastWord].immediate = true;
    }

    public static void RecurseSubst(Translator tr, ref Word word) {
        if(tr.recurseActions == null) throw new Exception("Semicolon outside a definition perhaps?");
        if(word.name == null) throw new Exception("Trying to recurse a word without a name");

        var funcName = ToCsharpId(word.name);

        var isRecurse = tr.recurseActions.Count != 0;
        var actions = tr.recurseActions;

        foreach(var a in tr.recurseActions) {
            a.immediate = word.immediate; a.export = word.export; a.tickdefined = true;
            a.def = (word, tr) => {
                tr.Emit($"{funcName}(ref vm);");
            };
        }
        if(isRecurse) EmitFunc(word, tr, funcName);
    }
    public static void RecurseDef(Word w, Translator tr) {
        if(tr.actions == null) throw new Exception("Null actions while processing 'recurse'");
        if(tr.recurseActions == null) throw new Exception("Null recurse actions while processing 'recurse'");

        var recurseAction = new Word { name = "recurse" };

        tr.actions.Add(recurseAction);
        tr.recurseActions.Add(recurseAction);
    }
    public static void CStringDef(Word w, Translator tr) {
        var s = tr.NextWord('"');
        if(s == null) throw new Exception("End of input stream after c\"");

        CompileOrEmit(inline($"VmExt._fromDotNetString(ref vm, \"{s}\", true);"), tr);
    }
    public static void SStringDef(Word w, Translator tr) {
        var s = tr.NextWord('"');
        if(s == null) throw new Exception("End of input stream after s\"");

        CompileOrEmit(inline($"VmExt._fromDotNetString(ref vm, \"{s}\", false);"), tr);
    }
    public static void dotString(Word w, Translator tr) {
        var s = tr.NextWord('"');
        if(s == null) throw new Exception("End of input stream after .\"");

        CompileOrEmit(function((Word w, Translator tr1) => tr1.Emit($"vm.output.Write(\"{s}\");"), false), tr);
    }
    public static void abort(Word w, Translator tr) {
        var s = tr.NextWord('"');
        if(s == null) throw new Exception("End of input stream after abort\"");

        void f(Word w, Translator tr1) {
            tr1.Emit($"if(VmExt.pop(ref vm) != 0) throw new Exception(\"{s}\");");
        }
        CompileOrEmit(function(f, false), tr);
    }
    public static void charIm(Word w, Translator tr) {
        var s = tr.NextWord();
        if(s == null) throw new Exception("End of input stream after [char]");
        CompileOrEmit(function((Word w, Translator tr1) => tr1.Emit($"VmExt.push(ref vm, {(int)s[0]});"), false), tr);
    }
    public static void charN(Word w, Translator tr) {
        var s = tr.NextWord();
        if(s == null) throw new Exception("End of input stream after char");
        tr.Emit($"VmExt.push(ref vm, {(int)s[0]});");
    }
    public static void PostponeDef(Word w, Translator tr) {
        var s = NextWordLower(w, tr);

        var word = tr.words[s];

        if(tr.actions == null) throw new Exception($"Trying to postpone {s} outside a definition");
        tr.actions.Add(word);
    }
    public static void LiteralDef(Word w, Translator tr) {
        tr.Emit("VmExt._comma(ref vm);"); // Store value from top of stack on Here, move here fwd.
        Compile(verbatim($"VmExt._pushLabelValue(ref vm, \"{tr.lastWord}\", {tr.literalCount++});"), tr);
    }
    public static void DoDef(Word w, Translator tr) {

        tr.nested++;
        tr.loopStack.Push(tr.nested);

        var i = $"___i{tr.nested}";
        var e = $"___e{tr.nested}";
        var c = $"___c{tr.nested}";
        tr.Emit($@"var {i} = VmExt.pop(ref vm);
var {e} = VmExt.pop(ref vm);
var {c} = VmExt.loopCond({i}, {e});
while({c}({i}, {e})) {{
");
    }
    public static void LoopDef(Word w, Translator tr) {
        var i = $"___i{tr.loopStack.Pop()}";
        tr.Emit($"{i}++;\n}}");
    }
    public static void LoopPlusDef(Word w, Translator tr) {
        var i = $"___i{tr.loopStack.Pop()}";
        tr.Emit($"{i} += VmExt.pop(ref vm);\n}}");
    }

    public static void IDef(Word w, Translator tr) {
        var i = $"___i{tr.nested}";
        tr.Emit($"VmExt.push(ref vm, {i});");
    }
    public static void JDef(Word w, Translator tr) {
        var i = $"___i{tr.nested - 1}";
        tr.Emit($"VmExt.push(ref vm, {i});");
    }
    public static bool IsIdentifier(string text)
    {
       if (string.IsNullOrEmpty(text))                return false;
       if (!char.IsLetter(text[0]) && text[0] != '_') return false;

       for (int ix = 1; ix < text.Length; ++ix)
          if (!char.IsLetterOrDigit(text[ix]) && text[ix] != '_')
             return false;

       return true;
    }

    public static string ToCsharpInst(string inst) {
        if(specialInsts.TryGetValue(inst, out var v)) return v;
        if(IsIdentifier(inst))                        return $"VmExt.{inst}(ref vm);";
        return inst;
    }

    public static string ToCsharpInst(string inst, string arg) {
        if(!IsIdentifier(inst)) throw new Exception($"{inst} not an identifier");
        return $"VmExt.{inst}(ref vm, {arg});";
    }
    public static string ToInstStream(string words) => String.Join(";\n", words.Split(';').Select(ToCsharpInst));

    public static string ToCsharpId(string forthId) {
        StringBuilder sb = new();
        foreach(var c in forthId)
            if(sym.TryGetValue(c, out var v)) sb.Append($"_{v}");
            else sb.Append(c);
        return sb.ToString();
    }

    public static Definition ExecuteWords(IEnumerable<Word> words) =>
        (Word w, Translator tr) => {
            // Ugly way to support return. Kind of simulate a subroutine call in a peephole optimization.
            // TODO: test it is optimized away as it should.
            if(!tr.InDefinition) tr.Emit("do {\n");

            foreach(var word in words) ExecuteDef(word, tr);

            if(!tr.InDefinition) tr.Emit("} while(false);\n");
        };

    public static Word inline(string instructions) => new Word {
        immediate = false, export = false, def = (word, tr) => {
            var fullInst = $"{{\n{ToInstStream(instructions)}\n}}";
            tr.Emit(fullInst);
        }
    };
    public static Word verbatim(string text) => new Word {
        immediate = false, export = false, def = (word, tr) => {
            tr.Emit(text);
        }
    };
    public static Word intrinsic(string name) => new Word {
        immediate = false, export = true, def = (word, tr) => {
            var csharp = ToCsharpInst(name);
            tr.Emit(csharp);
        }
    };
    public static Word function(Definition f, bool immediate) => new Word {
        immediate = immediate, export = true, def = f };

    public static void PushNumber(string n, Translator tr) {

// Having to support the bonkers base feature in Forth slows things down as every push needs to be
// base-converted. You can disable base aware input by defining NOBASE.
#if NOBASE
        var s = $"VmExt.push(ref vm, {n});";
#else
        var s = $"VmExt.pushs(ref vm, \"{n}\");";
#endif
        tr.Emit(s);
    }
    public static void CompileNumber(string word, Translator tr) {
        if(tr.actions == null) throw new Exception($"Compiling {word}, found null actions property");

        tr.actions.Add(new Word { immediate = false, export = false, def = (Word w, Translator tr) =>
                PushNumber(word, tr) });
    }
    public static void PerformNumber(string aNumber, Translator tr) {
        if(tr.InDefinition) CompileNumber(aNumber, tr);
        else PushNumber(aNumber, tr);
    }
    // At compile time we don't know the base and the size of the cell (nint) for the Forth VM.
    // We try them all and get a runtime exception if we guess wrong.
    private static bool IsANumberInAnyBase(string? s) {
        foreach(var b in new int[] { 2, 8, 10, 16 }) { // Screw you, base 9.
            try { var _ = (nint)Convert.ToSByte(s, b); return true;} catch(Exception) { }
            try { var _ = (nint)Convert.ToInt16(s, b); return true;} catch(Exception) { }
            try { var _ = (nint)Convert.ToInt32(s, b); return true;} catch(Exception) { }
            try { var _ = (nint)Convert.ToInt64(s, b); return true;} catch(Exception) { }
        }
        return false;
    }
    public static void TranslateWord(string word, Translator tr) {
        word = word.ToLowerInvariant();
        if(tr.words.TryGetValue(word, out Word? v)) {
            Perform(v, tr);
        } else if(IsANumberInAnyBase(word)) {
            PerformNumber(word, tr);
        } else {
            throw new Exception($"The word '{word}' was not found in the dictionary.");
        }
    }
    public static void InsertDo(string word, Translator tr) {
        tr.Emit(ToCsharpInst("_do", $"\"{word}\""));
    }

    public static void TranslateLine(Translator tr, string line) {
        tr.setLine(line);

        while(true) {
            var word = tr.NextWord();
            if(word == null) break;

            TranslateWord(word, tr);
        }
    }
    public static void TranslateReader(TextReader reader, Translator tr) {
        while(true) {
            var line = reader.ReadLine();
            if(line == null) break;
            TranslateLine(tr, line);
        }
    }

    public static void EmitFunctionPreamble(Translator tr, string name)
        => tr.Emit($"public static void {name}(ref Vm vm) {{\n");
    public static void EmitFunctionEnding(Translator tr)
        => tr.Emit("\n}");
    public static void EmitFunctionCall(Translator tr, string name)
        => tr.Emit($"{name}(ref vm);\n");
    public static string LoadVmCode()
        => File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vm.cs.kernel"));
    public static string FlushToString(Translator tr) {
        var s = tr.output.ToString();
        tr.output.Clear();
        return s;
    }

    public static string ToCSharp(string funcName, string outp) =>
        $@"
static public partial class __GEN {{
    static public long Test{funcName}() {{
        var vm = new Vm(System.Console.In, System.Console.Out);
        {funcName}(ref vm);
        var res = VmExt.pop(ref vm);
        VmExt.depth(ref vm);
        var zero = VmExt.pop(ref vm);
        return res + zero;
    }}
    static public void {funcName} (ref Vm vm) {{
        {outp}
    }}
}}
";
    public static Word binary(string op) => inline($"popa;popb;var c = b {op} a;pushc;");
    public static Word intbinary(string op) => inline($"popa;popb;var c = (int)b {op} (int)a;pushc;");
    public static Word unary(string op)  => inline($"popa;var c = {op}(a);pushc;");
    public static Word unaryp(string op)  => inline($"popa;var c = (a){op};pushc;");
    public static Word math2(string op)  => inline($"popa;popb;var c = Math.{op}(b, a);pushc;");
    public static Word comp(string op) => inline($"popa;popb;var f = b {op} a;var c = f ? -1 : 0;pushc;");
    public static Word compu(string op) => inline($"popa;var f = a {op};var c = f ? -1 : 0;pushc;");
    public static Word pusha(string op) => inline($"var a = {op};pusha;");

    // The Forth dictionary.
    public Dictionary<string , Word> words = new() {
            {"+"             , binary("+") }         ,
            {"-"             , binary("-") }         ,
            {"*"             , binary("*") }         ,
            {"/"             , binary("/") }         ,
            {"mod"           , binary("%") }         ,
            {"and"           , binary("&") }         ,
            {"or"            , binary("|") }         ,
            {"xor"           , binary("^") }         ,
            {"lshift"        , intbinary("<<") }        ,
            {"rshift"        , intbinary(">>") }        ,
            {"negate"        , unary("-") }          ,
            {"1+"            , unary("++") }         ,
            {"1-"            , unary("--") }         ,
            {"2*"            , unary("2 *") }         ,
            {"2/"            , unary("2 /") }         ,
            {"abs"           , unary("Math.Abs") }   ,
            {"min"           , math2("Min") }        ,
            {"max"           , math2("Max") }        ,
            {"="           , comp("==") }        ,
            {"<>"           , comp("!=") }        ,
            {"<"           , comp("<") }        ,
            {"<="           , comp("<=") }        ,
            {">"           , comp(">") }        ,
            {">="           , comp(">=") }        ,
            {"0="           , compu("== 0") }        ,
            {"0<>"           , compu("!= 0") }        ,
            {"0<"           , compu("< 0") }        ,
            {"0<="           , compu("<= 0") }        ,
            {"0>"           , compu("> 0") }        ,
            {"0>="           , compu(">= 0") }        ,
            {"true"           , pusha("-1") }        ,
            {"false"           , pusha("0") }        ,

            {"dup"     ,  intrinsic("dup")},
            {"dup2"    ,  intrinsic("dup2")},
            {"drop"    ,  intrinsic("drop")},
            {"drop2"   ,  intrinsic("drop2")},
            {"cells"   ,  intrinsic("cells")},
            {"cell+"   ,  intrinsic("cellp")},
            {"chars"   ,  intrinsic("chars")},
            {"char+"   ,  intrinsic("charp")},
            {"unused"    ,  intrinsic("unused")},
            {"here"    ,  intrinsic("here")},
            {"over"    ,  intrinsic("over")},
            {"@"       ,  intrinsic("_fetch")},
            {"c@"      ,  intrinsic("_cfetch")},
            {"!"       ,  intrinsic("_store")},
            {"c!"      ,  intrinsic("_cstore")},
            {","       ,  intrinsic("_comma")},
            {"c,"      ,  intrinsic("_ccomma")},
            {"allot"   ,  intrinsic("allot")},
            {"align"   ,  intrinsic("align")},
            {"aligned" ,  intrinsic("aligned")},
            {"type"    ,  intrinsic("type")},
            {"source"  ,  intrinsic("source")},
            {"count"   ,  intrinsic("count")},
            {"refill"  ,  intrinsic("refill")},
            {"bl"      ,  intrinsic("bl")},
            {"nl"      ,  intrinsic("nl")},
            {"_do"     ,  intrinsic("_do")},
            {".s"   ,  intrinsic("_dots")},
            {"dump"    ,  intrinsic("dump")},
            {"?dup"    ,  intrinsic("_qdup")},
            {"depth"    ,  intrinsic("depth")},
            {">r"    ,  intrinsic("toR")},
            {"r>"    ,  intrinsic("fromR")},
            {"r@"    ,  intrinsic("fetchR")},
            {"+!"    ,  intrinsic("_fetchP")},
            {"move"    ,  intrinsic("move")},
            {"word"    ,  intrinsic("word")},
            {"cmove"    ,  intrinsic("cmove")},
            {"cmove>"    ,  intrinsic("cmove")},
            {"fill"    ,  intrinsic("fill")},
            {"blank"    ,  intrinsic("blank")},
            {"erase"    ,  intrinsic("erase")},
            {"u.r"    ,  intrinsic("urdot")},

            {"_labelHere"      ,  intrinsic("_labelHere")},
            {"key"      ,  intrinsic("_key")},
            {">in"      ,  intrinsic("inpp")},

            {"variable" , function(CreateDef    , false)} ,
#if FASTCONSTANT
            {"constant" , function(ConstantDef    , false)} ,
#else
            {"constant" , function(ConstantDef1    , false)} ,
#endif
            {":"      , function(ColonDef     , false)} ,
            {"create" , function(CreateDef    , false)} ,
            {"does>"  , function(DoesDef      , true)}  ,
            {";"      , function(SemiColonDef , true)}  ,
            {"("  , function(CommentP      , true)}  ,
            {"\\"  , function(CommentS      , true)}  ,
            {"do"  , function(DoDef      , false)}  ,
            {"+loop"  , function(LoopPlusDef      , false)}  ,
            {"loop"  , function(LoopDef      , false)}  ,
            {"i"  , function(IDef      , false)}  ,
            {"j"  , function(JDef      , false)}  ,
            {".\""  , function(dotString      , true)}  ,
            {"[char]"  , function(charIm      , true)}  ,
            {"char"  , function(charN      , false)}  ,
            {"abort\""  , function(abort      , true)}  ,
            {"'"  , function(TickDef      , false)}  ,
            {"[']"  , function(TickDef      , true)}  ,
            {"exit"  , verbatim("break;\n")}  ,
            {"immediate"  , function(immediateDef, false)},
            {"["  , function((Word w, Translator tr) => tr.InDefinition = false, true)},
            {"]"  , function((Word w, Translator tr) => tr.InDefinition = true, true)},
            {"postpone"  , function(PostponeDef, true)},
            {"literal"  , function(LiteralDef, true)},
            {"s\""  , function(SStringDef, true)},
            {"c\""  , function(CStringDef, true)},
            {"recurse"  , function(RecurseDef, true)},
            {"bye"  , function((Word _, Translator tr) => tr.Emit("System.Environment.Exit(0);"), false)},

            {"."       ,   inline("_dot;")},
            {"cr"      ,   inline("vm.output.WriteLine();")},
            {"swap"      ,   inline("popa;popb;pusha;pushb;")},
            {"rot"      ,   inline("popa;popb;popc;pushb;pusha;pushc;")},
            {"base"      ,   inline("basepu;")},
            {"decimal"      ,   inline("var a = 10;pusha;basepu;_store;")},
            {"hex"      ,   inline("var a = 16;pusha;basepu;_store;")},
            {"?"      ,   inline("_fetch;_dot;")},
            {"pad"      ,   inline("var a = vm.pad;pusha;")},

            {"if"      ,   verbatim("if(VmExt.pop(ref vm) != 0) {")},
            {"else"      ,   verbatim("} else {")},
            {"then"      ,   verbatim("}")},
            {"endif"      ,   verbatim("}")},

            {"begin"      ,   verbatim("while(true) {")},
            {"repeat"      ,   verbatim("}")},
            {"again"      ,   verbatim("}")},
            {"while"      ,   verbatim("if(VmExt.pop(ref vm) == 0) break;")},
            {"until"      ,   verbatim("if(VmExt.pop(ref vm) != 0) break; }")},
            {"leave"      ,   verbatim("break;")},
            {"page"      ,   verbatim("vm.output.Clear();")},
            {"spaces"      ,   inline("popa;for(var i = 0; i < a; i++) vm.output.Write(' ');")},
            {"space"      ,   inline("vm.output.Write(' ');")},
            {"emit"      ,   inline("popa;vm.output.Write((char)a);")},
            {"execute"      ,   inline("popa;vm.xts[a](ref vm);")},
        };

    // Maps symbols to words
    public static Dictionary<char, string> sym = new() {
        {'+', "plus"},
        {'-', "minus"},
        {'>', "more"},
        {'<', "less"},
        {'=', "equal"},
        {'!', "store"},
        {'@', "fetch"},
        {'"', "apostr"},
        {'%', "percent"},
        {'$', "dollar"},
        {'*', "mult"},
        {'(', "oparens"},
        {')', "cparens"},
        {'.', "dot"},
    };

    public static Dictionary<string, string> specialInsts = new() {
        {"popa", "var a = VmExt.pop(ref vm)"},
        {"popb", "var b = VmExt.pop(ref vm)"},
        {"popc", "var c = VmExt.pop(ref vm)"},

        {"cpopa", "var aa = VmExt.cpop(ref vm)"},
        {"cpopb", "var bb = VmExt.cpop(ref vm)"},
        {"cpopc", "var cc = VmExt.cpop(ref vm)"},

        {"pusha", "VmExt.push(ref vm, a)"},
        {"pushb", "VmExt.push(ref vm, b)"},
        {"pushc", "VmExt.push(ref vm, c)"},

        {"cpusha", "VmExt.cpush(ref vm, ca)"},
        {"cpushb", "VmExt.cpush(ref vm, cb)"},
        {"cpushc", "VmExt.cpush(ref vm, cc)"},
        {"sstring", "var s = VmExt.dotNetString(ref vm)"},
    };
}
