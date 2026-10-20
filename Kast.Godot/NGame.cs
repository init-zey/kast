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
    /// <summary>词典面板，暂停菜单与快捷键都会用到。</summary>
    [Export] public NDictionary PlayerDict;
    /// <summary>暂停菜单，Esc 时切换。</summary>
    [Export] public NPauseMenu PauseMenu;
    /// <summary>
    /// 玩家侧分词器：只编译玩家已解锁的词条，供分词的实时显示与提交校验使用。
    /// 直接取自存档里的翻译表，后者在词条增删时已就地重编译（见 <see cref="GameTranslation"/>），
    /// 所以这里不需要再维护第二份。
    /// </summary>
    public Tokenizer PlayerTokenzier=>SaveSystem.Current.PlayerTranslation.Tokenizer;
    /// <summary>当前被选中（高亮、可编辑）的槽位或词条，没有选中时为 null。</summary>
    internal NSpellSymbolSlot SelectedSymbolSlot;

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
        Alphabet.SetupTokenizer();
        SaveSystem.Current.PlayerTranslation=DB.Translation;//FIXME:DEBUG
        // File.WriteAllText("words.csv",DB.Translation.AsText());//FIXME:DEBUG
        LoadLevel(pg.LevelID);
    }
    public override void _Ready()
    {
        _=NAnimatedMask.Instance.Play(GD.Load<Texture>("res://Assets/Masks/Circle.png"),NAnimatedMask.Instance.Size/2,0f,1f,0.5f);
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
    /// 末尾没画完的残片不显示；分词走的是和施放（<see cref="NCaster.CastSlot"/> 里的
    /// <see cref="NCaster.ReadSpells"/>）完全同一套规则（<see cref="Tokenizer.TokenizePrefix"/>：
    /// 逐段最长匹配），所以界面上显示的词序就是施放时会被接受的那一种，
    /// 不会出现"显示成词却放不出来"。
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

    /// <summary>把词典面板编辑好的词表写回存档（整体覆盖）。</summary>
    internal void SetTranslation(GameTranslation translation)
    {
        SaveSystem.Current.PlayerTranslation.Copy(translation);
    }

    /// <summary>切换当前选中的槽位/词条：旧的取消高亮，新的高亮，传 null 表示取消选择。</summary>
    public void SetSelectedSymbolSlot(NSpellSymbolSlot slot)
    {
        if(SelectedSymbolSlot!=null)
        {
            SelectedSymbolSlot.Deactive();
        }
        SelectedSymbolSlot=slot;
        if(slot!=null)
        {
            slot.Active();
        }
    }

    /// <summary>把选择切回九个快捷键槽里当前那一个（关掉词典面板时用）。</summary>
    public void SelecteSlotFromSlots()
    {
        SetSelectedSymbolSlot(SpellSlots.Slots[SpellSlots.Slot]);
    }
    /// <summary>Esc 切换暂停菜单。</summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if(@event.IsActionPressed("ui_cancel"))
        {
            PauseMenu.Toggle();
            GetViewport().SetInputAsHandled();
        }
    }
    public async void PlayerDie(string mask="Circle")
    {
        var player=ActiveCamera.Player;
        ActiveCamera.Player=null;
        player.LinearVelocity=new();
        var playerPos=player.GlobalPosition;
        player.Type.PlayAnimation("die");
        await NAnimatedMask.Instance.Play(GD.Load<Texture>($"res://Assets/Masks/{mask}.png"),ActiveCamera.Camera.UnprojectPosition(playerPos),1f,0f,0.5f);
        GetTree().ReloadCurrentScene();
    }

    internal async void DemoEnd()
    {
        var player=ActiveCamera.Player;
        ActiveCamera.Player=null;
        player.LinearVelocity=new();
        var playerPos=player.GlobalPosition;
        await NAnimatedMask.Instance.Play(GD.Load<Texture>($"res://Assets/Masks/DemoEnd.png"),ActiveCamera.Camera.UnprojectPosition(playerPos),1f,0f,2f);
        PauseMenu.Quit();
    }
}