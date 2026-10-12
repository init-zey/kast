using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Kast.Core;
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
[MessagePackObject]
public class Spell
{
    /// <summary>起笔的绝对方向；<c>'\0'</c> 表示这一槽为空。</summary>
    [Key(0)]
    public char FirstDir='\0';
    /// <summary>首笔之后的各个转折，顺序即作画顺序。</summary>
    [Key(1)]
    public List<char> Angles;

    public Spell()
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

    /// <summary>
    /// 只取 <see cref="Angles"/> 拼成字符串，<b>不含</b> <see cref="FirstDir"/>——
    /// 词形本身只由相对转角构成，根旋转是每个槽各自选的，所以这里正好是"词"的规范形式。
    /// 词典登记、查表都以它作键。
    /// </summary>
    internal string AsString()
    {
        return new string(Angles.ToArray());
    }
    /// <summary>
    /// 按真实词逐个取出词形，拼接到 <see cref="Angles"/> 上（<b>不动</b> <see cref="FirstDir"/>）。
    /// 用于按"真实词序列"生成咒语，例如开局填入初始咒语、或 <c>display</c> 拼出对象名字。
    /// </summary>
    public void BuildFromRealWords(string[] realWords,GameTranslation translation)
    {
        foreach(var realWord in realWords)
        {
            Angles.AddRange(translation.GetGameWord(realWord));
        }
    }
}