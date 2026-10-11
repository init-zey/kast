using System.Collections.Generic;
using Godot;

namespace Kast.Godot;

public static class GridHelper
{
    const float SQRT3=1.732050807568877f;
    static readonly Transform2D Transform=new Transform2D(SQRT3,0,3f/2,SQRT3/2,0,0);
    public readonly static Dictionary<char,Vector2> Vecs;
    public readonly static Dictionary<char, (int dx, int dy)> Axis;
    public const string Chars = "wqasde";
    public static Vector2 CharToVec(char c)
    {
        return Vecs[c];
    }
    public static Rect2 GetRect(IEnumerable<char> pattern)
    {
        var rect=new Rect2();
        var p=new Vector2();
        foreach(var c in pattern)
        {
            p+=CharToVec(c);
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
        if(a==' ') return b;
        return Chars[(Chars.IndexOf(a)+Chars.IndexOf(b))%Chars.Length];
    }
    /// <summary>是否是有效的方向字符。</summary>
    public static bool IsValid(char c)
    {
        return Vecs.ContainsKey(c);
    }
    /// <summary>两步是否互为反向（走回头路）。</summary>
    public static bool IsOpposite(char a,char b)
    {
        return IsValid(a)&&IsValid(b)&&Vecs[a]+Vecs[b]==Vector2.Zero;
    }
    public static (int,int) ExtendPoint(int x, int y,char next)
    {
        var nd = Axis[next];
        return (x + nd.dx ,y + nd.dy);
    }
    /// <summary>
    /// 从 (x,y) 沿 <paramref name="point"/> 方向走出去是否合法：这条边尚未被用过时为 <see langword="true"/>。
    /// 只判边，不判目标顶点是否访问过。
    /// </summary>
    public static bool CanExtendPoint(HashSet<((int x, int y) a, (int x, int y) b)> edges, int x, int y,(int,int) point)
    {
        var newEdge = NormalizeEdge((x, y), point);
        return !edges.Contains(newEdge);
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
    static GridHelper()
    {
        // var up=new Vector2(0,-3);
        var right=new Vector2(SQRT3,0);
        var upright=new Vector2(SQRT3*0.5f,-1.5f);
        Vecs = new()
        {
            ['w'] = new(-upright.X,upright.Y),
            ['q'] = -right,
            ['a'] = -upright,
            ['s'] = new(upright.X,-upright.Y),
            ['d'] = right,
            ['e'] = upright,
            // ['w'] = up,
            // ['x'] = -up,
        };
        Axis = new()
        {
            ['w'] = (1, 0),
            ['q'] = (0, 1),
            ['a'] = (-1, 1),
            ['s'] = (-1, 0),
            ['d'] = (0, -1),
            ['e'] = (1, -1)
        };
    }
}