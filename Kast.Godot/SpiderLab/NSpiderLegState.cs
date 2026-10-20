using Godot;

/// <summary>
/// 蜘蛛腿的三种驱动状态（优先级从高到低：Animation &gt; Grounded &gt; Airborne）。
/// </summary>
public enum NSpiderLegState
{
    /// <summary>动画驱动：代码指定了要播放的动画，姿态完全交给动画（暂无动画资源，预留）。</summary>
    Animation = 0,
    /// <summary>触地：RayCast 命中地面，由 JacobianIK3D 把脚拉到锚点。</summary>
    Grounded = 1,
    /// <summary>悬空：RayCast 未命中，交给 PhysicalBoneSimulator3D 做布娃娃模拟。</summary>
    Airborne = 2,
}
