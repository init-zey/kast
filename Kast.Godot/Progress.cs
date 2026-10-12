using Kast.Core;
using MessagePack;

namespace Kast.Godot;

/// <summary>
/// 存档内容：一局游戏的种子、玩家已解锁的翻译表、以及当前所在关卡。用 MessagePack 序列化。
/// </summary>
/// <remarks>
/// 翻译表从旧版的 <c>string[] UnlockedSymbols</c> 改成了完整的 <see cref="GameTranslation"/>：
/// 现在存档自己就带着"词形 → 真实词"的双向映射，读档后不必再用种子重算一遍词形分配，
/// 也就不会因为算法调整而让老存档的词形对不上。种子仍保留，供关卡内容等确定性随机使用。
/// </remarks>
[MessagePackObject]
public class Progress
{
    /// <summary>本局的随机种子。</summary>
    [Key(0)]
    public int Seed;
    /// <summary>玩家已解锁的词条与词形映射。</summary>
    [Key(1)]
    public GameTranslation PlayerTranslation;
    /// <summary>当前关卡 ID，对应 <c>Levels/{ID}.tscn</c>。</summary>
    [Key(2)]
    public string LevelID;
}