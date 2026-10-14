using Godot;
using System.Collections.Generic;

/// <summary>
/// XY 平面网格构造器。往里面 add 四边形，最后 Commit 得到一个 ArrayMesh。
/// 所有顶点 Z = 0，法线统一朝 +Z。
/// </summary>
public class PlaneMeshGenerator
{
    private readonly List<Vector3> _vertices = new();
    private readonly List<Vector3> _normals  = new();
    private readonly List<Vector2> _uvs      = new();
    private readonly List<int>     _indices  = new();

    /// <summary>
    /// 添加一个四边形面。
    /// 四个顶点必须按"从 +Z 方向看过去的顺时针顺序"环绕，
    /// 即：p0 → p1 → p2 → p3 形成一圈，不许交叉。
    /// 
    /// 例如，对一个左下-右上的矩形，正确的顺序是：
    ///     p0 = 左上, p1 = 右上, p2 = 右下, p3 = 左下
    /// </summary>
    public void AddQuadFace(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3)
    {
        int baseIdx = _vertices.Count;

        // ---- 4 个顶点 ----
        _vertices.Add(new Vector3(p0.X, p0.Y, 0f));
        _vertices.Add(new Vector3(p1.X, p1.Y, 0f));
        _vertices.Add(new Vector3(p2.X, p2.Y, 0f));
        _vertices.Add(new Vector3(p3.X, p3.Y, 0f));

        // ---- 法线全部朝 +Z ----
        _normals.Add(Vector3.Back);   // (0,0,1)
        _normals.Add(Vector3.Back);
        _normals.Add(Vector3.Back);
        _normals.Add(Vector3.Back);

        // ---- UV：按 p0..p3 顺序铺开 ----
        _uvs.Add(new Vector2(0f, 0f));
        _uvs.Add(new Vector2(1f, 0f));
        _uvs.Add(new Vector2(1f, 1f));
        _uvs.Add(new Vector2(0f, 1f));

        // ---- 两个三角形，保持相同绕序 ----
        // 三角形 1: p0 → p1 → p2
        _indices.Add(baseIdx + 0);
        _indices.Add(baseIdx + 1);
        _indices.Add(baseIdx + 2);

        // 三角形 2: p0 → p2 → p3
        _indices.Add(baseIdx + 0);
        _indices.Add(baseIdx + 2);
        _indices.Add(baseIdx + 3);
    }

    /// <summary>把累积的几何提交为 ArrayMesh。没有顶点时返回 null。</summary>
    public ArrayMesh Commit()
    {
        if (_vertices.Count == 0)
        {
            GD.PushWarning("PlaneMeshGenerator.Commit: 没有添加任何面");
            return null;
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = _vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = _normals.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV]  = _uvs.ToArray();
        arrays[(int)Mesh.ArrayType.Index]  = _indices.ToArray();

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    /// <summary>清空内部缓冲，允许复用同一个构造器。</summary>
    public void Clear()
    {
        _vertices.Clear();
        _normals.Clear();
        _uvs.Clear();
        _indices.Clear();
    }
    
    private const float DiscZOffset = 0.0001f;

    /// <summary>
    /// 在 center 处加一个半径 radius 的圆盘（三角扇）。
    /// segments 是圆周分段数，越大越圆滑；UV 是圆盘局部 [0,1]² 内切圆贴图。
    /// </summary>
    public void AddDisc(Vector2 center, float radius, int segments = 24)
    {
        if (radius <= 0f) return;
        if (segments < 3) segments = 3;

        int baseIdx = _vertices.Count;

        // ---- 中心点 ----
        _vertices.Add(new Vector3(center.X, center.Y, DiscZOffset));
        _normals.Add(Vector3.Back);
        _uvs.Add(new Vector2(0.5f, 0.5f));

        // ---- 圆周点（角度从 0 递增，即从 +Z 看逆时针）----
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.Tau / segments;
            float c = Mathf.Cos(a);
            float s = Mathf.Sin(a);

            _vertices.Add(new Vector3(center.X + c * radius, center.Y + s * radius, DiscZOffset));
            _normals.Add(Vector3.Back);
            _uvs.Add(new Vector2(0.5f + 0.5f * c, 0.5f + 0.5f * s));
        }

        // ---- 三角扇：从 +Z 看顺时针 = center → next → cur ----
        for (int i = 0; i < segments; i++)
        {
            int cur  = baseIdx + 1 + i;
            int next = baseIdx + 1 + (i + 1) % segments;
            _indices.Add(baseIdx);
            _indices.Add(next);
            _indices.Add(cur);
        }
    }
}