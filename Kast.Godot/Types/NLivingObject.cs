using System.Collections.Generic;
using Godot;

namespace Kast.Godot;

/// <summary>
/// “活物”类型基类：在 <see cref="NObjectType3D"/> 之上补三件事——
/// 1) 移动 API：<see cref="MoveTo"/> / <see cref="StopMoving"/>，可选 <see cref="NavigationAgent3D"/> 寻路；
/// 2) 站立高度：<see cref="StandHeight"/> 让刚体维持在离地一定高度，腿才有空间伸直（0 = 关闭，靠碰撞体支撑）；
/// 3) PhysicalBone 布娃娃：<see cref="SetRagdoll"/> 控制 <see cref="PhysicalBoneSimulator3D"/> 的启停。
///
/// 宿主是基类的 <see cref="NObjectType.Object"/>（<see cref="NObject"/>，一个 RigidBody3D）。
/// 移动与布娃娃互斥：布娃娃期间不再驱动刚体。
/// 所有绝对速度/距离都会乘 <see cref="SizeFactor"/>（宿主刚体的等比缩放），
/// 所以关卡里把实例 Scale 设成 0.1，也能得到一只比例、速度都正确的小蜘蛛。
/// </summary>
public abstract partial class NLivingObject : NObjectType3D
{
    // ------------------------------------------------------------------
    // 移动
    // ------------------------------------------------------------------
    /// <summary>水平巡航速度（会乘体型缩放）。</summary>
    [Export] public float MoveSpeed = 3f;
    /// <summary>速度逼近的快慢（越大越跟手）。</summary>
    [Export] public float Acceleration = 12f;
    /// <summary>转向逼近的快慢。</summary>
    [Export] public float TurnEase = 8f;
    /// <summary>离目标多近算到达（会乘体型缩放）。</summary>
    [Export] public float ArriveDistance = 0.4f;
    /// <summary>是否让刚体朝向移动方向。</summary>
    [Export] public bool FaceMoveDirection = true;
    /// <summary>可选寻路代理。留空会在 <see cref="NodeReady"/> 时自动在子树里查找。</summary>
    [Export] public NavigationAgent3D Navigation;

    Vector3? _destination;

    /// <summary>是否有移动目标。</summary>
    public bool HasDestination => _destination.HasValue;
    /// <summary>当前目标（没有时返回自身位置）。</summary>
    public Vector3 Destination => _destination ?? GlobalPosition;
    /// <summary>是否正在移动（有目标且非布娃娃）。</summary>
    public bool IsMoving => _destination.HasValue && !IsRagdoll;

    /// <summary>宿主刚体的等比缩放。把它乘到速度/距离上，整体放大缩小后行为才一致。</summary>
    public float SizeFactor
    {
        get
        {
            if (Object == null)
            {
                return 1f;
            }
            Vector3 scale = Object.GlobalTransform.Basis.Scale;
            return Mathf.Max(Mathf.Abs(scale.X), 0.0001f);
        }
    }

    /// <summary>到达目标（或 <see cref="StopMoving"/>）时发出。</summary>
    [Signal] public delegate void DestinationReachedEventHandler();

    // ------------------------------------------------------------------
    // 站立高度
    // ------------------------------------------------------------------
    /// <summary>
    /// 站立时刚体中心离地面的高度（本地单位，0 = 关闭）。
    /// 蜘蛛这类"用腿把身体撑起来"的生物必须开：否则刚体会被自己的碰撞体压在地面上，
    /// 腿没有下探空间，只能极度折叠 —— 看上去就是"骨骼严重变形"。
    /// 开启后用一根竖直弹簧把刚体维持在「地面 + StandHeight × 缩放」。
    /// </summary>
    [Export] public float StandHeight = 0f;
    /// <summary>站立弹簧刚度。</summary>
    [Export] public float StandStiffness = 40f;
    /// <summary>站立弹簧阻尼。</summary>
    [Export] public float StandDamping = 8f;
    /// <summary>竖直速度上限，防止弹飞。</summary>
    [Export] public float StandMaxSpeed = 6f;

    // ------------------------------------------------------------------
    // 布娃娃
    // ------------------------------------------------------------------
    /// <summary>物理骨骼的 influence。必须为 1：<see cref="Skeleton3D"/> 会按 influence 插值，0 会丢掉物理结果。</summary>
    [Export] public float RagdollInfluence = 1f;
    /// <summary>参与布娃娃的模拟器。留空会在 <see cref="NodeReady"/> 时自动在子树里查找。</summary>
    [Export] public PhysicalBoneSimulator3D PhysicalBones;

    /// <summary>是否处于全身布娃娃状态。</summary>
    public bool IsRagdoll { get; protected set; }

    // ------------------------------------------------------------------
    // PhysicalBone 碰撞
    // ------------------------------------------------------------------
    readonly List<PhysicalBone3D> _physicalBoneNodes = new List<PhysicalBone3D>();

    /// <summary>一根骨骼碰撞箱的原始数据与已应用的补偿系数。</summary>
    sealed class BoneShapeFix
    {
        public PhysicalBone3D Bone;
        public CollisionShape3D Node;
        public Shape3D Original;
        public Vector3 AuthoredPosition;
        public float Applied = float.NaN;
    }

    readonly List<BoneShapeFix> _boneShapeFixes = new List<BoneShapeFix>();

    /// <summary>本类型下的全部 PhysicalBone3D（供布娃娃碰撞开关与探针排除使用）。</summary>
    public IReadOnlyList<PhysicalBone3D> PhysicalBoneNodes => _physicalBoneNodes;

    /// <summary>
    /// 骨骼的碰撞开关，三种状态：
    /// <list type="bullet">
    /// <item>模拟中（<paramref name="enabled"/>）：层 = 宿主的层（含可瞄准层），掩码 = 宿主的掩码。
    /// 于是布娃娃能落到地形上、骨骼之间也能互撞。</item>
    /// <item>静止：只挂<b>可瞄准层</b>、掩码 0。既能让瞄准射线打到这条腿（
    /// <see cref="PlayerCamera.AimCollisionMask"/> 只看可瞄准层），又不会和任何东西发生物理接触——
    /// 静止时开着全部碰撞会把躯干顶飞、还会挡住腿部射线（脚会钉在自己身上）。</item>
    /// </list>
    /// </summary>
    public void SetPhysicalBoneCollision(PhysicalBone3D bone, bool enabled)
    {
        if (bone == null || !IsInstanceValid(bone))
        {
            return;
        }
        uint layer = Object != null ? Object.CollisionLayer : 1u;
        if (enabled)
        {
            bone.CollisionLayer = layer;
            bone.CollisionMask = Object != null ? Object.CollisionMask : 1u;
        }
        else
        {
            bone.CollisionLayer = layer & NObject.AimLayer;
            bone.CollisionMask = 0u;
        }
    }

    public void SetAllPhysicalBonesCollision(bool enabled)
    {
        for (int i = 0; i < _physicalBoneNodes.Count; i++)
        {
            SetPhysicalBoneCollision(_physicalBoneNodes[i], enabled);
        }
    }

    /// <summary>
    /// 把骨骼碰撞箱的尺度补回宿主刚体的缩放。
    ///
    /// Godot 用骨架的骨骼姿势摆放 <see cref="PhysicalBone3D"/>，而那套姿势是不带缩放的，
    /// 所以刚体缩到 0.1 时骨骼节点会拿到"世界尺度 1.0"：它挂着的碰撞箱（作者数据是模型单位，
    /// 例如 2×8×2 的箱子放在离骨骼 (0,3,0) 处）在世界里既大了 10 倍、又偏了 10 倍。
    /// 12 个这样的巨箱一进模拟就深插进地面，解算回弹顺着关节把躯干弹到 25～130 单位/秒——
    /// 就是"蜘蛛忽然瞬移几十米"。
    ///
    /// 这里按<b>实测</b>的骨骼世界尺度算补偿系数，把箱子的位置与尺寸一起缩回去。
    /// 用实测值而不是写死 1/SizeFactor：哪天 Godot 把骨骼摆对了，系数自动变成 1，不用改代码。
    /// </summary>
    void SyncBoneShapeScale()
    {
        if (_boneShapeFixes.Count == 0)
        {
            return;
        }
        float wanted = SizeFactor;
        for (int i = 0; i < _boneShapeFixes.Count; i++)
        {
            BoneShapeFix fix = _boneShapeFixes[i];
            if (fix.Node == null || !IsInstanceValid(fix.Node) || fix.Bone == null || !IsInstanceValid(fix.Bone))
            {
                continue;
            }
            float actual = Mathf.Abs(fix.Bone.GlobalTransform.Basis.Scale.X);
            if (actual < 1e-6f)
            {
                continue;
            }
            float k = wanted / actual;
            if (!float.IsNaN(fix.Applied) && Mathf.Abs(k - fix.Applied) < 1e-3f)
            {
                continue;
            }
            fix.Applied = k;
            fix.Node.Position = fix.AuthoredPosition * k;
            fix.Node.Shape = ScaleShape(fix.Original, k);
        }
    }

    /// <summary>复制一份形状并按 <paramref name="k"/> 缩放（不改共享资源）；认不出的形状原样返回。</summary>
    static Shape3D ScaleShape(Shape3D original, float k)
    {
        if (original == null || Mathf.Abs(k - 1f) < 1e-3f)
        {
            return original;
        }
        if (original.Duplicate() is not Shape3D copy)
        {
            return original;
        }
        switch (copy)
        {
            case BoxShape3D box:
                box.Size *= k;
                break;
            case CapsuleShape3D capsule:
                capsule.Radius *= k;
                capsule.Height *= k;
                break;
            case SphereShape3D sphere:
                sphere.Radius *= k;
                break;
            case CylinderShape3D cylinder:
                cylinder.Radius *= k;
                cylinder.Height *= k;
                break;
            default:
                return original;
        }
        return copy;
    }

    void CollectPhysicalBones()
    {
        _physicalBoneNodes.Clear();
        _boneShapeFixes.Clear();
        if (PhysicalBones == null)
        {
            return;
        }
        foreach (PhysicalBone3D bone in FindAll<PhysicalBone3D>(PhysicalBones))
        {
            _physicalBoneNodes.Add(bone);
            // 层与掩码都从宿主刚体推（由 Types/*.tscn 的 Layer / Mask + 可瞄准层决定）。
            SetPhysicalBoneCollision(bone, false);
            foreach (Node child in bone.GetChildren())
            {
                if (child is CollisionShape3D shape && shape.Shape != null)
                {
                    _boneShapeFixes.Add(new BoneShapeFix
                    {
                        Bone = bone,
                        Node = shape,
                        Original = shape.Shape,
                        AuthoredPosition = shape.Position,
                    });
                }
            }
            // 布娃娃时骨骼要能和地面碰撞，但绝不能撞到自己的躯干刚体——否则会把身体顶飞。
            if (Object != null)
            {
                bone.AddCollisionExceptionWith(Object);
                Object.AddCollisionExceptionWith(bone);
            }
        }
    }

    static IEnumerable<T> FindAll<T>(Node root) where T : class
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is T typed)
            {
                yield return typed;
            }
            foreach (T nested in FindAll<T>(child))
            {
                yield return nested;
            }
        }
    }

    /// <summary>装配模拟器 / 导航代理，由 <see cref="NObjectType3D"/> 的 sealed _Ready 回调。</summary>
    public override void NodeReady()
    {
        PhysicalBones ??= FindFirst<PhysicalBoneSimulator3D>(this);
        Navigation ??= FindFirst<NavigationAgent3D>(this);

        if (PhysicalBones != null)
        {
            PhysicalBones.Influence = RagdollInfluence;
        }
        CollectPhysicalBones();
        if (Navigation != null)
        {
            float size = SizeFactor;
            Navigation.MaxSpeed = MoveSpeed * size;
            Navigation.Radius = Mathf.Max(Navigation.Radius * size, 0.02f);
            Navigation.Height = Mathf.Max(Navigation.Height * size, 0.02f);
        }

        OnLivingReady();
    }

    /// <summary>子类的初始化回调（<see cref="NodeReady"/> 的延伸）。</summary>
    protected virtual void OnLivingReady() { }

    // ------------------------------------------------------------------
    // 移动 API
    // ------------------------------------------------------------------

    /// <summary>走到世界坐标 <paramref name="worldDestination"/>；挂了 <see cref="Navigation"/> 时自动寻路。</summary>
    public virtual void MoveTo(Vector3 worldDestination)
    {
        if (IsRagdoll)
        {
            return;
        }
        _destination = worldDestination;
        if (Navigation != null)
        {
            Navigation.TargetPosition = worldDestination;
        }
        PlayAnimation("walk");
    }

    /// <summary>停止移动、清空目标并清零水平速度。</summary>
    public virtual void StopMoving()
    {
        if (_destination == null)
        {
            return;
        }
        _destination = null;
        if (Object != null)
        {
            Vector3 velocity = Object.LinearVelocity;
            Object.LinearVelocity = new Vector3(0f, velocity.Y, 0f);
        }
        PlayAnimation("stop");
        EmitSignal(SignalName.DestinationReached);
    }

    public override void _PhysicsProcess(double delta)
    {
        SyncBoneShapeScale();
        if (Object == null || IsRagdoll)
        {
            return;
        }

        float dt = (float)delta;
        float size = SizeFactor;

        if (StandHeight > 0f)
        {
            ApplyStand(dt, size);
        }

        if (!_destination.HasValue)
        {
            return;
        }

        Vector3 waypoint = NextWaypoint();
        Vector3 to = waypoint - Object.GlobalPosition;
        to.Y = 0f;

        // 终点判别只在最后一个路径点上做，避免半路停下。
        float arrive = ArriveDistance * size;
        if (IsFinalWaypoint() && to.LengthSquared() <= arrive * arrive)
        {
            StopMoving();
            return;
        }
        if (to.LengthSquared() < 1e-6f)
        {
            return;
        }

        Vector3 direction = to.Normalized();
        Vector3 targetVelocity = direction * (MoveSpeed * size);
        Vector3 velocity = Object.LinearVelocity;
        Vector3 horizontal = new Vector3(velocity.X, 0f, velocity.Z)
            .Lerp(targetVelocity, 1f - Mathf.Exp(-Acceleration * dt));
        Object.LinearVelocity = new Vector3(horizontal.X, velocity.Y, horizontal.Z);

        if (FaceMoveDirection)
        {
            // 与 PlayerCamera 的转向写法保持一致：yaw 由 -dir 求得，直接写刚体 Rotation.Y。
            float targetYaw = Mathf.Atan2(-direction.X, -direction.Z);
            float yaw = Mathf.LerpAngle(Object.Rotation.Y, targetYaw, 1f - Mathf.Exp(-TurnEase * dt));
            Object.Rotation = new Vector3(0f, yaw, 0f);
        }
    }

    /// <summary>向下打射线找地面，再用竖直弹簧把刚体维持在「地面 + StandHeight × 缩放」。</summary>
    void ApplyStand(float dt, float size)
    {
        Vector3 from = Object.GlobalPosition;
        Vector3 to = from + Vector3.Down * (StandHeight * size * 2f + 2f);
        var exclude = new global::Godot.Collections.Array<Rid> { Object.GetRid() };
        var query = PhysicsRayQueryParameters3D.Create(from, to, Object.CollisionMask, exclude);
        var hit = Object.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0)
        {
            return;
        }

        float groundY = hit["position"].AsVector3().Y;
        float targetY = groundY + StandHeight * size;
        Vector3 velocity = Object.LinearVelocity;
        float error = targetY - Object.GlobalPosition.Y;
        float targetVy = Mathf.Clamp(error * StandStiffness, -StandMaxSpeed, StandMaxSpeed);
        float vy = Mathf.Lerp(velocity.Y, targetVy, 1f - Mathf.Exp(-StandDamping * dt));
        Object.LinearVelocity = new Vector3(velocity.X, vy, velocity.Z);
    }

    /// <summary>导航可用（存在且目标可达）时才走寻路，否则退回直线，避免没烘焙 NavMesh 时原地不动。</summary>
    bool NavigationUsable()
    {
        return Navigation != null && Navigation.IsTargetReachable();
    }

    Vector3 NextWaypoint()
    {
        if (!NavigationUsable())
        {
            return _destination ?? GlobalPosition;
        }
        return Navigation.GetNextPathPosition();
    }

    bool IsFinalWaypoint()
    {
        return !NavigationUsable() || Navigation.IsNavigationFinished();
    }

    // ------------------------------------------------------------------
    // 布娃娃 API
    // ------------------------------------------------------------------

    /// <summary>
    /// 布娃娃已移除。
    ///
    /// 原因：Godot 的 <c>PhysicalBone3D</c> 完全无视祖先缩放 —— 模拟时骨骼世界缩放恒为 1.0、
    /// 关节把腿拉到"模型单位"的距离（实测 0.1 缩放的蜘蛛，骨骼被甩到离身体 8 个世界单位），
    /// 于是整只生物被撑开、看着像放大了 10 倍。节点级补偿全部被引擎覆盖，所以不再启用。
    /// 保留这个空实现只为不破坏调用方；移动与站立全部由 IK 负责。
    /// </summary>
    public virtual void SetRagdoll(bool enabled)
    {
        // 布娃娃已移除：不做任何事。
    }

    /// <summary>在子树里深度优先找第一个 <typeparamref name="T"/>。</summary>
    protected static T FindFirst<T>(Node root) where T : class
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is T typed)
            {
                return typed;
            }
            T nested = FindFirst<T>(child);
            if (nested != null)
            {
                return nested;
            }
        }
        return null;
    }
}
