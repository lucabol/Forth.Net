using System.Text;
using Microsoft.CodeAnalysis.CSharp.Scripting;

using static System.Console;

try {

    CancelKeyPress += delegate {
        ResetColor();
    };

    var vmCode  = File.ReadAllText("../ForthToCsharp/vm.cs");
    var vmnew   = "var vm = new Vm(System.Console.In, System.Console.Out);";

    Write("Initializing. Please wait ...");
    var script = await CSharpScript.RunAsync(vmCode).ConfigureAwait(false);
    script     = await script.ContinueWithAsync(vmnew).ConfigureAwait(false);
    WriteLine(" done.");
    WriteLine("Say 'bye' to exit. No output means all good.");

    var newCode   = "";
    var line      = "";
    var lowerLine = "";
    var debug     = false;

    var input   = new StringBuilder();
    var output  = new StringBuilder();
    var tr      = new Translator(Console.In, output);

    System.ReadLine.HistoryEnabled = true;

    while(true) {

        try {
            tr.output.Clear();

            line = System.ReadLine.Read("");
            if(line == null) break;
            lowerLine = line.Trim().ToLowerInvariant();

            if(lowerLine == "bye") break;
            if(lowerLine == "debug") { debug = !debug; continue;}

            using var reader = new StringReader(line); // TODO: Refactor Translator API to stateless funcs.
            tr.inputReader   = reader;

            Translator.Translate(tr);
            newCode = tr.output.ToString();

            if(debug) WriteLine($"\n{newCode}");

            script = await script.ContinueWithAsync(newCode).ConfigureAwait(false);
        } catch(Exception e) {
            ColorLine(ConsoleColor.Red, e.ToString());
            script = await script.ContinueWithAsync("vm.reset()").ConfigureAwait(false);
        }
    }
} finally {
    ResetColor();
}

void ColorLine(ConsoleColor color, string s) {
    var backupcolor = ForegroundColor;
    ForegroundColor = color;
    WriteLine(s);
    ForegroundColor = backupcolor;
}
