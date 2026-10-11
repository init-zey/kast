# Player — 灰袍驼背人物模型

一个 Minecraft 风格的低模人物 **Player**：**无臂**、**披灰色袍子**、**兜帽遮面**、**驼背**，
附带一个 1.2 秒的**行走动画**。

## 文件

| 文件 | 说明 |
| --- | --- |
| `Player.bbmodel` | Blockbench 工程文件（模型 + 骨骼 + 贴图 + 行走动画，贴图已内嵌） |
| `player_texture.png` | 64×128 像素贴图（同样内嵌在 bbmodel 中，便于单独编辑） |
| `player.geo.json` | 附赠：Bedrock `minecraft:geometry` 1.12.0 几何体 |
| `player.animation.json` | 附赠：Bedrock 1.8.0 动画（`animation.player.walk`，可直接给实体用） |
| `player-model-kit.js` | Blockbench 插件：一键打开模型 / 导出各视角预览 / 查看统计 |

## 打开方式

**方式一（推荐）**：双击 `Player.bbmodel`，或在 Blockbench 里 `文件 ▸ 打开模型`。

**方式二（插件）**：把 `player-model-kit.js` 复制到 Blockbench 插件目录，重启后从
`工具 ▸ Player Kit` 打开。

- Windows：`%APPDATA%\Blockbench\plugins\`
- macOS：`~/Library/Application Support/Blockbench/plugins/`
- Linux：`~/.config/Blockbench/plugins/`

## 设计说明

- **比例**：脚底 `y=0`，头顶 `y=29.4`，约 29 像素高（略矮于原版玩家的 32，显得佝偻）。
- **无臂**：模型没有手臂骨骼。肩膀两侧垂下两条**空袖**（`sleeve_l` / `sleeve_r`），
  行走时随步伐摆动，读起来是「藏起来的双臂」而不是「断臂」。
- **遮面**：兜帽正面是一圈布料边框（额、颚、左右各一块），中间嵌一块**纯黑面板**
  （`face_void`），所以从任何角度看脸部都是一个没有五官的暗影。
- **驼背**：脊柱前倾 10°，胸口再前倾 8.5°，上背部单独堆了一块隆起（`hunch`），
  兜帽整体前伸，形成明显的弓背剪影。
- **袍子**：三段式——腰带 `robe_sash`（浅灰高光）、裙摆 `robe_skirt`（带布料褶皱的
  竖直渐变）、下摆 `robe_hem` 与前后两片会摆动的布片（`hem_front` / `hem_back`）。
- **贴图**：所有面共用 64×128 图集，每个面按「材质 + 像素尺寸」自动分组并打包，
  因此 UV 不存在拉伸；布料带竖向褶皱、底部磨损带和边缘压暗。

## 骨骼结构

```
root
└── pelvis
└── skirt ── hem_front, hem_back
└── spine ── chest ── head
│                   ├── sleeve_l, sleeve_r
└── hip_l ── knee_l ── ankle_l
└── hip_r ── knee_r ── ankle_r
```

共 16 根骨骼 / 27 个立方体。

## 行走动画 `walk`

- 时长 **1.2 秒**（24 帧 @ 20 帧/秒），`loop` 循环，线性插值，首尾帧完全一致。
- 腿部完整周期：髋关节 −22°…+22°，膝关节 2°…46°，踝关节 −12°…+12°。
- 次级动作：躯干左右摇摆 ±3.6°、兜帽随脚步下沉回弹、双袖反向摆动、前后下摆摆动、
  整体上下起伏（root 的 `position`）。
- 共 15 条骨骼通道 / 147 个关键帧。

## 想改模型？

模型是脚本生成的，源文件在同级目录的 `tools/`：

```bash
cd ../tools
node build-player.js        # 重新生成 player/ 与 previews/
node validate-bbmodel.js    # 校验 .bbmodel 结构、UV、动画与贴图
```

- `player-model.js` — 骨骼、立方体、材质与配色（改这里就能改模型）
- `player-walk.js` — 行走动画的关键帧数据
- `paint-texture.js` / `png.js` — 程序化生成贴图
- `bbmodel.js` — 组装 Blockbench 工程
- `raster.js` — 离屏渲染，用于生成 `previews/` 里的检查图

## 预览

`../previews/` 下：

| 文件 | 内容 |
| --- | --- |
| `player-views.png` | 正面 / 侧面 / 四分之三 / 背面 |
| `player-head.png` | 兜帽与暗面特写 |
| `player-walk.png` | 行走动画四个相位 × 正面/侧面 |
| `player-texture.png` | 贴图放大 6 倍 |
