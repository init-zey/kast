using System.Linq;
using MessagePack;

namespace Kast.Godot;

/// <summary>
/// 一条玩家画出来的咒语（一串笔画），也是九个咒语槽里存的内容，随存档用 MessagePack 序列化。
/// </summary>
/// <remarks>
/// 字符约定与 <see cref="GridHelper"/> / <c>Tokenizer</c> 一致：<see cref="FirstDir"/> 是起笔的
/// <b>绝对</b>方向，<see cref="Angles"/> 里后续每一笔都是相对上一笔的<b>转折</b>。
/// 之所以把首笔单独拎出来，是因为起笔方向随玩家画的方向自由变化，而词形本身只约束
/// 相对转角——首笔即"根旋转"（见 <c>NSpellEntry.Init</c> 的 <c>rootRotation</c>）。
/// <para>
/// <see cref="FirstDir"/> 为空字符 <c>'\0'</c> 表示这一槽还是空的（见 <see cref="HasFirstDir"/>）；
/// <c>' '</c> 是另一回事：那是"词典里自己慢慢转"的展示用值，见 <c>NDictionary.Open</c>。
/// </para>
/// </remarks>
public class FullSpell : Spell
{
    /// <summary>起笔的绝对方向；<c>'\0'</c> 表示整个符号都为相对方向，在NDictionary中使用。</summary>
    public char FirstDir='\0';

    public FullSpell()
    {
        FirstDir='\0';
        Angles=new();
    }

    /// <summary>是否已经起笔（槽非空）。</summary>
    [IgnoreMember]
    public bool HasFirstDir => FirstDir != '\0';

    /// <summary>最后一笔的字符：有转折时是最后一个转折，否则就是起笔方向。</summary>
    public char Last()
    {
        return Angles.Count>0?Angles.Last():FirstDir;
    }

    /// <summary>追加一笔。首笔写进 <see cref="FirstDir"/>，之后的写进 <see cref="Angles"/>。</summary>
    internal void Add(char finaldir)
    {
        if(FirstDir=='\0')
        {
            FirstDir=finaldir;
        }
        else
        {
            Angles.Add(finaldir);
        }
    }

    /// <summary>撤销最后一笔；退掉首笔后本槽回到空状态。</summary>
    internal void RemoveLast()
    {
        if(Angles.Count>0)
        {
            Angles.RemoveAt(Angles.Count-1);
        }
        else
        {
            FirstDir='\0';
        }
    }
    /// <summary>展开成完整笔画序列 <c>[FirstDir, ...Angles]</c>，供解析与绘制使用。</summary>
    public char[] ToArray()
    {
        return [FirstDir,..Angles];
    }
    /// <summary>清空本槽，回到未起笔状态。</summary>
    public void Clear()
    {
        FirstDir='\0';
        Angles.Clear();
    }
}
