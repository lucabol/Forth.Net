namespace ForthToCsharp;

using cell      = System.Int64;
using cellIndex = System.Int32;

using System;
using System.Text;

public class Translator
{
    TextReader _in;
    TextWriter _out;

    public Translator(TextReader input, TextWriter output) => (_in, _out) = (input, output);

    Dictionary<string, string> words = new () {
        {"+", "plus"},
        {"-", "minus"}
    };

    Dictionary<string, Func<Vm, string>> immediates = new() {
        {":", vm => "public void"
        }
    };

    const string prelude = @"using ForthToCsharp;
        static public partial class Forth {
            static public void Execute(Vm vm) {
        ";
    const string epilog = "}}";

    public void Translate() {
        Vm vm = new(_in, _out);
        StringBuilder sb = new();
        //sb.AppendLine(prelude);

        while(true) {
            // Fill the input buffer.
            vm.refill();
            if(vm.pop() == vm.ffalse()) break;

            // Read the next word.
            while(true) {
                vm.bl();
                vm.word();
                vm.count();
                var w = vm.dotNetString();
                // If we are at the end of the buffer start again reloading the input buffer.
                if(String.IsNullOrEmpty(w)) break;

                // If it is a word spelled differently than Forth (i.e., +) or a newly defined one, insert it.
                if(words.TryGetValue(w, out string? p)) {
                    sb.AppendLine($"vm.{p.ToLowerInvariant()}();");
                }

                // If it is a number, push it.
                if(cell.TryParse(w, out cell _)) {
                    sb.AppendLine($"vm.push({w.ToLowerInvariant()});");
                }

                // If it is a word spelled the same as Forth, just emit it.
                sb.AppendLine($"vm.{w.ToLowerInvariant()}();");
            }
        }
        //sb.AppendLine(epilog);
        _out.Write(sb);
    }


}
