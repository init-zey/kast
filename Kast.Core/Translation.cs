using System.Text;
using MessagePack;
using MessagePack.Formatters;

namespace Kast.Core;

/// <summary>
/// 真实词 ↔ 游戏词形 的双向翻译表，也是玩家已解锁词条的存档载体。
/// </summary>
/// <remarks>
/// 两张字典互为镜像，但<b>不再强制互逆</b>：<see cref="RegisterRealToGame"/> / <see cref="RemoveReal"/>
/// / <see cref="Copy"/> 都会同时维护两边，直接改 <see cref="ToReal"/> / <see cref="ToGame"/> 则可能让两者
/// 失去同步，请优先走这三个方法。
/// <para>
/// <see cref="Tokenizer"/> 随词表变化就地重编译，所以 <c>NGame.PlayerTokenzier</c> 直接取这里的实例，
/// 不必在别处再 <c>CompileTokens</c> 一次。该字段不参与序列化（见 <see cref="GameTranslationFormatter"/>）。
/// </para>
/// <para>
/// 序列化只写 <see cref="ToGame"/> 一张表，<see cref="ToReal"/> 与分词器都在反序列化后重建，
/// 因此存档里不会出现两份可能互相矛盾的数据。
/// </para>
/// </remarks>
[MessagePackFormatter(typeof(GameTranslationFormatter))]
public class GameTranslation
{
    public Dictionary<string,string> ToReal=new();
    public Dictionary<string,string> ToGame=new();
    /// <summary>按当前词表编译好的分词器，随 <see cref="RegisterRealToGame"/> 等写操作自动重建。</summary>
    public Tokenizer Tokenizer=new();
    /// <summary>玩家持有的全部游戏词形（即预测与分词用的词条表来源）。</summary>
    public IEnumerable<string> Tokens => ToReal.Keys;

    /// <summary>由真实词查游戏词形；没登记过返回 null。</summary>
    public string? GetGameWord(string realWord)
    {
        return ToGame.TryGetValue(realWord,out var game)?game:null;
    }

    /// <summary>由游戏词形查真实词；没登记过返回 null。</summary>
    public string? GetRealWord(string gameWord)
    {
        return ToReal.TryGetValue(gameWord,out var real)?real:null;
    }
    /// <summary>导出成 CSV 文本，供调试时人工查看词表。</summary>
    public string AsText()
    {
        var text=new StringBuilder("Real Word,Game Word");
        foreach(var kvp in ToGame)
        {
            text.Append('\n').Append(kvp.Key).Append(',').Append(kvp.Value);
        }
        return text.ToString();
    }

    /// <summary>登记一条双向映射，同时写入 <see cref="ToReal"/> 与 <see cref="ToGame"/>，保证两表互逆。</summary>
    public void RegisterRealToGame(string real, string token)
    {
        ToReal[token]=real;
        ToGame[real]=token;
        Tokenizer.CompileTokens(Tokens);
    }

    /// <summary>
    /// 删除一个真实词及其词形（词典里删掉自造词时用），两张表与分词器一并更新。
    /// </summary>
    /// <exception cref="KeyNotFoundException">该真实词没有登记过。</exception>
    public void RemoveReal(string real)
    {
        ToReal.Remove(ToGame[real]);
        ToGame.Remove(real);
        Tokenizer.CompileTokens(Tokens);
    }

    /// <summary>整体覆盖为另一份翻译表的内容（词典编辑期间先在副本上改，确认后才写回存档）。</summary>
    public void Copy(GameTranslation other)
    {
        ToReal.Clear();
        ToGame.Clear();
        foreach(var pair in other.ToGame)
        {
            ToGame[pair.Key]=pair.Value;
            ToReal[pair.Value]=pair.Key;
        }
        Tokenizer.CompileTokens(Tokens);
    }
}

/// <summary>
/// <see cref="GameTranslation"/> 的 MessagePack 格式化器：只序列化 <see cref="GameTranslation.ToGame"/>
/// 一张表，反序列化时重建反向表与分词器。
/// </summary>
/// <remarks>
/// 沿用 <see cref="DictionaryFormatter{TKey,TValue}"/> 的线格式，因此存档与"直接序列化字典"时代保持兼容，
/// 同时避免把 <see cref="GameTranslation.Tokens"/> 这类派生数据也写进文件。
/// </remarks>
internal class GameTranslationFormatter : IMessagePackFormatter<GameTranslation?>
{
    DictionaryFormatter<string,string> _formatter=new();
    public GameTranslation? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        var toGameDict=_formatter.Deserialize(ref reader,options);
        var result=new GameTranslation();
        result.ToGame=toGameDict!;

        foreach(var pair in result.ToGame)
        {
            result.ToReal[pair.Value]=pair.Key;
        }
        result.Tokenizer.CompileTokens(result.Tokens);
        return result;
    }

    public void Serialize(ref MessagePackWriter writer, GameTranslation? value, MessagePackSerializerOptions options)
    {
        _formatter.Serialize(ref writer, value!.ToGame, options);
    }
}