using Godot;

/// <summary>
/// 一条蜘蛛腿的运行时数据（普通类，不参与场景树）。
/// 由 <see cref="NSpiderLegController"/> 创建并驱动。
/// </summary>
public class NSpiderLeg
{
    /// <summary>在 JacobianIK3D 中的 setting 索引。</summary>
    public int IkIndex;

    /// <summary>腿名（IK 目标节点名，如 LF / LM / LB ...）。</summary>
    public string LegName = "";

    /// <summary>探测地面的射线。</summary>
    public RayCast3D Probe;

    /// <summary>脚部锚点（负责把地面位置喂给 IK 目标）。</summary>
    public NSpiderAnchor Anchor;

    /// <summary>IK 目标节点（场景根下的 LF/LM/... 顶层节点）。</summary>
    public Node3D IkTarget;

    /// <summary>IK 目标在 JacobianIK3D 上注册的原始 NodePath，重新启用 IK 时恢复。</summary>
    public NodePath IkTargetPath;

    /// <summary>参与布娃娃模拟的 PhysicalBone3D 骨骼名。</summary>
    public string[] RagdollBones = System.Array.Empty<string>();

    /// <summary>与 <see cref="RagdollBones"/> 对应的 PhysicalBone3D 节点，用于开关骨骼碰撞。</summary>
    public System.Collections.Generic.List<PhysicalBone3D> RagdollBoneNodes = new System.Collections.Generic.List<PhysicalBone3D>();

    /// <summary>当前状态。</summary>
    public NSpiderLegState State = NSpiderLegState.Animation;

    /// <summary>当前该腿的 IK 是否启用（target_node 非空）。</summary>
    public bool IkEnabled;

    /// <summary>该腿单独请求播放的动画名（空表示没有单独请求）。</summary>
    public string RequestedAnimation;

    /// <summary>是否有单独动画请求。</summary>
    public bool HasAnimationRequest => !string.IsNullOrEmpty(RequestedAnimation);

    /// <summary>是否有可用的射线。</summary>
    public bool HasProbe => Probe != null;
}
