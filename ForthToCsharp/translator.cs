using cell      = System.Int64;
using cellIndex = System.Int32;

using System;
using System.IO;
using System.Collections.Generic;
using System.Text;

public class Translator
{
    TextReader _in;
    TextWriter _out;

    public Translator(TextReader input, TextWriter output) {
        (_in, _out) = (input, output);
        immediates = new() {
            {"create", Create}
        };
    }

    Dictionary<string, string> synonyms = new () {
        {"+",   "plus"},
        {"-",   "minus"},
        {",",   "comma"},
        {".",   "dot"},
        {".s",  "dots"},
        {"@",   "at"},
    };

    Dictionary<string, Func<Vm, string>> immediates;
    Dictionary<string, Func<Vm, string>> words = new();

    const string prelude = @"
        static public partial class Forth {
            static public long Run() {
                var vm = new Vm(System.Console.In, System.Console.Out);
                Forth.Execute(vm);
                return vm.pop();
            }
            static public void Execute(Vm vm) {
        ";
    const string epilog = "}}";


    private string Colon(Vm vm) {
        return "";
    }

    private string GetNextWord(Vm vm) {
        vm.bl();
        vm.word();
        vm.count();
        return vm.dotNetString().ToLowerInvariant();
    }
    private string Create(Vm vm) {
        var s = GetNextWord(vm);
        words[s] = vm => ""; // The default 'does' for a word is not to do anything with address on stack.
        return $"vm.create(\"{s}\");";
    }
    static public string ToCSharp(string forthCode) {
        if(String.IsNullOrEmpty(forthCode)) throw new ArgumentException("No forth code?");

        using TextReader tr = new StringReader(forthCode);
        using TextWriter tw = new StringWriter();
        var translator = new Translator(tr, tw);
        tw.WriteLine(prelude);
        translator.Translate();
        tw.WriteLine(epilog);
        var res = tw.ToString();
        if(String.IsNullOrEmpty(res)) throw new ArgumentException("Error generating forth code.");
        return res;
    }
    public void Translate() {
        Vm vm = new(_in, _out);

        while(true) {
            // Fill the input buffer.
            vm.refill();
            if(vm.pop() == vm.ffalse()) break;

            // Process the next word.
            while(true) {
                var w = GetNextWord(vm);
                // If we are at the end of the buffer start again reloading the input buffer.
                if(String.IsNullOrEmpty(w)) break;

                // TODO: is the order of checks below correct? I don't remember seeing it on ANS FORTH.
                // If it is a word spelled differently than Forth (i.e., +) or a newly defined one, insert it.
                if(synonyms.TryGetValue(w, out string? p)) {
                    _out.WriteLine($"vm.{p.ToLowerInvariant()}();");
                } else
                // If it is an immediate word, execute it outright and print whatever it returns.
                if(immediates.TryGetValue(w, out Func<Vm, string>? f)) {
                    var s = f(vm);
                    if(!String.IsNullOrEmpty(s)) _out.WriteLine(s);
                } else
                // If it is a created label, execute the appropiate 'does' code.
                if(words.TryGetValue(w, out var does)) {
                    // Push address of the Created cell on the stack.
                    _out.WriteLine($"vm.push(vm.addressof(\"{w}\"));");
                    var s = does(vm);
                    if(!String.IsNullOrEmpty(s)) _out.WriteLine(s);
                } else
                // If it is a number, push it.
                if(cell.TryParse(w, out cell _)) {
                    _out.WriteLine($"vm.push({w.ToLowerInvariant()});");
                } else
                // If it is a word spelled the same as Forth, just emit it.
                _out.WriteLine($"vm.{w.ToLowerInvariant()}();");
            }
        }
    }


}
