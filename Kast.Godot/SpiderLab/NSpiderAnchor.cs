using Godot;
using System;

[Tool]
public partial class NSpiderAnchor : Node3D
{
    [Export] int SnapDistance = 6;
    [Export] bool IsForwardFoot = false;
    [Export] public Node3D IKTarget;
    [Export] public NSpiderAnchor PairFoot;

    /// <summary>
    /// 是否处于“触地”状态。由 NSpiderLegController 按腿状态设置：
    /// 只有 Enabled 时才会把锚点位置写进 IK 目标；悬空/动画状态保持不动，
    /// 让布娃娃物理或动画去驱动骨骼。
    /// </summary>
    [Export] public bool Enabled = true;

    /// <summary>
    /// 把锚点贴到射线的**命中点**（而不是自己写死的局部偏移 z=12）。
    /// 旧行为会把脚放到地面以下，腿就会穿地/极度折叠。
    /// </summary>
    [Export] public bool SnapToHitPoint = true;

    /// <summary>落脚点在命中点上方抬高多少（世界单位）。</summary>
    [Export] public float FootHeight = 0.05f;

    float TargetDistance = 0;
    float AutoSnapTimer = 0;

    RayCast3D Probe => GetParent() as RayCast3D;

    /// <summary>宿主骨架/刚体的等比缩放，用来把 SnapDistance 这类绝对距离按体型缩放。</summary>
    float BodyScale
    {
        get
        {
            RayCast3D probe = Probe;
            if (probe == null)
            {
                return 1f;
            }
            Vector3 scale = probe.GlobalTransform.Basis.Scale;
            return Mathf.Max(Mathf.Abs(scale.X), 0.0001f);
        }
    }

    /// <summary>请求下一物理帧立刻把 IK 目标吸附到锚点（脚落地时复位步态计时）。</summary>
    public void RequestSnap()
    {
        AutoSnapTimer = 0.0f;
    }

    /// <summary>取地面落点（射线命中点 + 抬脚高度）。拿不到返回 false。</summary>
    public bool TryGetGroundPoint(out Vector3 point)
    {
        point = Vector3.Zero;
        RayCast3D probe = Probe;
        if (!SnapToHitPoint || probe == null)
        {
            return false;
        }
        probe.ForceRaycastUpdate();
        if (!probe.IsColliding())
        {
            return false;
        }
        point = probe.GetCollisionPoint() + Vector3.Up * FootHeight;
        return true;
    }

    /// <summary>
    /// 立刻把 IK 目标钉到地面。
    /// <paramref name="useFallback"/> 为真且拿不到地面点时（例如刚进场景树、物理空间还没就绪），
    /// 用 <paramref name="fallback"/> 兜底，避免 IK 朝类型场景里写死的作者坐标把腿拉断。
    /// </summary>
    public bool SnapNow(Vector3 fallback, bool useFallback)
    {
        if (IKTarget == null)
        {
            return false;
        }

        AutoSnapTimer = 1.0f;
        if (TryGetGroundPoint(out Vector3 ground))
        {
            GlobalPosition = ground;
            IKTarget.GlobalPosition = ground;
            return true;
        }

        if (!useFallback)
        {
            return false;
        }
        GlobalPosition = fallback;
        IKTarget.GlobalPosition = fallback;
        return false;
    }

    public override void _PhysicsProcess(double delta)
    {
        // 编辑器里不动：否则打开类型场景时锚点/脚目标会被物理帧改写。
        if (Engine.IsEditorHint())
        {
            return;
        }

        if (IKTarget == null)
        {
            AutoSnapTimer = 0.0f;
            return;
        }

        // 触地时锚点跟着地面命中点走：这样“步距”判断才是相对当前落脚点，
        // 脚也会落在地面上，而不是固定局部偏移（旧行为会把脚放到地面以下）。
        if (Enabled && TryGetGroundPoint(out Vector3 ground))
        {
            GlobalPosition = ground;
        }

        // 非触地：不驱动 IK 目标；同时清零计时，等落地时立即吸附。
        if (!Enabled)
        {
            AutoSnapTimer = 0.0f;
            return;
        }

        if (AutoSnapTimer > 0)
        {
            AutoSnapTimer -= (float)delta;
        }

        var targetXZ = new Vector2(IKTarget.GlobalPosition.X, IKTarget.GlobalPosition.Z);
        var XZ = new Vector2(GlobalPosition.X, GlobalPosition.Z);
        TargetDistance = (targetXZ - XZ).LengthSquared();

        float factor = 1.0f;
        if (IsForwardFoot && PairFoot != null && !ReferenceEquals(PairFoot, this)
            && Mathf.Abs(PairFoot.TargetDistance - TargetDistance) < 1.0f)
        {
            // 对角/对侧脚步态接近时，前脚更早抬起，形成 trot 步态。
            factor = 0.2f * 0.2f;
        }

        // SnapDistance 按体型缩放：整体缩到 0.1 时步距应该是 0.6，而不是 6。
        float snap = SnapDistance * BodyScale;
        if (AutoSnapTimer <= 0 || TargetDistance > snap * snap * factor)
        {
            AutoSnapTimer = 1.0f;
            IKTarget.GlobalPosition = GlobalPosition;
        }
    }
}
