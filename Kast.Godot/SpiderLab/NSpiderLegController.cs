using Godot;
using System;
using System.Collections.Generic;
using Kast.Godot;

/// <summary>
/// 蜘蛛腿状态机。
///
/// 状态优先级（从高到低）：
///   1. Animation  代码通过 <see cref="PlayAnimation"/> / <see cref="PlayLegAnimation"/> 指定播放动画 -> 动画驱动；
///   2. Grounded   RayCast 发生碰撞 -> 触地，由 JacobianIK3D 驱动；
///   3. Airborne   否则 -> 悬空，由 PhysicalBoneSimulator3D 做布娃娃效果。
///
/// 挂载在 Skeleton3D 下（与 JacobianIK3D / PhysicalBoneSimulator3D 同级）。
/// 每帧为每条腿求值并切换状态；状态切换时开关该腿的 IK target 与布娃娃模拟。
/// </summary>
public partial class NSpiderLegController : Node3D
{
    /// <summary>腿状态变化时发出（legIndex, state）。</summary>
    [Signal]
    public delegate void LegStateChangedEventHandler(int legIndex, int state);

    /// <summary>状态机总开关。关闭后不再改动 IK target 与物理模拟。</summary>
    [Export] public bool EnableStateMachine = true;

    /// <summary>可选：Animation 状态使用的动画播放器。留空会在同级节点里自动查找。</summary>
    [Export] public AnimationPlayer AnimationPlayer;

    /// <summary>是否把每条腿的状态打印到控制台（调试用）。</summary>
    [Export] public bool DebugLog = false;

    /// <summary>
    /// "在坠落"的判定：竖直速度超过 <c>FallSpeedFactor × SizeFactor</c>（单位/秒）就不算站稳。
    /// 出生下落/被击飞时，腿一交给物理就会带着下坠速度穿进地面，
    /// 解算出的回弹能把骨骼飙到上千单位/秒再把躯干弹飞。
    /// </summary>
    [Export] public float FallSpeedFactor = 8f;

    Skeleton3D _skeleton;
    JacobianIK3D _ik;
    PhysicalBoneSimulator3D _physics;
    /// <summary>宿主活物，用来统一管理 PhysicalBone 的碰撞开关。</summary>
    NLivingObject _living;
    /// <summary>本帧是否至少有一条腿触地（决定悬空腿是"撑住"还是"布娃娃"）。</summary>
    bool _supported;

    readonly List<NSpiderLeg> _legs = new List<NSpiderLeg>();
    readonly HashSet<int> _ragdollNow = new HashSet<int>();
    readonly HashSet<int> _ragdollNext = new HashSet<int>();

    // 全局动画请求；非空时所有腿进入 Animation 状态。
    string _globalAnimation;

    public int LegCount => _legs.Count;

    /// <summary>当前正在布娃娃模拟的腿数（出生下落期间应为 0）。</summary>
    public int RagdollLegCount => _ragdollNow.Count;

    /// <summary>本帧整只生物是否被至少一条腿支撑着。</summary>
    public bool IsSupported => _supported;

    /// <summary>
    /// 清空并重建六条腿的绑定，然后重新应用状态。
    /// 供 <see cref="NSpider"/> 在重接 IK 路径后调用；正常情况下 _Ready 已接好，不需要手动调。
    /// </summary>
    public void Rebuild()
    {
        _legs.Clear();
        _ragdollNow.Clear();
        BuildLegs();

        if (!EnableStateMachine)
        {
            return;
        }

        for (int i = 0; i < _legs.Count; i++)
        {
            ApplyState(_legs[i], ResolveState(_legs[i]), true);
        }
        SyncRagdoll();
    }

    public override void _Ready()
    {
        base._Ready();

        _skeleton = GetParent() as Skeleton3D;
        if (_skeleton == null)
        {
            GD.PushError("[NSpiderLegController] 必须作为 Skeleton3D 的子节点。");
            SetPhysicsProcess(false);
            return;
        }

        // 向上找宿主 NLivingObject（蜘蛛类型根），用于统一管理 PhysicalBone 碰撞。
        Node walk = _skeleton;
        while (walk != null && !(walk is NLivingObject))
        {
            walk = walk.GetParent();
        }
        _living = walk as NLivingObject;

        foreach (Node child in _skeleton.GetChildren())
        {
            if (child is JacobianIK3D ik)
            {
                _ik = ik;
            }
            else if (child is PhysicalBoneSimulator3D simulator)
            {
                _physics = simulator;
            }
            else if (AnimationPlayer == null && child is AnimationPlayer player)
            {
                AnimationPlayer = player;
            }
        }

        if (_physics != null)
        {
            // 注意：Skeleton3D 会按 influence 插值 modifier 的结果，
            // influence = 0 时物理结果会被完全丢弃，所以布娃娃必须保持 1。
            _physics.Influence = 1.0f;
            _physics.PhysicalBonesStopSimulation();
        }

        BuildLegs();

        if (EnableStateMachine)
        {
            for (int i = 0; i < _legs.Count; i++)
            {
                ApplyState(_legs[i], ResolveState(_legs[i]), true);
            }
            SyncRagdoll();
        }

        GD.Print($"[NSpiderLegController] 已接管 {_legs.Count} 条腿（IK={( _ik != null )}, Physics={( _physics != null )}）。");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!EnableStateMachine || _legs.Count == 0)
        {
            return;
        }

        for (int i = 0; i < _legs.Count; i++)
        {
            NSpiderLeg leg = _legs[i];
            ApplyState(leg, ResolveState(leg));
        }

        // 是否"被支撑"：至少有一条腿触地。
        // 整只生物都不着地时（出生下落、被击飞），悬空腿绝不能转布娃娃——
        // 否则落地瞬间要从瘫软姿态猛拉回 IK，视觉上就是"出生时骨骼严重变形"。
        _supported = false;
        for (int i = 0; i < _legs.Count; i++)
        {
            if (_legs[i].State == NSpiderLegState.Grounded)
            {
                _supported = true;
                break;
            }
        }
        // "有腿触地"还不等于"站稳"：下落途中射线会先扫到地面，这时候把其余腿交给物理，
        // 骨骼会带着下坠速度穿进地面，回弹能把躯干弹飞。竖直速度过大时一律按"没支撑"处理。
        if (_supported && _living != null && _living.Object != null)
        {
            float limit = FallSpeedFactor * Mathf.Max(_living.SizeFactor, 0.01f);
            if (Mathf.Abs(_living.Object.LinearVelocity.Y) > limit)
            {
                _supported = false;
            }
        }

        if (!_supported)
        {
            for (int i = 0; i < _legs.Count; i++)
            {
                if (_legs[i].State == NSpiderLegState.Airborne)
                {
                    HoldLeg(_legs[i]);
                }
            }
        }

        SyncRagdoll();
    }

    /// <summary>
    /// 悬空但整只生物没有被支撑时：保持 IK，把脚维持在该腿锚点的静止位（沿射线方向、随身体走）。
    /// 这样出生下落/被击飞时腿是"撑住"的姿态，落地能无缝切回触地 IK。
    /// </summary>
    void HoldLeg(NSpiderLeg leg)
    {
        SetLegIk(leg, true);
        if (leg.Anchor != null)
        {
            leg.Anchor.Enabled = false;
        }
        RayCast3D probe = leg.Probe;
        if (probe == null || leg.IkTarget == null)
        {
            return;
        }
        // 锚点的作者局部偏移是沿射线 z=12；用射线自身变换算，天然带缩放且与身体相对。
        leg.IkTarget.GlobalPosition = probe.GlobalTransform.Origin + probe.GlobalTransform.Basis.Z * 12f;
    }

    // ------------------------------------------------------------------
    // 对外 API（动画预留）
    // ------------------------------------------------------------------

    /// <summary>全局指定动画：所有腿进入 Animation 状态并由动画驱动。</summary>
    public void PlayAnimation(string animationName)
    {
        _globalAnimation = animationName;
        if (AnimationPlayer != null && AnimationPlayer.HasAnimation(animationName))
        {
            AnimationPlayer.Play(animationName);
        }
    }

    /// <summary>结束全局动画，腿恢复 RayCast 自动判定。</summary>
    public void StopAnimation()
    {
        _globalAnimation = null;
        if (AnimationPlayer != null)
        {
            AnimationPlayer.Stop();
        }
    }

    /// <summary>单独指定某条腿播放动画（按 IK 索引）。</summary>
    public void PlayLegAnimation(int legIndex, string animationName)
    {
        NSpiderLeg leg = FindLeg(legIndex);
        if (leg != null)
        {
            leg.RequestedAnimation = animationName;
        }
    }

    /// <summary>单独指定某条腿播放动画（按腿名，如 "LF"）。</summary>
    public void PlayLegAnimation(string legName, string animationName)
    {
        NSpiderLeg leg = FindLeg(legName);
        if (leg != null)
        {
            leg.RequestedAnimation = animationName;
        }
    }

    /// <summary>取消某条腿的动画请求。</summary>
    public void StopLegAnimation(int legIndex)
    {
        NSpiderLeg leg = FindLeg(legIndex);
        if (leg != null)
        {
            leg.RequestedAnimation = null;
        }
    }

    /// <summary>取消某条腿的动画请求。</summary>
    public void StopLegAnimation(string legName)
    {
        NSpiderLeg leg = FindLeg(legName);
        if (leg != null)
        {
            leg.RequestedAnimation = null;
        }
    }

    public NSpiderLegState GetLegState(int legIndex)
    {
        NSpiderLeg leg = FindLeg(legIndex);
        return leg != null ? leg.State : NSpiderLegState.Airborne;
    }

    public NSpiderLegState GetLegState(string legName)
    {
        NSpiderLeg leg = FindLeg(legName);
        return leg != null ? leg.State : NSpiderLegState.Airborne;
    }

    // ------------------------------------------------------------------
    // 内部实现
    // ------------------------------------------------------------------

    void BuildLegs()
    {
        if (_ik == null)
        {
            GD.PushWarning("[NSpiderLegController] 未找到 JacobianIK3D，无法建立腿部状态。");
            return;
        }

        var probes = new List<RayCast3D>();
        foreach (Node child in _skeleton.GetChildren())
        {
            if (child is RayCast3D probe)
            {
                probes.Add(probe);
            }
        }

        int settingCount = _ik.SettingCount;
        for (int i = 0; i < settingCount; i++)
        {
            NodePath targetPath = _ik.GetTargetNode(i);
            if (targetPath.IsEmpty)
            {
                continue;
            }

            // target_node 的 NodePath 是相对于 JacobianIK3D 节点的，必须从 _ik 解析。
            Node3D target = _ik.GetNodeOrNull<Node3D>(targetPath);
            if (target == null)
            {
                continue;
            }

            NSpiderAnchor anchor = null;
            RayCast3D owner = null;
            for (int p = 0; p < probes.Count; p++)
            {
                NSpiderAnchor candidate = probes[p].GetNodeOrNull<NSpiderAnchor>("Anchor");
                if (candidate != null && candidate.IKTarget == target)
                {
                    anchor = candidate;
                    owner = probes[p];
                    break;
                }
            }

            if (anchor == null || owner == null)
            {
                continue;
            }

            var leg = new NSpiderLeg
            {
                IkIndex = i,
                LegName = target.Name.ToString(),
                Probe = owner,
                Anchor = anchor,
                IkTarget = target,
                IkTargetPath = targetPath,
                IkEnabled = true, // 场景加载时 target_node 本来就是有效的
                State = NSpiderLegState.Animation,
            };
            leg.RagdollBones = CollectRagdollBones(i, leg.RagdollBoneNodes);
            _legs.Add(leg);
        }
    }

    /// <summary>收集某条腿链上存在的 PhysicalBone3D：名字给模拟器，节点用来开关碰撞。</summary>
    string[] CollectRagdollBones(int setting, List<PhysicalBone3D> nodes)
    {
        if (_physics == null || _ik == null)
        {
            return Array.Empty<string>();
        }

        int root = _ik.GetRootBone(setting);
        int end = _ik.GetEndBone(setting);
        if (root < 0 || end < 0)
        {
            return Array.Empty<string>();
        }

        var chain = new HashSet<int>();
        int bone = end;
        while (bone >= 0)
        {
            chain.Add(bone);
            if (bone == root)
            {
                break;
            }
            bone = _skeleton.GetBoneParent(bone);
        }

        var names = new List<string>();
        foreach (Node child in _physics.GetChildren())
        {
            if (child is PhysicalBone3D physicalBone)
            {
                string boneName = physicalBone.Get("bone_name").AsString();
                if (string.IsNullOrEmpty(boneName))
                {
                    continue;
                }
                int boneId = _skeleton.FindBone(boneName);
                if (chain.Contains(boneId))
                {
                    names.Add(boneName);
                    nodes.Add(physicalBone);
                }
            }
        }
        return names.ToArray();
    }

    NSpiderLegState ResolveState(NSpiderLeg leg)
    {
        // 1) 代码指定动画：动画驱动
        if (leg.HasAnimationRequest || !string.IsNullOrEmpty(_globalAnimation))
        {
            return NSpiderLegState.Animation;
        }

        // 2) RayCast 碰撞：触地，IK 驱动
        if (leg.HasProbe && leg.Probe.IsColliding())
        {
            return NSpiderLegState.Grounded;
        }

        // 3) 否则：悬空，布娃娃
        return NSpiderLegState.Airborne;
    }

    void ApplyState(NSpiderLeg leg, NSpiderLegState state, bool force = false)
    {
        if (!force && leg.State == state)
        {
            return;
        }

        leg.State = state;

        switch (state)
        {
            case NSpiderLegState.Grounded:
                SetLegIk(leg, true);
                SetAnchorEnabled(leg, true, true);
                break;

            case NSpiderLegState.Airborne:
                SetLegIk(leg, false);
                SetAnchorEnabled(leg, false, false);
                break;

            case NSpiderLegState.Animation:
                SetLegIk(leg, false);
                SetAnchorEnabled(leg, false, false);
                PlayIfPossible(leg);
                break;
        }

        if (DebugLog)
        {
            GD.Print($"[NSpiderLegController] {leg.LegName} -> {state}");
        }

        EmitSignal(SignalName.LegStateChanged, leg.IkIndex, (int)state);
    }

    void SetLegIk(NSpiderLeg leg, bool enabled)
    {
        if (_ik == null || leg.IkEnabled == enabled)
        {
            return;
        }

        leg.IkEnabled = enabled;
        // 置空 target_node 即可让 JacobianIK3D 跳过该 setting（_process_ik 里 target 为空会 abort）。
        _ik.SetTargetNode(leg.IkIndex, enabled ? leg.IkTargetPath : new NodePath());
    }

    void SetAnchorEnabled(NSpiderLeg leg, bool enabled, bool snap)
    {
        if (leg.Anchor == null)
        {
            return;
        }

        leg.Anchor.Enabled = enabled;
        if (!snap)
        {
            return;
        }

        // 立刻钉到地面；查询不到（例如刚进场景树、物理空间还没就绪）就先收到骨架下方一点，
        // 避免 IK 朝类型场景里写死的作者坐标把腿拉断。
        float scale = Mathf.Max(Mathf.Abs(_skeleton.GlobalTransform.Basis.Scale.X), 0.0001f);
        Vector3 fallback = _skeleton.GlobalPosition + Vector3.Down * 0.5f * scale;
        leg.Anchor.SnapNow(fallback, true);
    }

    void PlayIfPossible(NSpiderLeg leg)
    {
        if (AnimationPlayer == null)
        {
            return;
        }

        string clip = leg.HasAnimationRequest ? leg.RequestedAnimation : _globalAnimation;
        if (!string.IsNullOrEmpty(clip) && AnimationPlayer.HasAnimation(clip))
        {
            AnimationPlayer.Play(clip);
        }
    }

    /// <summary>把当前处于 Airborne 的腿同步给 PhysicalBoneSimulator3D。</summary>
    void SyncRagdoll()
    {
        if (_physics == null)
        {
            return;
        }

        _ragdollNext.Clear();
        for (int i = 0; i < _legs.Count; i++)
        {
            // 只有"被其它腿支撑着"的抬腿才做布娃娃；整只生物在自由落体时一律撑住姿态。
            if (_legs[i].State == NSpiderLegState.Airborne && _supported)
            {
                _ragdollNext.Add(_legs[i].IkIndex);
            }
        }

        if (_ragdollNow.SetEquals(_ragdollNext))
        {
            return;
        }

        _ragdollNow.Clear();
        foreach (int index in _ragdollNext)
        {
            _ragdollNow.Add(index);
        }

        // 引擎只提供“全部停止 / 指定骨骼启动”，所以先全停再按集合启动。
        // 骨骼碰撞同步：静止时全关（否则会把刚体顶飞、还会挡住腿部射线），只有要模拟的腿才打开。
        _living?.SetAllPhysicalBonesCollision(false);
        _physics.PhysicalBonesStopSimulation();
        if (_ragdollNow.Count == 0)
        {
            return;
        }

        var bones = new Godot.Collections.Array<StringName>();
        for (int i = 0; i < _legs.Count; i++)
        {
            NSpiderLeg leg = _legs[i];
            if (!_ragdollNow.Contains(leg.IkIndex))
            {
                continue;
            }
            for (int b = 0; b < leg.RagdollBones.Length; b++)
            {
                bones.Add(leg.RagdollBones[b]);
            }
            if (_living != null)
            {
                for (int b = 0; b < leg.RagdollBoneNodes.Count; b++)
                {
                    _living.SetPhysicalBoneCollision(leg.RagdollBoneNodes[b], true);
                }
            }
        }

        if (bones.Count > 0)
        {
            _physics.PhysicalBonesStartSimulation(bones);
        }
    }

    NSpiderLeg FindLeg(int legIndex)
    {
        for (int i = 0; i < _legs.Count; i++)
        {
            if (_legs[i].IkIndex == legIndex)
            {
                return _legs[i];
            }
        }
        return null;
    }

    NSpiderLeg FindLeg(string legName)
    {
        if (string.IsNullOrEmpty(legName))
        {
            return null;
        }
        for (int i = 0; i < _legs.Count; i++)
        {
            if (_legs[i].LegName == legName)
            {
                return _legs[i];
            }
        }
        return null;
    }
}
