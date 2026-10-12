using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Kast.Godot;

public static class GridHelper
{
    public readonly static Dictionary<char, (int dx, int dy)> Axis;
    public readonly static char[] Chars = "wqasde".ToArray();
    public readonly static char[] NextAngles = "wqade".ToArray();
    public static int GetAngle(int angle,char c)
    {
        var a=angle+Array.IndexOf(Chars,c);
        if(a>=Chars.Length) a-=Chars.Length;
        return a;
    }
    public static Vector2 AngleToVec(int angle)
    {
        return new(MathF.Cos(angle*MathF.Tau/Chars.Length),MathF.Sin(angle*MathF.Tau/Chars.Length));
    }
    public static Vector2 CharToVec(ref int lastAngle,char c)
    {
        lastAngle=GetAngle(lastAngle,c);
        return AngleToVec(lastAngle);
    }
    public static Rect2 GetRect(Spell spell)
    {
        var rect=new Rect2();
        var p=new Vector2();
        var a=0;
        foreach(var c in spell.ToArray())
        {
            p+=CharToVec(ref a,c);
            var dx=rect.Position.X-p.X;
            if(dx>0)
            {
                rect=rect.GrowSide(Side.Left,dx);
            }
            else
            {
                dx=p.X-rect.End.X;
                if(dx>0)
                {
                    rect=rect.GrowSide(Side.Right,dx);
                }
            }
            var dy=rect.Position.Y-p.Y;
            if(dy>0)
            {
                rect=rect.GrowSide(Side.Top,dy);
            }
            else
            {
                dy=p.Y-rect.End.Y;
                if(dy>0)
                {
                    rect=rect.GrowSide(Side.Bottom,dy);
                }
            }
        }
        return rect;
    }
    public static char AddChar(char a,char b)
    {
        if(!IsValid(a))
        {
            throw new Exception($"非法字符{a}");
        }
        if(!IsValid(b))
        {
            throw new Exception($"非法字符{b}");
        }
        return Chars[(Array.IndexOf(Chars,a)+Array.IndexOf(Chars,b))%Chars.Length];
    }
    /// <summary>是否是有效的方向字符。</summary>
    public static bool IsValid(char c)
    {
        return Array.IndexOf(Chars,c)>=0;
    }
    /// <summary>两步是否互为反向（走回头路）。</summary>
    public static bool IsOpposite(char a,char b)
    {
        return IsValid(a)&&IsValid(b)&&MathF.Abs(Array.IndexOf(Chars,a)-Array.IndexOf(Chars,b))==Chars.Length/2;
    }
    public static (int,int) ExtendAxis((int x, int y) p,int a)
    {
        var nd = Axis[Chars[a]];
        return (p.x + nd.dx ,p.y + nd.dy);
    }
    /// <summary>
    /// 从 (x,y) 沿 <paramref name="point"/> 方向走出去是否合法：这条边尚未被用过时为 <see langword="true"/>。
    /// 只判边，不判目标顶点是否访问过。
    /// </summary>
    public static bool CanExtendPoint(List<(int,int)> points, (int,int) nextPoint)
    {
        if(points.Count<1) return true;
        var lastPoint=points[^1];
        for(int i=1;i<points.Count;i++)
        {
            if(points[i-1]==lastPoint&&points[i]==nextPoint) return false;
            if(points[i-1]==nextPoint&&points[i]==lastPoint) return false;
        }
        return true;
    }

    public static ((int x, int y) a, (int x, int y) b) NormalizeEdge(
        (int x, int y) p,
        (int x, int y) q)
    {
        // 将边的两个端点排序，使得同一条边无论方向如何都得到相同表示
        if (p.x < q.x || (p.x == q.x && p.y < q.y))
            return (p, q);

        return (q, p);
    }

    public static IEnumerable<(char,Vector2)> Vecs(int lastAngle)
    {
        foreach(var c in Chars)
        {
            yield return (c,AngleToVec(GetAngle(lastAngle,c)));
        }
    }

    static GridHelper()
    {
        Axis = new()
        {
            ['w'] = (2, 0),
            ['q'] = (1, 1),
            ['a'] = (-1, 1),
            ['s'] = (-2, 0),
            ['d'] = (-1, -1),
            ['e'] = (1, -1)
        };
    }
}