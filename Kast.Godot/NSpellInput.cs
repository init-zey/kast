using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Kast.Core;
using Kast.Godot;

/// <summary>
/// 画咒语的输入层：按住拖动，按六边形网格把轨迹吸附成一笔笔方向，
/// 实时给出候补预览与转写，抬手提交到当前选中的槽位。
/// </summary>
/// <remarks>
/// 只在 <see cref="NGame.SelectedSymbolSlot"/> 非空时工作，目标槽位取自该选中项，
/// 因此咒语槽与词典条目共用同一套作画界面。
/// <para>
/// 移动端由 <see cref="Control._GuiInput"/> 接收触摸，是否可画由屏上的作画/施法模式切换
/// （见 <c>NTouchControls</c>）决定；非移动端保留原有右键起笔、松手提交。
/// 两类都不合成引擎输入事件。
/// </para>
/// <para>
/// 一笔的判定是"取离当前位置最近的网格方向"（<see cref="GridHelper.Vecs"/>），
/// 而不是等距离达到整格——手感上更跟手，见 <see cref="DrawStep"/> 里的 <c>minlen</c> 比较。
/// </para>
/// </remarks>
public partial class NSpellInput : ColorRect
{
    [Export] public NSpellSymbol Symbol;
    /// <summary>候补预览的容器，每次预测结果变化时重建。</summary>
    [Export] public Control Predictions;
    /// <summary>屏幕上方实时显示"当前输入逐词翻译并拼接"的标签。</summary>
    [Export] public Label Transcript;
    /// <summary>当前显示的候补条目，按词形索引，用于复用而不必每次重建场景。</summary>
    public Dictionary<string,NSpellEntry> PredictionItems=new();
    const float GridSize=80;
    /// <summary>当前这一笔尚未吸附到某个网格方向前的累计位移。</summary>
    Vector2 Accumulate;
    /// <summary>已画部分的屏幕原点，随每一笔推进。</summary>
    Vector2 Origin;
    /// <summary>正在编辑的咒语，直接引用选中槽里的实例，所以改动即时落到槽上。</summary>
    Spell Result;
    /// <summary>本次输入所用的翻译表（词典里画新词时可能是未写回的副本）。</summary>
    GameTranslation Translation;
    Prediction Prediction;
    /// <summary>每一笔的绝对方向，供候补预览回推词条的根旋转（见 <see cref="RootRotation"/>）。</summary>
    List<int> TailAngles=new();
    /// <summary>起笔前槽里原有的内容；放弃时据此还原，使整个作画过程可以撤销。</summary>
    Spell Backup=new();
    /// <summary>当前是否有一个正在画的手势。</summary>
    bool Drawing;

    /// <summary>
    /// 移动端（Android/iOS）走触摸逻辑；其余环境沿用原有键鼠逻辑（右键起笔、松手提交）。
    /// </summary>
    public static bool IsTouchPlatform => OS.HasFeature("mobile");

    /// <summary>作画层是否可画（由模式按钮切换；关闭时放弃未完成的手势）。</summary>
    public bool IsActive=>Visible;

    /// <summary>
    /// 开关作画层。只改本控件的可见性：可见且 <c>MouseFilter</c> 允许时，引擎才会把
    /// 触摸/鼠标事件送进 <see cref="_GuiInput"/>，不需要在这里抢全局输入。
    /// </summary>
    public void SetActive(bool active)
    {
        if(!active)
        {
            Cancel();
        }
        Visible=active;
    }

    /// <summary>Esc 放弃；非移动端另由右键起笔（原有逻辑）。</summary>
    public override void _Input(InputEvent @event)
    {
        if(Visible&&@event.IsActionPressed("deselect"))
        {
            Cancel();
            GetViewport().SetInputAsHandled();
            return;
        }
        if(!IsTouchPlatform&&@event is InputEventMouseButton mouse&&mouse.ButtonMask==MouseButtonMask.Right)
        {
            if(mouse.Pressed&&!Visible)
            {
                Visible=true;
                Begin(mouse.Position);
            }
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if(IsTouchPlatform)
        {
            // 移动端：触摸直接作画
            switch(@event)
            {
                case InputEventScreenTouch touch:
                    if(touch.Pressed)
                    {
                        Begin(touch.Position);
                    }
                    else
                    {
                        Commit();
                    }
                    AcceptEvent();
                    break;
                case InputEventScreenDrag drag:
                    if(Drawing) DrawStep(drag.Relative);
                    AcceptEvent();
                    break;
            }
            return;
        }
        // 非移动端：原有右键逻辑——右键松开即提交，右键按住时跟随鼠标拖动
        if(!Input.IsMouseButtonPressed(MouseButton.Right))
        {
            Commit();
        }
        else if(@event is InputEventMouseMotion motion&&Drawing)
        {
            DrawStep(motion.Relative);
        }
    }

    /// <summary>
    /// 起笔：绑定到选中槽位并从它<b>已有的内容</b>接着画。
    /// </summary>
    void Begin(Vector2 pos)
    {
        if(Drawing||NGame.Instance.SelectedSymbolSlot==null)
        {
            return;
        }
        Drawing=true;
        Result=NGame.Instance.SelectedSymbolSlot.Symbol.Spell;
        Backup.FirstDir=Result.FirstDir;
        Backup.Angles= [.. Result.Angles];
        Translation=NGame.Instance.SelectedSymbolSlot.Translation;
        Prediction=NGame.Instance.PlayerTokenzier.CreatePrediction();
        if(Result.FirstDir==' ')Result.FirstDir='w';
        Symbol.Spell=Result;
        Symbol.Rotation=0;
        Modulate=Colors.White;
        Accumulate=new();
        Helper.ClearChild(Predictions);
        PredictionItems.Clear();
        TailAngles.Clear();

        // 空槽没有起笔方向，此时不预置 TailAngles：第一笔由下面的实时路径
        // 按"没有上一笔就从 0 起算"的约定建立（见 DrawStep 里的 angle 取值）。
        var vec=Vector2.Zero;
        if(Result.HasFirstDir)
        {
            var angle=Array.IndexOf(GridHelper.Chars,Symbol.Spell.FirstDir);
            TailAngles.Add(angle);
            vec=GridHelper.AngleToVec(angle);
            foreach(var ch in Result.Angles)
            {
                Prediction.PushChar(ch);
                angle=GridHelper.GetAngle(angle,ch);
                TailAngles.Add(angle);
                vec+=GridHelper.AngleToVec(angle);
            }
        }
        UpdateTranscript();
        UpdatePredictions();
        Origin=-pos+vec*GridSize;
        SetOrigin(Origin+Accumulate);
        QueueRedraw();
    }

    /// <summary>把这一段位移吸附成网格方向，推进当前咒语与候补预览。</summary>
    void DrawStep(Vector2 relative)
    {
        QueueRedraw();
        Accumulate+=relative;
        var finaldir='\0';
        var finalvec=new Vector2();
        var minlen=Accumulate.LengthSquared();
        var angle=TailAngles.Count>0?TailAngles[^1]:0;
        foreach(var (dir,vec) in GridHelper.Vecs(angle))
        {
            var len=(vec*GridSize*0.5f-Accumulate).LengthSquared();
            if(len<minlen)
            {
                minlen=len;
                finaldir=dir;
                finalvec=vec;
            }
        }
        if(finaldir!='\0')
        {
            if(Result.HasFirstDir&&finaldir=='s')
            {
                TailAngles.RemoveAt(TailAngles.Count-1);
                Result.RemoveLast();
                Prediction.Reset();
                foreach(var c in Result.Angles)
                {
                    Prediction.PushChar(c);
                }
                UpdatePredictions();
            }
            else
            {
                if(Result.HasFirstDir)
                {
                    Prediction.PushChar(finaldir);
                }
                Result.Add(finaldir);
                var nextAngle=GridHelper.GetAngle(angle,finaldir);
                TailAngles.Add(nextAngle);
                UpdatePredictions();
            }
            var vec=finalvec*GridSize*0.5f;
            Accumulate-=vec;
            Origin+=vec;
            UpdateTranscript();
        }
        SetOrigin(Origin+Accumulate);
    }

    /// <summary>提交这一次画出来的串；移动端层保持开启，非移动端回到未激活。</summary>
    void Commit()
    {
        Drawing=false;
        Modulate=Colors.White;
        if(!IsTouchPlatform)
        {
            Visible=false;
        }
    }

    /// <summary>放弃当前手势，把槽还原成起笔前的内容。</summary>
    void Cancel()
    {
        if(Drawing)
        {
            Result.FirstDir=Backup.FirstDir;
            Result.Angles= [.. Backup.Angles];
            UpdateTranscript();
            UpdatePredictions();
        }
        Drawing=false;
        Modulate=Colors.White;
        if(!IsTouchPlatform)
        {
            Visible=false;
        }
    }

    /// <summary>
    /// 重建候补列表：只保留"接着画就能得到"的候补，并尽量复用上一轮的条目节点
    /// （按词形匹配），避免每次移动都重新实例化场景。
    /// </summary>
    private void UpdatePredictions()
    {
        var predictionMatches=Prediction.PredictionResult;
        var prevPredictionItems=PredictionItems;
        PredictionItems=new();
        var index=0;
        foreach(var match in predictionMatches)
        {
            // 只显示"接着画就能得到"的候补（偏移 0）。偏移大于 0 的那些，词条开头那几笔补不上去
            // （要么还没画，要么那个起点根本不可分词），预览只能把手上这一截画在词条中间，
            // 既对不上已经画出来的笔画，也换不来这个词——与"所见即所得"相悖。
            if(!match.FromStart)
            {
                continue;
            }
            NSpellEntry item;
            if(prevPredictionItems.TryGetValue(match.Token,out var reused))
            {
                prevPredictionItems.Remove(match.Token);
                item=reused;
            }
            else
            {
                item=Helper.LoadScene<NSpellEntry>("SpellEntry",n=>Predictions.AddChild(n));
                item.Translation=Translation;
            }
            if(item is null)
            {
                continue;   // 场景缺失，Helper 已经报过错
            }
            PredictionItems[match.Token]=item;
            item.MouseFilter=MouseFilterEnum.Ignore;   // 预览条目不参与选中，触摸留给作画层
            // 每一条都要重摆一次：这一截变长、或者落回词条里的另一个位置，朝向都得跟着变，
            // 所以复用的条目也要重新 Init，不能只在新建时摆一次
            item.Init(null,new Spell{FirstDir=RootRotation(match),Angles=match.Token.ToList()});
            Predictions.MoveChild(item,index++);
        }
        foreach(var prevItem in prevPredictionItems.Values)
        {
            prevItem.QueueFree();
        }
    }

    /// <summary>
    /// 候补词条的根旋转（整词的起笔方向）：界面画的是<b>整个词条</b>，而这一截只是它的一段，
    /// 所以要把它摆回词条里的位置再往回退到词条起笔那一笔，见 <see cref="PredictionMatch.RootAngle"/>。
    /// 调用时 <see cref="TailAngles"/> 必须已经包含刚画下的这一笔。
    /// </summary>
    char RootRotation(PredictionMatch match)
        =>GridHelper.Chars[match.RootAngle(TailAngles[^match.MatchedLength],GridHelper.Chars)];
    /// <summary>铺一层半透明黑底，让画出来的笔画在游戏画面上看得清。</summary>
    public override void _Draw()
    {
        DrawRect(GetRect(),new(0,0,0,0.5f));
    }
    /// <summary>初始化：格距、六边形着色器参数，并监听尺寸变化。</summary>
    public override void _Ready()
    {
        Result=null;
        Symbol.EdgeLength=GridSize;
        ((ShaderMaterial)Material).SetShaderParameter("hex_size", GridSize*0.577350269189626f);
        Resized += ()=>UpdateSize();
        UpdateSize();
    }

    /// <summary>把当前输入（可以是不完整语句）逐词翻译后显示在屏幕上方。</summary>
    private void UpdateTranscript()
    {
        var text=NGame.Instance.Transcript(Result.AsString());
        Transcript.Visible=text.Length>0;
        Transcript.Text=text;
    }

    /// <summary>把控件尺寸同步给着色器，使网格铺满整块输入区。</summary>
    private void UpdateSize()
    {
        ((ShaderMaterial)Material).SetShaderParameter("node_size", Size);
    }

    /// <summary>把当前绘制原点同时写进着色器与词形符号。</summary>
    void SetOrigin(Vector2 origin)
    {
        ((ShaderMaterial)Material).SetShaderParameter("offset",origin);
        Symbol.Origin=origin;
        Symbol.QueueRedraw();
    }
}
