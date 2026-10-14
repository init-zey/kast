using System;
using System.Collections.Generic;
using Godot;
using Kast.Godot;

public partial class NSymbol3d : MeshInstance3D
{
    [Export] public string[] RealWords;
    [Export] public Vector2 Size;
    [Export] public float Width;
    [Export] public BaseMaterial3D Material;
    public override void _Ready()
    {
    }

    static Vector2 PlacePoint(Vector2 p, Vector2 o, float s)    {
        return (p - o) * s;
    }

    private void SetSymbol(string angleChars)
    {
        var rect=GridHelper.GetRect(angleChars);
        float s;
        if(rect.Size.X<rect.Size.Y)
        {
            s=Size.Y/rect.Size.Y;
        }
        else
        {
            s=Size.X/rect.Size.X;
        }
        var o=rect.GetCenter()-Size/2/s;
        var path=new List<Vector2>();
        var tail=new Vector2();
        var angle=0;
        path.Add(tail);
        foreach(var c in angleChars)
        {
            angle=GridHelper.GetAngle(angle,c);
            tail += GridHelper.AngleToVec(angle);
            path.Add(PlacePoint(tail,o,s));
        }
        Mesh=GenerateMesh(path,1,Width);
        Mesh.SurfaceSetMaterial(0,Material);
    }
    public static Mesh GenerateMesh(
    List<Vector2> path,
    float scale,
    float width,
    int discSegments = 8)
    {
        if (path == null || path.Count < 2) return null;

        int n = path.Count;
        float hw = width * 0.5f;
        float circleRadius=0.5f;

        // ---- 1. 缩放后的路径点 ----
        var P = new Vector2[n];
        for (int i = 0; i < n; i++) P[i] = path[i] * scale;

        // ---- 2. 每段的左法线 ----
        var normals = new Vector2[n - 1];
        for (int i = 0; i < n - 1; i++)
        {
            Vector2 d = P[i + 1] - P[i];
            float len = d.Length();
            if (len < 1e-8f) { normals[i] = Vector2.Zero; continue; }
            Vector2 t = d / len;
            normals[i] = new Vector2(-t.Y, t.X);
        }

        // ---- 3. 每点的左右偏移点（miter） ----
        var leftPts  = new Vector2[n];
        var rightPts = new Vector2[n];

        leftPts[0]      = P[0]      + normals[0]     * hw;
        rightPts[0]     = P[0]      - normals[0]     * hw;
        leftPts[n - 1]  = P[n - 1]  + normals[n - 2] * hw;
        rightPts[n - 1] = P[n - 1]  - normals[n - 2] * hw;

        for (int i = 1; i < n - 1; i++)
        {
            Vector2 n0 = normals[i - 1];
            Vector2 n1 = normals[i];
            float denom = 1f + n0.Dot(n1);

            if (denom < 1e-4f)
            {
                leftPts[i]  = P[i] + n0 * hw;
                rightPts[i] = P[i] - n0 * hw;
            }
            else
            {
                Vector2 miter = (n0 + n1) / denom;
                leftPts[i]  = P[i] + miter * hw;
                rightPts[i] = P[i] - miter * hw;
            }
        }

        // ---- 4. 组装网格 ----
        var gen = new PlaneMeshGenerator();

        // 4.1 条带四边形
        for (int i = 0; i < n - 1; i++)
        {
            gen.AddQuadFace(leftPts[i], leftPts[i + 1], rightPts[i + 1], rightPts[i]);
        }

        // 4.2 每个路径点处加一个圆盘
        for (int i = 0; i < n; i++)
        {
            gen.AddDisc(P[i], width * (1+circleRadius), discSegments);
            circleRadius *= 0.5f;
        }

        return gen.Commit();
    }
}
