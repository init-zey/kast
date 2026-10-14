using System.Collections.Generic;
using Godot;
using Kast.Godot;
using Kast.Core;
using System;

/// <summary>
/// 施法者：只持有一份自己的咒语槽数据。移动、动画、选中等控制逻辑都在 PlayerCamera 里。
/// </summary>
public partial class NCaster : NObjectType3D
{
    /// <summary>九个快捷槽里的咒语输入。</summary>
    public Spell[] Spells;

    [Export] public AnimationTree AnimationTree;
    /// <summary><see cref="CheckSpell"/> 的返回值：输入的串读不出完整咒语，槽位没有改动。</summary>
    public const int SpellInputRejected=-1;
    /// <summary><see cref="CheckSpell"/> 的返回值：空输入（这次不施法）。</summary>
    public const int SpellInputCancelled=-2;
    /// <summary><see cref="CheckSpell"/> 的返回值：输入的串能读成完整咒语。</summary>
    public const int SpellInputAccept=0;
    
    public override void NodeReady()
    {
        Object.GravityScale = 0f;
        // 只锁翻转/侧倒，保留绕 Y 的转向（PlayerCamera 用角冲量驱动朝向）
        // Object.AxisLockAngularX = true;
        // Object.AxisLockAngularZ = true;
    }

    /// <summary>
    /// 把画出来的串读成候补咒语：整串必须完整成词，末尾还留着没画完的残片就算失败。
    /// 完整性由 <see cref="Alphabet{O}.Parse(Span{char})"/> 内部保证——它先做逐段最长匹配的
    /// 前缀分词，只有覆盖满整串时才返回解析结果（残缺时返回空集）。
    /// 与 SpellInput 顶部的实时翻译（<see cref="NGame.Transcript"/>）共用同一套分词规则，
    /// 所以"界面上显示成词"与"能填进槽"永远一致。
    /// </summary>
    public HashSet<Word> ReadSpells(Spell input)
    {
        if(input==null)
        {
            return [];
        }
        var alphabet=NGame.Instance.Alphabet;
        return alphabet.Parse(input.Angles.ToArray());
    }
    /// <summary>施放指定槽（0..8）。越界、空槽或解析不出咒语时什么都不做，返回是否真的施放了。</summary>
    public bool CastSlot(int index,List<NObject> targets=null)
    {
        if(index<0||index>=Spells.Length) return false;
        var spells=ReadSpells(Spells[index]);
        if(spells.Count==0) return false;
        return NGame.Instance.CastWords([..spells],Object,targets);
    }
    /// <summary>清空指定槽。</summary>
    public bool ClearSlot(int index)
    {
        var i=index;
        Spells[i].Clear();
        return true;
    }
    /// <summary>
    /// 判断画出来的串能否填进槽：能读成完整咒语返回 <see cref="SpellInputAccept"/>；
    /// 读不出完整咒语返回 <see cref="SpellInputRejected"/>（槽位不变）；
    /// 空输入返回 <see cref="SpellInputCancelled"/>。本方法不改动槽位数据，由调用方按返回值决定。
    /// </summary>
    public int CheckSpell(Spell input)
    {
        if (input==null)
        {
            return SpellInputCancelled;
        }
        if(ReadSpells(input).Count==0)
        {
            return SpellInputRejected;
        }
        return SpellInputAccept;
    }
}
