using System.Text;
using MessagePack;

namespace Kast.Core;

/// <summary>
/// 真实词 ↔ 游戏词形 的双向翻译表，也是玩家已解锁词条的存档载体。
/// </summary>
/// <remarks>
/// 两张字典必须互逆：<see cref="RegisterRealToGame"/> 是唯一的写入入口，它同时更新两边，
/// 因此不要直接改 <see cref="ToReal"/> / <see cref="ToGame"/>，否则查询会给出不一致的结果。
/// 同一个真实词重复登记会覆盖为新的词形（词形分配器 <c>Database.Gen</c> 在调用前已做幂等判断）。
/// <para>
/// 本类不依赖 Godot，可用 MessagePack 直接序列化进存档。
/// </para>
/// </remarks>
[MessagePackObject]
public class GameTranslation
{
    [Key(0)]
    public Dictionary<string,string> ToReal=new();
    [Key(1)]
    public Dictionary<string,string> ToGame=new();
    /// <summary>玩家持有的全部游戏词形（即预测与分词用的词条表来源）。</summary>
    [IgnoreMember]
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
    }

    /// <summary>玩家持有的全部游戏词形；与 <see cref="Tokens"/> 等价，保留给 UI 按"词条"语义取用。</summary>
    public IEnumerable<string> Entrys()
    {
        return ToReal.Keys;
    }
}