using Xunit;
using System.IO;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using System.Text;

public class TranslatorTests
{
    [Theory]
    [InlineData("10 20 +", 30)]
    [InlineData("1 dup dup drop drop", 1)]
    [InlineData("here 3 2 + , @\n10 +", 15)]
    [InlineData("here bl c, c@", ' ')]
    [InlineData("create bob 20 ,\n bob @", 20)]
    [InlineData("create bob 10 allot\ncreate rob 20 ,\n rob @", 20)]
    [InlineData(": my+ 1 + ; \n 20 my+", 21)]
    [InlineData(": uarray create allot ;\n80 uarray ar\n 100 10 ar + ! 10 ar + @", 100)]
    [InlineData(": uarray create allot does> + ;\n80 uarray ar\n 1000 10 ar ! 10 ar @", 1000)]
    [InlineData("1 ( afafdaf ) 1 + \n 2 + \\ fadfafdaf", 4)]
    async public void Run(string forth, long result) {
        Translator tr = new(new StringBuilder());
        Translator.TranslateReader(new StringReader(forth), tr);
        var csharp = Translator.ToCSharp("Run", tr.output.ToString());
        const string runExpr = "__GEN.TestRun()";

        var vmCode = Translator.LoadVmCode();

        var state  = await CSharpScript.RunAsync(vmCode + csharp + runExpr);
        //state      = await state.ContinueWithAsync(csharp);
        //var res    = await state.ContinueWithAsync(runExpr);

        Assert.Equal(result, state.ReturnValue);
    }
}
