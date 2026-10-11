using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Kast.Core;

namespace Kast.Godot;

public partial class NGame : Node3D
{
    public static NGame Instance;
    Alphabet<Func<Context,object>> FullAlphabet;
    public Alphabet<Func<Context,object>> ProgressAlphabet;
    /// <summary>cast 递归施法的深度上限，防止槽里的咒语互相施放导致无限递归。</summary>
    const int MaxCastDepth=8;
    int castDepth;
    public Database DB;
    /// <summary>当前正在操纵某个对象的相机；HUD 与"当前施法者"都从这里取。</summary>
    public PlayerCamera ActiveCamera;
    [Export] ClientWorld ClientWorld;
    [Export] GpuParticles3D Particles;
    Label SpellstateLabel;
    public void LoadProgress(Progress pg)
    {
        Instance=this;
        TranslationServer.SetLocale(SaveSystem.LoadSettings().Locale);
        DB=new(pg.Seed);
        FullAlphabet=Alphabet<Func<Context,object>>.Load();
        FullAlphabet.Grammar.AddRule("N -> Adv S",(adv, sentence) =>
        {
            return (Func<Context,Context>)sentence;
        });
        FullAlphabet.AddWord(DB.Gen("that",5),"Adv",typeof(Vector3));
        FullAlphabet.AddNoun(DB.Gen("i",2),(ctx)=>ctx.GetItem(".me") as NObject); // 我
        FullAlphabet.AddNoun(DB.Gen("you",2),(ctx)=> ctx.GetItem(".you") as NObject); // 你
        FullAlphabet.AddNoun(DB.Gen("sky/air",4), (ctx) =>{
            var vec=Vector3.Up*10f;
            if(ctx.GetItem(".s") is Func<Context,object> snoun && snoun!=null)
            {
                if(ctx.GetFlag("#be"))
                {
                    return (NObject obj)=>obj.SetMass(obj.Mass-(float)GetPhysicsProcessDeltaTime());
                }
                return snoun(ctx).GetPosition()+vec;
            }
            return null;
        }); // 天
        FullAlphabet.AddNoun(DB.Gen("earth/solid",4), (ctx) =>{
            var vec=Vector3.Up*10f;
            if(ctx.GetItem(".s") is Func<Context,object> snoun && snoun!=null)
            {
                if(ctx.GetFlag("#be"))
                {
                    return (NObject obj)=>obj.SetMass(obj.Mass+(float)GetPhysicsProcessDeltaTime());
                }
                return snoun(ctx).GetPosition()-vec;
            }
            return null;
        }); // 地
        FullAlphabet.AddTVerb(DB.Gen("go",3), (ctx,s,o)=>{
            var sobj=(NObject)s(ctx);
            if(o!=null)
            {
                var oin=o(ctx);
                sobj.ApplyCentralImpulse((o(ctx).GetPosition()-sobj.GlobalPosition).Normalized()*0.5f);
                sobj.EnableAttractor();
            }
            else
            {
                ctx.Set(".n",(Func<Context,object>)((Context ctx) =>
                {
                    return sobj.GetPosition();
                }));
            }
        }); // 到
        FullAlphabet.AddTVerb(DB.Gen("look",3), (ctx,s,o)=>
        {
            var sobj=(NObject)s(ctx);
            if(o!=null)
            {
                var oin=o(ctx);
                var camera=CameraOf(sobj);
                if(camera != null)
                {
                    var vec=sobj.GlobalPosition-oin.GetPosition();
                    camera.TargetYaw= (float)Math.Atan2(vec.X,vec.Z);
                }
                else
                {
                    sobj.TurnTo(oin.GetPosition());
                    sobj.EnableAttractor();
                }
            }
            else
            {
                ctx.Set(".n", (Func<Context,object>)((Context ctx) =>
                {
                    var camera=CameraOf(sobj);
                    return camera!=null?camera.AimPoint:sobj.GetAimPoint();
                }));
            }
        }); // 看
        FullAlphabet.AddTVerb(DB.Gen("be",4), (ctx,s,o)=>
        {
            var sobj=(NObject)s(ctx);
            if(o!=null)
            {
                var oobj=o(ctx.WithFlag("#be"));
                if (oobj is string typeid)
                {
                    sobj.SetType(typeid);
                }
                else if (oobj is Action<NObject> converter)
                {
                    converter(sobj);
                }
            }
            else
            {
                ctx.Set(".n", (Func<Context,object>)((Context ctx) =>
                {
                    return sobj.TypeID;
                }));
            }
        }); // 成为
        FullAlphabet.AddTVerb(DB.Gen("display",4), (ctx,s,o)=>
        {
            var sval=s(ctx);
            var str=sval.ToString();
            GetNode<Label>("%DisplayLabel").Text=str;
        }); // 显示
        FullAlphabet.AddTVerb(DB.Gen("cast",4),(ctx,s,o)=>
        {
            var bywho=s(ctx) as NObject;
            if(bywho==null) return;
            var caster=bywho.Type as NCaster;
            if(caster==null) return;   // 只有施法者能放咒语
            var targets=ctx.GetItem(".targets") as List<NObject> ?? [];
            if(o==null)
            {
                // 不带数词就是释放当前选中的槽
                caster.CastCurrent(targets);
                return;
            }
            if(o(ctx) is not int number)
            {
                return;   // 宾语不是数词（例如误用了普通名词），不施法
            }
            caster.CastSlot(number-1,targets);
        }); // 施放
        // 数词作宾语：Num 的负载本身就是"取出这个数字"的取值函数（不像 N 的负载是上下文变换），
        // 所以这里直接把负载绑成 .o，及物动词便能用 o(ctx) 取出数字，语义与 Vt -> Vt N 一致。
        FullAlphabet.Grammar.AddRule("Vt -> Vt Num",(verb, num) => (Func<Context,Context>)((Context ctx) =>
        {
            var inner=new Context(ctx);
            inner.Add(".o",num);
            return ((Func<Context,Context>)verb)(inner);
        }));
        AddNumerals();
        FullAlphabet.CompileTokens();
        // ProgressAlphabet=FullAlphabet.Derive(pg.UnlockedSymbols);
        ProgressAlphabet=new Alphabet<Func<Context, object>>(FullAlphabet);//FIXME:DEBUG
        File.WriteAllText("words.csv",DB.AsText());
    }

    /// <summary>
    /// 登记 1..9 的数词：每个数字是一个由 <see cref="Database.Gen"/> 生成的程序词（可用手势画出），
    /// 词法范畴为 Num，语义负载是"取出这个数字"的取值函数，供 cast 这类及物动词当宾语。
    /// </summary>
    void AddNumerals()
    {
        for(var number=1;number<=9;number++)
        {
            var token=DB.Gen(number.ToString());
            var value=number;   // 闭包捕获：不能直接用循环变量
            FullAlphabet.AddWord(token,"Num",(Func<Context,object>)(ctx=>value));
        }
    }
    public void SaveProgress(Progress pg)
    {
        pg.UnlockedSymbols=ProgressAlphabet.GetKeys().ToArray();
        pg.Seed=DB.GetSeed();
    }

    /// <summary>相机正操纵着这个对象时返回相机，否则 null。</summary>
    public static PlayerCamera CameraOf(NObject obj)
    {
        var camera=Instance?.ActiveCamera;
        return camera!=null&&camera.Player==obj?camera:null;
    }

    /// <summary>当前被操纵对象上挂的施法者，没有就是 null。</summary>
    public static NCaster ActiveCaster => Instance?.ActiveCamera?.Player?.Type as NCaster;

    /// <summary>更新 HUD 上的施法状态文字。</summary>
    public void SetSpellState(string state)
    {
        SpellstateLabel.Text=state;
    }

    /// <summary>
    /// 把输入切成最左最长匹配的词序列。与 <see cref="Alphabet{O}.Tokenize"/> 的区别是后者
    /// 遇到不完整输入会整体返回 null，这里只保留能完整成词的部分，末尾没写完的残片直接丢掉。
    /// </summary>
    public static List<string> SplitTokens(string input)
    {
        var result=new List<string>();
        if(string.IsNullOrEmpty(input))
        {
            return result;
        }
        var tokens=Instance.ProgressAlphabet.Tokens;
        var rest=input;
        while(rest.Length>0)
        {
            string matched=null;
            foreach(var token in tokens)   // Tokens 按长度降序，第一个命中即最长匹配
            {
                if(token.Length<=rest.Length&&rest.AsSpan(0,token.Length).SequenceEqual(token))
                {
                    matched=token;
                    break;
                }
            }
            if(matched==null)
            {
                break; // 剩下的都不成词（末尾残片），不显示
            }
            result.Add(matched);
            rest=rest[matched.Length..];
        }
        return result;
    }

    /// <summary>
    /// 把输入里已经成词的完整 token 逐个翻译成真实单词并拼接，供 SpellInput 顶部实时显示。
    /// 末尾未完成的 token 不显示
    /// </summary>
    public string Transcript(string input)
    {
        var parts=new List<string>();
        foreach(var token in SplitTokens(input))
        {
            parts.Add(DB.GetRealWord(token));
        }
        return string.Join(" ",parts);
    }

    /// <summary>
    /// 求值一批候补咒语：随机取一个，注入 .me（施法者）与 .targets（目标表）后求值；
    /// 有目标时逐个把目标绑成 .you 分别施放。咒语里的 cast 再施放别的槽会递归到这里，用深度上限兜底。
    /// 返回是否真的求值了。
    /// </summary>
    public bool CastWords(Word[] spells,NObject bywho,List<NObject> targets)
    {
        if(spells.Length==0||bywho==null||castDepth>=MaxCastDepth) return false;
        targets??=[];
        var spell=spells[GD.Randi()%spells.Length];
        var extend=new Context(ProgressAlphabet);
        extend.Set(".me",bywho);
        extend.Set(".targets",targets);
        Particles.Emitting=true;
        Particles.Position=bywho.Position;
        castDepth++;
        try
        {
            if(targets.Count==0)
            {
                spell.Eval(extend);
            }
            else
            {
                foreach(var target in targets)
                {
                    var inner=new Context(extend);
                    inner.Set(".you",target);
                    spell.Eval(inner);
                }
            }
        }
        finally
        {
            castDepth--;
        }
        return true;
    }
    public override void _EnterTree()
    {
        SpellstateLabel=GetNode<Label>("%SpellStateLabel");
        SpellstateLabel.Text="spell_state.empty";
        Particles.OneShot=true;
        // 从主菜单进来时读存档（新游戏已经写好了种子）；直接运行 Game.tscn 调试则保持满字母表
        var progress=SaveSystem.LoadProgress();
        if(progress!=null)
        {
            LoadProgress(progress);
        }
    }

}