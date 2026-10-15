# Kast 触屏输入支持计划

基准：现有代码（`Kast.Core` / `Kast.Godot`，Godot 4.7 + .NET 8/9，Android 导出预设已存在）。
日期：2026-10-15，基线提交 `6356585`（"优化操作"）。

关联文档：[`docs/demo-island-feasibility.md`](./demo-island-feasibility.md) —— 核心机制闭环（`display` / `be` / `cast`）是本计划的验收对象；触屏化不改玩法，只改操作入口。

---

## 0. 结论摘要

1. **所有输入目前都是鼠标 + 键盘**：移动是 WASD，相机是 Q/E/Z/C，作画是**右键按住拖动**，施法是 F，切槽是 1-9/滚轮，词典是 Tab，暂停/取消是 Esc。
2. **触屏最大的硬骨头是"作画"**：Godot 的 `emulate_mouse_from_touch` 只会产生**左键**，没有右键。当前作画完全绑定右键（`NSpellInput.cs:61,107`），必须为触屏设计**显式的作画模式**，不能指望右键被模拟。
3. **项目里已有一处半成品触屏痕迹**：`Game.tscn:128-151` 有 `TouchHud/VirtualJoystick` 节点，类型 `VirtualJoystick`，但**该类的源码在整个仓库不存在**，也**不在 `.godot/global_script_class_cache.cfg` 里**。这是一个会加载失败/被跳过的孤儿节点，必须最先处理（补齐实现或删除重建）。
4. Android 导出预设（`export_presets.cfg` preset.1）已配好：arm64-v8a、`immersive_mode=true`、预编译模板（非 gradle）。触屏支持主要是**运行时输入层和 UI 布局**的工作，不需要新的构建链。
5. 建议分 6 个阶段落地，先做"能玩通闭环"的最小集合（摇杆 + 相机手势 + 作画模式 + 三个按钮），再做词典/文本输入与布局打磨。

---

## 1. 现状盘点

### 1.1 输入动作与其消费点

动作定义见 `Kast.Godot/project.godot:41-122`。

| 动作 | 键/鼠标 | 消费点 | 触屏问题 |
|---|---|---|---|
| `move_forward/back/left/right` | W/S/A/D | `PlayerCamera._PhysicsProcess`（`PlayerCamera.cs:153`） | 需要虚拟摇杆 |
| `cam_left/right/up/down` | Q/E/Z/C | `PlayerCamera.CameraUpdate`（`PlayerCamera.cs:133,136`） | 需要相机手势；**且当前没有 pitch 输入** |
| `select` | 鼠标左键 | `PlayerCamera.HandleSelectionInput`（:201）、`NSpellEntry._GuiInput`（:55） | 模拟左键可点选物体，但**槽位控件没有点击选中逻辑** |
| `deselect` | Esc | `PlayerCamera`(:209)、`NSpellInput`(:52)、`NSpellEntry`(:44)、`NDictionary`(:86) | 需要"取消/返回"按钮 |
| `multiselect` | Shift | `PlayerCamera`(:203) | 需要多选切换按钮 |
| `cast` | F | `PlayerCamera`(:213) | 注意：这里是 `IsActionPressed`，按住会**逐物理帧重复施法**；触屏按钮应改为一次性触发 |
| `scroll_up/down` | W/S | `NPredictionsContainer._PhysicsProcess`（`NPredictionsContainer.cs:20,24`） | 与移动键**复用同一对按键**；触屏应改为原生拖动滚动 |
| `dict_toggle` | Tab | `NDictionary._UnhandledInput`（:86） | 需要按钮 |
| `ui_cancel` | Esc（Android 返回键默认映射到此） | `NGame`(:204)、`NMainMenu`(:243) | 需要暂停按钮；Android 返回键可直接复用 |

### 1.2 作画流程（触屏改造的核心）

`NSpellInput`（`Hud/SpellInput`，`Game.tscn:291-342`）是覆盖整屏的 `ColorRect` + 十六边形网格：

- **起笔**：`_Input` 里 `InputEventMouseButton` 且 `ButtonMask==MouseButtonMask.Right` 且 `Pressed`（`NSpellInput.cs:61-102`）→ 置 `Visible=true`，从选中槽的已有内容接着画。
- **画**：`_GuiInput` 里 `InputEventMouseMotion`，按最近网格方向吸附（`:118-174`）。
- **提交**：`_GuiInput` 里一旦 `!Input.IsMouseButtonPressed(Right)`（`:107-111`）→ `Commit()` 关界面（`:180-184`）。
- **放弃**：Esc/deselect 还原 `Backup`（`:52-60`）。

结论：整套状态机以"右键按下/松开"为生命周期锚点，触屏没有等价事件。

### 1.3 触屏相关既有资产与缺陷

- **孤儿节点**：`Game.tscn:136-151`

  ```
  [node name="VirtualJoystick" type="VirtualJoystick" parent="TouchHud"]
  joystick_size = 216.0
  action_left = &"move_left"  ...  visibility_mode = 1
  ```

  没有任何 `.cs`/`.gd` 定义 `VirtualJoystick`，`global_script_class_cache.cfg` 里也没有。它的导出属性（`joystick_size` / `action_*` / `visibility_mode`）恰好描述了一个"移动摇杆"，可作为实现规格；但当前场景大概率加载报错或该节点被丢弃。
- **瞄准**：`PlayerCamera.UpdateAim` 用 `GetViewport().GetMousePosition()` 投射射线（`PlayerCamera.cs:222`）——触屏依赖鼠标模拟位置，需确认与手势的优先级。
- **悬停**：`NPointerHint._Process` 跟随鼠标（`NPointerHint.cs:42-48`），词典靠 hover 弹预览（`NDictionary.cs:125-134`）——触屏没有 hover。
- **文本**：词条改名用 `LineEdit`（`NSpellEntry.cs:16`，场景 `SpellEntry.tscn:36`），移动端需要虚拟键盘/IME。
- **键位编辑**：暂停菜单里的 keychain 键位编辑器（`NPauseMenu.cs:70-74`）是桌面概念。
- **固定布局**：`Game.tscn` 的 `SubViewport` 固定 `1152×648`（:120），UI 大量用固定 `offset_*`，异形屏/不同宽高比需适配 + 安全区。

---

## 2. 触屏核心难点

1. **右键不可用**：`emulate_mouse_from_touch` 只给左键；Android 的"长按转右键"会引入长按延迟、且会吞掉起笔前半段轨迹，不能作为主方案。
2. **多点触控**：摇杆 + 相机拖动 + 按钮要能同时工作，不能让所有触摸都走单一的鼠标模拟。
3. **作画层是全屏的**：它和相机拖动、点选物体共享同一块屏幕区域，必须显式区分模式。
4. **没有 hover**：瞄准高亮、词典预览都要改成"点按/长按"。
5. **W/S 键复用**：移动与候补列表滚动共用一对键，触屏应让列表回归原生拖动。
6. **手指比鼠标粗**：`GridSize=80` 与吸附阈值（`NSpellInput.cs:135`）在手感上要重新标定，且作画原点要让开手指（见 §4.3）。
7. **文本输入**：改名依赖物理键盘，移动端必须走系统虚拟键盘，并处理中文/英文 IME。

---

## 3. 目标与非目标

**目标（触屏设备可完整通关）**
- 移动（摇杆）、转视角（手势）、近距离/缩放。
- 点选与多选目标、施放、切槽/清槽。
- 作画一个新咒语或修改已有槽。
- 打开/关闭词典、新增词条、给词条改名。
- 暂停/返回、关闭提示。

**非目标**
- 不做手柄/震动/陀螺仪。
- 不重做玩法与关卡。
- 不要求键位自定义在移动端可用（隐藏入口即可）。
- 不追求竖屏体验（本作锁定横屏，见 §4.7）。

---

## 4. 总体设计

### 4.1 设备模式判定

新增一个轻量入口（建议挂在 `NGame` 或新建 `NTouchHud`）：

```csharp
public static bool IsTouch =>
    DisplayServer.IsTouchscreenAvailable()
    || OS.HasFeature("mobile");
```

- 桌面：维持现有鼠标键盘路径，零回归。
- 触屏：显示 `TouchHud`，启用触摸专用输入路径。
- 建议同时加一个设置项 `GameSettings.ForceTouchUI`（默认 false），方便在桌面上**用鼠标模拟触摸**做回归测试（配合 `emulate_touch_from_mouse`）。

### 4.2 左手摇杆 + 右手相机手势

- **移动**：实现 `VirtualJoystick : Control`（`[GlobalClass]`），导出属性与 `Game.tscn` 里已有节点一致：`joystick_size` / `action_left|right|up|down` / `visibility_mode`。内部用 `InputEventScreenTouch/Drag` 按触摸索引跟踪，通过对 `Input.ActionPress/ActionRelease` 写 `move_*` 动作来驱动 `PlayerCamera`（也可直接调用相机方法，取决于是否希望复用现有动作映射）。放在左下角，`visibility_mode=1` 时仅触屏显示。
- **相机**：新增 `NTouchCameraArea : Control`，覆盖右半屏（或整屏但位于 HUD 之下、作画层之上），收到单指 `InputEventScreenDrag` 时改 `ActiveCamera.TargetYaw`；需要为 `PlayerCamera` **补上 pitch 支持**（当前只有 `Yaw`/`Radius`，`Pitch` 是 `[Export]` 常量，`PlayerCamera.cs:87-88,249-256`）：
  ```csharp
  public float TargetPitch;               // 新增
  // CameraUpdate 内：TargetPitch = Mathf.Clamp(TargetPitch - dy*Sens, MinPitch, MaxPitch);
  // ApplyCameraTransform：Rotation = new(Pitch, Yaw, 0);
  ```
  双指捏合 → `TargetRadius`（可选，先不做也能玩）。
- **触摸索引隔离**：摇杆吞掉自己接管的 touch index（`GetViewport().SetInputAsHandled()` 仅限其区域），相机区只处理未被摇杆/按钮接管的触摸，避免"拖相机时人也在走"。

### 4.3 显式作画层（推荐方案）

不模拟右键，改为**模式化**：

1. 玩家点选一个槽（见 §4.4）→ 槽高亮。
2. 点屏幕上的**"作画"按钮** → 打开全屏 `SpellInput`（`Visible=true`），并冻结相机手势与移动（或保留，按手感决定；初期冻结更稳）。
3. 作画层内：**单指按下 = 起笔，拖动 = 吸附画线，抬手 = 提交**。这正好对应现在的"右键按下 → 拖动 → 松开"三段。
4. 作画层提供可点区域：**取消**（还原 `Backup`）、**清空**（`Result.Clear()`）、**提交确认**（可选，或直接用抬手提交）。
5. 起笔原点让开手指：`Origin = -touch.Position + vec*GridSize` 后额外上移约一个手指高度（如 `+ (0, 24)`），并在 `:99-100` 附近统一处理。

实现要点（`NSpellInput.cs`）：
- 抽一个"输入源"判定：桌面按键用 `InputEventMouseButton`，触屏用 `InputEventScreenTouch`（`Pressed` 起笔 / `!Pressed` 提交）。
- 把提交条件从 `!Input.IsMouseButtonPressed(Right)`（`:107`）改成显式状态位 `bool Drawing`，由起笔置 true、抬手/取消置 false，桌面与触屏共用。
- `_GuiInput` 的 `InputEventMouseMotion` 与 `InputEventScreenDrag` 都能拿到相对位移，封装成同一个 `Accumulate += delta`。

**兜底方案（不推荐）**：在 Android 上开启 `input_devices/pointing/android/enable_long_press_as_right_click` + `emulate_mouse_from_touch`，让长按产生"右键按下"，从而零改动复用现有作画。缺点是长按延迟 + 起笔轨迹丢失，只作为临时验证手段。

### 4.4 触屏按钮组

在 `TouchHud` 下新增右下角按钮簇（`Button`/`TextureButton`，锚定右下，避开安全区）：

| 按钮 | 行为 |
|---|---|
| 槽位条（复用 `SpellSlots`） | 点击槽 = `NSpellSlots.SelectSlot(i)`；给基类 `NSpellSymbolSlot` 补 `_GuiInput` 点选（目前只有 `NSpellEntry` 会响应，`SpellSlots.tscn` 的槽点了没反应） |
| 作画 | 打开 `SpellInput`（选中槽存在时可用） |
| 施法 | 一次性 `NGame.Instance.SpellSlots.CastCurrent(Selection)`（不要用按住 F 的逐帧施法，`PlayerCamera.cs:213`） |
| 多选 | 切换 `multiselect` 状态（`Input.ActionPress/Release`） |
| 取消/清空 | `deselect` 语义；长按可清空当前槽 |
| 词典 | `PlayerDict` 显隐切换 |
| 暂停 | 调 `PauseMenu.Toggle()`；Android 返回键（`ui_cancel`）已在 `NGame._UnhandledInput` 处理 |

按钮应使用大点击区域（≥ 44×44 dp），并随 `DisplayServer.GetDisplaySize()` 缩放。

### 4.5 悬停 → 点按

- 物体瞄准：`UpdateAim` 在触屏下改用一个每帧维护的"最后触摸位置"（由相机手势/点选更新），或直接不做 hover 高亮、改为**点选中高亮**（`select` 已有）。
- `NPointerHint`：触屏改成"长按词条 → 在按住位置显示预览"，松手消失；位置取 `lastTouch.Position` 而非 `GetMousePosition()`。
- 词典说明里的 `meta_clicked`（`Game.tscn:472-477`）本身就是点击，触屏可复用；`meta_hover_started` 相关信号在触屏下应降级或忽略。

### 4.6 文本输入

- 依赖 `LineEdit` 默认的 `virtual_keyboard_enabled`：`NSpellEntry.Active()` 里 `Real.GrabFocus()`（`:66`）会唤起系统键盘；如需保证，显式调用 `DisplayServer.VirtualKeyboardShow(Real.Text)`，`Deactive` 时 `VirtualKeyboardHide()`。
- 改名流程保持不变；触屏要确保点空白处 = 失焦提交（`OnRealFocusExited`，`:117-120`）。
- 键位编辑入口在触屏下隐藏（`NPauseMenu` 的 Keybinding 按钮）。

### 4.7 布局、方向与安全区

- `project.godot` 增加/核对：`display/window/handheld/orientation` 锁横屏（当前未设置，枚举以编辑器为准，建议 Landscape）；`display/window/stretch/mode="canvas_items"`（已有）。
- `SubViewport` 固定 `1152×648`（`Game.tscn:120`）：需确认在宽高比差异下相机与 `SubViewportContainer` 的缩放/拉伸正确。
- 安全区：用 `DisplayServer.GetDisplaySafeArea()` 给 `TouchHud` 加边距，避开刘海/手势条。

---

## 5. 分阶段实施

### 阶段 0：清理与基线（必须先做）
- 处理 `Game.tscn:136` 的 `VirtualJoystick` 孤儿节点：要么按 §4.2 补齐 `VirtualJoystick.cs`（`[GlobalClass] Control`，导出属性与现有节点对齐）并确认场景能加载；要么在编辑器里先移除，后续重建。
- 在触屏分支上跑通一次桌面版核心闭环，作为回归基线。
- **验收**：`Game.tscn` 在编辑器与运行时无"未知类型"报错；桌面操作与改造前一致。

### 阶段 1：摇杆 + 相机手势
- 新建 `VirtualJoystick.cs`、`NTouchCameraArea.cs`；`PlayerCamera` 增加 pitch 输入与触摸相机驱动；接入 `TouchHud`。
- `GameSettings` 加 `ForceTouchUI`，配合 `emulate_touch_from_mouse` 做桌面自测。
- **验收**：触屏/鼠标模拟下可连续移动、转视角，且摇杆与相机手势不互相抢触摸。

### 阶段 2：作画模式（最小闭环的关键）
- 重构 `NSpellInput`：`Drawing` 状态位、`ScreenTouch/Drag` 支持、抬手提交、取消/清空按钮、原点让开手指。
- 加"作画"按钮；选中槽与 `SpellInput` 的联动。
- **验收**：能在触屏上画出一个新词、提交进槽，并成功 `cast`；Esc/取消可撤销（保留桌面行为）。

### 阶段 3：操作按钮组
- 槽位点选（补 `NSpellSymbolSlot` 点击）、施法（一次性）、多选、取消、词典、暂停。
- 修 `NPredictionsContainer`：去掉 W/S 键接管与每帧 `SetInputAsHandled`，改为原生拖动滚动。
- **验收**：不接键盘即可完成"选目标 → 施法 → 切槽 → 打开词典"。

### 阶段 4：词典与文本输入
- hover → 长按预览；`NPointerHint` 定位改触摸点。
- `LineEdit` 虚拟键盘显式唤起/收起；触屏隐藏键位编辑。
- **验收**：移动端新增词条并改名，重开仍保留。

### 阶段 5：布局与安全区
- 安全区边距、按钮缩放、宽高比适配；`SubViewport` 拉伸核对。
- **验收**：至少两种宽高比（16:9 与更长屏）下 UI 不越界、不被刘海遮挡。

### 阶段 6：Android 导出与真机
- 用现有 preset.1 导出 arm64 APK，真机验证核心闭环、性能与发热。
- 补 `project.godot` 的触摸相关设置并记录到本文档。
- **验收**：真机通关"岛"灰盒（见上游文档 §5）且无输入死锁。

---

## 6. 逐文件改动清单

| 文件 | 改动 |
|---|---|
| `Kast.Godot/Game.tscn` | 修复 `VirtualJoystick` 节点；`TouchHud` 内加相机手势区、按钮簇、槽位条引用；`SpellInput` 加取消/清空按钮 |
| `Kast.Godot/project.godot` | 显式声明 `input_devices/pointing/emulate_mouse_from_touch`（建议 true）；核对 Android 长按转右键/手势设置；锁横屏；可选 `emulate_touch_from_mouse`（仅调试） |
| 新增 `Kast.Godot/VirtualJoystick.cs` | 虚拟摇杆，`[GlobalClass]`，导出属性对齐现有节点 |
| 新增 `Kast.Godot/NTouchCameraArea.cs` | 单指拖动转视角、双指捏合缩放 |
| 新增 `Kast.Godot/NTouchHud.cs`（可选） | 设备判定、按钮装配、模式切换 |
| `Kast.Godot/NSpellInput.cs` | 输入源抽象（鼠标右键 / 触摸）、`Drawing` 状态、抬手提交、取消/清空、原点偏移 |
| `Kast.Godot/PlayerCamera.cs` | 新增 pitch 输入；触屏相机驱动；`UpdateAim` 支持触摸位置；施法改一次性触发 |
| `Kast.Godot/NSpellSlots.cs` | 槽位点击选中；按钮调用现成的 `SelectSlot/CastCurrent` |
| `Kast.Godot/NSpellSymbolSlot.cs` | 基类补 `_GuiInput` 点选（当前只有 `NSpellEntry` 有） |
| `Kast.Godot/NPredictionsContainer.cs` | 去掉 W/S 键滚动与每帧 `SetInputAsHandled`，改用原生触摸滚动 |
| `Kast.Godot/NPointerHint.cs` | 定位改"最后触摸点"；长按显示 |
| `Kast.Godot/NDictionary.cs` | hover 预览在触屏下降级为长按；入口按钮化 |
| `Kast.Godot/NSpellEntry.cs` | 显式虚拟键盘唤起/收起 |
| `Kast.Godot/NPauseMenu.cs` | 触屏隐藏键位编辑；可由按钮触发 `Toggle` |
| `Kast.Godot/NGame.cs` | Android 返回键/`ui_cancel` 行为确认；可加触屏模式入口 |
| `Kast.Godot/GameSettings.cs` | 可选 `ForceTouchUI` 调试开关 |
| `Kast.Godot/Localization/strings.csv` | 新增按钮文案 key（作画/施法/多选/取消/清空/词典/暂停），复用现有 `pause.*`、`menu.*` |

---

## 7. 测试与验证

1. **桌面回归**：不启用触屏模式，确认鼠标键盘流程零回归（作画、槽位、词典、暂停）。
2. **桌面模拟触摸**：`emulate_touch_from_mouse=true` 或 `ForceTouchUI`，验证摇杆、相机手势、作画模式。
3. **Android 真机**：核心闭环 + 多点触控（边走边转视角）+ 虚拟键盘改名。
4. **边界用例**：
   - 摇杆与相机手势同时触摸不串扰；
   - 作画中收到 `ui_cancel`（Android 返回键）应先取消作画而不是弹暂停；
   - 词典打开时触摸不穿透到 3D 点选；
   - 空白槽打开作画、抬手未画任何笔画；
   - 预测列表在触屏下可滚动、不与移动冲突；
   - 刘海/手势条区域按钮可点。
5. **无输入死锁**：切场景、死亡重载（`NGame.PlayerDie`）、暂停恢复后输入仍正常。

---

## 8. 风险与取舍

| 风险 | 说明 | 缓解 |
|---|---|---|
| 触摸与鼠标模拟冲突 | `emulate_mouse_from_touch` 会把触摸也变成左键，可能与手势/点选重复触发 | 手势区与按钮显式接管并 `SetInputAsHandled`；必要时在触屏模式关闭不必要的模拟 |
| 手感 | 手指粗、`GridSize=80` 吸附阈值固定 | 真机标定阈值与原点偏移；提供灵敏度设置 |
| pitch 缺失 | 相机当前无俯仰输入，新增会改动相机手感 | 先用保守的 pitch 范围（如 [-1.2, 0.2]），保留桌面原值 |
| 作画层与施法/暂停的按键竞争 | `deselect` 与 `ui_cancel` 都是 Esc，事件顺序依赖 `_Input`/`_UnhandledInput` | 统一"先作画层、后全局"的处理顺序并加测试 |
| 词典/文本 | 移动端 IME 与改名交互复杂 | 先保证系统虚拟键盘可用，后续再优化 |
| keychain | 键位编辑在移动端无意义 | 触屏隐藏入口，不改 addon |
| 性能 | Jolt + Terrain3D + 水面在手机端开销 | 阶段 6 真机压测，必要时降画质 |
| 存档兼容 | 新增设置项 | `GameSettings` 用 MessagePack，新增 `[Key(n)]` 默认值即可向后兼容 |

---

## 9. 里程碑（相对工作量，非工期承诺）

| 里程碑 | 内容 | 相对量 |
|---|---|---|
| M0 | 阶段 0：清理 + 基线 | 小 |
| M1 | 阶段 1：摇杆 + 相机手势 | 中 |
| M2 | 阶段 2：作画模式（**核心**） | 大 |
| M3 | 阶段 3：操作按钮组 | 中 |
| M4 | 阶段 4：词典/文本 | 中 |
| M5 | 阶段 5 + 6：布局 + 真机 | 中 |

建议先交付 M0-M2，即可在触屏上跑通"画咒语 → 施法"的最小闭环，再逐步补齐。

---

## 附录 A：建议核对/新增的项目设置

```ini
[display]
window/handheld/orientation=0        ; 横屏（以编辑器枚举为准）

[input_devices]
pointing/emulate_mouse_from_touch=true
; pointing/emulate_touch_from_mouse=false   ; 仅调试用，正式包关闭
; pointing/android/enable_long_press_as_right_click=true  ; 仅作兜底方案时依赖
```

> 注：Godot 各小版本键名可能有差异，落地时以编辑器 Project Settings 中的实际项为准，并回填本文档。
