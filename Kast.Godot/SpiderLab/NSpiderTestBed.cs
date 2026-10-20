using System.Collections.Generic;
using Godot;
using Kast.Godot;

/// <summary>
/// 蜘蛛（NLivingObject / NSpider）的 headless 自动化测试台。
///
/// 它自己搭地面并生成一只 NSpider，按阶段推进断言，最后打印 PASS/FAIL 汇总，
/// 并以 0（全过）/ 1（有失败）退出。不依赖 NGame / NLevel，可以直接 F6 运行：
///
///   godot --headless --path Kast.Godot res://SpiderLab/SpiderTestBed.tscn
///
/// 覆盖：装配契约、站立高度、触地判定与 IK 落点、MoveTo/到达、全身布娃娃开关、
/// 追击 AI 的手动目标与自动索敌/脱战、失去地面转悬空布娃娃。
/// 视觉类问题（姿态是否自然、步态是否成对、布娃娃是否瘫软）仍然要靠人看，见 docs/spider-test-plan.md。
/// </summary>
public partial class NSpiderTestBed : Node3D
{
    /// <summary>被测蜘蛛的整体缩放；改它就能验证"缩放后行为是否同比"。</summary>
    [Export] public float SpiderScale = 0.1f;
    /// <summary>静止/等待类阶段的帧数。</summary>
    [Export] public int SettleFrames = 300;
    /// <summary>动作类阶段的帧数。</summary>
    [Export] public int ActionFrames = 240;
    /// <summary>出生高度（本地单位，会乘缩放）。42 × 0.1 ≈ Levels/SpiderTest 里的 4.18。</summary>
    [Export] public float SpawnHeightLocal = 42f;
    /// <summary>出生后逐帧采样的帧数（0.5s @60Hz = 30）。</summary>
    [Export] public int SampleFrames = 30;
    /// <summary>是否打印逐帧采样。</summary>
    [Export] public bool LogTransient = true;

    enum Phase { Probe, Settle, LiftLeg, Move, RagdollOn, RagdollOff, AiChase, AiDetect, PassThrough, Airborne, Done }

    StaticBody3D _ground;
    NObject _spider;
    NSpider _living;
    NSpiderLegController _legs;
    JacobianIK3D _ik;

    Phase _phase = Phase.Probe;
    int _frame;

    readonly List<string> _failures = new List<string>();
    int _passed;

    Vector3 _moveStart;
    Vector3 _moveTarget;
    bool _reached;
    float _fallStartY;
    float _worstTargetD;
    int _worstAirborne;
    int _worstAirborneFrame;
    int _worstRagdoll;
    RayCast3D _liftProbe;
    uint _liftMask;
    bool _liftRestored;
    Vector3 _liftStart;
    NSpiderAI _ai;
    Node3D _bait;
    bool _aiStarted;
    float _aiStartDistance;

    NObject _casterUnit;
    NCaster _casterType;
    bool _casterMovedIn;
    float _detectStartDistance;
    int _acquiredSignals;
    int _lostSignals;
    int _acquiredAtDetectStart;
    int _lostAtDetectStart;
    Vector3 _passStart;
    Vector3 _standVisualSize;
    float _standBoneSpread;

    const float GroundTopY = 0f;

    public override void _Ready()
    {
        GD.Print($"[TEST] 开始：SpiderScale={SpiderScale}");

        BuildGround();
        SpawnSpider();

        if (_spider == null || _living == null)
        {
            _failures.Add("生成蜘蛛失败（Types/NSpider.tscn 或脚本未就绪）");
            Report();
            return;
        }

        _ai = FindFirst<NSpiderAI>(_spider);

        // 用 Connect + 生成的 SignalName，避免依赖源码生成器的事件访问器写法。
        _living.Connect(NLivingObject.SignalName.DestinationReached, Callable.From(OnDestinationReached));

        if (_ai != null)
        {
            _ai.Connect(NSpiderAI.SignalName.TargetAcquired, Callable.From<Node3D>(OnTargetAcquired));
            _ai.Connect(NSpiderAI.SignalName.TargetLost, Callable.From(OnTargetLost));
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_phase == Phase.Done)
        {
            return;
        }

        _frame++;
        switch (_phase)
        {
            case Phase.Probe:
                SampleTransient();
                if (_frame >= SampleFrames)
                {
                    CheckTransient();
                    Advance(Phase.Settle);
                }
                break;

            case Phase.Settle:
                if (_frame >= SettleFrames)
                {
                    CheckAssembly();
                    CheckStanding();
                    CheckGrounded("静止");
                    Advance(Phase.LiftLeg);
                }
                break;

            case Phase.LiftLeg:
                // 前 20 帧：把一条腿的射线遮掉 -> 该腿悬空，但其它 5 条还撑着 -> 应进入逐腿布娃娃
                if (!_liftRestored && _frame >= 20)
                {
                    _liftRestored = true;
                    Check(_legs.IsSupported, "抬起一条腿时整只生物仍被支撑");
                    Check(_legs.RagdollLegCount >= 1, $"被支撑的悬空腿进入布娃娃 (ragdoll={_legs.RagdollLegCount})");
                    if (_liftProbe != null) _liftProbe.CollisionMask = _liftMask;
                }
                else if (_liftRestored && _frame >= 70)
                {
                    Check(_legs.RagdollLegCount == 0, $"腿落回后布娃娃结束 (ragdoll={_legs.RagdollLegCount})");
                    // 把腿交给物理的瞬间骨骼可能带着 IK 的张力起跳，把躯干弹飞——这里盯住它。
                    float drift = HorizontalDistance(_spider.GlobalPosition, _liftStart);
                    float tolerance = 0.5f * Mathf.Max(SpiderScale, 0.05f);
                    Check(drift < tolerance, $"抬腿布娃娃没有把蜘蛛弹飞 (位移 {drift:F3} < {tolerance:F3})");
                    Advance(Phase.Move);
                }
                break;

            case Phase.Move:
                if (_frame >= ActionFrames)
                {
                    CheckMove();
                    Advance(Phase.RagdollOn);
                }
                break;

            case Phase.RagdollOn:
                if (_frame >= 30)
                {
                    CheckRagdollOn();
                    Advance(Phase.RagdollOff);
                }
                break;

            case Phase.RagdollOff:
                if (_frame >= SettleFrames)
                {
                    CheckRagdollOff();
                    Advance(Phase.AiChase);
                }
                break;

            case Phase.AiChase:
                // 放一个静止诱饵，验证 AI 会主动追近并进入追击/攻击态。
                if (!_aiStarted && _frame >= 20)
                {
                    _aiStarted = true;
                    Check(_ai != null, "找到 NSpiderAI 组件");
                    if (_ai != null)
                    {
                        _bait = new Node3D { Name = "Bait" };
                        AddChild(_bait);
                        _bait.GlobalPosition = _spider.GlobalPosition + new Vector3(3f, 0f, 0f);
                        _ai.Target = _bait;
                        _aiStartDistance = HorizontalDistance(_spider.GlobalPosition, _bait.GlobalPosition);
                    }
                }
                if (_frame >= 520)
                {
                    if (_ai != null && _bait != null)
                    {
                        float now = HorizontalDistance(_spider.GlobalPosition, _bait.GlobalPosition);
                        Check(_aiStartDistance - now >= 0.5f, $"AI 追近了目标 ({_aiStartDistance:F2} → {now:F2})");
                        Check(_ai.State == NSpiderAI.AiState.Chase || _ai.State == NSpiderAI.AiState.Attack,
                            $"AI 处于追击/攻击态 ({_ai.State})");
                    }
                    Advance(Phase.AiDetect);
                }
                break;

            case Phase.AiDetect:
                // 自动识别：场景里放一个真正的 NCaster 刚体，全程不给 AI 手动指定目标。
                if (_frame == 20)
                {
                    Check(_ai != null, "拿到 NSpiderAI（自动侦测阶段）");
                    Check(_casterUnit != null, "生成了 NCaster 刚体作为目标");
                    Check(_ai != null && _ai.Target == null,
                        $"施法者在侦测范围外时不被锁定 (Target={NameOf(_ai?.Target)})");
                    Check(_ai == null || _ai.State != NSpiderAI.AiState.Chase,
                        $"范围外不会进入追击态 (state={_ai?.State})");
                }
                else if (_frame == 30)
                {
                    if (_casterUnit != null)
                    {
                        _casterUnit.GlobalPosition = _spider.GlobalPosition + new Vector3(4f, 0f, 0f);
                        _casterMovedIn = true;
                        _detectStartDistance = HorizontalDistance(_spider.GlobalPosition, _casterUnit.GlobalPosition);
                        _acquiredAtDetectStart = _acquiredSignals;
                        _lostAtDetectStart = _lostSignals;
                    }
                }
                else if (_frame == 100)
                {
                    Check(_casterMovedIn && _ai != null && _ai.Target == _casterType,
                        $"自动锁定范围内的 NCaster (Target={NameOf(_ai?.Target)})");
                    Check(_ai != null && (_ai.State == NSpiderAI.AiState.Chase || _ai.State == NSpiderAI.AiState.Attack),
                        $"自动进入追击/攻击态 (state={_ai?.State})");
                    Check(_acquiredSignals > _acquiredAtDetectStart, "TargetAcquired 信号已发出");
                }
                else if (_frame == 400)
                {
                    if (_casterUnit != null)
                    {
                        float now = HorizontalDistance(_spider.GlobalPosition, _casterUnit.GlobalPosition);
                        Check(_detectStartDistance - now >= 0.4f,
                            $"自动追击时确实靠近了 ({_detectStartDistance:F2} → {now:F2})");
                    }
                }
                else if (_frame == 410)
                {
                    // 把施法者挪出 LoseRange：应当立刻放弃，而不是继续黏着。
                    if (_casterUnit != null)
                    {
                        _casterUnit.GlobalPosition = _spider.GlobalPosition + new Vector3(20f, 0f, 0f);
                    }
                }
                else if (_frame == 470)
                {
                    Check(_ai != null && _ai.Target == null,
                        $"跑出 LoseRange 后不再持有目标 (Target={NameOf(_ai?.Target)})");
                    Check(_ai == null || (_ai.State != NSpiderAI.AiState.Chase && _ai.State != NSpiderAI.AiState.Attack),
                        $"放弃后退出追击/攻击态 (state={_ai?.State})");
                    Check(_lostSignals > _lostAtDetectStart, "TargetLost 信号已发出");
                    if (_ai != null)
                    {
                        _ai.AiEnabled = false; // 后续阶段不要再插手移动
                    }
                    Advance(Phase.PassThrough);
                }
                break;

            case Phase.PassThrough:
                // 层 1（玩家层）的刚体不该挡住蜘蛛：直接叠上去，看物理会不会把它弹开。
                if (_frame == 10)
                {
                    Check(_casterUnit != null, "有层 1 的刚体用来做穿透测试");
                    if (_casterUnit != null)
                    {
                        _casterUnit.GlobalPosition = _spider.GlobalPosition + new Vector3(0.05f, 0f, 0f);
                        _passStart = _spider.GlobalPosition;
                        float overlap = HorizontalDistance(_spider.GlobalPosition, _casterUnit.GlobalPosition);
                        Check(overlap < 0.15f, $"测试刚体已和蜘蛛重叠 (d={overlap:F3})");
                    }
                }
                else if (_frame == 90)
                {
                    float moved = HorizontalDistance(_spider.GlobalPosition, _passStart);
                    float tolerance = Mathf.Max(SpiderScale, 0.05f);
                    Check(moved < tolerance, $"和层 1 刚体重叠不会被弹开 (位移 {moved:F3} < {tolerance:F3})");
                    Advance(Phase.Airborne);
                }
                break;

            case Phase.Airborne:
                if (_frame >= 90)
                {
                    CheckAirborne();
                    Advance(Phase.Done);
                }
                break;
        }
    }

    // ------------------------------------------------------------------
    // 搭场景
    // ------------------------------------------------------------------

    void BuildGround()
    {
        // 与 Levels/SpiderTest.tscn 的地形一致：层 1|2（层1 给玩家/道具，层2 给活物）。
        _ground = new StaticBody3D
        {
            Name = "TestGround",
            Position = new Vector3(0f, -1f, 0f),
            CollisionLayer = 3u,
            CollisionMask = 3u,
        };
        _ground.AddChild(new CollisionShape3D
        {
            Name = "CollisionShape3D",
            Shape = new BoxShape3D { Size = new Vector3(200f, 2f, 200f) },
        });
        AddChild(_ground);
    }

    void SpawnSpider()
    {
        NObjectType type = Helper.LoadScene<NObjectType>("Types/NSpider");
        if (type == null)
        {
            return;
        }

        // 与 NLevel.Spawn 一致：类型自身保持 1，缩放由刚体承载。
        type.Scale = Vector3.One;

        float scale = Mathf.Max(SpiderScale, 0.0001f);
        Basis basis = Basis.Identity.Scaled(Vector3.One * scale);
        Vector3 origin = new Vector3(0f, GroundTopY + SpawnHeightLocal * scale, 0f);
        _spider = new NObject { Name = "TestSpider", GlobalTransform = new Transform3D(basis, origin) };
        AddChild(_spider);
        _spider.SetType(type);

        _living = type as NSpider;
        _legs = _living?.Legs;
        _ik = _living?.Ik;
    }

    void Advance(Phase next)
    {
        _phase = next;
        _frame = 0;

        switch (next)
        {
            case Phase.LiftLeg:
                _liftProbe = FindProbe(_spider);
                _liftRestored = false;
                _liftStart = _spider.GlobalPosition;
                if (_liftProbe != null)
                {
                    _liftMask = _liftProbe.CollisionMask;
                    _liftProbe.CollisionMask = 0u;
                }
                break;

            case Phase.Move:
                _moveStart = _spider.GlobalPosition;
                _moveTarget = _moveStart + new Vector3(6f * Mathf.Max(SpiderScale, 0.05f), 0f, 0f);
                _reached = false;
                _living.MoveTo(_moveTarget);
                Check(_living.IsMoving, "MoveTo 后 IsMoving == true");
                break;

            case Phase.RagdollOn:
                _living.SetRagdoll(true);
                // 布娃娃期间 MoveTo 必须被忽略
                _living.MoveTo(_moveTarget + new Vector3(20f, 0f, 0f));
                Check(!_living.IsMoving, "布娃娃期间 MoveTo 被忽略");
                break;

            case Phase.RagdollOff:
                _living.SetRagdoll(false);
                break;

            case Phase.AiDetect:
                _ai?.ClearTarget();
                if (_bait != null)
                {
                    _bait.QueueFree();
                    _bait = null;
                }
                // 靶子放在侦测范围（8）之外，逼 AI 自己去认范围内的 NCaster。
                _casterUnit = SpawnCaster(_spider.GlobalPosition + new Vector3(25f, 0f, 0f));
                _casterMovedIn = false;
                break;

            case Phase.PassThrough:
                _living.StopMoving(); // 别让上一阶段残留的目标把蜘蛛带走
                break;

            case Phase.Airborne:
                _ground.Position = new Vector3(0f, -500f, 0f);
                _fallStartY = _spider.GlobalPosition.Y;
                break;

            case Phase.Done:
                Report();
                break;
        }
    }

    // ------------------------------------------------------------------
    // 断言
    // ------------------------------------------------------------------

    /// <summary>出生后逐帧采样：腿状态 + IK 目标离身体多远（目标越远说明 IK 在朝错误方向拉）。</summary>
    void SampleTransient()
    {
        int grounded = 0;
        int airborne = 0;
        if (_legs != null)
        {
            for (int i = 0; i < _legs.LegCount; i++)
            {
                NSpiderLegState state = _legs.GetLegState(i);
                if (state == NSpiderLegState.Grounded) grounded++;
                else if (state == NSpiderLegState.Airborne) airborne++;
            }
        }

        float maxTargetD = 0f;
        int ikOn = 0;
        if (_ik != null)
        {
            for (int i = 0; i < _ik.SettingCount; i++)
            {
                NodePath path = _ik.GetTargetNode(i);
                if (path.IsEmpty) continue;
                Node3D target = _ik.GetNodeOrNull<Node3D>(path);
                if (target == null) continue;
                ikOn++;
                maxTargetD = Mathf.Max(maxTargetD, (target.GlobalPosition - _spider.GlobalPosition).Length());
            }
        }

        int ragdoll = _legs != null ? _legs.RagdollLegCount : 0;
        _worstTargetD = Mathf.Max(_worstTargetD, maxTargetD);
        _worstRagdoll = Mathf.Max(_worstRagdoll, ragdoll);
        if (airborne > _worstAirborne)
        {
            _worstAirborne = airborne;
            _worstAirborneFrame = _frame;
        }

        if (LogTransient)
        {
            GD.Print($"[PROBE] f={_frame,3} y={_spider.GlobalPosition.Y,8:F3} vy={_spider.LinearVelocity.Y,8:F2} grounded={grounded} airborne={airborne} ikOn={ikOn} ragdoll={ragdoll} maxTargetD={maxTargetD,7:F3}");
        }
    }

    void CheckTransient()
    {
        float stand = _living.StandHeight * _living.SizeFactor;
        float limit = stand * 2.5f;
        Check(_worstTargetD <= limit,
            $"出生 0.5s 内 IK 目标始终贴近身体 (最远 {_worstTargetD:F3} ≤ {limit:F3})");
        Check(_worstRagdoll == 0,
            $"出生 0.5s 内没有任何腿进入布娃娃瘫软 (最多 {_worstRagdoll} 条)");
        Check(_worstAirborne == 0 || _worstTargetD > 0f,
            "悬空腿保持 IK 撑住姿态（不是清空 target 后放任不管）");
        GD.Print($"[TEST] 出生 0.5s 内最多同时 {_worstAirborne} 条腿悬空（第 {_worstAirborneFrame} 帧）");
    }

    void CheckAssembly()
    {
        Check(_legs != null, "找到 NSpiderLegController");
        Check(_legs != null && _legs.LegCount == 6, $"绑定六条腿 (LegCount={_legs?.LegCount})");
        Check(_ik != null && _ik.SettingCount == 6, $"JacobianIK3D 有 6 个 setting ({_ik?.SettingCount})");
        Check(_living.PhysicalBones != null, "找到 PhysicalBoneSimulator3D");
        Check(_living.PhysicalBones != null && Mathf.Abs(_living.PhysicalBones.Influence - 1f) < 1e-3f,
            $"布娃娃 influence == 1 ({_living.PhysicalBones?.Influence})");
        Check(Mathf.Abs(_living.SizeFactor - SpiderScale) < 1e-3f,
            $"SizeFactor 跟随刚体缩放 ({_living.SizeFactor} vs {SpiderScale})");

        int shapes = 0;
        foreach (Node child in _spider.GetChildren())
        {
            if (child is CollisionShape3D)
            {
                shapes++;
            }
        }
        Check(shapes >= 1, $"躯干碰撞体已挂到 NObject (count={shapes})");

        float expectedMass = _spider.Scale.Dot(_spider.Scale) * _living.Density;
        Check(Mathf.Abs(_spider.Mass - expectedMass) < 0.05f,
            $"质量 = Scale·Scale×Density ({_spider.Mass:F3} vs {expectedMass:F3})");

        // 碰撞层契约：层与掩码由 Types/NSpider.tscn 的 Layer / Mask 声明，NObject.SetType 落到刚体上。
        Check(_spider.CollisionMask == 2u, $"蜘蛛只与第 2 层（地形）碰撞 (mask={_spider.CollisionMask})");
        Check((_spider.CollisionLayer & 4u) != 0u, $"蜘蛛在可瞄准层上，AimCollisionMask=4 照得到它 (layer={_spider.CollisionLayer})");
        Check((_ground.CollisionLayer & 3u) == 3u, $"地形在第 1|2 层 (layer={_ground.CollisionLayer})");
        Check((_ground.CollisionLayer & _spider.CollisionMask) != 0u, "地形落在蜘蛛的碰撞掩码里（不会掉下去）");

        var rays = new List<RayCast3D>();
        CollectRays(_spider, rays);
        int raysOk = 0;
        foreach (RayCast3D ray in rays)
        {
            if ((ray.CollisionMask & 2u) != 0u)
            {
                raysOk++;
            }
        }
        Check(rays.Count == 6 && raysOk == 6, $"六条腿部射线都只看第 2 层 ({raysOk}/{rays.Count})");

        // 瞄准契约：所有 NObject 自动带可瞄准层；骨骼静止时只当瞄准靶（掩码 0，推不动任何东西）。
        Check((_spider.CollisionLayer & NObject.AimLayer) != 0u, $"蜘蛛自动带上可瞄准层 (layer={_spider.CollisionLayer})");
        Check(NObject.OwnerOf(_living) == _spider, "从类型节点能沿父链找回所属 NObject");
        int bonesAimable = 0;
        foreach (PhysicalBone3D bone in _living.PhysicalBoneNodes)
        {
            if ((bone.CollisionLayer & NObject.AimLayer) != 0u && bone.CollisionMask == 0u)
            {
                bonesAimable++;
            }
        }
        Check(_living.PhysicalBoneNodes.Count > 0 && bonesAimable == _living.PhysicalBoneNodes.Count,
            $"骨骼静止时只当瞄准靶：掩码 0 ({bonesAimable}/{_living.PhysicalBoneNodes.Count})");
        CheckBoneAim();

        // 骨骼碰撞箱尺度：Godot 用不带缩放的骨骼姿势摆放 PhysicalBone3D，刚体缩到 0.1 时
        // 骨骼节点会拿到"世界尺度 1"，它挂的箱子（作者数据是模型单位，例如 2×8×2）就大了 10 倍、
        // 也偏了 10 倍，一进模拟就深插地面把躯干弹飞。NLivingObject 会按实测世界尺度补回来。
        // 断言用体型相对量，不写死具体数值。
        float boxLimit = 2f * Mathf.Max(_living.StandHeight * SpiderScale, 0.01f);
        int boxCount = 0;
        int boxOk = 0;
        int offsetOk = 0;
        foreach (PhysicalBone3D bone in _living.PhysicalBoneNodes)
        {
            foreach (Node child in bone.GetChildren())
            {
                if (child is not CollisionShape3D shapeNode || shapeNode.Shape is not BoxShape3D box)
                {
                    continue;
                }
                boxCount++;
                Vector3 worldSize = box.Size * shapeNode.GlobalTransform.Basis.Scale;
                if (worldSize.Length() <= boxLimit)
                {
                    boxOk++;
                }
                if ((shapeNode.GlobalPosition - bone.GlobalPosition).Length() <= boxLimit * 0.5f)
                {
                    offsetOk++;
                }
            }
        }
        Check(boxCount > 0 && boxOk == boxCount,
            $"骨骼碰撞箱世界尺寸随体型缩放 ({boxOk}/{boxCount}, 上限 {boxLimit:F3})");
        Check(boxCount > 0 && offsetOk == boxCount,
            $"骨骼碰撞箱不偏离骨骼 ({offsetOk}/{boxCount}, 上限 {boxLimit * 0.5f:F3})");

        CheckBoneScale("静止");
        _standVisualSize = MeasureVisualSize("静止");
        _standBoneSpread = MeasureBoneSpread("静止");
    }

    /// <summary>
    /// 拿 AimCollisionMask 的射线真的去打每根骨骼，验证两件事：
    /// 骨头上有可瞄准层（打得中），且打中的任何部件都能沿父链归到蜘蛛这个 NObject。
    /// </summary>
    void CheckBoneAim()
    {
        var space = _spider.GetWorld3D().DirectSpaceState;
        if (space == null)
        {
            return;
        }
        float reach = 3f * Mathf.Max(SpiderScale, 0.1f);
        int boneHits = 0;
        int stray = 0;
        foreach (PhysicalBone3D bone in _living.PhysicalBoneNodes)
        {
            Vector3 center = bone.GlobalPosition;
            foreach (Node child in bone.GetChildren())
            {
                if (child is CollisionShape3D shape)
                {
                    center = shape.GlobalPosition;
                    break;
                }
            }
            var query = PhysicsRayQueryParameters3D.Create(
                center + Vector3.Up * reach, center - Vector3.Up * reach, NObject.AimLayer);
            // 骨骼很小（缩放 0.1 时只有几厘米），射线起点可能已经在它体内，
            // 不开这个的话"起点在形状内部"不算命中，小尺寸下会全部漏掉。
            query.HitFromInside = true;
            var hit = space.IntersectRay(query);
            if (hit.Count == 0)
            {
                continue;
            }
            var collider = (Node3D)hit["collider"];
            if (NObject.OwnerOf(collider) != _spider)
            {
                stray++;
                continue;
            }
            if (collider is PhysicalBone3D)
            {
                boneHits++;
            }
        }
        Check(stray == 0, $"瞄准射线打到蜘蛛的任何部件都归属蜘蛛 (跑偏 {stray})");
        Check(boneHits >= 1, $"瞄准射线能直接打到骨骼刚体 (命中 {boneHits} 根)");
    }

    /// <summary>
    /// 量蜘蛛所有网格在世界空间的包围盒——这才是"看上去多大"。
    /// 骨架姿势缩放那类代理量可能失真，包围盒不会。
    /// </summary>
    Vector3 MeasureVisualSize(string tag)
    {
        var meshes = new List<MeshInstance3D>();
        CollectMeshes(_spider, meshes);
        bool first = true;
        Vector3 min = Vector3.Zero;
        Vector3 max = Vector3.Zero;
        foreach (MeshInstance3D mesh in meshes)
        {
            Aabb box = mesh.GetAabb();
            Transform3D xform = mesh.GlobalTransform;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = box.Position + new Vector3(
                    (i & 1) == 0 ? 0f : box.Size.X,
                    (i & 2) == 0 ? 0f : box.Size.Y,
                    (i & 4) == 0 ? 0f : box.Size.Z);
                Vector3 world = xform * corner;
                if (first)
                {
                    min = world;
                    max = world;
                    first = false;
                }
                else
                {
                    min = min.Min(world);
                    max = max.Max(world);
                }
            }
        }
        Vector3 size = max - min;
        GD.Print($"[SIZE] {tag}: 视觉包围盒={size.ToString("F3")} 本体Scale={_spider.Scale.ToString("F4")} 网格数={meshes.Count}");
        return size;
    }

    /// <summary>量骨骼离身体中心最远多远。缩放不生效时骨骼会跑到"未缩放"的距离上。</summary>
    float MeasureBoneSpread(string tag)
    {
        Vector3 center = _spider.GlobalPosition;
        float worst = 0f;
        string who = "-";
        foreach (PhysicalBone3D bone in _living.PhysicalBoneNodes)
        {
            float d = (bone.GlobalPosition - center).Length();
            if (d > worst)
            {
                worst = d;
                who = bone.Name;
            }
        }
        GD.Print($"[SPREAD] {tag}: 骨骼离身体最远 {worst:F3}（{who}）, 体型={SpiderScale}, 身体={center.ToString("F3")}");
        return worst;
    }

    static void CollectMeshes(Node root, List<MeshInstance3D> into)
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is MeshInstance3D mesh)
            {
                into.Add(mesh);
            }
            CollectMeshes(child, into);
        }
    }

    void CheckStanding()
    {
        float expected = _living.StandHeight * _living.SizeFactor;
        float actual = _spider.GlobalPosition.Y - GroundTopY;
        float tolerance = Mathf.Max(0.25f * expected, 0.05f);
        Check(Mathf.Abs(actual - expected) < tolerance,
            $"站立高度 (实际 {actual:F3} vs 期望 {expected:F3} ±{tolerance:F3})");
    }

    void CheckGrounded(string tag)
    {
        int grounded = CountGrounded();
        Check(grounded >= 5, $"{tag}：至少 5 条腿触地 (grounded={grounded})");

        int nearGround = 0;
        if (_ik != null)
        {
            for (int i = 0; i < _ik.SettingCount; i++)
            {
                NodePath path = _ik.GetTargetNode(i);
                if (path.IsEmpty)
                {
                    continue;
                }
                Node3D target = _ik.GetNodeOrNull<Node3D>(path);
                if (target == null)
                {
                    continue;
                }
                float tolerance = Mathf.Max(0.6f * SpiderScale, 0.08f);
                if (Mathf.Abs(target.GlobalPosition.Y - GroundTopY) < tolerance)
                {
                    nearGround++;
                }
            }
        }
        Check(nearGround >= 5, $"{tag}：至少 5 个 IK 目标落在地面附近 (near={nearGround})");
    }

    void CheckMove()
    {
        float toTarget = HorizontalDistance(_spider.GlobalPosition, _moveTarget);
        float moved = HorizontalDistance(_spider.GlobalPosition, _moveStart);
        float arrive = _living.ArriveDistance * Mathf.Max(SpiderScale, 0.0001f);

        Check(_reached, "MoveTo 触发 DestinationReached");
        Check(toTarget <= arrive * 3f + 0.05f, $"停在目标附近 (剩余 {toTarget:F3})");
        Check(moved >= 3f * Mathf.Max(SpiderScale, 0.05f), $"确实位移了 (moved={moved:F3})");
        Check(!_living.IsMoving, "到达后 IsMoving == false");
    }

    /// <summary>
    /// 物理骨骼的全局缩放必须等于体型缩放。Godot 会把骨骼摆到"世界尺度 1"，
    /// 于是它写回骨架的姿势带着 1/缩放的倍数——腿会突然放大、全身布娃娃时整只蜘蛛变回原始大小。
    /// </summary>
    void CheckBoneScale(string tag)
    {
        var skeleton = _living.PhysicalBones != null ? _living.PhysicalBones.GetParent() as Skeleton3D : null;
        if (skeleton == null)
        {
            Check(false, $"{tag}：找到 Skeleton3D");
            return;
        }
        float expected = Mathf.Max(SpiderScale, 0.0001f);
        int ok = 0;
        int total = 0;
        float worst = 0f;
        foreach (PhysicalBone3D bone in _living.PhysicalBoneNodes)
        {
            int index = bone.GetBoneId();
            if (index < 0)
            {
                continue;
            }
            total++;
            // 决定渲染大小的是骨架姿势（骨骼的 override 会顶掉普通姿势），换算到世界再量。
            Transform3D pose = bone.IsSimulatingPhysics()
                ? skeleton.GetBoneGlobalPoseOverride(index)
                : skeleton.GetBoneGlobalPose(index);
            float actual = (skeleton.GlobalTransform.Basis * pose.Basis).Scale.X;
            worst = Mathf.Max(worst, Mathf.Abs(actual - expected));
            if (Mathf.Abs(actual - expected) < expected * 0.2f)
            {
                ok++;
            }
        }
        Check(total > 0 && ok == total,
            $"{tag}：骨架姿势世界缩放 == 体型缩放 ({ok}/{total}, 偏差最大 {worst:F4} vs {expected:F3})");
    }

    void CheckRagdollOn()
    {
        Check(_living.IsRagdoll, "IsRagdoll == true");
        CheckBoneScale("全身布娃娃");
        Vector3 ragdollSize = MeasureVisualSize("全身布娃娃");
        float allowed = Mathf.Max(_standVisualSize.Y * 2f, 0.01f);
        Check(ragdollSize.Y <= allowed,
            $"全身布娃娃后蜘蛛没有变大 (高 {ragdollSize.Y:F3} ≤ {allowed:F3}, 站立时 {_standVisualSize.Y:F3})");
        float ragdollSpread = MeasureBoneSpread("全身布娃娃");
        float spreadAllowed = 3f * Mathf.Max(_standBoneSpread, 0.05f);
        Check(ragdollSpread <= spreadAllowed,
            $"全身布娃娃后骨骼没有散开 ({ragdollSpread:F3} ≤ {spreadAllowed:F3}, 站立时 {_standBoneSpread:F3})");
        Check(_living.PhysicalBones != null && _living.PhysicalBones.IsSimulatingPhysics(),
            "全身布娃娃已启动模拟");
        Check(_legs != null && !_legs.EnableStateMachine, "腿部状态机已关闭（不与物理抢骨骼）");
    }

    void CheckRagdollOff()
    {
        Check(!_living.IsRagdoll, "IsRagdoll == false");
        Check(_legs != null && _legs.EnableStateMachine, "腿部状态机已恢复");
        GD.Print($"[TEST] 恢复站立后 grounded={CountGrounded()}/6, 模拟中={_living.PhysicalBones?.IsSimulatingPhysics()}");
    }

    void CheckAirborne()
    {
        int airborne = 0;
        if (_legs != null)
        {
            for (int i = 0; i < _legs.LegCount; i++)
            {
                if (_legs.GetLegState(i) == NSpiderLegState.Airborne)
                {
                    airborne++;
                }
            }
        }
        Check(airborne >= 4, $"失去地面后至少 4 条腿悬空 (airborne={airborne})");
        // 新的正确行为：整只生物都不着地时，悬空腿保持 IK 撑住姿态，而不是集体瘫软。
        Check(!_legs.IsSupported, "整只生物处于无支撑状态");
        Check(_legs.RagdollLegCount == 0, $"无支撑时不做布娃娃，腿保持撑住姿态 (ragdoll={_legs.RagdollLegCount})");
        Check(_spider.GlobalPosition.Y < _fallStartY - 0.2f * Mathf.Max(SpiderScale, 0.05f),
            $"失去支撑后确实下坠 (Δy={_spider.GlobalPosition.Y - _fallStartY:F3})");
    }

    // ------------------------------------------------------------------
    // 工具
    // ------------------------------------------------------------------

    int CountGrounded()
    {
        if (_legs == null)
        {
            return 0;
        }
        int n = 0;
        for (int i = 0; i < _legs.LegCount; i++)
        {
            if (_legs.GetLegState(i) == NSpiderLegState.Grounded)
            {
                n++;
            }
        }
        return n;
    }

    /// <summary>在子树里深度优先找第一个 <typeparamref name="T"/>。</summary>
    static T FindFirst<T>(Node root) where T : class
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

    /// <summary>把子树里所有 RayCast3D 收进列表（用来检查腿部射线的碰撞掩码）。</summary>
    static void CollectRays(Node root, List<RayCast3D> into)
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is RayCast3D ray)
            {
                into.Add(ray);
            }
            CollectRays(child, into);
        }
    }

    /// <summary>在子树里找第一条腿部射线。</summary>
    static RayCast3D FindProbe(Node root)
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is RayCast3D probe)
            {
                return probe;
            }
            RayCast3D nested = FindProbe(child);
            if (nested != null)
            {
                return nested;
            }
        }
        return null;
    }

    static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        return new Vector2(a.X - b.X, a.Z - b.Z).Length();
    }

    void OnDestinationReached()
    {
        _reached = true;
    }

    void OnTargetAcquired(Node3D target)
    {
        _acquiredSignals++;
    }

    void OnTargetLost()
    {
        _lostSignals++;
    }

    /// <summary>
    /// 照 <see cref="NLevel.Spawn"/> 的做法，在场景里生成一个真的 NCaster 刚体当靶子。
    /// 冻结住是为了断言可复现——否则它会被蜘蛛推着跑。
    /// </summary>
    NObject SpawnCaster(Vector3 worldPosition)
    {
        PackedScene scene = GD.Load<PackedScene>("res://Types/NCaster.tscn");
        if (scene == null)
        {
            return null;
        }
        _casterType = scene.Instantiate<NCaster>();
        if (_casterType == null)
        {
            return null;
        }
        _casterType.Scale = Vector3.One;

        var body = new NObject { Name = "TestCaster", GlobalTransform = new Transform3D(Basis.Identity, worldPosition) };
        AddChild(body);
        body.SetType(_casterType);
        body.Freeze = true;
        return body;
    }

    static string NameOf(Node node)
    {
        return node != null && GodotObject.IsInstanceValid(node) ? node.Name.ToString() : "null";
    }

    void Check(bool ok, string name)
    {
        if (ok)
        {
            _passed++;
            GD.Print($"[TEST] PASS  {name}");
        }
        else
        {
            _failures.Add(name);
            GD.PrintErr($"[TEST] FAIL  {name}");
        }
    }

    void Report()
    {
        GD.Print("==================================================");
        GD.Print($"[TEST] 通过 {_passed} 项，失败 {_failures.Count} 项  (SpiderScale={SpiderScale})");
        foreach (string f in _failures)
        {
            GD.Print($"       FAIL: {f}");
        }
        GD.Print("==================================================");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }
}
