using System;
using System.Collections.Generic;
using Godot;
using Kast.Core;
using O=System.Func<Kast.Core.Context,object>;

namespace Kast.Godot;

/// <summary>
/// 动词词条：到 / 看 / 成为 / 显示 / 施放。
/// </summary>
/// <remarks>
/// 每个动词都用 <see cref="Alphabet{O}.AddTVerb"/> 登记，回调签名是
/// <c>(ctx, s, o)</c>——主语、宾语各是一个"取值函数"，<c>o</c> 为 null 表示这句没带宾语。
/// 这里的约定是：带宾语就作用于宾语，不带宾语就把自己的结果绑成 <c>.n</c>，
/// 让整句能作为名词继续参与上层句子（关系从句）。
/// </remarks>
public partial class KastAlphabet
{
    void AddVerbs()
    {
        AddGo();
        AddLook();
        AddBe();
        AddDisplay();
        AddCast();
    }

    /// <summary>到：把主语推向宾语；不带宾语时表达"主语的位置"。</summary>
    void AddGo()
    {
        AddTVerb(db.Gen("go",3), (ctx,s,o) =>
        {
            var subject=(NObject)s(ctx);
            if(subject==null) return;
            if(o!=null)
            {
                var target=o(ctx);
                subject.ApplyCentralImpulse((target.GetPosition()-subject.GlobalPosition).Normalized()*0.5f);
                subject.EnableAttractor();
            }
            else
            {
                BindPosition(ctx,subject);
            }
        });
    }

    /// <summary>
    /// 看：转向宾语（正被相机操纵时转而转相机）；不带宾语时表达"主语正看着的点"。
    /// </summary>
    void AddLook()
    {
        AddTVerb(db.Gen("look",3), (ctx,s,o) =>
        {
            var subject=(NObject)s(ctx);
            if(subject==null) return;
            if(o!=null)
            {
                var target=o(ctx);
                var camera=NGame.CameraOf(subject);
                if(camera!=null)
                {
                    var vec=subject.GlobalPosition-target.GetPosition();
                    camera.TargetYaw=(float)Math.Atan2(vec.X,vec.Z);
                }
                else
                {
                    subject.TurnTo(target.GetPosition());
                    subject.EnableAttractor();
                }
            }
            else
            {
                BindAimPoint(ctx,subject);
            }
        });
    }

    /// <summary>
    /// 成为：把主语换成宾语描述的类型或外观；不带宾语时表达"主语的类型"。
    /// </summary>
    /// <remarks>
    /// 宾语带 <c>#be</c> 旗标求值，让"天/地"这类词切到"改质量"的读法。
    /// </remarks>
    void AddBe()
    {
        AddTVerb(db.Gen("be",4), (ctx,s,o) =>
        {
            var subject=(NObject)s(ctx);
            if(subject==null) return;
            if(o!=null)
            {
                var value=o(ctx.WithFlag("#be"));
                if(value is NObjectType type)
                {
                    subject.SetType(type);
                }
                else if(value is Action<NObject> converter)
                {
                    converter(subject);
                }
            }
            else
            {
                BindType(ctx,subject);
            }
        });
    }

    /// <summary>显示：把主语按其"可显示名"逐词拼成词形，在主界面上画出来并淡出。</summary>
    void AddDisplay()
    {
        AddTVerb(db.Gen("display",4), (ctx,s,o) =>
        {
            if(s(ctx) is not NObjectType objType) return;

            var symbol=game.GetNode<NDisplaySpellSymbol>("%DisplaySpellSymbol");
            symbol.Spell.Angles.Clear();
            symbol.Spell.BuildFromRealWords(objType.GetDisplayRealName(),db.Translation);
            symbol.Modulate=Colors.White;
            symbol.Visible=true;
            symbol.QueueRedraw();
            objType.Object?.EnableAttractor();
        });
    }

    /// <summary>
    /// 施放：让施法者放一个咒语槽。不带数词就放当前选中的槽，带数词放对应槽（1..9）。
    /// </summary>
    /// <remarks>
    /// 宾语不是 1..9 的数词就什么都不做——二进制写法很容易出现 0 或超出九槽的值。
    /// </remarks>
    void AddCast()
    {
        AddTVerb(db.Gen("cast",4),(ctx,s,o) =>
        {
            var caster=(s(ctx) as NObject)?.Type as NCaster;
            if(caster==null) return;   // 只有施法者能放咒语

            var targets=ctx.GetItem(".targets") as List<NObject> ?? [];
            if(o==null)
            {
                caster.CastSlot(game.SpellSlots.Slot,targets);
                return;
            }
            if(o(ctx) is not int number||number<1||number>9) return;
            caster.CastSlot(number-1,targets);
        });
    }

    // —— 把结果绑成名词（.n），供关系从句继续使用 ——

    /// <summary>让整句表达"主语的位置"。</summary>
    void BindPosition(Context ctx,NObject subject)
        => ctx.Set(".n",(O)(_ => subject.GetPosition()));

    /// <summary>让整句表达"主语正看着的点"：被相机操纵时用鼠标射线，否则用自身朝向射线。</summary>
    void BindAimPoint(Context ctx,NObject subject)
        => ctx.Set(".n",(O)(_ =>
        {
            var camera=NGame.CameraOf(subject);
            return camera!=null?camera.AimPoint:subject.GetAimPoint();
        }));

    /// <summary>让整句表达"主语的类型"。</summary>
    void BindType(Context ctx,NObject subject)
        => ctx.Set(".n",(O)(_ => subject.Type));
}
