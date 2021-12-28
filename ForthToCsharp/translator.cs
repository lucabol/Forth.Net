using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Linq;

public delegate void Definition(Word w, Translator tr);

public struct Word {
    public Definition def;
    public bool       immediate;
    public bool       export;
}

public class Translator {

    // Input and outputs.
    public StringBuilder output;
    public TextReader inputReader;

    public string? line;

    // Manage definition words.
    public bool InDefinition  = false;
    public string lastWord    = "";
    public string lastCreated = "";

    List<Word>? defActions;
    List<Word>? doesActions;
    List<Word>? actions;

    // Iteration count for for loops.
    public int nested = 0;

    public Translator(TextReader inputReader, StringBuilder output) {
        this.output  = output;
        this.inputReader = inputReader;
    }

    // I can't use an iterator returning method because CreateWord need access to the next word.
    public string? NextWord(char sep = ' ') {
        while(string.IsNullOrWhiteSpace(line)) {
            line = inputReader.ReadLine();
            if(line == null) return null;
        }

        string word;
        line = line.Trim();
        var index = line.IndexOf(sep);
        if(index == -1) {
            word = line;
            line = null;
            return word;
        }

        word = line.Substring(0, index);
        line = line.Substring(index + 1);
        return word;
    }

    public static void CommentP(Word w, Translator tr) {
        string? word;
        do {
            word = tr.NextWord();
        } while(word != null && word != ")");
    }
    public static void CommentS(Word w, Translator tr) {
        tr.line = "";
    }

    public void Emit(string s) => output.AppendLine(s);

    public static void Execute(Word w, Translator tr) => w.def(w, tr);
    public static void Compile(Word w, Translator tr) {
        if(w.immediate)
            w.def(w, tr);
        else if(tr.actions == null)
            throw new Exception($"Trying to compile {w} outside a definition");
        else
            tr.actions.Add(w);
    }
    public static void Perform(Word w, Translator tr) {
        if(tr.InDefinition) Compile(w, tr);
        else Execute(w, tr);
    }

    public static void ColonDef(Word w, Translator tr) {
        var s = tr.NextWord();
        if(s == null) throw new Exception("End of input stream after Colon");
        s = s.ToLowerInvariant();

        tr.lastWord    = s;
        tr.defActions  = new();
        tr.doesActions = null;
        tr.actions     = tr.defActions;

        tr.Emit(ToCsharpInst("_labelHere", $"\"{s}\""));

        tr.InDefinition = true;
    }
    public static void CreateDef(Word w, Translator tr) {
        var s = tr.NextWord();
        if(s == null) throw new Exception("End of input stream after Create");
        s = s.ToLowerInvariant();

        tr.lastCreated = s;
        tr.Emit(ToCsharpInst("_labelHere", $"\"{s}\""));

        // By default, calling a created word pushes the address on the stack.
        // This behavior can be overridden by does>, which is managed below.
        tr.words[ToCsharpId(s)] = inline(ToCsharpInst("_pushLabel", $"\"{s}\""));
    }
    public static void DoesDef(Word w, Translator tr) {
        // Encountering does> finishes the definition of the word and starts the definition of does.
        tr.doesActions = new();
        tr.actions = tr.doesActions;
        tr.doesActions.Add(
                function((Word w, Translator tr1) => // tr1 is at time of does> execution.
                    tr.Emit(ToCsharpInst("_pushLabel", $"\"{tr1.lastCreated}\"")), false));
    }
    public static void SemiColonDef(Word w, Translator tr) {
        if(tr.defActions == null) throw new Exception("Semicolon (;) seen in interpret mode");

         // Gnarly. At creation (i.e. 10 array ar) attach the words from does> part of : array to
         // the dictionary word for ar.
        if(tr.doesActions != null)
            tr.defActions.Add(function((Word w, Translator tr1) =>
                        tr.words[tr1.lastCreated] = function((Word w, Translator tr2) => 
                                            ExecuteWords(tr.doesActions), false), false));

        // Attach the definition actions to the defining word (: array)
        tr.words[ToCsharpId(tr.lastWord)] = new Word { immediate = false, export = false, def = ExecuteWords(tr.defActions)};
        tr.InDefinition = false;
    }
    public static void dotString(Word w, Translator tr) {
        var s = tr.NextWord('"');
        if(s == null) throw new Exception("End of input stream after .\"");

        Compile(function((Word w, Translator tr1) => tr1.Emit($"Console.Write(\"{s}\");"), false), tr);
    }
    public static void abort(Word w, Translator tr) {
        var s = tr.NextWord('"');
        if(s == null) throw new Exception("End of input stream after abort\"");

        void f(Word w, Translator tr1) {
            tr1.Emit($"if(VmExt.pop(ref vm) != 0) throw new Exception(\"{s}\");");
        }
        Compile(function(f, false), tr);
    }
    public static void charIm(Word w, Translator tr) {
        var s = tr.NextWord();
        if(s == null) throw new Exception("End of input stream after [char]");
        tr.Emit($"VmExt.push(ref vm, {(int)s[0]});");
    }
    public static void DoDef(Word w, Translator tr) {

        tr.nested++;

        var i = $"___i{tr.nested}";
        var s = $"___s{tr.nested}";
        var e = $"___e{tr.nested}";
        tr.Emit($@"var {s} = VmExt.pop(ref vm);
var {e} = VmExt.pop(ref vm);
for(var {i} = {s};{i} < {e}; {i}++) {{
");
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
        (Word w, Translator tr) => { foreach(var word in words) word.def(w, tr); };

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
        var s = $"VmExt.push(ref vm, {n});";
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
    public static void TranslateWord(string word, Translator tr) {
        word = word.ToLowerInvariant();
        if(tr.words.TryGetValue(word, out var v)) { // Keep symbols (i.e, +, -).
            Perform(v, tr);
        } else if(tr.words.TryGetValue(ToCsharpId(word), out var vc)) { // Transform symbols to C#.
            Perform(vc, tr);
        } else if(nint.TryParse(word, out var _)) {
            PerformNumber(word, tr);
        } else {
            throw new Exception($"Word '{word}' not in the dictionary.");
        }
    }
    public static void InsertDo(string word, Translator tr) {
        tr.Emit(ToCsharpInst("_do", $"\"{word}\""));
    }

    public static void Translate(Translator tr) {
        while(true) {
            var word = tr.NextWord();
            if(word == null) break;

            TranslateWord(word, tr);
        }
    }
    public static string TranslateString(string forthCode) {
        var outp = new StringBuilder();
        var tr = new Translator(new StringReader(forthCode), outp);
        Translate(tr);
        return tr.output.ToString();
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
            {"word"    ,  intrinsic("word")},
            {"bl"      ,  intrinsic("bl")},
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
            {"cmove"    ,  intrinsic("cmove")},
            {"cmove>"    ,  intrinsic("cmove")},
            {"fill"    ,  intrinsic("fill")},
            {"blank"    ,  intrinsic("blank")},
            {"erase"    ,  intrinsic("erase")},

            {"_labelHere"      ,  intrinsic("_labelHere")},

            {"create" , function(CreateDef    , false)} ,
            {":"      , function(ColonDef     , false)} ,
            {";"      , function(SemiColonDef , true)}  ,
            {"does>"  , function(DoesDef      , true)}  ,
            {"("  , function(CommentP      , true)}  ,
            {"\\"  , function(CommentS      , true)}  ,
            {"do"  , function(DoDef      , false)}  ,
            {"loop"  , verbatim("}")}  ,
            {"i"  , function(IDef      , false)}  ,
            {"j"  , function(JDef      , false)}  ,
            {".\""  , function(dotString      , true)}  ,
            {"[char]"  , function(charIm      , true)}  ,
            {"abort\""  , function(abort      , true)}  ,

            {"."       ,   inline("popa;vm.output.Write(a);vm.output.Write(' ');")},
            {"cr"      ,   inline("vm.output.WriteLine();")},
            {"swap"      ,   inline("popa;popb;pusha;pushb;")},
            {"rot"      ,   inline("popa;popb;popc;pushb;pusha;pushc;")},

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
