# 蜘蛛测试方案（NLivingObject / NSpider）

测试分两层。蜘蛛逻辑与 Godot 强耦合（NObject / Node3D / SkeletonModifier / 物理），
所以 `Kast.Test`（纯 `Kast.Core` 控制台）覆盖不到，自动化放在**引擎内 headless**。

当前状态：**三档缩放 32/32 全过**（`SpiderScale = 0.1 / 1.0 / 2.0`）。

---

## 1. 自动化测试台（L2）

- 场景：`res://SpiderLab/SpiderTestBed.tscn`
- 脚本：`res://SpiderLab/NSpiderTestBed.cs`
- 它自己搭 200×2×200 地面，并用与 `NLevel.Spawn` 相同的方式生成一只 `NObject + NSpider`，
  **不依赖 NGame / NLevel**，可以直接 F6 运行。

```powershell
# 编辑器：打开 SpiderLab/SpiderTestBed.tscn 按 F6
# 命令行（退出码 0 = 全过，1 = 有失败）
godot --headless --path Kast.Godot res://SpiderLab/SpiderTestBed.tscn
```

参数（根节点导出）：`SpiderScale`（默认 0.1）、`SpawnHeightLocal`（出生高度，默认 42，×0.1≈关卡里的 4.18）、
`SampleFrames`（出生采样帧数，默认 30 = 0.5s @60Hz）、`LogTransient`、`SettleFrames`、`ActionFrames`。

### 阶段与断言

| 阶段 | 帧数 | 断言 |
|---|---|---|
| **Probe** | 30（0.5s） | **出生瞬间**：IK 目标始终贴近身体（≤ 2.5×StandHeight×scale）；**没有任何腿进入布娃娃瘫软**；悬空腿保持 IK 撑住姿态；打印逐帧 `y / vy / grounded / airborne / ikOn / ragdoll / maxTargetD` |
| Settle | 300 | 找到 `NSpiderLegController`；`LegCount == 6`；IK `SettingCount == 6`；找到 `PhysicalBoneSimulator3D`；`Influence == 1`；`SizeFactor == SpiderScale`；躯干碰撞体已 reparent 到 `NObject`；`Mass == Scale·Scale×Density`；站立高度 == `StandHeight×SizeFactor`；≥5 条腿触地；≥5 个 IK 目标 Y ≈ 地面 |
| **LiftLeg** | 70 | 遮掉一条腿的射线 → 该腿 `Airborne` 且其它腿撑着且身体站稳 → **该腿进入布娃娃**；射线恢复后布娃娃结束（ragdoll 回到 0） |
| Move | 240 | `MoveTo` 后 `IsMoving`；触发 `DestinationReached`；停在目标附近；位移 ≥ 3×scale；到达后 `IsMoving == false` |
| RagdollOn | 30 | `IsRagdoll`；`IsSimulatingPhysics`；腿部状态机关闭；布娃娃期间 `MoveTo` 被忽略 |
| RagdollOff | 300 | `!IsRagdoll`；状态机恢复（并打印恢复后的触地数） |
| **AiChase** | 520 | 在 3 世界单位处放一个静止靶子并**手动**指派 `Target`：AI 追近 ≥0.5；进入 `Chase`/`Attack` |
| **AiDetect** | 470 | 照 `NLevel.Spawn` 生成一个**真的 NCaster 刚体**（冻结当靶子），全程**不手动指定目标**：靶子在 25 处时 `Target == null` 且不进追击；挪到 4 处后自动锁定该 `NCaster`、发出 `TargetAcquired`、进入 `Chase`；再挪到 20 处（> `LoseRange`）后立刻 `Target == null`、退出追击、发出 `TargetLost` |
| **PassThrough** | 90 | 把一个**层 1 的冻结刚体**叠到蜘蛛身上（`d=0.05`）：位移必须 < 0.05×缩放 —— 即"蜘蛛只碰层 2"生效，不会被层 1 的玩家/道具弹开 |
| Airborne | 90 | 地面移走后 ≥4 条腿 `Airborne`；**无支撑 → 不做布娃娃、保持撑住姿态**；身体确实下坠 |

### 通过标准

1. 退出码 0；
2. `0.1 / 1 / 2` 三档缩放全过；
3. stderr 里**没有新的**脚本异常。引擎原有的
   `ERROR: Index p_bone = -1 ... get_bone_rest` 是 Godot 4.7.2 内置 IK 的加载缺陷（当前 6 个 setting → 15 条），与蜘蛛无关，不算失败。

---

## 2. 悬空腿的行为规则（重要）

`Airborne` 不等于"一律布娃娃"，要看**整只生物有没有被支撑**：

| 情况 | 悬空腿行为 | 原因 |
|---|---|---|
| 至少一条腿触地**且身体站稳**（走路时抬腿） | **布娃娃**（按原始需求） | 身体有支撑，抬起的腿自然下垂 |
| 没有任何腿着地，或**竖直速度还很大**（出生下落 / 被击飞 / 跳崖） | **保持 IK 撑住姿态** | 否则腿一起瘫软，落地瞬间要从瘫软猛拉回 IK —— 视觉上就是"出生时骨骼严重变形"；下落途中把腿交给物理，骨骼会带着下坠速度穿进地面，回弹能把躯干弹飞 |

实现：`NSpiderLegController` 每帧算 `_supported`＝「≥1 条腿触地」**且**「`|vy| ≤ FallSpeedFactor × SizeFactor`」，
不满足时对 Airborne 腿调用 `HoldLeg()`（开 IK、关锚点、把脚放在射线方向 z=12 的静止位，随身体走），
`SyncRagdoll()` 也只把"被支撑的悬空腿"送进模拟。
对外仍只有 `Animation / Grounded / Airborne` 三态，只是 Airborne 的具体行为分两种。

下坠速度那一项是**必须的**：下落途中腿部射线会先扫到地面，此时"有腿触地"成立，但身体还在高速下坠
（`Levels/SpiderTest.tscn` 里出生第 51 帧：`rl=4, |vy|=7.0`），腿一交给物理就穿地、
骨骼速度瞬间飙到 `57`（后续帧实测到 `1005`）并把躯干弹飞。加门控后全程 `boneV = 0`。

---

## 3. 追击 AI（SpiderLab/NSpiderAI.cs）

挂在 `Types/NSpider.tscn` 的 `AI` 子节点上，是纯逻辑节点（不碰骨骼、不碰 IK），
只通过 `NLivingObject.MoveTo / StopMoving` 指挥躯干刚体，因此布娃娃时不会和物理抢控制权。

### 状态机

```
Idle ──距离 ≤ DetectRange──► Chase ──距离 ≤ AttackRange──► Attack
  ▲                            │                              │
  │                            └───距离 > LoseRange───► Return（走回出生点）──► Idle
  └────────────────────────────────────────────────────────────┘
```

| 状态 | 行为 |
|---|---|
| `Idle` | 不指挥移动。范围内出现目标就转 `Chase` |
| `Chase` | `MoveTo(目标位置)`；目标位移超过 `RepathDistance` 才重新下发（省寻路重算） |
| `Attack` | `StopMoving()`，每帧朝目标转向；`OnAttackTick` 留给子类做伤害/动画，并调用 `PlayAnimation("attack")`（目前是空实现，为未来动画预留） |
| `Return` | `ReturnHome` 打开时走回"家"（落地站稳后记下的位置），到了就回 `Idle` |

### 参数（导出）

| 参数 | 默认 | 说明 |
|---|---|---|
| `AiEnabled` | true | 总开关；关掉后完全不碰移动 |
| `Target` | null | 手动指定目标；留空则自动索敌 |
| `DetectRange` | 8 | 发现目标的距离 |
| `LoseRange` | 12 | 放弃追击的距离（**必须 > DetectRange**，形成滞回，避免边界抖动） |
| `AttackRange` | 1.2 | 进入攻击的距离 |
| `RepathDistance` | 0.4 | 目标移动多少才重新 `MoveTo` |
| `AttackHoldTime` | 0.6 | 进入攻击后至少停这么久才允许重新起步 |
| `AttackExitScale` | 1.8 | 退出攻击的距离倍率：`distance > AttackRange × 这个值` 才重新追 |
| `ReturnHome` | true | 脱战后是否回家 |
| `HomeArriveDistance` | 0.6 | 到家判定距离 |
| `TargetRescanInterval` | 0.25 | 重新扫描目标的间隔 |
| `LogState` | false | 打印状态切换 |

**距离单位是"世界单位"，不随体型缩放**——目标是世界尺度的玩家，不是蜘蛛自身部件。
（`NLivingObject` / `NSpiderAnchor` 里的 `StandHeight` / `SnapDistance` 会乘 `SizeFactor`，两者规则不同，改参数时注意。）

### 索敌优先级

1. 手动指定的 `Target`（`ForceTarget()`，比如被咒语激怒）；
2. `NGame.Instance.ActiveCamera.Player`——是 `NCaster` 且在 `DetectRange` 内；
3. 场景里最近的 `NCaster`（递归扫 `CurrentScene`），同样只在 `DetectRange` 内。

范围外的 `NCaster` **不会被持有引用**，所以 `Target` 能干净地回到 `null`。
没有 `NGame`（例如测试关卡）时自动退化成第 3 条。

### 对外接口

`State`、`HasTarget`、`ForceTarget(Node3D)`、`ClearTarget()`；
信号 `StateChanged(int)` / `TargetAcquired(Node3D)` / `TargetLost` / `Attack(Node3D)`；
子类覆写 `OnAttackTick(double)` 实现咬人。

### 与关卡脚本的交接

`Levels/NSpiderTest.cs` 里那份"每隔几秒随机挑个点巡逻"的逻辑会和 AI 抢 `MoveTo`，
两边都下发目标时蜘蛛会走一步停一步。所以关卡脚本在 AI 非 `Idle`（Chase / Attack / Return）期间
暂停巡逻，并把计时器重置成 `PatrolInterval`，脱战后重新开始计时。
该关卡还把 `LogState` 打开了，状态切换会打印 `[NSpiderAI] -> Chase` 之类，方便目视核对 D2 清单。

**注意出生距离**：`Levels/SpiderTest.tscn` 里蜘蛛在 `(0, 4.18, 5)`、`CasterSpanwer` 在 `(-1, 1.5, 7)`，
水平距离约 2.2，**一进关卡就会 Chase → Attack**。想看"范围外不追"，先操控玩家走到 12 以外。

---

## 4. 碰撞层约定

| 对象 | `collision_layer` | `collision_mask` | 说明 |
|---|---|---|---|
| 地形（`Levels/SpiderTest.tscn` 的 `Ground`） | `3`（层 1\|2） | `3` | 层 1 让玩家/道具站得住，层 2 让活物站得住 |
| 蜘蛛（`Types/NSpider.tscn` 的 `Layer` / `Mask`） | `3`（层 1\|2）**＋自动的层 3** = 实际 `7` | `2` | **只和层 2 碰**（= 地形、以及同样在层 2 的同类） |
| 骨骼 `PhysicalBone3D`（模拟中） | 跟随宿主刚体（含层 3） | 跟随宿主刚体 | 布娃娃时能落到地形上、骨骼之间互撞，不会去撞层 1 的玩家 |
| 骨骼 `PhysicalBone3D`（静止） | **只有层 3** | **`0`** | 纯瞄准靶：瞄准射线打得到这条腿，但它不推任何东西 |
| 六条腿部 `RayCast3D` | — | `2` | 踩脚点只看地形，不会踩到玩家身上 |
| 玩家 / 道具（默认） | `1` | `1` | 不受影响 |

层与掩码由**类型**声明：`NObjectType` 上的 `Layer` / `Mask` 两个导出，
`NObject.SetType` 把它们写到刚体上——其中 `CollisionLayer = Type.Layer | NObject.AimLayer`，
**可瞄准层（层 3 = 值 4）是自动加的**，所以每个 `NObject` 都能被瞄准射线打到，类型不用自己写。
`Mask` 默认 1，等于 Godot 默认，所以石头、树、施法者的行为没变。
（这个赋值必须在 `AddChild(Type)` **之前**：类型的 `_Ready` 里要读宿主的层去初始化骨骼碰撞，
放后面的话骨骼会拿到还没赋值的层。）
腿射线在 `Types/NSpider.tscn` 里逐条写 `collision_mask = 2`；
`PhysicalBone3D` 的层/掩码在 `CollectPhysicalBones()` 里从宿主刚体抄一份。

`PlayerCamera.AimCollisionMask` 默认改成 `4`（**只打层 3 = 可瞄准层**）。
于是"能不能被瞄准"和"能不能撞"彻底分开了：撞什么由 `Mask` 决定，能不能瞄由可瞄准层决定，
而可瞄准层是所有 `NObject` 自动带的，`NRock` / `NTree` / 施法者一样照得到。

### 骨骼也响应瞄准

蜘蛛的腿是 12 个独立的 `PhysicalBone3D` 刚体，瞄准射线很容易先打到腿而不是躯干，所以两头都做了处理：

1. **打得中**：骨骼静止时层只留可瞄准层、掩码设 0（见上表）。既进瞄准射线的视野，
   又不和任何东西发生物理接触——静止时开着全部碰撞会把躯干顶飞、还会挡住腿部射线（脚会钉在自己身上）。
   正在模拟的腿则换成宿主的层与掩码，该撞地形就撞地形。
2. **认得出来**：`PlayerCamera.UpdateAim` 改用 `NObject.OwnerOf(collider)`，
   沿父链往上找第一个 `NObject`。于是"瞄准一条腿"和"瞄准这只蜘蛛"是同一件事，
   `Aim` / 轮廓高亮 / 施法目标都不会因为打到腿而丢掉。

测试台每根骨骼都会挨一发 `AimLayer` 的射线：断言射线打到的任何部件都能归属蜘蛛，
且至少有一根是被**直接打中骨骼**的（`0.1 / 1 / 2` 三档实测 12/12）。

**实测结论（Godot 4.7.2，测试台 PassThrough 阶段）**：两个刚体是否碰撞**由活物自己的掩码决定**，
对方的掩码不会把它拉进来。所以掩码 `2` 就足以让蜘蛛无视层 1 的玩家；
层保留在 `1` 是为了让咒语、水体这些只看层 1 的系统还找得到蜘蛛，另加层 3 供瞄准射线（`AimCollisionMask = 4`）使用。

验证过程（值得记下来，因为它推翻了"Godot 取并集"的直觉）：

| 蜘蛛 layer | 蜘蛛 mask | 重叠的层 1 刚体 | 结果 |
|---|---|---|---|
| `1` | `2` | 冻结（static） | 不被弹开 |
| `1` | `2` | 动态、无重力 | 不被弹开 |
| `3`（含层 1） | `2` | 动态、无重力 | 不被弹开 |
| `1` | **`3`（含层 1）** | 冻结 | **被弹开 0.343**（阳性对照，证明这套测法真的能测出碰撞） |

---

## 5. 人工目视清单（L3）

在 `Levels/SpiderTest.tscn` 里跑（按住 `R` 切全身布娃娃）。自动化测不了"好不好看"。

### A 出生与前 0.5s（本次重点）
- [ ] 出生瞬间六条腿是**撑住**的姿态，不是软趴趴垂着
- [ ] 下落过程中腿不抽搐、不出现反向折叠
- [ ] 落地瞬间没有"猛拉一下"的跳变
- [ ] 落地后立刻站稳（不是先瘫软再爬起来）

### B 装配与姿态
- [ ] 六条腿左右对称，膝关节不反转、不穿地、不交叉
- [ ] 身体悬在 `StandHeight×scale` 高度，腿自然外撑（不是贴地折叠）
- [ ] 鼠标瞄准/选中时轮廓高亮覆盖整只蜘蛛

### C 步态
- [ ] 走动时左右腿成对交替（trot），不是六条腿一起抽搐
- [ ] 抬起的单腿是自然下垂（布娃娃），落地瞬间归位
- [ ] 停下后脚不再滑动；转身时脚重新落位

### D 移动与寻路
- [ ] 巡逻朝目标走，到达后停下
- [ ] 上下坡时身体贴合地面高度
- [ ] 烘焙 `NavigationRegion3D` 后绕开障碍；未烘焙时退回直线

### D2 追击 AI（`Levels/SpiderTest.tscn`）
- [ ] 远处站着不动：蜘蛛不过来（`Target` 保持 `null`）
- [ ] 走进 `DetectRange`（8）：蜘蛛转身、起步、直线逼近
- [ ] 逼近到 `AttackRange`（1.2）：停下并**一直面朝玩家**，不再抖动前进
- [ ] 玩家绕圈走：蜘蛛跟得上，不会因为"目标没动够 `RepathDistance`"而卡住
- [ ] 玩家跑出 `LoseRange`（12）：蜘蛛放弃、掉头回出生点，回去后停下
- [ ] 在 8～12 之间来回横跳：不会一惊一乍地反复起停（滞回生效）
- [ ] 被击飞 / 布娃娃期间：AI 不发号施令（不出现"瘫软还被拖着走"）

### E 布娃娃
- [ ] 按住 `R` 整只瘫软、倒下、不穿地（骨骼与躯干不互撞）
- [ ] 松开 `R` 恢复站立并重新找到地面
- [ ] 布娃娃期间不移动、不响应 `MoveTo`

### F 缩放与性能
- [ ] `0.1 / 1 / 2` 三档外观等比、无拉伸（**禁止非等比缩放**）
- [ ] 三档的移动速度 / 步距 / 导航半径同比；质量随 `Scale·Scale` 增长
- [ ] 6 条 IK + 12 个 `PhysicalBone3D` 下帧率稳定

---

## 6. 回归触发点

| 改动 | 重点回归 |
|---|---|
| `Types/NSpider.tscn`：`StandHeight` / `Density` / `BoxShape3D_body` / 脚目标 / `RayCast.target_position` | 站立高度、触地判定、姿态 |
| `Types/NLivingObject.cs`：`MoveSpeed` / `ArriveDistance` / `StandHeight` / `SizeFactor` / 骨骼碰撞 | Move、缩放矩阵、布娃娃 |
| `SpiderLab/NSpiderAnchor.cs`：`SnapToHitPoint` / `FootHeight` / `SnapDistance` | 触地、脚步落点、步态 |
| `SpiderLab/NSpiderLegController.cs`：状态判定、`_supported`、`HoldLeg`、`FallSpeedFactor` | 三态、出生下落、布娃娃互斥、被弹飞 |
| `Types/NLivingObject.cs` 的 `SyncBoneShapeScale` / `ScaleShape` / `FixRagdolledBonePoses`、`Types/NSpider.tscn` 里骨骼 `CollisionShape3D` 的位置与形状尺寸 | 骨骼碰撞箱世界尺寸/偏移、骨架姿势世界缩放（静止 + 全身布娃娃）共 4 条断言 |
| `Types/NSpider.tscn`：`Density`（决定质量 `Scale·Scale×Density`） | 缩放 0.1 时蜘蛛只有 0.018 kg，撞上 2.4 kg 的玩家会被弹开；调 AI 的 `AttackRange` 时留意 |
| `SpiderLab/NSpiderAI.cs`：`DetectRange` / `LoseRange` / `AttackRange` / `ReturnHome` / 索敌优先级 | AiChase、AiDetect |
| `NObjectType` 的 `Layer` / `Mask`、`NObject.SetType` 的层赋值与 `OwnerOf`、`NLivingObject.SetPhysicalBoneCollision`、`Types/NSpider.tscn` 的腿射线 `collision_mask`、`PlayerCamera.AimCollisionMask`、关卡地形的 `collision_layer` | 碰撞层/瞄准契约共 7 条断言、PassThrough、站立与触地、瞄准高亮 |
| `Types/NLivingObject.cs`：`MoveTo` / `ArriveDistance` | AI 的追击也走同一条路径，改这里会同时影响测试里的 Move 阶段 |
| `RayCast.target_position` 与 `StandHeight` | 需满足 `StandHeight / min(射线方向 \|Y\|) ≤ 射线本地长度`。当前最小 \|Y\| ≈ 0.494，射线长 25 → `StandHeight ≤ 12` |

---

## 7. 测试实际跑出来的问题（2026-10-16）

首次 headless：**16 通过 / 9 失败** → 修完 **25/25** → 加出生探针后又发现一个 → **32/32** →
加追击 AI（AiChase + AiDetect）后 **46/46**；加碰撞层契约、穿透阶段（PassThrough）、瞄准契约、骨骼碰撞箱尺度与骨架姿势缩放后 **64/64**（`0.1 / 1 / 2` 三档缩放全过）。

**自动化测不出来的那类问题**：上面 7 号"被弹飞"在测试台里从来没复现过——测试台的地面上没有第二个刚体，
蜘蛛下落途中没有任何东西可扫，`_supported` 一直是 false。
是把它放进真正的关卡（`res://Game.tscn`，玩家站在出生点旁边）跑，逐帧打出
`rl / gr / sim / boneV / |v|` 才抓到的。**"测试台全绿"不等于"关卡里没问题"**，关卡冒烟跑是必要的一层。

| # | 现象 | 根因 | 修复 |
|---|---|---|---|
| 1 | 编译失败 CS0234 | `NLivingObject` 在 `namespace Kast.Godot` 内，`Godot.Collections` 的 `Godot` 被解析成 `Kast.Godot` | 改 `global::Godot.Collections` |
| 2 | 编译失败 CS0246 | `NSpiderLegController` 是全局命名空间，找不到 `NLivingObject` | 加 `using Kast.Godot;` |
| 3 | 站立高度 -113（穿地飞走） | 12 个 `PhysicalBone3D` 是独立物理体，静止时碰撞没关，重叠在躯干上把刚体顶飞/顶穿地面 | `NLivingObject` 统一管理骨骼碰撞：静止关、模拟开；并与躯干互加碰撞例外 |
| 4 | grounded=1 | 腿部射线本地长 15，最平的射线 \|Y\|=0.494，`StandHeight=8` 需要 16.2 > 15 | 射线 `target_position` 15 → 25 |
| 5 | StandHeight 探针失效 | 向下探针会先打到自己的 `PhysicalBone3D` | 探针 exclude 里加入全部骨骼 RID |
| 6 | **出生前 0.5s 六条腿全部布娃娃瘫软**（`ikOn=0, airborne=6`），落地猛拉回 IK = "出生时骨骼严重变形" | `Airborne → 布娃娃` 规则在整只生物都不着地时也生效 | 加 `_supported` 门控：无支撑时悬空腿改为 `HoldLeg` 撑住姿态 |
| 7 | **蜘蛛被弹飞**：`Levels/SpiderTest.tscn` 里追到玩家附近后突然瞬移 20+ 单位并飞到 y=17 | 出生下落途中射线已扫到地面 → `_supported=true` → 4 条腿进布娃娃；骨骼带着 `|vy|=7` 穿进 WorldBoundary 地面，解算回弹把骨骼飙到 `1005` 单位/秒，再通过关节把躯干弹飞 | `_supported` 追加"站稳"条件：`|vy| ≤ FallSpeedFactor × SizeFactor`（默认 8×缩放） |
| 8 | **停下来时被弹飞**（追到玩家身边进入 `Attack` 后，蜘蛛一帧飞出 30 单位、速度 25～130 单位/秒） | **骨骼碰撞箱在带缩放的刚体下大了 10 倍、也偏了 10 倍**。Godot 用骨架的骨骼姿势摆放 `PhysicalBone3D`，而那套姿势不带缩放，所以刚体缩到 0.1 时骨骼节点拿到"世界尺度 1.0"：它挂的 `CollisionShape3D`（作者数据是模型单位——`BoxShape3D` = `2×8×2`、放在离骨骼 `(0,3,0)` 处）在世界里变成 **2×8×2 世界单位的巨箱、中心离骨骼 3.0 而不是 0.3**，12 个这样的箱子一进模拟就深插地面 | `NLivingObject.SyncBoneShapeScale()`：每帧用**实测**的骨骼世界尺度算出补偿系数 `k = SizeFactor / 实测`，把碰撞箱的位置（`× k`）和尺寸（复制形状后 `× k`）一起缩回去。用实测值是为了自适应：哪天 Godot 摆对了，`k` 自动变成 1 |

| 9 | **骨骼异常形变 / 按 R 失去缩放效果**：一条腿进布娃娃时那条腿当场放大 10 倍；全身布娃娃时整只蜘蛛变回原始大小 | 与问题 8 **同源**：Godot 的 `PhysicalBone3D` 活在骨架的局部空间里、**完全无视祖先缩放**（实测祖先缩放到 0.1 或 2.0，骨骼全局缩放都恒为 1.0）。它写回骨架的姿势 = `骨架全局⁻¹ × 骨骼全局` 因此带着 `1/缩放` 倍。实测：蜘蛛 0.1 缩放时，静止和布娃娃下骨架姿势的世界缩放都是 **1.0** 而不是 0.1 | `NLivingObject.FixRagdolledBonePoses()`：在 `_Process`（骨架修改器跑完、渲染之前）把布娃娃骨骼的骨架姿势 basis 归一化。位置本来就对（世界算回去正好抵消），只有缩放是多余的。修复后三档缩放的骨架姿势世界缩放偏差都是 **0.0000** |

**统一根因**：问题 8 和问题 9 是同一件事的两个症状——Godot 的 `PhysicalBone3D` 与祖先缩放不兼容。
缩放生物下它必然：① 碰撞箱的模型单位数据被当成世界单位（大 `1/缩放` 倍、偏 `1/缩放` 倍）→ 弹飞；
② 写回骨架的姿势带 `1/缩放` 倍缩放 → 形变/变回原始大小。
**要彻底避免，只能不缩放带物理骨骼的生物**（在模型资源里做小），运行时补偿则是上面两处。

**问题 8 的定位过程**（值得记：一开始查错了方向）：
先怀疑"布娃娃整组重启的关节冲量"，试了夹骨骼速度、夹重启后躯干速度，都没用；
也排除了"地形是 `WorldBoundary`"和"测试台复现不出来"这两条。
最后是逐帧看门狗在**速度尖峰那一帧**打出全部 12 根骨骼的局部/全局变换，
看到 `PhysicalBone3D` 的局部缩放是 **10**（父链全是 0.1）、
且每根骨骼的碰撞箱到骨骼的距离**恒等于 3.0000**，才定位到缩放补偿。

修复后实测（真关卡，进 `Attack` 之后的最大躯干速度）：

| | 修复前 | 修复后 |
|---|---|---|
| 躯干速度峰值 | 25 / 38 / **130** 单位/秒 | **0.64 / 0.74 / 1.41**（正常行走就是 0.3） |
| 骨骼碰撞箱离骨骼 | 3.0000 | 0.3000 |
| 骨架姿势世界缩放（0.1 缩放时） | 1.0000（大了 10 倍） | **0.1000** |
| AI 表现 | 瞬移后掉出 `LoseRange` → `Return` | 稳定 `Chase → Attack → Chase (d=2.2)` |

残留：重启那一帧 `boneV` 仍会到 20 左右（一帧的关节冲量），但箱子尺寸对了以后它不再转化成躯干位移。

**两条通用结论：**
1. "物理骨骼 + 单刚体"混用时，骨骼碰撞必须在非模拟状态关闭，否则刚体会被自己顶飞。
2. "悬空 → 布娃娃"必须加**支撑条件**，否则出生/坠落时整只生物会瘫软，落地瞬间产生巨大跳变。
