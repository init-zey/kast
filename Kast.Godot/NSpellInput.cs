using System.Collections.Generic;
using System.Linq;
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
    List<char> Result=new();
    Prediction Prediction;
    public override void _UnhandledInput(InputEvent @event)
    {
        if(@event is InputEventMouseButton mouse && mouse.ButtonMask==MouseButtonMask.Right)
        {
            if(mouse.Pressed&&!Visible)
            {
                Visible=true;
                Accumulate=new();
                Prediction.Reset();
                Result.Clear();
                Helper.ClearChild(Predictions);
                PredictionItems.Clear();
                Origin=-mouse.Position;
                QueueRedraw();
            }
        }
    }
    public override void _GuiInput(InputEvent @event)
    {
        if(!Input.IsMouseButtonPressed(MouseButton.Right))
        {
            Visible=false;
            NGame.ActiveCaster?.SetSpellInput(new string(Result.ToArray()));
        }
        else if(Visible && @event is InputEventMouseMotion motion)
        {
            QueueRedraw();
            Accumulate+=motion.Relative;
            var finaldir='\0';
            var finalvec=new Vector2();
            var minlen=Accumulate.LengthSquared();
            foreach(var (dir,vec) in GridHelper.Vecs)
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
                if(Result.Count>0&&GridHelper.IsOpposite(Result.Last(),finaldir))
                {
                    Result.RemoveAt(Result.Count-1);
                    Prediction.Reset();
                    foreach(var c in Result)
                    {
                        Prediction.PushChar(c);
                    }
                }
                else
                {
                    Result.Add(finaldir);
                    Prediction.PushChar(finaldir);
                }
                var vec=finalvec*GridSize*0.5f;
                Accumulate-=vec;
                Origin+=vec;
                UpdateTranscript();
                UpdatePredictions();
            }
            SetOrigin(Origin+Accumulate);
        }
    }

    private void UpdatePredictions()
    {
        var resultstr=new string(Result.ToArray());
        var predictionWords=Prediction.PredictionResult;
        var prevPredictionItems=PredictionItems;
        PredictionItems=new();
        foreach(var predictionWord in predictionWords)
        {
            if(prevPredictionItems.TryGetValue(predictionWord,out var item))
            {
                PredictionItems[predictionWord]=item;
                prevPredictionItems.Remove(predictionWord);
            }
            else
            {
                Helper.LoadScene<NDictEntry>("DictEntry", (n) =>
                {
                    PredictionItems[predictionWord]=n;
                    Predictions.AddChild(n);
                    n.Init(predictionWord);
                });
            }
        }
        foreach(var prevItem in prevPredictionItems.Values)
        {
            prevItem.QueueFree();
        }
    }


    public override void _Draw()
    {
        DrawRect(GetRect(),new(0,0,0,0.5f));
        
    }
    public override void _Ready()
    {
        Symbol.EdgeLength=GridSize;
        ((ShaderMaterial)Material).SetShaderParameter("hex_size", GridSize);
        Resized += ()=>UpdateSize();
        Symbol.Spell=Result;
        Prediction=NGame.Instance.ProgressAlphabet.CreatePrediction();
        UpdateTranscript();
    }

    /// <summary>把当前输入（可以是不完整语句）逐词翻译后显示在屏幕上方。</summary>
    private void UpdateTranscript()
    {
        var text=NGame.Instance.Transcript(new string(Result.ToArray()));
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
