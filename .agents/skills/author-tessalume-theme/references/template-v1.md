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

## 冻结几何

`theme.css` 最末尾由以下标记包围：

```css
/* TESSALUME_TEMPLATE_V1_GEOMETRY_START */
/* ... */
/* TESSALUME_TEMPLATE_V1_GEOMETRY_END */
```

这段必须保持为文件最后一段，不能手工修改，也不能在其后追加 CSS。使用
`scripts/sync_template_geometry.py` 刷新或检查。

| 区域 | 模板 1.0 几何 |
|---|---|
| 首页横幅 | 宽 `calc(100% - 40px)`；高按容器宽高自适应，`500–840px` |
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
- `theme.css` 冻结几何块之前的颜色变量、图片裁切、边框、纹样和阴影；
- `hero-motion`、`memory-meter`、`sync-*` 和 `composer-accessory` 的内部
  元素外观及关键帧；
- 卡片内部标题和角色状态文案。

不能修改：

- `data-theme-role`、`data-theme-part`、主次优先级和节点归属；
- 冻结几何块；
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

1. 用 `scripts/scaffold_theme.py` 新建主题。
2. 替换占位图片和 `manifest.json` 文案。
3. 只编辑冻结几何块之前的主题皮肤与动效。
4. 改造旧主题时补齐 Template 1.0 结构，再运行：

   ```powershell
   python .agents/skills/author-tessalume-theme/scripts/sync_template_geometry.py `
     themes/<主题目录>
   ```

5. 运行 `validate_theme_contract.py`，错误为零后才允许同步便携版或提交。
