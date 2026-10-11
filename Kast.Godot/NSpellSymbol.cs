using System.Collections.Generic;
using System.Linq;
using Godot;
using Kast.Godot;

public partial class NSpellSymbol : Control
{
    public Spell Spell=null;
    public Vector2 Origin;
    [Export] public float EdgeLength=-1;
    [Export] public float EdgeWidth=4;
    double t;
    readonly float[] Curve;
    public void SetSpell(Spell spell)
    {
        Spell=spell;
        QueueRedraw();
    }
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


    Vector2 PlacePoint(Vector2 p, Vector2 o, float s)    {
        if(EdgeLength<0)
        {
            return (p - o) * s - Origin;
        }
        return p * EdgeLength - Origin;
    }

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



    public override void _Ready()
    {
        PivotOffsetRatio=new(0.5f,0.5f);
    }
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
