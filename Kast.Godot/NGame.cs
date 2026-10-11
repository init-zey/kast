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
    const int MaxCastDepth=999;
    int castDepth;
    public Database DB;
    /// <summary>当前正在操纵某个对象的相机；HUD 与"当前施法者"都从这里取。</summary>
    public PlayerCamera ActiveCamera;
    [Export] NLevel CurrentLevel;
    [Export] GpuParticles3D Particles;
    [Export] public NSpellSlots SpellSlots;
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
        // 天 = 0、地 = 1：同一个字既是名词（天空/大地，原来的方向与质量语义），又能当二进制位用。
        // 位读法靠 #bit 旗标区分，和名词读法的 #be 是同一个套路：只是"问法"不同，词条本身不用改。
        var sky=DB.Gen("sky/air",4);
        Func<Context,object> skyValue=(ctx) =>{
            if(ctx.GetFlag("#bit")) return 0;   // 数词读法：天 = 0
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
        };
        FullAlphabet.AddNoun(sky,skyValue);          // N   -> 天（名词语义）
        FullAlphabet.AddWord(sky,"Num",skyValue);    // Num -> 天（单独一个位也是一个数）
        FullAlphabet.AddWord(sky,"Bit",skyValue);    // Bit -> 天（拼接时的右操作数）
        var earth=DB.Gen("earth/solid",4);
        Func<Context,object> earthValue=(ctx) =>{
            if(ctx.GetFlag("#bit")) return 1;   // 数词读法：地 = 1
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
        };
        FullAlphabet.AddNoun(earth,earthValue);          // N   -> 地（名词语义）
        FullAlphabet.AddWord(earth,"Num",earthValue);    // Num -> 地
        FullAlphabet.AddWord(earth,"Bit",earthValue);    // Bit -> 地
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
                caster.CastSlot(SpellSlots.Slot,targets);
                return;
            }
            if(o(ctx) is not int number||number<1||number>9)
            {
                return;   // 宾语不是数词，或者写成了 0 / 超出九个槽（二进制写法很容易出现这两种）
            }
            caster.CastSlot(number-1,targets);
        }); // 施放
        // —— 二进制数词：天 = 0、地 = 1，位序列最左位是最高位 ——
        // Num -> Num Bit 是左递归拼接：左边的数左移一位，再补上这一位。
        // "Num -> Bit"（一元规则）文法不支持，所以位词条是直接登记成 Num 的（见上面 AddWord）。
        FullAlphabet.Grammar.AddRule("Num -> Num Bit", AppendBit);
        // 数词作宾语：Num 的值是"取出这个数字"的槽（不像 N 的负载是上下文变换）。
        // 因为 Vt -> Vt Num 也是左递归的，一串位可能被切成好几段数词宾语，
        // 所以这里把各段拼成一个数（见 BindNumber）：无论怎么切，动词拿到的都是同一个值。
        FullAlphabet.Grammar.AddRule("Vt -> Vt Num",(verb, num) => (Context ctx) =>
        {
            var inner = BindNumber(ctx, num);
            return ((Func<Context, Context>)verb)(inner);
        });
        FullAlphabet.CompileTokens();
        // ProgressAlphabet=FullAlphabet.Derive(pg.UnlockedSymbols);
        ProgressAlphabet=FullAlphabet.Derive(FullAlphabet.GetKeys());//FIXME:DEBUG
        File.WriteAllText("words.csv",DB.AsText());//FIXME:DEBUG
        LoadLevel(pg.LevelID);
    }

    /// <summary>
    /// 数词绑进 .o 时用的槽：值 + 位数。绑成 <c>slot.Get</c> 这个委托，拼接时靠 <c>Delegate.Target</c>
    /// 认出"上一个 .o 是不是一个数"——只靠旗标分不清"名词宾语"和"被名词覆盖过的数词宾语"。
    /// </summary>
    sealed class NumSlot(int bits,Func<Context,object> value)
    {
        public readonly int Bits=bits;
        public readonly Func<Context,object> Value=value;
        public object Get(Context ctx)=>Value(ctx);
    }

    /// <summary>把数词的值统一成槽：终结数词（单独一个位）的负载要加 #bit 才读得出 0/1。</summary>
    static NumSlot AsNum(object? value)=>value as NumSlot
        ?? new NumSlot(1,(Func<Context,object>)(c=>((Func<Context,object>)value!)(c.WithFlag("#bit"))));

    /// <summary>Num -> Num Bit：左边的数左移一位，再补上这一位。</summary>
    static NumSlot AppendBit(object? high,object? low)
    {
        var upper=AsNum(high);
        return new NumSlot(upper.Bits+1,(Func<Context,object>)(c=>
            Convert.ToInt32(upper.Value(c))*2+Convert.ToInt32(((Func<Context,object>)low!)(c.WithFlag("#bit")))));
    }

    /// <summary>
    /// Vt -> Vt Num：本段在高位，接到"已经绑好的低位段"前面；没有低位段就自成一段。
    /// 这样 <c>地天天地</c> 无论被切成几段数词宾语，动词拿到的都是同一个 9。
    /// </summary>
    static Context BindNumber(Context ctx,object? num)
    {
        var segment=AsNum(num);
        var lower=ctx.GetItem(".o") is Func<Context,object> value&&value.Target is NumSlot slot?slot:null;
        var combined=lower is null
            ? new NumSlot(segment.Bits,segment.Value)
            : new NumSlot(segment.Bits+lower.Bits, c =>
                Convert.ToInt32(segment.Value(c)) * (1 << lower.Bits) + Convert.ToInt32(lower.Value(c)));
        var inner=new Context(ctx);
        inner.Add(".o",(Func<Context,object>)combined.Get);
        return inner;
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

    /// <summary>
    /// 把输入里已经成词的完整 token 逐个翻译成真实单词并拼接，供 SpellInput 顶部实时显示。
    /// 末尾没画完的残片不显示；分词走的是和提交（<see cref="NCaster.CheckSpellInput"/>）完全同一套规则
    /// （<see cref="Alphabet{O}.TokenizePrefix"/>：逐段最长匹配），
    /// 所以界面上显示的词序就是提交时会被接受的那一种，不会再出现"显示成词却填不进槽"。
    /// </summary>
    public string Transcript(string input)
    {
        var parts=new List<string>();
        foreach(var token in Instance.ProgressAlphabet.TokenizePrefix(input,out _))
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
        Particles.OneShot=true;
        // 从主菜单进来时读存档（新游戏已经写好了种子）；直接运行 Game.tscn 调试则保持满字母表
        var progress=SaveSystem.LoadProgress();
        if(progress!=null)
        {
            LoadProgress(progress);
        }
    }
    public void LoadLevel(string id)
    {
        SaveSystem.Current.LevelID=id;
        SaveProgress(SaveSystem.Current);
        if(CurrentLevel!=null)
        {
            CurrentLevel.QueueFree();
        }
        CurrentLevel=Helper.LoadScene<NLevel>($"Levels/{id}");
        AddChild(CurrentLevel);
    }
}