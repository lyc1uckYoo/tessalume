# Tessalume 开放主题契约

Tessalume 负责 Codex 启动、本机 CDP、主题加载、资源解析、实时切换、生命周期和故障恢复。仓库内的沉浸式旗舰主题还统一使用 canonical host，避免每个主题重复实现页面注入和路由时序。

## v2 主题格式

- `type` 固定为 `advanced`。
- 必须提供 `theme.js`，可以另外提供 CSS。

Tessalume 不再提供普通 CSS 主题类型。导入成功后，主题直接保存到便携主题库的 `themes/<主题ID>`。

资源完全由主题清单命名，不存在固定的横幅、头像或拍立得要求。当前支持常见图片、字体、文本、JSON、音频和视频格式；单文件不超过 25 MiB，总资源不超过 100 MiB。

## 主题生命周期

`theme.js` 调用 `registerTheme({ mount, unmount? })`。切换主题时 Studio 先执行旧主题的 `unmount`，再逆序执行托管清理，移除 Studio 创建的样式、根节点、资源变量和主题类，最后挂载新主题。

仓库内主题必须在 `mount` 中调用一次 `context.mountCanonicalTheme(...)`。它统一处理首页/任务页识别、主页面与侧栏标记、聊天消息与输出面板绑定、观察器、延时修复和清理。主题只保留资源、文案、颜色、角色元素和专属 CSS 动效。

聊天背景必须设置在稳定的 `main.<namespace>-main` 任务态背景上，不能设置在会被 React 切页重建的聊天内容容器伪元素上，否则会出现先露出纯色底、再闪出主题图的现象。

首页横幅、左栏人物图片和聊天背景属于用户可调图层。主题 CSS 只负责选择
亮暗原图以及设置 `background-position` / `background-size`，不得在这些图片
层上写死 `filter` 或 `opacity`。Tessalume 会按主题、亮暗模式分别持久化亮度、
对比度、饱和度和不透明度；默认 100% 即直接展示原图。文字可读性遮罩必须放在
独立层，不能与原图共用滤镜。

主要上下文：

- `context.root`：本主题独占的 DOM 根节点。
- `context.config`：清单中的任意 JSON 配置。
- `context.assets` / `assetDataUrl(name)`：清单声明的本地资源。
- `context.mode`：当前 `light` 或 `dark`。
- `addCleanup(fn)`：注册切换时必须执行的清理。
- `on(...)`、`observe(...)`、`interval(...)`、`timeout(...)`：自动托管生命周期的辅助方法。
- `mountCanonicalTheme(spec)`：旗舰主题统一注入宿主；新主题应优先使用它，不应自行注册路由观察器。
- `window` / `document`：当前 Codex 渲染页面，供完全自定义主题使用。

默认不在宠物浮层执行。主题只有明确声明 `compatibility.petOverlay: true` 才会在该页面挂载。

## 旗舰主题模板 1.0

`心 · 朝月孤城` 是旗舰主题模板 1.0 的首个验收基准。新主题在
`mountCanonicalTheme` 中声明：

```javascript
templateVersion: "1.0",
adaptiveLayout: true
```

运行时会检查首页主视觉、顶部身份牌、左侧主卡、记忆卡、右侧主次双卡、
双卡上方状态面板和输入框挂件的数量、嵌套及优先级。CSS 文件最末尾的冻结
几何块统一首页横幅、卡片、聊天内容和浮动组件的大小与位置；校验器会阻止
主题私自改变这部分。

因此，新主题只需要替换：

- 亮暗色图片资源和角色文案；
- 颜色、边框、纹样与阴影；
- 首页轨道、记忆卡、状态面板、输入框挂件和卡片的角色专属动效。

空间不足时，左侧双组件共同隐藏；右侧先隐藏次卡与状态面板，只保留主卡，
再在更窄空间全部隐藏。主题不再自行编写一套断点显隐逻辑。

从 [旗舰主题模板 1.0](examples/README.md) 开始。完整结构、
冻结尺寸表、脚手架和验证命令见仓库 Skill：
`.agents/skills/author-tessalume-theme/`。
