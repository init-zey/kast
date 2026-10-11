using System.Collections.Generic;
using Godot;
using Kast.Godot;
using Kast.Core;

/// <summary>
/// 施法者：只持有一份自己的咒语槽数据。移动、动画、选中等控制逻辑都在 PlayerCamera 里。
/// </summary>
public partial class NCaster : NObjectType3D
{
    /// <summary>九个快捷槽里的咒语输入。</summary>
    public Spell[] Spells=
    [
        new(),
        new(),
        new(),
        new(),
        new(),
        new(),
        new(),
        new(),
        new()
    ];

    [Export] public AnimationTree AnimationTree;
    /// <summary><see cref="CheckSpellInput"/> 的返回值：输入的串读不出完整咒语，槽位没有改动。</summary>
    public const int SpellInputRejected=-1;
    /// <summary><see cref="CheckSpellInput"/> 的返回值：空输入（这次不施法）。</summary>
    public const int SpellInputCancelled=-2;
    
    public override void NodeReady()
    {
        Object.GravityScale = 0f;
        Object.AxisLockAngularX = true;
        Object.AxisLockAngularZ = true;
    }

    /// <summary>
    /// 把画出来的串读成候补咒语：整串必须完整成词，末尾还留着没画完的残片就算失败。
    /// 与 SpellInput 顶部的实时翻译（<see cref="NGame.Transcript"/>）共用
    /// <see cref="Alphabet{O}.TokenizePrefix"/> 这一套分词规则（逐段最长匹配），
    /// 所以"界面上显示成词"与"能填进槽"永远一致。
    /// </summary>
    public HashSet<Word> ReadSpells(Spell input)
    {
        if(input==null)
        {
            return [];
        }
        var alphabet=NGame.Instance.ProgressAlphabet;
        var tokens=alphabet.TokenizePrefix(new string(input.Angles.ToArray()),out var consumed);
        return consumed==input.Angles.Count?alphabet.Parse(tokens):[];
    }
    /// <summary>施放指定槽（0..8）。越界、空槽或解析不出咒语时什么都不做，返回是否真的施放了。</summary>
    public bool CastSlot(int index,List<NObject> targets=null)
    {
        if(index<0||index>=Spells.Length) return false;
        var spells=ReadSpells(Spells[index]);
        if(spells.Count==0) return false;
        return NGame.Instance.CastWords([..spells],Object,targets);
    }
    public bool ClearSlot(int index)
    {
        var i=index;
        Spells[i].Clear();
        return true;
    }
    /// <summary>
    /// 返回写入的槽位下标 0..8；读不出完整咒语返回 <see cref="SpellInputRejected"/>（槽位不变）；
    /// 空输入返回 <see cref="SpellInputCancelled"/>（按现有语义它会清掉当前槽）。
    /// </summary>
    public int CheckSpellInput(Spell input,int idx)
    {
        if (input==null)
        {
            // 空输入 = 这次不施法：清掉当前槽。注意这不是空操作，当前槽里存的东西会没
            ClearSlot(idx);
            return SpellInputCancelled;
        }
        if(ReadSpells(input).Count==0)
        {
            return SpellInputRejected;
        }
        return idx;
    }
}
