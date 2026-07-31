# Tessalume 旗舰主题模板 1.0

## 目录

1. 模板目标
2. 固定结构
3. 冻结几何
4. 可替换层
5. 自适应显隐
6. 新建与改造流程

## 模板目标

模板 1.0 以已验收的 `xin.moonfox-sovereign` 为基准。所有采用
`templateVersion: "1.0"` 的主题必须复用相同 DOM 角色、组件尺寸、位置、
聊天宽度和自适应优先级。主题之间只改变图片、文案、颜色、纹样、角色符号
和 CSS 动效。

模板资产位于 `assets/theme-template/`。不要从某个已发布主题复制并删改。
模板资产中的圆形挂件、通用轨道和示例关键帧只供新主题起步，不是旧主题
迁移目标。改造已有主题时，必须保留该主题已经存在的首页装置、卡片动效、
记忆徽记、同步仪、亮暗武器/SVG 和消息框，仅把它们映射到统一结构。

## 固定结构

```text
#cts-theme-root
├─ [data-theme-stage]
│  ├─ hero
│  │  ├─ hero-kicker
│  │  ├─ hero-title-light / hero-title-dark
│  │  ├─ hero-motion
│  │  └─ hero-note
│  ├─ identity
│  │  ├─ identity-emblem
│  │  ├─ identity-copy
│  │  └─ identity-status
│  ├─ task-left
│  ├─ task-right[secondary]
│  ├─ task-right[primary]
│  └─ memory
├─ sync-panel[secondary]
└─ composer-accessory
```

`hero`、`identity`、`task-left`、`memory`、`sync-panel` 和
`composer-accessory` 各一个；`task-right` 必须正好两个。同步面板与次要
右卡都使用 `data-theme-priority="secondary"`，空间不足时一起消失。

运行时会在挂载时检查数量、嵌套和优先级。结构错误直接阻止主题挂载。

固定结构节点的内部可以保持角色专属实现。例如 `composer-accessory` 可以
包含亮暗两套完全不同的 SVG，`memory-meter` 可以是已有徽记，`sync-*`
可以复用已有仪表节点。不要为了让内部 DOM 看起来像模板示例而删除它们。

## 冻结几何

Template 1.0 的几何和公共表面现在只存在于运行时共享的
`theme-template-v1.css`。主题包中的 `skin.css` 不得包含几何块、公共表面块、
`data-theme-role` 布局选择器或主题私有响应式布局。使用
`scripts/sync_template_geometry.py --check` 检查共享几何以及皮肤隔离。

| 区域 | 模板 1.0 几何 |
|---|---|
| 首页横幅 | 宽 `calc(100% - 40px)`；先为输入区预留 `195px`，再按容器宽高自适应，`320–840px` |
| 首页文案 | 左 `max(5%, 76px)`；上 `10%`；宽 `min(46%, 780px)` |
| 顶部身份牌 | 水平居中；上 `6px`；高 `38px`；宽 `250–390px` |
| 左侧主卡 | 左 `4px`；上 `72px`；`146×234px` |
| 左侧记忆卡 | 左 `4px`；上 `334px`；宽 `146px`；最小高 `165px` |
| 右侧次卡 | 右 `218px`；底 `52px`；`188×334px` |
| 右侧主卡 | 右 `18px`；底 `52px`；`188×334px` |
| 右侧状态面板 | `320×56px`；由运行时居中置于双卡上方，安全间距 `40px` |
| 输入框挂件 | `76×76px`；由运行时置于输入框左侧 |
| 助手内容 | 左对齐；最大宽 `min(88%, 820px)` |
| 用户气泡 | 右对齐；最大宽 `min(79%, 760px)` |

输出面板打开后右卡缩为 `156×278px`。窄屏时右卡为 `138×246px`；
实际保留一张还是两张由运行时按聊天区域真实空间决定。

## 可替换层

可以修改：

- `manifest.json` 中的主题名称、作者、文案和本地资源映射；
- 亮色/暗色横幅、侧栏、聊天、记忆卡和三张角色图；
- `skin.css` 固定章节中的颜色变量、图片裁切、边框、纹样和阴影；
- `hero-motion`、`memory-meter`、`sync-*` 和 `composer-accessory` 的内部
  元素外观及关键帧；
- 卡片内部标题和角色状态文案。
- 助手/用户消息的边框、方向性强调线、角形、标签和角色专属纹样；有聊天
  背景时填充保持透明，但框体不能随之消失。

不能修改：

- `data-theme-role`、`data-theme-part`、主次优先级和节点归属；
- 运行时共享几何和公共表面；
- `mountCanonicalTheme`、`adaptiveLayout` 或两个公共定位函数；
- 聊天背景的稳定 `main::before` 注入方式；
- 运行时观察器、路由判断和清理逻辑。

## 自适应显隐

| 右侧可用空间 | 显示内容 |
|---|---|
| `full` | 次卡、主卡、状态面板 |
| `single` | 只显示主卡 |
| `none` | 右侧组件全部隐藏 |

左侧主卡与记忆卡共同显隐。代码审查、内容不可用或高度不足时，公共运行时
隐藏对应组件并暂停动画。主题不写额外固定宽度显隐规则。

## 新建与改造流程

### 新主题

1. 用 `scripts/scaffold_theme.py` 新建主题。
2. 替换占位图片、示例内部组件、关键帧和 `manifest.json` 文案。
3. 只编辑 `skin.css` 的主题变量、角色皮肤与专属动效。

### 旧主题迁移

1. 在修改前记录 Git 基线，并列出首页、左栏、聊天、消息框、左卡、两张
   右卡、记忆、同步组件、输入框挂件、亮暗形态和关键帧。
2. 在原 DOM 上补 `data-theme-stage`、`data-theme-role`、
   `data-theme-part` 和优先级。只有无法满足固定嵌套时才移动节点，不用模板
   示例节点替换原组件。
3. 将 `sync-panel` 和 `composer-accessory` 调整为主题根节点直属元素，
   但保留它们原来的内部 DOM、SVG、文案、类名和动效。
4. 给 `mountCanonicalTheme` 补齐 `templateVersion: "1.0"` 与
   `adaptiveLayout: true`，并固定使用 `320, 56, 40` 的面板定位参数。
5. 移除会压过冻结层的旧 `!important` 位置、尺寸和显隐规则；不要删除
   同一规则中的颜色、边框、滤镜、伪元素或 `animation`。
6. 核对 CSS 中每个 `--cts-asset-*` 都有 manifest 键。保留旧变量别名通常
   比一次性重命名整套角色皮肤更安全。
7. 运行保留审计，再检查共享几何和皮肤隔离：

   ```powershell
   python .agents/skills/author-tessalume-theme/scripts/audit_migration_preservation.py `
     --repo-root . --baseline-ref HEAD themes/<主题目录>

   python .agents/skills/author-tessalume-theme/scripts/sync_template_geometry.py `
     themes/<主题目录>
   ```

8. 运行 `validate_theme_contract.py`，再执行根目录 `一键构建EXE.ps1`。
   不手工同步便携版和受信指纹。
9. 运行检查前重新应用当前源码或本次构建，并先确认一个本次特有 DOM、
   SVG 数量、动画名或计算样式已经出现，避免把旧注入误判为新结果。
