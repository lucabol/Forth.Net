using Forth;
using static System.Console;

using CommandLine;
using CommandLine.Text;

var verbose      = false;
Options? options = null;
Vm? vm            = null;

var parser       = new CommandLine.Parser(with => with.HelpWriter = null);
var parserResult = parser.ParseArguments<Options>(args);
parserResult
    .WithParsed<Options>(options => Run(options))
    .WithNotParsed(errs => DisplayHelp(parserResult, errs));

void Run(Options o) {

    options = o;
    verbose = options.Verbose;

    vm = new Vm(parameterStackSize: o.ParamSize,
                returnStackSize   : o.ReturnSize,
                dataStackSize     : o.DictSize);

    foreach (var fileName in o.Files)
        InterpretFile(fileName);

    if(o.Exec is not null)
        vm.EvaluateSingleLine(o.Exec);

    WriteLine("LForth.Net by Luca Bolognese (2022)");
    WriteLine("Say 'bye' to exit. 'debug' to see more. The rest is Forth.\n");
    System.ReadLine.HistoryEnabled = true;

    vm.NextLine = NextLine;
    while(true)
        try
        {
            vm.Quit();
        } catch(ForthException e) { 
            ColorLine(ConsoleColor.Red, e.Message);
            vm.Reset();
        } catch(ArgumentOutOfRangeException e) {
            if(e.StackTrace?.ToString().Contains("Pop") is not null)
                ColorLine(ConsoleColor.Red, "Possible stack underflow (it is expensive to check). Enable 'debug' to see the full exception.");
            if(vm.Debug) ColorLine(ConsoleColor.Gray, e.ToString());
            vm.Reset();
        } catch(Exception e)
        {
            ColorLine(ConsoleColor.Red, e.Message + " Enable 'debug' to see the full exception.");
            if(vm.Debug) ColorLine(ConsoleColor.Gray, e.ToString());
            vm.Reset();
        }
}

static void DisplayHelp<T>(ParserResult<T> result, IEnumerable<Error> errs)
{
  var helpText = HelpText.AutoBuild(result, h =>
  {
    h.AdditionalNewLineAfterOption = false;
    h.AddPreOptionsLine("Usage: nforth [Forth files] [Options]");
    h.AddPostOptionsText
        ("EXAMPLES:\n\nforth\n\nforth Test1.fth Test2.fth -e bye\n\nforth Test1.fth Test2.fth -o Forth.fim");
    return HelpText.DefaultParsingErrorsHandler(result, h);
  }, e => e);

  Console.WriteLine(helpText);
}

string NextLine() {
    
        if(CursorLeft != 0) WriteLine();
        if(!options.HideStack) ColorLine(ConsoleColor.Gray, vm.DotS());

        System.ReadLine.AutoCompletionHandler = new AutoCompletionHandler(vm);

        CursorVisible = true;
        var line = System.ReadLine.Read();
        CursorVisible = false;

        return line;
};


void VWrite(string s)     { if(verbose) Write(s); };
void VWriteLine(string s) { if(verbose) Write(s); };

void InterpretFile(string fileName)
{

    var lineNum = 0;
    var lineText = "";

    try {
        VWrite($"Interpreting file {fileName} ...");
        using var stream = File.OpenRead(fileName);
        using var reader = new StreamReader(stream);
        vm.NextLine = () => { lineNum++; lineText = reader.ReadLine()! ; return lineText; };
        vm.Quit();
        VWriteLine(" done.\n");
    } catch(Exception)
    {
        ColorLine(ConsoleColor.Red, $"File: {fileName} Line: {lineNum}\n{lineText}");
        throw; 
    }
}
void ColorLine(ConsoleColor color, string s) {
    var backupcolor = ForegroundColor;
    ForegroundColor = color;
    Console.WriteLine(s);
    ForegroundColor = backupcolor;
}

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

    public AutoCompletionHandler(Vm vm) {
        words = vm.Words();
    }
}

class Options {
    [Option('e', "exec [forthstring]", Required = false, HelpText = "Execute forthstring after starting up.")]
    public string? Exec {get; set;}

    [Value(0, MetaName="Forth files", HelpText = "Optional Forth files to compile or execute.")]
    public IEnumerable<string>? Files {get; set;}

    [Option('v', "verbose", Required = false, HelpText = "Produce verbose output.")]
    public bool Verbose {get; set;} = false;

    [Option('s', "hidestack", Required = false, HelpText = "Hides the stack banner.")]
    public bool HideStack {get; set;} = false;

    [Option('d', "dictsize", Required = false, HelpText = "Size of the words dictionary in bytes.")]
    public int DictSize {get; set;} = Config.MediumStack;

    [Option('p', "paramsize", Required = false, HelpText = "Size of the parameter stack in bytes.")]
    public int ParamSize {get; set;} = Config.SmallStack;

    [Option('r', "returnsize", Required = false, HelpText = "Size of the return stack in bytes.")]
    public int ReturnSize {get; set;} = Config.SmallStack;
}

static class Config {
    const int K                  = 1_024;
    public const int SmallStack  = 16    * K;
    public const int MediumStack = 256   * K;
    public const int LargeStack  = 1_024 * K;
}
