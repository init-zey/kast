using System;
using Godot;
using O=System.Func<Kast.Core.Context,object>;

namespace Kast.Godot;

/// <summary>
/// 名词词条：我 / 你 / 天 / 地。
/// </summary>
/// <remarks>
/// 每个词的值都是一个"上下文变换"：执行时把语义负载绑成 <c>.n</c>，
/// 供上层规则（<c>Vt -&gt; Vt N</c>、<c>S -&gt; N Vt</c>）取出对象。
/// </remarks>
public partial class KastAlphabet
{
    void AddNouns()
    {
        AddNoun(db.Gen("i",2), ctx => ctx.GetItem(".me") as NObject);   // 我
        AddNoun(db.Gen("you",2), ctx => ctx.GetItem(".you") as NObject); // 你

        // 天：既是名词（天空 / 向上 / 减轻），又能当二进制位 0。
        // 两种读法靠旗标区分，词条本身不必知道自己在被当名词还是当位用：
        // #bit 走位义；否则看 #be——有它是"成为"的宾语（改质量），没它是普通名词（取相对位置）。
        //
        // 地取"天"的镜像词形（Flip 是 q↔e、a↔d 的对合）：两个字在语义上一对相反
        // （上/下、轻/重、0/1），画面上也就成一对手性相反的图形。
        // PairGen 会同时把两个词形登记进翻译表与前缀树，缺一不可。
        var (sky,earth)=db.PairGen("sky/air", "earth/solid", 4);
        RegisterBitWord(sky,(ctx) =>
        {
            if(ctx.GetFlag("#bit")) return 0;
            if(ctx.GetItem(".s") is O subject)
            {
                if(ctx.GetFlag("#be"))
                {
                    return DensityChanger(-1f);
                }
                return subject(ctx).GetPosition()+Vector3.Up*10f;
            }
            return null;
        });

        // 地：与"天"相对，名词义为大地 / 向下 / 加重，位义为 1。
        RegisterBitWord(earth,(ctx) =>
        {
            if(ctx.GetFlag("#bit")) return 1;
            if(ctx.GetItem(".s") is O subject)
            {
                if(ctx.GetFlag("#be"))
                {
                    return DensityChanger(1f);
                }
                return subject(ctx).GetPosition()-Vector3.Up*10f;
            }
            return null;
        });
    }

    /// <summary>
    /// 登记一个"既能当名词又能当二进制位"的词：同一个值分别挂到 N / Num / Bit 三个范畴上。
    /// </summary>
    /// <remarks>
    /// 位词条要直接登记成 Num（而不是只登记 Bit），因为 CNF 文法没有一元规则，
    /// 表达不了 <c>Num -&gt; Bit</c>：单个位必须自己就是数词。
    /// </remarks>
    void RegisterBitWord(string token,O value)
    {
        AddNoun(token,value);       // N   -> 词（名词语义）
        AddWord(token,"Num",value); // Num -> 词（单独一个位也是一个数）
        AddWord(token,"Bit",value); // Bit -> 词（拼接时的右操作数）
    }

    /// <summary>
    /// "成为"的宾语负载：返回一个逐帧改密度的委托，由 <c>be</c> 动词交给目标执行。
    /// <paramref name="sign"/> 为正是加重（地），为负是减轻（天）。
    /// </summary>
    O DensityChanger(float sign) => (ctx) =>
        (Action<NObject>)(target =>
        {
            target.Type.Density=target.Type.Density+sign*(float)game.GetPhysicsProcessDeltaTime();
            target.UpdateMass();
        });

    /// <summary>逐帧缩放目标（"变大 / 变小"），并同步由尺寸决定的质量。</summary>
    /// <remarks>
    /// 待接词条：目前还没有词条指向它，见 <c>docs/demo-island-feasibility.md</c> 第 5 节第 3 步。
    /// 已知限制：只改 <see cref="NObjectType.Scale"/>，<b>不会</b>同步碰撞体外形（碰撞体是在
    /// <see cref="NObject.SetType"/> 里 reparent 过来的那批），缩得很小会出现"看得见、碰不着"。
    /// 接词条之前需要先把缩放应用到碰撞形状上。
    /// </remarks>
    O ScaleChanger(float sign) => (ctx) =>
        (Action<NObject>)(target =>
        {
            target.Type.Scale=target.Type.Scale*(1+sign*(float)game.GetPhysicsProcessDeltaTime());
            target.UpdateMass();
        });
}
