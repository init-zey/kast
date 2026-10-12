using System;
using System.Collections.Generic;
using Godot;
using Kast.Core;

namespace Kast.Godot;

/// <summary>
/// 游戏主节点：装配词表与文法、持有玩家自己的分词器、加载关卡，并提供咒语求值入口。
/// </summary>
/// <remarks>
/// 职责划分：<see cref="Alphabet"/> 是"这部语法能表达什么"（全量词表，含固有权能词），
/// <see cref="PlayerTokenzier"/> 是"玩家现在认得哪些词"（只含已解锁词条）。
/// 两者共用同一份 <see cref="Database"/> 生成的词形，但分词与预测只走玩家那一份，
/// 因此没解锁的词画出来也读不出、不会出现在候补里。
/// </remarks>
public partial class NGame : Node3D
{
    public static NGame Instance;
    /// <summary>本作的字母表（文法 + 全量词表），由 <see cref="LoadProgress"/> 装配。</summary>
    public KastAlphabet Alphabet;
    /// <summary>cast 递归施法的深度上限，防止槽里的咒语互相施放导致无限递归。</summary>
    const int MaxCastDepth=999;
    int castDepth;
    /// <summary>程序词形分配器，词形由种子决定，因此同一存档每次启动得到的词形一致。</summary>
    public Database DB;
    /// <summary>当前正在操纵某个对象的相机；HUD 与"当前施法者"都从这里取。</summary>
    public PlayerCamera ActiveCamera;
    [Export] NLevel CurrentLevel;
    [Export] GpuParticles3D Particles;
    [Export] public NSpellSlots SpellSlots;
    /// <summary>玩家侧分词器：只编译玩家已解锁的词条，供分词的实时显示与提交校验使用。</summary>
    public Tokenizer PlayerTokenzier=new();
    /// <summary>
    /// 按存档装配一局游戏：建词形分配器、装文法与玩法词、编译玩家分词器、加载关卡。
    /// 由 <see cref="_EnterTree"/> 在读到存档时调用。
    /// </summary>
    public void LoadProgress(Progress pg)
    {
        Instance=this;
        TranslationServer.SetLocale(SaveSystem.LoadSettings().Locale);
        DB=new(pg.Seed);
        Alphabet=new KastAlphabet();
        Alphabet.Init(this,DB);
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
        Alphabet.AddNoun(earth,earthValue);          // N   -> 地（名词语义）
        Alphabet.AddWord(earth,"Num",earthValue);    // Num -> 地
        Alphabet.AddWord(earth,"Bit",earthValue);    // Bit -> 地
        Alphabet.AddTVerb(DB.Gen("go",3), (ctx,s,o)=>{
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
        Alphabet.AddTVerb(DB.Gen("look",3), (ctx,s,o)=>
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
        Alphabet.AddTVerb(DB.Gen("be",4), (ctx,s,o)=>
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
        Alphabet.AddTVerb(DB.Gen("display",4), (ctx,s,o)=>
        {
            var sval=s(ctx);
            var str=sval.ToString();
            GetNode<Label>("%DisplayLabel").Text=str;
        }); // 显示
        Alphabet.AddTVerb(DB.Gen("cast",4),(ctx,s,o)=>
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
        Alphabet.Grammar.AddRule("Num -> Num Bit", AppendBit);
        // 数词作宾语：Num 的值是"取出这个数字"的槽（不像 N 的负载是上下文变换）。
        // 因为 Vt -> Vt Num 也是左递归的，一串位可能被切成好几段数词宾语，
        // 所以这里把各段拼成一个数（见 BindNumber）：无论怎么切，动词拿到的都是同一个值。
        Alphabet.Grammar.AddRule("Vt -> Vt Num",(verb, num) => (Context ctx) =>
        {
            var inner = BindNumber(ctx, num);
            return ((Func<Context, Context>)verb)(inner);
        });
        Alphabet.SetupTokenizer();
        // Alphabet=Alphabet.Derive(Alphabet.GetKeys());//FIXME:DEBUG
        // File.WriteAllText("words.csv",DB.Translation.AsText());//FIXME:DEBUG
        LoadLevel(pg.LevelID);
        OnUpdatedTranslation();
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
        ?? new NumSlot(1, c => ((Func<Context, object>)value!)(c.WithFlag("#bit")));

    /// <summary>Num -> Num Bit：左边的数左移一位，再补上这一位。</summary>
    static NumSlot AppendBit(object? high,object? low)
    {
        var upper=AsNum(high);
        return new NumSlot(upper.Bits+1, c =>
            Convert.ToInt32(upper.Value(c)) * 2 + Convert.ToInt32(((Func<Context, object>)low!)(c.WithFlag("#bit"))));
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
    /// <summary>存档占位：种子与翻译表现在由 <see cref="SaveSystem"/> 直接持有，这里暂无额外内容要写。</summary>
    public void SaveProgress(Progress pg)
    {
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
    /// （<see cref="Tokenizer.TokenizePrefix"/>：逐段最长匹配），
    /// 所以界面上显示的词序就是提交时会被接受的那一种，不会再出现"显示成词却填不进槽"。
    /// </summary>
    public string Transcript(string input)
    {
        var parts=new List<string>();
        foreach(var token in PlayerTokenzier.TokenizePrefix(input,out _))
        {
            parts.Add(SaveSystem.Current.PlayerTranslation.GetRealWord(token));
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
        var extend=new Context(Alphabet);
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
    /// <summary>进入场景树时读存档并装配游戏；没有存档（直接调试 Game.tscn）则保持默认状态。</summary>
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
    /// <summary>切换到指定关卡（对应 <c>Levels/{id}.tscn</c>），先卸掉旧关卡。</summary>
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
    /// <summary>
    /// 玩家解锁新词后重建分词表。词条表变了，之前取出的预测器会作废
    /// （见 <see cref="Tokenizer.CompileTokens"/>），<c>NSpellInput</c> 需重新获取。
    /// </summary>
    public void OnUpdatedTranslation()
    {
        PlayerTokenzier.CompileTokens(SaveSystem.Current.PlayerTranslation.Tokens);
    }
}