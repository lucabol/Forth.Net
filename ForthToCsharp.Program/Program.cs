using System.Text;
using CommandLine;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using static Translator;

using static System.Console;

bool Verbose = false;

Parser.Default.ParseArguments<Options>(args).WithParsed<Options>(o => {

    ValidateOptions(o);

    StringBuilder sb = new();
    Translator tr = new(sb);

    ProcessFiles(o, tr);

    if(o.Output != null)
        CompileTo(o, tr);
    else
        Interpret(o, tr).Wait();
});


void ColorLine(ConsoleColor color, string s) {
    var backupcolor = ForegroundColor;
    ForegroundColor = color;
    WriteLine(s);
    ForegroundColor = backupcolor;
}

void ValidateOptions(Options o) {
    if(o.Exec != null && o.Output != null) {
        WriteLine("You can either execute or compile code, not both.");
        Environment.Exit(1);
    }
    if(o.Output != null && (o.Files == null || o.Files.Count() == 0)) {
        WriteLine("You say you want to compile, but didn't pass any files.");
        Environment.Exit(1);
    }
    Verbose = o.Verbose;
}

void ProcessFiles(Options o, Translator tr) {
    IEnumerable<(string, TextReader)> files = 
        o.Files.Select(f => (Path.GetFileNameWithoutExtension(f), (TextReader) new StreamReader(f)));

    foreach(var (name, reader) in files) {
        EmitFunctionPreamble(tr, name);
        TranslateReader(reader, tr);
        EmitFunctionEnding(tr);
    }
    if(files.Count() != 0) {
        EmitFunctionPreamble(tr, "RunAll");
        foreach(var (name, _) in files)
            EmitFunctionCall(tr, name);
        EmitFunctionEnding(tr);
    }
}

void CompileTo(Options o, Translator tr) {
}

async Task Interpret(Options o, Translator tr) {

    var vmCode  = LoadVmCode();

    var globals = new Globals { input = Console.In, output = Console.Out };
    var vmnew   = "var vm = new Vm(input, output);";

    var repl = o.Exec == null;

    if(repl) Console.Write("Initializing. Please wait ..."); else Write("Initializing. Please wait ...");
    var script = await CSharpScript.RunAsync(vmCode, globals: globals).ConfigureAwait(false);
    script     = await script.ContinueWithAsync(vmnew).ConfigureAwait(false);
    if(repl) Console.WriteLine(" done."); else WriteLine(" done.");

    var filesCode = FlushToString(tr);
    if(!string.IsNullOrWhiteSpace(filesCode)) {
        Write("Interpreting Forth Files. Please wait ...");
        script     = await script.ContinueWithAsync(filesCode).ConfigureAwait(false);
        script     = await script.ContinueWithAsync("RunAll(ref vm);").ConfigureAwait(false);
        WriteLine(" done.");
    }

    if(o.Exec != null) {
        Write("Interpreting Exec instruction. Please wait ...");
        TranslateLine(tr, o.Exec);
        script     = await script.ContinueWithAsync(FlushToString(tr)).ConfigureAwait(false);
        WriteLine(" done.");
    }

    try {

        CancelKeyPress += delegate {
            ResetColor();
        };

        WriteLine("Say 'bye' to exit. No output means all good.");

        var debug     = false;

        var input   = new StringBuilder();
        var output  = new StringBuilder();

        System.ReadLine.HistoryEnabled = true;

        while(true) {

            try {
                tr.output.Clear();
                System.ReadLine.AutoCompletionHandler = new AutoCompletionHandler(tr);

                var line = System.ReadLine.Read("");
                if(line == null) break;
                var lowerLine = line.Trim().ToLowerInvariant();

                if(lowerLine == "debug") { debug = !debug; continue;}


                Translator.TranslateLine(tr, line);
                var newCode = tr.output.ToString();

                if(debug) WriteLine($"\n{newCode}");

                script = await script.ContinueWithAsync(newCode).ConfigureAwait(false);
            } catch(Exception e) {
                ColorLine(ConsoleColor.Red, e.ToString());
                tr.Reset();
                script = await script.ContinueWithAsync("vm.reset()").ConfigureAwait(false);
            }
        }
    } finally {
        ResetColor();
    }
}

void Write(string s) { if(Verbose) Console.Write(s);}
void WriteLine(string s) { if(Verbose) Console.WriteLine(s);}

public class Options {
    [Option('e', "exec", Required = false, HelpText = "Execute the code in the Forth files and append this instruction at the ned.")]
    public string? Exec {get; set;}

    [Option('o', "output", Required = false, HelpText = "Compile C# code to this file.")]
    public string? Output {get; set;}

    [Value(1, HelpText = "Forth files to compile or execute.")]
    public IEnumerable<string>? Files {get; set;}

    [Option('v', "verbose", Required = false, HelpText = "Produce verbose output.")]
    public bool Verbose {get; set;} = false;
}

public class Globals { public TextReader? input; public TextWriter? output; }

class AutoCompletionHandler : IAutoCompleteHandler
{
    public char[] Separators { get; set; } = new char[] { ' ' };
    public IEnumerable<string> words;

    public string[] GetSuggestions(string text, int index)
    {
        if(string.IsNullOrWhiteSpace(text)) return words.ToArray();

        return words.Where(
                s => s.StartsWith(
                text.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.TrimEntries).Last())
                ).ToArray();
    }

    public AutoCompletionHandler(Translator tr) {
        words = tr.words.Keys;
    }
}

