using System.Collections.Generic;
using Godot;
using Kast.Core;
using Kast.Godot;

public partial class NSpellInput : ColorRect
{
    [Export] public NSpellSymbol Symbol;
    [Export] public Control Predictions;
    /// <summary>屏幕上方实时显示"当前输入逐词翻译并拼接"的标签。</summary>
    [Export] public Label Transcript;
    public Dictionary<string,NDictEntry> PredictionItems=new();
    const float GridSize=40;

    Vector2 Accumulate;
    Vector2 Origin;
    Spell Result;
    Prediction Prediction;
    List<int> TailAngles=new();
    // List<(int x,int y)> AxisPoints=new();
    public override void _UnhandledInput(InputEvent @event)
    {
        // 画到一半放弃：Esc 关掉界面，不动槽里已有的数据
        if(Visible&&@event.IsActionPressed("ui_cancel"))
        {
            Visible=false;
            Modulate=Colors.White;
            GetViewport().SetInputAsHandled();
            return;
        }
        if(@event is InputEventMouseButton mouse && mouse.ButtonMask==MouseButtonMask.Right)
        {
            if(mouse.Pressed&&!Visible)
            {
                Visible=true;
                Result=NGame.Instance.SpellSlots.Caster.Spells[NGame.Instance.SpellSlots.Slot];
                Symbol.Spell=Result;
                Modulate=Colors.White;
                Accumulate=new();
                Prediction.Reset();
                Result.Clear();
                Helper.ClearChild(Predictions);
                PredictionItems.Clear();
                Origin=-mouse.Position;
                TailAngles.Clear();
                // AxisPoints.Clear();
                // AxisPoints.Add((0,0));
                QueueRedraw();
            }
        }
    }
    public override void _GuiInput(InputEvent @event)
    {
        if(!Input.IsMouseButtonPressed(MouseButton.Right))
        {
            // 右键松开 = 提交这一次画出来的串；读不出咒语时界面留着并染红
            Commit();
        }
        else if(Visible && @event is InputEventMouseButton pressed
                && pressed.ButtonIndex==MouseButton.Right && pressed.Pressed)
        {
            // 重新起笔：清掉上一次的失败提示
            Modulate=Colors.White;
        }
        else if(Visible && @event is InputEventMouseMotion motion)
        {
            QueueRedraw();
            Accumulate+=motion.Relative;
            var finaldir='\0';
            var finalvec=new Vector2();
            var minlen=Accumulate.LengthSquared();
            var angle=TailAngles.Count>0?TailAngles[^1]:0;
            foreach(var (dir,vec) in GridHelper.Vecs(angle))
            {
                // var lastAxisPoint=AxisPoints[^1];
                var nextAngle=GridHelper.GetAngle(angle,dir);
                // var axisPoint=GridHelper.ExtendAxis(lastAxisPoint,nextAngle);
                // if(!(Result.HasFirstDir&&dir=='s')&&!GridHelper.CanExtendPoint(AxisPoints,axisPoint))
                // {
                //     continue;
                // }
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
                    // AxisPoints.RemoveAt(AxisPoints.Count-1);
                }
                else
                {
                    if(Result.HasFirstDir)
                    {
                        Prediction.PushChar(finaldir);
                    }
                    Result.Add(finaldir);
                    // 先把这一笔记进 TailAngles，再刷新预测：候补预览的根旋转要从"这一截第一笔"的
                    // 绝对方向回推，TailAngles 里必须已经有这一笔，否则会整整多退一个转折。
                    var nextAngle=GridHelper.GetAngle(angle,finaldir);
                    TailAngles.Add(nextAngle);
                    UpdatePredictions();
                    // var lastAxisPoint=AxisPoints[^1];
                    // var axisPoint=GridHelper.ExtendAxis(lastAxisPoint,nextAngle);
                    // AxisPoints.Add(axisPoint);
                }
                var vec=finalvec*GridSize*0.5f;
                Accumulate-=vec;
                Origin+=vec;
                UpdateTranscript();
            }
            SetOrigin(Origin+Accumulate);
        }
    }

    /// <summary>
    /// 提交当前画出来的串。<see cref="NCaster.CheckSpellInput"/> 说读不出完整咒语时**不关界面**：
    /// 整体染红、已经画的笔画留着，玩家可以接着补笔画再松手重试；按 Esc 放弃。
    /// 成功（或空输入取消）才关界面并清掉提示色。
    /// </summary>
    void Commit()
    {
        var result=NGame.Instance.SpellSlots.Caster.CheckSpellInput(Result,NGame.Instance.SpellSlots.Slot);
        if(result==NCaster.SpellInputRejected)
        {
            Modulate=Colors.Red;
            return;
        }
        Visible=false;
        Modulate=Colors.White;
    }

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
            NDictEntry item;
            if(prevPredictionItems.TryGetValue(match.Token,out var reused))
            {
                prevPredictionItems.Remove(match.Token);
                item=reused;
            }
            else
            {
                item=Helper.LoadScene<NDictEntry>("DictEntry",n=>Predictions.AddChild(n));
            }
            if(item is null)
            {
                continue;   // 场景缺失，Helper 已经报过错
            }
            PredictionItems[match.Token]=item;
            // 每一条都要重摆一次：这一截变长、或者落回词条里的另一个位置，朝向都得跟着变，
            // 所以复用的条目也要重新 Init，不能只在新建时摆一次
            item.Init(RootRotation(match),match.Token);
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
    /// 调用时 <see cref="TailAngles"/> 必须已经包含刚画下的这一笔——见 <c>_GuiInput</c> 里的记录顺序。
    /// </summary>
    char RootRotation(PredictionMatch match)
        =>GridHelper.Chars[match.RootAngle(TailAngles[^match.MatchedLength],GridHelper.Chars)];
    public override void _Draw()
    {
        DrawRect(GetRect(),new(0,0,0,0.5f));
        
    }
    public override void _Ready()
    {
        Result=null;
        Symbol.EdgeLength=GridSize;
        ((ShaderMaterial)Material).SetShaderParameter("hex_size", GridSize*0.577350269189626f);
        Resized += ()=>UpdateSize();
        Prediction=NGame.Instance.PlayerTokenzier.CreatePrediction();
    }

    /// <summary>把当前输入（可以是不完整语句）逐词翻译后显示在屏幕上方。</summary>
    private void UpdateTranscript()
    {
        var text=NGame.Instance.Transcript(new string(Result.Angles.ToArray()));
        Transcript.Visible=text.Length>0;
        Transcript.Text=text;
    }

    private void UpdateSize()
    {
        ((ShaderMaterial)Material).SetShaderParameter("node_size", Size);
    }


    void SetOrigin(Vector2 origin)
    {
        ((ShaderMaterial)Material).SetShaderParameter("offset",origin);
        Symbol.Origin=origin;
        Symbol.QueueRedraw();
    }
}
