using System.Collections.Generic;
using Godot;
using Kast.Godot;

/// <summary>
/// 把一条 <see cref="Spell"/> 画成六边形网格上的折线，用于咒语槽、候补列表与词典条目。
/// </summary>
/// <remarks>
/// <see cref="EdgeLength"/> 决定两笔之间的像素间距：为负时按控件尺寸自动缩放（词典里那种
/// 大小不一的条目），非负时按固定边长绘制（咒语槽与候补预览，各槽之间间距保持一致）。
/// <see cref="Origin"/> 是当前画到的位置，由 <c>NSpellInput</c> 随输入推进。
/// </remarks>
public partial class NSpellSymbol : Control
{
    /// <summary>要绘制的咒语；为 null 时什么都不画。</summary>
    public Spell Spell=null;
    /// <summary>绘制原点，由输入逻辑推进，使笔画跟着玩家画的方向走。</summary>
    public Vector2 Origin;
    [Export] public float EdgeLength=-1;
    [Export] public float EdgeWidth=4;
    double t;
    /// <summary>折线的抖动曲线：让线看起来像手绘而不是直尺画的，见构造函数。</summary>
    readonly float[] Curve;
    /// <summary>换一条咒语并请求重绘。</summary>
    public void SetSpell(Spell spell)
    {
        Spell=spell;
        QueueRedraw();
    }
    /// <summary>沿 <see cref="Spell"/> 的笔画逐段画折线，笔头用半径递减的圆表示作画方向。</summary>
    public override void _Draw()
    {
        if(Spell==null||!Spell.HasFirstDir) return;
        var points = new List<Vector2>();
        var tail = new Vector2();
        var rect = GridHelper.GetRect(Spell);
        float k=0;
        float s=0;
        float w=0.7f;
        if(EdgeLength<0)
        {
            if(rect.Size.X<rect.Size.Y)
            {
                s=Size.Y/rect.Size.Y;
            }
            else
            {
                s=Size.X/rect.Size.X;
            }
        }
        var o=rect.GetCenter()-Size/2/s;
        var a=0;
        foreach(var c in Spell.ToArray())
        {
            var p=PlacePoint(tail, o, s);
            ExtendCurve(points, p,ref k);
            DrawCircle(p,EdgeWidth*(0.5f+w),Colors.White);
            w*=0.6f;
            tail += GridHelper.CharToVec(ref a,c);
        }
        ExtendCurve(points, PlacePoint(tail, o, s),ref k);
        DrawPolyline(points.ToArray(), new Color(1,1,1,0.8f), EdgeWidth);
    }

    /// <summary>把折线从上一段延伸到 <paramref name="point"/>，途中按 <see cref="Curve"/> 叠加垂直抖动。</summary>
    private void ExtendCurve(List<Vector2> points, Vector2 point,ref float k)
    {
        if(points.Count>0)
        {
            var last=points[^1];
            var vec=point-last;
            var dir=vec.Normalized();
            var nor=new Vector2(-dir.Y,dir.X);
            for(float d=0;d<vec.Length();d+=5f)
            {
                k+=1f;
                var p=k-t*15;
                points.Add(last+dir*d+nor*(float)Mathf.Lerp(Curve[Mathf.PosMod((int)p,Curve.Length)],Curve[Mathf.PosMod((int)p+1,Curve.Length)],p-Mathf.FloorToInt(p)));    
            }
        }
        points.Add(point);
    }


    /// <summary>把笔画坐标换算成控件坐标：自动缩放模式按包围盒居中，固定边长模式直接乘边长。</summary>
    Vector2 PlacePoint(Vector2 p, Vector2 o, float s)    {
        if(EdgeLength<0)
        {
            return (p - o) * s - Origin;
        }
        return p * EdgeLength - Origin;
    }

    /// <summary>推进抖动动画的时间基准（<see cref="t"/> 供 <see cref="ExtendCurve"/> 使用）。</summary>
    public override void _PhysicsProcess(double delta)
    {
        if(Spell==null) return;
        t+=delta;
        if(Spell.FirstDir==' ')
        {
            Rotation += (float)delta*0.1f;
        }
        QueueRedraw();
    }



    /// <summary>把轴心放到控件中心，使词典条目能以自身中心慢慢旋转。</summary>
    public override void _Ready()
    {
        PivotOffsetRatio=new(0.5f,0.5f);
    }
    /// <summary>构造抖动曲线：前四分之一抬高，让折线起手处带一点弧度。</summary>
    public NSpellSymbol()
    {
        Curve=new float[16];
        for(int i=0;i<Curve.Length;i++)
        {
            // Curve[i]=(GD.Randf()-0.5f)*2*2;
            if(i<Curve.Length/4)
            {
                Curve[i]+=Mathf.Sin((float)i/Curve.Length*Mathf.Pi/2)*8;
            }
        }
    }
}