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
        var (i,you) = db.PairGen("i","you",2);
        AddNoun(i, ctx => ctx.GetItem(".me") as NObject);   // 我
        AddNoun(you, ctx => ctx.GetItem(".you") as NObject); // 你
        // 天/上/轻/0
        var (sky,earth)=db.PairGen("0", "1", 4);
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

        // 地/下/重/1
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

        var (big,small) = db.PairGen("big","small",4);
        AddNoun(big, (ctx) =>
        {
            if(ctx.GetItem(".s") is O subject)
            {
                if(ctx.GetFlag("#be"))
                {
                    return ScaleChanger(1f);
                }
            }
            return null;
        });
        AddNoun(small, (ctx) =>
        {
            if(ctx.GetItem(".s") is O subject)
            {
                if(ctx.GetFlag("#be"))
                {
                    return ScaleChanger(-1f);
                }
            }
            return null;
        });

        db.PairGen("plant","animal",4);
        db.Gen("road",5);
        db.Gen("more",5);
        db.PairGen("leg","arm",5);
        db.PairGen("sea","land",5);
    }

    /// <summary>
    /// 登记一个"既能当名词又能当二进制位"的词：同一个值挂到 N / Num / Bit / Factor / Term / Expr 上。
    /// 仅用2次。
    /// </summary>
    /// <remarks>
    /// Factor / Term / Expr 让单个位也能直接参与四则运算；多位数由 <c>X -&gt; Num Bit</c>
    /// 提升（见 <c>KastAlphabet.Numeral.cs</c>），因为 CNF 文法没有一元规则，
    /// 不能事后把 Num 直接提升成 Factor。
    /// </remarks>
    void RegisterBitWord(string token,O value)
    {
        AddNoun(token,value);          // N      -> 词（名词语义）
        AddWord(token,"Num",value);    // Num    -> 词（单独一个位也是一个数）
        AddWord(token,"Bit",value);    // Bit    -> 词（拼接时的右操作数）
        AddWord(token,"Factor",value); // Factor -> 词（乘除层的原子）
        AddWord(token,"Term",value);   // Term   -> 词（乘除层的值）
        AddWord(token,"Expr",value);   // Expr   -> 词（加减层的值）
    }

    Action<NObject> DensityChanger(float sign) => 
        target =>
        {
            target.Type.Density=Math.Max(0,target.Type.Density+sign*(float)game.GetPhysicsProcessDeltaTime()*2);
            target.UpdateMass();
            target.EnableAttractor();
        };

    Action<NObject> ScaleChanger(float sign) =>
        target =>
        {
            var newScale=target.Scale * (1 + sign * (float)game.GetPhysicsProcessDeltaTime()*0.5f);
            if(newScale.X<0) return;
            target.Scale = newScale;
            target.UpdateMass();
            target.EnableAttractor();
        };
}
