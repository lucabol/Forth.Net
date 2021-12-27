using System.Text;
using Microsoft.CodeAnalysis.CSharp.Scripting;

using static System.Console;

try {

    CancelKeyPress += delegate {
        ResetColor();
    };

    var vm      = File.ReadAllText("../ForthToCsharp/vm.cs");
    var vmnew   = "var vm = new Vm(System.Console.In, System.Console.Out);";

    WriteLine("Initializing. Please wait ...");
    var script = await CSharpScript.RunAsync(vm).ConfigureAwait(false);
    script     = await script.ContinueWithAsync(vmnew).ConfigureAwait(false);
    WriteLine("Done. Say 'bye' to exit.");

    var newCode = "";
    var line    = "";
    var debug   = false;

    var input   = new StringBuilder();
    var output  = new StringBuilder();
    var tr      = new Translator(Console.In, output);

    System.ReadLine.HistoryEnabled = true;

    while(true) {

        try {
            tr.output.Clear();

            line = System.ReadLine.Read("");
            if(line == null) break;
            line = line.Trim().ToLowerInvariant();

            if(line == "bye") break;
            if(line == "debug") { debug = !debug; continue;}

            using var reader = new StringReader(line); // TODO: Refactor Translator API to stateless funcs.
            tr.inputReader   = reader;

            Translator.Translate(tr);
            newCode = tr.output.ToString();

            if(debug) WriteLine($"\n{newCode}");

            script = await script.ContinueWithAsync(newCode).ConfigureAwait(false);

            if(!debug) SetCursorPosition(line.Length + 5, BufferHeight - 2);
            ColorLine(ConsoleColor.DarkGreen, "ok");

        } catch(Exception e) {
            ColorLine(ConsoleColor.Red, e.ToString());
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
