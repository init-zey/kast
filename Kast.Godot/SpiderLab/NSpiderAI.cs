using Godot;
using Kast.Godot;

/// <summary>
/// 蜘蛛的追击 AI。
///
/// 规则：
///   DetectRange 内出现 NCaster        → Chase（MoveTo 目标；挂了 NavigationAgent3D 时自动寻路）
///   进入 AttackRange                  → Attack（停下并面向目标，发 Attack 信号 / 可覆写 <see cref="OnAttackTick"/>）
///   目标超出 LoseRange（滞回，避免边界抖动）→ 放弃；开启 ReturnHome 时先走回出生点再回 Idle
///
/// 两处滞回都是为了消抖：Idle/Chase 用 DetectRange 进、LoseRange 出；
/// Chase/Attack 用 AttackRange 进、AttackRange × AttackExitScale 出，且至少停 AttackHoldTime。
///
/// 说明：
/// - 三个距离都是**世界单位**，不随体型缩放（目标 NCaster 是世界尺度的玩家，不是蜘蛛自身部件）。
/// - 布娃娃状态（SetRagdoll(true)）期间完全不指挥移动，避免和物理抢刚体。
/// - 目标优先级：手动指定的 <see cref="Target"/> → 玩家（NGame.ActiveCamera.Player）→ 场景里最近的 NCaster；
///   后两者都只在 <see cref="DetectRange"/> 内才会被锁定，范围外不会持有目标引用。
/// </summary>
public partial class NSpiderAI : Node
{
    public enum AiState { Idle, Chase, Attack, Return }

    /// <summary>总开关；关掉后 AI 不碰移动。</summary>
    [Export] public bool AiEnabled = true;
    /// <summary>可选：手动指定追击目标（留空则自动找玩家，再退化为场景里最近的 NCaster）。</summary>
    [Export] public Node3D Target;
    /// <summary>发现目标的距离（世界单位）。</summary>
    [Export] public float DetectRange = 8f;
    /// <summary>放弃追击的距离（应大于 DetectRange，形成滞回）。</summary>
    [Export] public float LoseRange = 12f;
    /// <summary>进入攻击的距离。</summary>
    [Export] public float AttackRange = 1.2f;
    /// <summary>目标移动超过这个距离才重新下发 MoveTo（避免每帧重算寻路）。</summary>
    [Export] public float RepathDistance = 0.4f;
    /// <summary>进入攻击后至少停这么久才允许重新起步（否则刚体的余速会让 Attack/Chase 反复横跳）。</summary>
    [Export] public float AttackHoldTime = 0.6f;
    /// <summary>退出攻击的距离倍率：distance &gt; AttackRange × 这个值才重新追（滞回）。</summary>
    [Export] public float AttackExitScale = 1.8f;
    /// <summary>放弃追击后是否走回出生点。</summary>
    [Export] public bool ReturnHome = true;
    /// <summary>回到出生点的判定距离。</summary>
    [Export] public float HomeArriveDistance = 0.6f;
    /// <summary>多久重新扫描一次目标。</summary>
    [Export] public float TargetRescanInterval = 0.25f;
    /// <summary>打印状态切换。</summary>
    [Export] public bool LogState = false;

    [Signal] public delegate void StateChangedEventHandler(int state);
    [Signal] public delegate void TargetAcquiredEventHandler(Node3D target);
    [Signal] public delegate void TargetLostEventHandler();
    [Signal] public delegate void AttackEventHandler(Node3D target);

    NSpider _spider;
    AiState _state = AiState.Idle;
    Vector3 _lastGoal;
    Vector3 _home;
    bool _homeCaptured;
    double _settleTimer;
    double _rescanTimer;
    double _stateTime;

    public AiState State => _state;
    public bool HasTarget => Target != null && IsInstanceValid(Target);

    public override void _Ready()
    {
        _spider = FindSpider();
        if (_spider == null)
        {
            GD.PushError("[NSpiderAI] 必须挂在 NSpider（NObjectType）的子树下。");
            SetPhysicsProcess(false);
        }
    }

    /// <summary>外部强制指定目标（比如被咒语激怒）。</summary>
    public void ForceTarget(Node3D target)
    {
        Target = target;
    }

    /// <summary>外部清空目标。</summary>
    public void ClearTarget()
    {
        Target = null;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!AiEnabled || _spider == null || _spider.Object == null)
        {
            return;
        }

        Vector3 me = _spider.Object.GlobalPosition;

        // 出生落地并站稳后，把当时的位置记为"家"。
        if (!_homeCaptured)
        {
            _settleTimer += delta;
            if (_settleTimer > 0.3 && _spider.Legs != null && _spider.Legs.IsSupported)
            {
                _home = me;
                _homeCaptured = true;
            }
        }

        _stateTime += delta;
        _rescanTimer -= delta;
        if (_rescanTimer <= 0.0)
        {
            _rescanTimer = Mathf.Max(0.05f, TargetRescanInterval);
            if (!HasTarget)
            {
                Target = FindTarget();
            }
        }

        float distance = HasTarget ? Horizontal(me, Target.GlobalPosition) : float.MaxValue;

        switch (_state)
        {
            case AiState.Idle:
                if (distance <= DetectRange)
                {
                    BeginChase();
                }
                break;

            case AiState.Chase:
                if (distance > LoseRange)
                {
                    GiveUp();
                }
                else if (distance <= AttackRange)
                {
                    BeginAttack();
                }
                else
                {
                    MoveTowards(Target.GlobalPosition, false);
                }
                break;

            case AiState.Attack:
                FaceTarget(delta);
                OnAttackTick(delta);
                if (distance > LoseRange)
                {
                    GiveUp();
                }
                else if (_stateTime >= AttackHoldTime && distance > AttackRange * AttackExitScale)
                {
                    // 双保险：停够 AttackHoldTime，且真的走远到滞回带外，才重新起步。
                    SetState(AiState.Chase);
                }
                break;

            case AiState.Return:
                if (distance <= DetectRange)
                {
                    BeginChase();
                    break;
                }
                if (!_homeCaptured || Horizontal(me, _home) <= HomeArriveDistance)
                {
                    _spider.StopMoving();
                    SetState(AiState.Idle);
                }
                else
                {
                    MoveTowards(_home, true);
                }
                break;
        }
    }

    /// <summary>攻击状态每帧回调；子类覆写做伤害/动画。</summary>
    protected virtual void OnAttackTick(double delta) { }

    // ------------------------------------------------------------------
    // 内部
    // ------------------------------------------------------------------

    void BeginChase()
    {
        SetState(AiState.Chase);
        EmitSignal(SignalName.TargetAcquired, Target);
        MoveTowards(Target.GlobalPosition, true);
    }

    void BeginAttack()
    {
        _spider.StopMoving();
        SetState(AiState.Attack);
        _spider.PlayAnimation("attack"); // 预留：目前没有攻击动画，是空实现
        EmitSignal(SignalName.Attack, Target);
    }

    void GiveUp()
    {
        _spider.StopMoving();
        EmitSignal(SignalName.TargetLost);
        Target = null;
        SetState(ReturnHome && _homeCaptured ? AiState.Return : AiState.Idle);
    }

    void MoveTowards(Vector3 goal, bool force)
    {
        if (!force && Horizontal(_lastGoal, goal) < RepathDistance)
        {
            return; // 目标没怎么动，不重新下发，省掉寻路重算
        }
        _lastGoal = goal;
        _spider.MoveTo(goal);
    }

    void FaceTarget(double delta)
    {
        if (!HasTarget)
        {
            return;
        }
        Vector3 to = Target.GlobalPosition - _spider.Object.GlobalPosition;
        to.Y = 0f;
        if (to.LengthSquared() < 1e-4f)
        {
            return;
        }
        Vector3 direction = to.Normalized();
        float targetYaw = Mathf.Atan2(-direction.X, -direction.Z);
        Vector3 euler = _spider.Object.Rotation;
        float yaw = Mathf.LerpAngle(euler.Y, targetYaw, 1f - Mathf.Exp(-8f * (float)delta));
        // 只改朝向，保留俯仰/翻滚：身体姿态归站立弹簧和物理管，硬掰平会在坡上抖。
        _spider.Object.Rotation = new Vector3(euler.X, yaw, euler.Z);
    }

    void SetState(AiState next)
    {
        if (_state == next)
        {
            return;
        }
        _state = next;
        _stateTime = 0.0;
        if (LogState)
        {
            float d = HasTarget ? Horizontal(_spider.Object.GlobalPosition, Target.GlobalPosition) : -1f;
            GD.Print($"[NSpiderAI] -> {next} (d={d:F2})");
        }
        EmitSignal(SignalName.StateChanged, (int)next);
    }

    Node3D FindTarget()
    {
        // 1) 游戏里优先追玩家。
        NGame game = NGame.Instance;
        if (game != null && IsInstanceValid(game))
        {
            PlayerCamera camera = game.ActiveCamera;
            if (camera != null && IsInstanceValid(camera))
            {
                NObject player = camera.Player;
                if (player != null && IsInstanceValid(player) && player.Type is NCaster
                    && Horizontal(player.GlobalPosition, _spider.Object.GlobalPosition) <= DetectRange)
                {
                    return player;
                }
            }
        }

        // 2) 退化：场景里最近的 NCaster（测试关卡没有 NGame 时也能跑）。
        Node root = GetTree().CurrentScene;
        if (root == null)
        {
            root = GetTree().Root;
        }
        Node3D best = null;
        float bestDistance = float.MaxValue;
        ScanCasters(root, ref best, ref bestDistance);
        return best;
    }

    void ScanCasters(Node root, ref Node3D best, ref float bestDistance)
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is NCaster caster)
            {
                float d = Horizontal(caster.GlobalPosition, _spider.Object.GlobalPosition);
                if (d < bestDistance && d <= DetectRange)
                {
                    bestDistance = d;
                    best = caster;
                }
            }
            ScanCasters(child, ref best, ref bestDistance);
        }
    }

    NSpider FindSpider()
    {
        Node node = GetParent();
        while (node != null)
        {
            if (node is NSpider spider)
            {
                return spider;
            }
            node = node.GetParent();
        }
        return null;
    }

    static float Horizontal(Vector3 a, Vector3 b)
    {
        return new Vector2(a.X - b.X, a.Z - b.Z).Length();
    }
}
