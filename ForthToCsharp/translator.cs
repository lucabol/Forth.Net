using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Linq;

public delegate void Definition(Word w, Translator tr);

public struct Word {
    public string     lastWordName;
    public Definition def;
    public bool       immediate;
}

public class Translator {
    // Input and outputs.
    public StringBuilder interpr;
    public StringBuilder compile;
    public TextReader inputReader;

    // State of the interpret.
    public bool Interpreting = true;

    public Translator(TextReader inputReader, StringBuilder interpr, StringBuilder compile) {
        this.interpr = interpr;
        this.compile = compile;
        this.inputReader = inputReader;
    }

    public IEnumerable<string> InputWords() {
        while(true) {
            var line = inputReader.ReadLine();
            if(line == null) yield break; // end of stream

            // The strange empty array is an optimized way to split on system specific whitespace.
            var ss = line.Split(new char[0], StringSplitOptions.RemoveEmptyEntries);
            foreach(var s in ss) yield return s;
        }
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
        if(IsIdentifier(inst))                        return $"vm.{inst}()";
        return inst;
    }

    public static string ToInstStream(string words) => String.Join(";\n", words.Split(';').Select(ToCsharpInst));

    public static string ToCsharpId(string forthId) {
        StringBuilder sb = new();
        foreach(var c in forthId)
            if(sym.TryGetValue(c, out var v)) sb.Append($"_{v}");
            else sb.Append(c);
        return sb.ToString();
    }
    public static Word ft(string word, string instructions) => new Word {
        lastWordName = ToCsharpId(word), immediate = false, def = (word, tr) => {
            var fullInst = ToInstStream(instructions);
            if(tr.Interpreting) tr.interpr.AppendLine(fullInst);
            else                tr.compile.AppendLine(fullInst);
        }
    };

    public static void PushNumber(string n, Translator tr) {
        var s = $"vm.push({n})";
        if(tr.Interpreting) tr.interpr.AppendLine(s); else tr.compile.AppendLine(s);
    }

    public static void TranslateWord(string word, Translator tr) {
        if(tr.words.TryGetValue(word, out var v)) {
            v.def(v, tr);
        } else if(nint.TryParse(word, out var _)) {
            PushNumber(word, tr);
        } else
        throw new Exception($"{word} is not in the dictionary");
    }
    public static void Translate(Translator tr) {
        foreach(var w in tr.InputWords()) TranslateWord(w, tr);
    }
    public static (string,string) TranslateString(string forthCode) {
        var isb = new StringBuilder();
        var csb = new StringBuilder();
        var tr = new Translator(new StringReader(forthCode), isb, csb);
        Translate(tr);
        return (tr.interpr.ToString(), tr.compile.ToString());
    }
    // The Forth dictionary.
    public Dictionary<string, Word> words = new() {
            {"+", ft("plus_0", "popa;popb;var c = a + b;pushc;") }
        };

    // Maps symbols to words
    public static Dictionary<char, string> sym = new() {
        {'+', "plus"},
        {'-', "minus"}
    };

    public static Dictionary<string, string> specialInsts = new() {
        {"popa", "var a = vm.pop()"},
        {"popb", "var b = vm.pop()"},
        {"popc", "var c = vm.pop()"},
        {"pusha", "vm.push(a)"},
        {"pushb", "vm.push(b)"},
        {"pushc", "vm.push(c)"},
    };
}
