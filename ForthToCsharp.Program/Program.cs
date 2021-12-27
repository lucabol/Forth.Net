using System.Text;
using Microsoft.CodeAnalysis.CSharp.Scripting;

using static System.Console;

try {
    var vm      = File.ReadAllText("../ForthToCsharp/vm.cs");
    var vmnew   = "var vm = new Vm(System.Console.In, System.Console.Out);";

    WriteLine("Please wait. Interpreting initial script ...");
    var script  = await CSharpScript.RunAsync(vm).ConfigureAwait(false);
    script  = await script.ContinueWithAsync(vmnew).ConfigureAwait(false);
    WriteLine("Done.");
    var newCode = "";
    var line = "";
    var debug = false;

    var input   = new StringBuilder();
    var output  = new StringBuilder();
    var tr      = new Translator(Console.In, output);

    while(true) {

        tr.output.Clear();

        line = ReadLine();
        if(line == null) break;
        line = line.Trim().ToLowerInvariant();

        if(line == "bye") break;
        if(line == "debug") { debug = !debug; continue;}

        using var reader = new StringReader(line); // TODO: Refactor Translator API to stateless funcs.
        tr.inputReader = reader;

        Translator.Translate(tr);
        newCode = tr.output.ToString();

        if(debug) WriteLine(newCode);

        script = await script.ContinueWithAsync(newCode).ConfigureAwait(false);
        SetCursorPosition(line.Length + 2, BufferHeight - 2);
        WriteLine("ok");
    }
} catch(Exception e) {
    WriteLine(e);
} finally {
    ResetColor();
}
