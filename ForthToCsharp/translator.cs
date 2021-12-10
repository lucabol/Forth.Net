namespace ForthToCsharp;

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


}
