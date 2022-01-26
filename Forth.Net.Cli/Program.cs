using System.Text;
using CommandLine;
using CommandLine.Text;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using System.Reflection;
using static TranslatorUtils.TranslatorExt;

using static System.Console;
using System.Collections.Immutable;
using System;

// Nice fat global variables to simplify code. It is unlikely this will go multithread.
bool Verbose = false;
bool Repl    = false;
bool Debug   = false;

Vm vm        = new(Console.In, Console.Out);

ScriptState<object>? script = null;

// I cannot compile these words to C# as they assume there is a vm running at compile time (aka an interpret)
HashSet<string> invalidCompileWords = new(new string[] {"word", "source", ">in"});

// Operating on the vm variable directly, without groing through the script object
// introduces bizarre problems, where the state of the vm changes randomly.
// Took forever to debug. Hence the inelegant/slow approach below.
// This get passed into the Translator to avoid a direct dependency to the VM.
Action<string> setLine = line => {
    if(script == null) throw new Exception("Script cannot be null");
    var s = EscapeString(line);
    script = script.ContinueWithAsync($"vm.inputBuffer = \"{s}\";VmExt.refill(ref vm);VmExt.drop(ref vm);").Result;
};

Func<string> getTosString = () => {
    if(script == null) throw new Exception("Script cannot be null");
    script = script.ContinueWithAsync($"VmExt.dup(ref vm); VmExt.count(ref vm); return VmExt.dotNetString(ref vm);").Result;
    var w = (string)script.ReturnValue; 
    return w;

};
// If compiling, we don't need to go through the script object, speeding up things considerably.
Action<string> setLineC = line => {
    vm.inputBuffer = line; VmExt.refill(ref vm);VmExt.drop(ref vm);
};

Func<char, string> getNextWordC = c => VmExt.nextword(ref vm, c);

Func<string> getTosStringC = () => throw new Exception("There is no running vm while compiling to C#");

var parser       = new CommandLine.Parser(with => with.HelpWriter = null);
var parserResult = parser.ParseArguments<Options>(args);
parserResult
    .WithParsed<Options>(options => Run(options))
    .WithNotParsed(errs => DisplayHelp(parserResult, errs));

void Run(Options o) {

    ValidateOptions(o);

    Repl = o.Exec == null;

    if(o.Output != null)
        CompileTo(o);
    else
        Interpret(o).Wait();
}

static void DisplayHelp<T>(ParserResult<T> result, IEnumerable<Error> errs)
{
  var helpText = HelpText.AutoBuild(result, h =>
  {
    h.AdditionalNewLineAfterOption = false;
    h.AddPreOptionsLine("Usage: nforth [Forth files] [Options]");
    h.AddPostOptionsText
        ("EXAMPLES:\n\tnforth\n\tnforth Test1.fth Test2.fth -e bye\n\tnforth Test1.fth Test2.fth -o Forth.cs");
    return HelpText.DefaultParsingErrorsHandler(result, h);
  }, e => e);

  Console.WriteLine(helpText);
}

void ColorLine(ConsoleColor color, string s) {
    var backupcolor = ForegroundColor;
    ForegroundColor = color;
    Console.WriteLine(s);
    ForegroundColor = backupcolor;
}

void ValidateOptions(Options o) {
    if(o.Exec != null && o.Output != null) {
        WriteLine("You can either execute or compile code, not both.");
        Environment.Exit(1);
    }
    if(o.Output != null && (o.Files == null || !o.Files.Any())) {
        WriteLine("You say you want to compile, but didn't pass any files.");
        Environment.Exit(1);
    }
    Verbose = o.Verbose;
}

void CompileFiles(Options o, Translator tr) {
    /*
    IEnumerable<(string, TextReader)> files = 
        o.Files.Select(f => (Path.GetFileNameWithoutExtension(f), (TextReader) new StreamReader(f)));

    foreach(var (name, reader) in files) {
        EmitFunctionPreamble(tr, name);
        ProcessReader(reader, tr);
        EmitFunctionEnding(tr);
    }
    if(files.Any()) {
        EmitFunctionPreamble(tr, "RunAll");
        foreach(var (name, _) in files)
            EmitFunctionCall(tr, name);
        EmitFunctionEnding(tr);
    }
    */
}

void CompileTo(Options o) {

    /*
    Translator tr = new(ImmutableList.Create<Word>(), new Code("", ""), Status.Executing, ImmutableList.Create<Word>(), null, null, 0);

    CompileFiles(o, tr);

    Write("Compiling. Please wait ...");
    StringBuilder sb = new();

    var vmCode  = LoadVmCode();
    sb.Append(vmCode);

    sb.Append("public static class Forth {\n");
    sb.Append(FlashStatements(tr));
    sb.Append("\n}");
    File.WriteAllText(o.Output, sb.ToString());
    WriteLine(" done.");
    */
}

/*
void ProcessReader(TextReader reader, Translator tr) {

    while(true) {

            var line = reader.ReadLine();
            if(line == null) break;

            tr.setLine(line);

            while(true) {
                var word = tr.NextWord();
                if(word == null) break;

                if(invalidCompileWords.Contains(word.ToLowerInvariant()))
                    ColorLine(ConsoleColor.Red,
              $"You cannot use the word '{word}' at compile time as there is no input text buffer.");

                TranslateWord(word, tr);
            }
    }
}

async Task InterpretFiles(Options o, Translator tr) {

    if(script == null) throw new Exception("InitEngine failed.");

    IEnumerable<(string, TextReader)> files = 
        o.Files.Select(f => (Path.GetFileNameWithoutExtension(f), (TextReader) new StreamReader(f)));

    foreach (var (_, reader) in files)
    {
        while(true) {
                var line = reader.ReadLine();
                if(line == null) break;

                tr.setLine(line);

                while(true) {
                    tr.statements.Clear();
                    var word = tr.NextWord();
                    if(word == null) break;

                    TranslateWord(word, tr);

                    var newCode = tr.statements.ToString();

                    script = await script.ContinueWithAsync(newCode);
                }
        }
    }
}
*/
string GetNextWord(char c) {
    if(script == null) throw new Exception("Script cannot be null");
    script = script.ContinueWithAsync($"return VmExt.nextword(ref vm, '{c}');").Result;
    var w = (string)script.ReturnValue; 
    return w;
};
void ExecuteDebug (Code code) {
    if(script == null) throw new Exception("Script cannot be null");
    if(Debug) WriteLine(code.ToString());
    Execute(code);
};
void Execute (Code code) {
    script = script.ContinueWithAsync(code.Declarations + code.Statements).Result;
};

async Task Interpret(Options o) {

    Translator tr = Create(GetNextWord, ExecuteDebug);

    InitEngine(tr, o);

    if(script == null) throw new Exception("InitEngine failed.");

    //await InterpretFiles(o, tr);

    if(o.Exec != null) {
        Write("Interpreting Exec instruction. Please wait ...");
        setLine(o.Exec);
        tr = tr.Translate();
        WriteLine(" done.");
    }

    try {

        CancelKeyPress += delegate {
            ResetColor();
        };

        WriteLine("Say 'bye' to exit. No output means all good.");


        System.ReadLine.HistoryEnabled = true;

        // Trying to remove a delay in the first execution by 'priming the pump'.
        setLine("1 drop");
        tr = tr.Translate();

        while(true) {

            try {
                System.ReadLine.AutoCompletionHandler = new AutoCompletionHandler(tr);

                Console.CursorVisible = true;
                var line = System.ReadLine.Read("");
                Console.CursorVisible = false;
                if(line == null) break;

                var lowerLine = line.Trim().ToLowerInvariant();

                if(lowerLine == "debug") { Debug = !Debug; continue;}

                setLine(line);
                tr = tr.Translate();

                // This is excedingly clever. It forces the input cursor to always be on the next
                // line and the first position on the left.
                if(CursorLeft != 0) Console.WriteLine();

            } catch(Exception e) {
                ColorLine(ConsoleColor.Red, e.ToString());
                tr = tr.Reset();
                script = await script.ContinueWithAsync("vm.reset()");
            } finally {
                Execute(new Code("", "VmExt._dots(ref vm);"));
            }

        }
    } finally {
        ResetColor();
        Console.CursorVisible = true;
    }
}

string EscapeString(string str) {
    
    var s = str;
    s = s.Replace("\\", "\\\\");
    s = s.Replace("\"", "\\\"");
    s = s.Replace("{", "{{");
    s = s.Replace("}", "}}");
    return s;
}
void InitEngine(Translator tr, Options o) {

    Write("Initializing. Please wait ...");

    var globals = new Globals { vm = vm };
    var initCode = "";

    script = CSharpScript.RunAsync(
            initCode,
            ScriptOptions.Default.WithReferences(new Assembly[] {
                typeof(Globals).Assembly, typeof(Environment).Assembly}),
            globals: globals).Result;
    WriteLine(" done.");
}

void Write(string s) { if(Verbose || Repl) Console.Write(s);}
void WriteLine(string s) { if(Verbose || Repl) Console.WriteLine(s);}

public class Options {
    [Option('e', "exec [forthstring]", Required = false, HelpText = "Execute forthstring after starting up.")]
    public string? Exec {get; set;}

    [Option('o', "output [csfile]", Required = false, HelpText = "Compile code in the Forth files to csfile.")]
    public string? Output {get; set;}

    [Value(0, MetaName="Forth files", HelpText = "Optional Forth files to compile or execute.")]
    public IEnumerable<string>? Files {get; set;}

    [Option('v', "verbose", Required = false, HelpText = "Produce verbose output.")]
    public bool Verbose {get; set;} = false;
}

public class Globals {public Vm vm;}

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
        words = tr.Words.Select(w => w.Name);
    }
}

