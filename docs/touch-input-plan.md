# Kast 触屏输入支持计划

基准：现有代码（`Kast.Core` / `Kast.Godot`，Godot 4.7 + .NET 8/9）。
日期：2026-10-15，基线提交 `6356585`。

关联文档：[`docs/demo-island-feasibility.md`](./demo-island-feasibility.md)。

---

## 0. 设计约束

1. **最小改动**：只新增触屏控制层，能交给引擎的不写代码。
2. **不在 C# 里控制输入**：不合成 `InputEvent`（不用 `Input.ParseInputEvent`）、不手动 `Input.ActionPress`。触摸由引擎按 **节点树顺序 + `Control.MouseFilter` + 事件传播顺序** 分派；需要响应触摸的界面自己实现 `_GuiInput`。
3. **允许修改 `NSpellInput`**：作画原先绑定右键（`_Input` 全局起笔、`_GuiInput` 靠 `IsMouseButtonPressed(Right)` 提交），与触摸不兼容；现改为直接处理触摸，右键路径停用。
4. **只复用现有资源**：复用内置 `VirtualJoystick`、现有 `TouchHud`/`Hud`、现有主题与翻译表；不新增美术资源。
5. **文本一律使用占位符**：界面文字只引用本地化 key（`strings.csv` / `NLocalization`），代码与场景里不出现字面文本。
6. **按平台分流**：移动端（`OS.HasFeature("mobile")`）走触摸；非移动端沿用原有键鼠逻辑（右键作画、F 施法、数字键/滚轮切槽），触屏控件隐藏。

---

## 1. 输入清单与处理方式

### 1.1 动作与消费点

动作定义见 `Kast.Godot/project.godot:41-122`。

| 动作 | 桌面绑定 | 消费点 | 触屏处理 |
|---|---|---|---|
| `move_forward/back/left/right` | W/S/A/D | `PlayerCamera._PhysicsProcess`（`PlayerCamera.cs:153`） | 内置 `VirtualJoystick` 直接写动作 |
| `cam_left/right/up/down` | Q/E/Z/C | `PlayerCamera.CameraUpdate`（:133,136） | 本次不做（见 §2） |
| `select` | 鼠标左键 | `PlayerCamera.HandleSelectionInput`（:201） | 本次不做：不再把触摸变成左键，避免与作画冲突 |
| `cast` | F | `PlayerCamera`（:213） | `CastSurface` 按钮 `Pressed` → 直接 `SpellSlots.CastCurrent` |
| 右键 | 鼠标右键 | 旧 `NSpellInput._Input`/ `_GuiInput` | **停用**；作画改由 `NSpellInput._GuiInput` 处理触摸 |
| `deselect` / `ui_cancel` | Esc | `NGame`(:204) 等 | 复用场景已有 UI，`NSpellInput` 用它放弃手势 |
| 槽位 1-9 / 滚轮 | 数字键 / 滚轮 | `NSpellSlots._UnhandledInput`（:66-94） | 槽位控件 `_GuiInput` 触摸选中（见阶段 4） |
| `dict_toggle` / `scroll_up/down` | Tab / W,S | `NDictionary`(:86) / `NPredictionsContainer`(:20,24) | 本次不做（见 §2） |

### 1.2 作画流程（改造后）

`NSpellInput`（`Hud/SpellInput`，一个全屏 `ColorRect`，`MouseFilter=Stop`）：

- **起笔**：`InputEventScreenTouch` 按下（桌面可用左键）→ `Begin`。
- **画**：`InputEventScreenDrag` / `InputEventMouseMotion` → `DrawStep`（最近网格方向吸附）。
- **提交**：触摸抬手 / 左键松开 → `Commit`。
- **放弃**：Esc（`deselect`）→ `Cancel`，还原成起笔前的内容。
- **开关**：由 `NTouchControls` 调 `SetActive`（即改 `Visible`），只有可见时引擎才把事件送进 `_GuiInput`。

### 1.3 已就绪的触屏资源

- **内置虚拟摇杆**：`TouchHud/VirtualJoystick` 是 Godot 4.x **内置类** `VirtualJoystick`（继承 `Control`，可模拟 `action_up/down/left/right`）。场景已配好 `joystick_size` / `action_*` / `visibility_mode`，**无需自定义脚本**。参考：`https://docs.godotengine.org/zh-cn/4.x/classes/class_virtualjoystick.html`。
- **Android 预设**：`export_presets.cfg` preset.1 已配 arm64-v8a + `immersive_mode`。

---

## 2. 目标与非目标

**目标（触屏跑通核心闭环）**
- 移动（内置摇杆）、作画（作画模式）、施法（施法模式）、模式切换。

**非目标（本次不做）**
- 相机转动/俯仰、目标选中（`select`）、词典改名/虚拟键盘、hover 预览、预测滚动。
- 竖屏、手柄、陀螺仪。

---

## 3. 总体设计

### 3.1 节点结构

```
Game
├── TouchHud  (NTouchControls)   只连按钮信号、切两个界面的可见性
│   ├── CastSurface   (Button, 全屏透明, 施法模式可见) → pressed: CastCurrent
│   └── VirtualJoystick (内置)    移动
└── Hud
    ├── SpellInput   (NSpellInput, 作画模式可见) → _GuiInput 接收触摸
    ├── …
    └── ModeButton   (Button, 在 Hud 末尾 = 最上层) 切换作画/施法
```

### 3.2 事件怎么走（不合成）

- **传播顺序**：`_input` → `_gui_input` → `_unhandled_input`。本方案只用 `_gui_input`；`NSpellInput._Input` 只保留 Esc 放弃。
- **MouseFilter**：`SpellInput` 全屏 `Stop`（可见时接收触摸）；`Hud` 为 `Ignore`（自身不吃事件，也不影响子节点）；`CastSurface` 是 `Button`（默认 `Stop`）。
- **节点树顺序**：`ModeButton` 声明在 `SpellInput` 之后（同属 `Hud`），因此作画层开着时它仍在最上层可点；`CastSurface` 声明在 `VirtualJoystick` 之前，摇杆压过它，不会被抢触摸。

### 3.3 两种模式（仅移动端）

- **作画模式**：`SpellInput.Visible=true`，全屏接收触摸；按下起笔、拖动吸附、抬手提交；Esc 放弃。
- **施法模式**：`SpellInput.Visible=false`、`CastSurface.Visible=true`；空白区触摸触发 `CastSurface` 的 `pressed`，直接调 `SpellSlots.CastCurrent(ActiveCamera.Selection)`。
- **模式按钮**：只改两者的可见性与自己的占位符文案。

### 3.4 非移动端：沿用原输入逻辑

`NTouchControls._Ready` 检测到非移动端时隐藏 `ModeButton` 与 `CastSurface`、不接管 `SpellInput`；`NSpellInput` 恢复原有右键路径：`_Input` 右键按下起笔并打开层，`_GuiInput` 在右键松开时提交并关闭。`NSpellSymbolSlot` 的鼠标选中只对词典条目生效，咒语槽仍用数字键/滚轮。

### 3.5 触摸为何能进 `_GuiInput`

实测：即使关闭 `emulate_mouse_from_touch`，Godot 也会把 `InputEventScreenTouch` / `InputEventScreenDrag` 直接送进命中 `Control` 的 `_gui_input`。所以无需任何事件合成，也不需要 `_Input` 全局抢事件。

---

## 4. 关键实现点

1. `NSpellInput._GuiInput` 接 `InputEventScreenTouch`/`ScreenDrag` 并 `AcceptEvent()`；桌面左键仍可作画（触摸与左键是两类事件，不会互相污染）。
2. 保持 `emulate_mouse_from_touch=false`：否则触摸会被引擎再变成左键，触发 `select`。
3. 模式按钮文案用占位符 key（见 §6），运行时 `NLocalization.Tr` 刷新。
4. `NTouchControls` 只连 `Pressed` 信号，不碰输入队列、不判坐标。
5. 作画层全屏会让摇杆在其可见期间收不到触摸（作画时不能移动）；如需同时移动，可把作画层裁到摇杆之外的区域。

---

## 5. 改动面

| 位置 | 改动 |
|---|---|
| `Kast.Godot/Game.tscn` | `TouchHud` 挂 `NTouchControls`；新增 `CastSurface`；`ModeButton` 移入 `Hud` 末尾 |
| `Kast.Godot/NSpellInput.cs` | 改为触摸/左键驱动，去掉右键路径（`Begin` / `DrawStep` / `Commit` / `Cancel` / `SetActive`） |
| `Kast.Godot/NSpellSymbolSlot.cs` | 基类加 `_GuiInput`：触摸按下/左键点按选中；`SlotIndex≥0` 的槽调 `NSpellSlots.SelectSlot` |
| `Kast.Godot/NSpellSlots.cs` | `CreateSlot` 记录 `SlotIndex` |
| `Kast.Godot/NSpellEntry.cs` | 移除 `_GuiInput`，选中交给基类 |
| 新增 `Kast.Godot/NTouchControls.cs` | 模式控制 + 施法按钮信号；非移动端隐藏触屏控件、不接管作画层 |
| 删除 `Kast.Godot/NTouchPad.cs` | 旧的合成输入桥接层 |
| `Kast.Godot/Localization/strings.csv` | `touch.mode.draw` / `touch.mode.cast` |
| `Kast.Godot/project.godot` | `pointing/emulate_mouse_from_touch=false` |

---

## 6. 文本占位符

- 复用现有 key：暂停/返回等直接沿用 `pause.*`、`menu.*`。
- 新增 key（全部在 `strings.csv` 登记）：

| key | 用途 |
|---|---|
| `touch.mode.draw` | 模式按钮处于作画模式时的文案 |
| `touch.mode.cast` | 模式按钮处于施法模式时的文案 |

- 含动态值的文案一律用 `{0}` 占位符，经 `NLocalization.Format` 拼接。
- 场景与代码中只出现 key；不写字面中英文。

---

## 7. 分阶段实施

**阶段 0：场景接线（已完成）**
- `TouchHud` 下接入内置 `VirtualJoystick` 与模式按钮。
- 验收：桌面运行无报错；摇杆能驱动移动。

**阶段 1：作画对象改造（已实现）**
- `NSpellInput` 由右键改为触摸/左键驱动，`SetActive` 控制开关。
- 验收：可见时按下—拖动—抬手能完成一次作画；不可见时不接收事件。

**阶段 2：作画模式（核心）（已实现，待真机验证）**
- `SpellInput` 可见即接收触摸，`_GuiInput` 完成吸附、预览、提交、放弃。
- 验收：触屏画出一个词并提交进槽，随后成功施法。

**阶段 3：施法模式 + 模式按钮（已实现，待真机验证）**
- `CastSurface` 按钮触发 `CastCurrent`；`ModeButton` 切换两个界面的可见性。
- 验收：不接键盘即可完成"切到作画 → 作画 → 切回施法 → 施法"。

**阶段 4：切槽（已实现，待真机验证）**
- 选中逻辑下沉到 `NSpellSymbolSlot._GuiInput`：触摸按下或左键（`select`）都触发；咒语槽（`SlotIndex≥0`）调 `NSpellSlots.SelectSlot` 同时更新高亮与 `Slot`，词典条目（`SlotIndex=-1`）走 `SetSelectedSymbolSlot`。
- 验收：触屏可切换 9 个槽并分别施法。

**阶段 5：平台分流（已实现）**
- 移动端走触摸；非移动端隐藏触屏控件并恢复原有输入逻辑。
- 验收：桌面右键作画、F 施法、数字键/滚轮切槽与改造前一致。

**阶段 6：真机**（待做）
- 用现有 preset.1 导出 arm64 真机验证；必要时按 §4.5 调整作画层与摇杆的占位。
- 验收：真机跑通"画咒语 → 施法"闭环，无输入死锁。

---

## 8. 验证清单

1. 桌面：作画模式下左键拖动可作画；施法模式下点空白触发施法。
2. 触屏：作画/施法模式切换；作画层开着时模式按钮仍可点。
3. 摇杆：不被 `CastSurface`（施法模式）或作画层（作画模式）误吞。
4. 边界：空槽抬手无笔画；Esc 放弃还原；切模式放弃未完成手势；切场景/死亡重载后输入正常。

---

## 9. 风险与取舍

| 风险 | 说明 | 缓解 |
|---|---|---|
| 触摸被引擎再转成鼠标 | 打开 `emulate_mouse_from_touch` 会产生额外左键、触发 `select` | 保持关闭；需要指针时另想办法 |
| 作画层全屏挡住摇杆 | 作画期间不能移动 | 默认接受；必要时把作画层裁到摇杆之外 |
| 目标选中缺失 | `select` 未接，`CastCurrent` 沿用现有 `Selection` | 后续在对应界面 `_GuiInput` 里补选中 |
| 手感 | 手指粗、`GridSize=80` 吸附阈值固定 | 真机标定；不改吸附算法 |
| 无相机控制 | 视角固定 | 后续单独做 |

---

## 10. 后续（可选）

- 相机转动、目标选中、切槽、词典改名：都在各自界面的 `_GuiInput` 或按钮信号里补，仍然不合成引擎输入。
- 预测滚动：让 `ScrollContainer` 走原生触摸拖动。

---

## 附录 A：相关项目设置

```ini
[display]
window/handheld/orientation=0        ; 横屏（以编辑器枚举为准）

[input_devices]
pointing/emulate_mouse_from_touch=false      ; 不把触摸转成鼠标：作画等由 Control._GuiInput 直接处理
```

> 键名以当前 Godot 小版本的 Project Settings 实际项为准。
