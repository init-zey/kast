using Kast.Core;

namespace Kast.Test;

internal class Entry
{
    
    private static void Main(string[] args)
    {
        var code=TokenizerTests.Run();

        var alphabet=Alphabet<string>.Load();
        alphabet.AddNoun("你","[你]");
        alphabet.AddNoun("我","[我]");
        alphabet.AddNoun("它","[它]");
        alphabet.AddIVerb("看",(Context ctx,string s) =>
        {
            Console.WriteLine($"{s} 看");
        });
        alphabet.CompileTokens();
        var spells=alphabet.Parse(alphabet.Tokenize("我看"));
        spells.First().Eval(alphabet);

        Environment.ExitCode=code;
    }
}