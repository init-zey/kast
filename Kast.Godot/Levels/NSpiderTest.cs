using Godot;
using Kast.Godot;

/// <summary>
/// 蜘蛛测试关卡：用 NLevel 正常生成玩家与相机，并接管场景里那只 NSpider 做巡逻 / 布娃娃测试。
/// 按住 R = 全身布娃娃，松开 = 恢复站立；到达目标后隔几秒随机选下一个巡逻点。
/// </summary>
public partial class NSpiderTest : NLevel
{
    /// <summary>巡逻半径（以蜘蛛初始位置为中心）。</summary>
    [Export] public float PatrolRadius = 8f;
    /// <summary>两次巡逻之间的等待秒数。</summary>
    [Export] public float PatrolInterval = 4f;

    NObject _spider;
    NSpider _living;
    NSpiderAI _ai;
    Vector3 _home;
    double _timer;
    bool _scaleKeyHeld;

    public override void _Ready()
    {
        if (NGame.Instance == null)
        {
            GD.PushWarning("[NSpiderTest] 请通过 Game.tscn 加载本关卡（NGame.Instance 为空，已跳过玩家/相机的生成）。");
            return;
        }

        base._Ready();

        _spider = FindSpider(this);
        _living = _spider?.Type as NSpider;
        if (_spider != null)
        {
            _home = _spider.GlobalPosition;
        }
        _ai = FindAI(_spider);
        if (_ai != null)
        {
            // 手动目视测试时打开状态日志，方便对照 docs/spider-test-plan.md 的 D2 清单。
            _ai.LogState = true;
        }
        _timer = 1.0;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_living == null || _spider == null)
        {
            return;
        }

        bool ragdollKey = Input.IsPhysicalKeyPressed(Key.R);
        if (ragdollKey != _living.IsRagdoll)
        {
            _living.SetRagdoll(ragdollKey);
        }
        // 变异时按 F9 打一份缩放数据，方便定位"失去缩放效果"。
        bool scaleKey = Input.IsPhysicalKeyPressed(Key.F9);
        if (scaleKey && !_scaleKeyHeld)
        {
            DumpScale("F9");
        }
        _scaleKeyHeld = scaleKey;

        if (_living.IsRagdoll)
        {
            return;
        }

        // 追击 AI 接管期间不巡逻：两边都调 MoveTo 会互相打断，蜘蛛会走一步停一步。
        if (_ai != null && _ai.AiEnabled && _ai.State != NSpiderAI.AiState.Idle)
        {
            _timer = PatrolInterval;
            return;
        }

        _timer -= delta;
        if (_timer <= 0.0 && !_living.IsMoving)
        {
            _timer = PatrolInterval;
            Vector2 dir = new Vector2((float)GD.RandRange(-1.0, 1.0), (float)GD.RandRange(-1.0, 1.0));
            if (dir.LengthSquared() < 1e-4f)
            {
                dir = Vector2.Right;
            }
            // 巡逻半径也按体型缩放：蜘蛛整体缩到 0.1 时半径应该是 0.8 而不是 8。
            dir = dir.Normalized() * PatrolRadius * _living.SizeFactor;
            _living.MoveTo(_home + new Vector3(dir.X, 0f, dir.Y));
        }
    }

    /// <summary>按下 F9 时打印一份"蜘蛛看上去多大"的数据，用来定位缩放类问题。</summary>
    void DumpScale(string tag)
    {
        var bones = _living.PhysicalBoneNodes;
        float spread = 0f;
        float boneScale = 0f;
        float farthest = 0f;
        Vector3 min = Vector3.Zero, max = Vector3.Zero;
        bool first = true;
        var meshes = new System.Collections.Generic.List<MeshInstance3D>();
        CollectMeshes(_spider, meshes);
        foreach (MeshInstance3D m in meshes)
        {
            Aabb box = m.GetAabb();
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = box.Position + new Vector3(
                    (i & 1) == 0 ? 0f : box.Size.X,
                    (i & 2) == 0 ? 0f : box.Size.Y,
                    (i & 4) == 0 ? 0f : box.Size.Z);
                Vector3 w = m.GlobalTransform * c;
                if (first) { min = w; max = w; first = false; }
                else { min = min.Min(w); max = max.Max(w); }
            }
        }
        var sk0 = _living.PhysicalBones != null ? _living.PhysicalBones.GetParent() as Skeleton3D : null;
        float worstPose = 1f;
        string worstPoseWho = "-";
        foreach (PhysicalBone3D bone in bones)
        {
            float d = (bone.GlobalPosition - _spider.GlobalPosition).Length();
            if (d > farthest) { farthest = d; }
            spread = Mathf.Max(spread, d);
            boneScale = Mathf.Max(boneScale, bone.GlobalTransform.Basis.Scale.X);
            int bi = bone.GetBoneId();
            if (sk0 != null && bi >= 0)
            {
                float ps = sk0.GetBoneGlobalPose(bi).Basis.Scale.X;
                if (Mathf.Abs(ps - 1f) > Mathf.Abs(worstPose - 1f)) { worstPose = ps; worstPoseWho = bone.Name; }
            }
        }
        var meshInfo = new System.Text.StringBuilder();
        foreach (MeshInstance3D m in meshes)
        {
            meshInfo.Append($" {m.Name}[节点缩放={m.GlobalTransform.Basis.Scale.ToString("F4")} 局部AABB={m.GetAabb().Size.ToString("F3")} skin={m.Skin}]");
        }
        var sk = _living.PhysicalBones != null ? _living.PhysicalBones.GetParent() as Skeleton3D : null;
        float skelScale = sk != null ? sk.GlobalTransform.Basis.Scale.X : -1f;
        float poseScale = -1f;
        if (sk != null && bones.Count > 0)
        {
            poseScale = sk.GetBoneGlobalPose(bones[0].GetBoneId()).Basis.Scale.X;
        }
        // 蒙皮变换 = 骨骼世界变换 × 绑定逆矩阵；它的缩放就是最终渲染出来的缩放。
        float skinScale = -1f;
        float boneWorld = bones.Count > 0 ? bones[0].GlobalTransform.Basis.Scale.X : -1f;
        if (meshes.Count > 0 && meshes[0].Skin != null && bones.Count > 0)
        {
            Skin skin = meshes[0].Skin;
            int bindBone = bones[0].GetBoneId();
            for (int b = 0; b < skin.GetBindCount(); b++)
            {
                if (skin.GetBindBone(b) == bindBone)
                {
                    skinScale = (bones[0].GlobalTransform * skin.GetBindPose(b)).Basis.Scale.X;
                    break;
                }
            }
        }
        GD.Print($"[SCALE] {tag}: 本体={_spider.Scale.X:F4} 骨架={skelScale:F4} 骨骼世界={boneWorld:F4} 蒙皮={skinScale:F4} bone0姿势={poseScale:F4} 最差姿势={worstPose:F4}({worstPoseWho}) 离身体={spread:F3} 包围盒={(max - min).ToString("F3")} IsRagdoll={_living.IsRagdoll}");
        GD.Print($"[SCALE] {tag}: 网格{meshInfo}");
    }

    static void CollectMeshes(Node root, System.Collections.Generic.List<MeshInstance3D> into)
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is MeshInstance3D mesh) { into.Add(mesh); }
            CollectMeshes(child, into);
        }
    }

    /// <summary>在蜘蛛子树里找它的追击 AI。</summary>
    static NSpiderAI FindAI(Node root)
    {
        if (root == null)
        {
            return null;
        }
        foreach (Node child in root.GetChildren())
        {
            if (child is NSpiderAI ai)
            {
                return ai;
            }
            NSpiderAI nested = FindAI(child);
            if (nested != null)
            {
                return nested;
            }
        }
        return null;
    }

    /// <summary>在子树里找由 NLevel.ScanAndSpawn 生成的那只蜘蛛 NObject。</summary>
    static NObject FindSpider(Node root)
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is NObject obj && obj.Type is NSpider)
            {
                return obj;
            }
            NObject nested = FindSpider(child);
            if (nested != null)
            {
                return nested;
            }
        }
        return null;
    }
}
