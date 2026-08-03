# Tessalume 旗舰主题模板 1.0

## 目录

1. 模板目标
2. 固定结构
3. 冻结几何
4. 可替换层
5. 自适应显隐
6. 从零创建流程

## 模板目标

模板 1.0 以已验收的 `xin.moonfox-sovereign` 为基准。所有采用
`templateVersion: "1.0"` 的主题必须复用相同 DOM 角色、组件尺寸、位置、
聊天宽度和自适应优先级。主题之间只改变图片、文案、颜色、纹样、角色符号
和 CSS 动效。

模板资产位于 `assets/theme-template/`。不要从某个已发布主题复制并删改。
模板中的圆形挂件、通用轨道、扫描线和示例关键帧都带有草稿语义，只用于说明
DOM 所有权。新主题必须将它们全部替换成角色专属设计，不能把示例稍作换色后
当作第一版。

## 固定结构

```text
#tessalume-theme-root
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
| 首页横幅 | 宽 `calc(100% - 40px)`；先为输入区预留 `240px`，再按容器宽高自适应，`320–840px` |
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
- 首页横幅、左栏图片和聊天背景只声明原图、裁切大小与位置；三者的亮度、
  对比度、饱和度和不透明度由 Tessalume 高级图像调节统一接管，并按主题、
  亮色和暗色分别持久化；
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
- 在首页横幅 `::before`、左栏 `::after` 或聊天 `main::before` 上写死
  `filter`/`opacity`；默认必须保持原图 100%。

## 自适应显隐

| 右侧可用空间 | 显示内容 |
|---|---|
| `full` | 次卡、主卡、状态面板 |
| `single` | 只显示主卡 |
| `none` | 右侧组件全部隐藏 |

左侧主卡与记忆卡共同显隐。代码审查、内容不可用或高度不足时，公共运行时
隐藏对应组件并暂停动画。主题不写额外固定宽度显隐规则。

## 从零创建流程

1. 先完成角色研究卡和 11 张图片矩阵，再运行 `scripts/scaffold_theme.py`。
2. 生成并验收全部图片；横幅人物偏右、聊天人物居中、左栏人物占画面约
   3/5-4/5，三张卡各自适配固定裁切。
3. 替换全部占位图片、草稿标记、示例文案、示例内部组件和关键帧。亮暗模式
   分别设计，但不要机械绑定角色形态。
4. 按 [flagship-completeness.md](flagship-completeness.md) 完成首页、左栏、聊天、
   标题行、消息气泡、环境信息内部、输入区底栏、三张卡、记忆、同步组件和挂件。
5. 只编辑 `skin.css` 的主题变量、角色皮肤与专属动效，保持 01-13 顺序和冻结几何。
6. 运行 `sync_template_geometry.py --check` 与 `validate_theme_contract.py`，任何草稿
   遗留或视觉覆盖缺失都必须在构建前解决。
7. 执行根目录 `一键构建EXE.ps1`，不手工同步便携版和受信指纹。
8. 运行检查前重新应用当前源码或本次构建，并先确认一个本次特有 DOM、SVG、
   动画名或计算样式已经出现，避免把旧注入误判为新结果。
